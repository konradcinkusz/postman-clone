using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Collections;

/// <summary>
/// The collection list reads the request count of every row in one statement, not one query per collection (WP08: no N+1).
/// The statements a list sends must not grow with the number of collections it returns. The statements are counted with
/// an EF interceptor, which only the PostgreSQL provider produces, so this runs on PostgreSQL alone.
/// </summary>
public sealed class CollectionListingQueryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_list_sends_the_same_number_of_statements_for_two_collections_as_for_twenty()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; counting statements needs the PostgreSQL provider.");
        }

        using var tokens = new TestTokens();
        var small = await CountListStatementsAsync(tokens, collections: 2);
        var large = await CountListStatementsAsync(tokens, collections: 20);

        Assert.Equal(small.Statements, large.Statements);
        Assert.Equal(2, small.Statements);
        Assert.Equal(20, large.Rows);
    }

    /// <summary>A workspace with the given collections, each with one request, and the statements one list call sends.</summary>
    private async Task<(int Statements, int Rows)> CountListStatementsAsync(TestTokens tokens, int collections)
    {
        var counter = new StatementCounter();
        await using var factory = new CollectionsFactory(tokens, postgres.ConnectionString, interceptor: counter);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        for (var index = 0; index < collections; index++)
        {
            var collection = await CollectionsCalls.CreateCollectionAsync(client, token, $"Collection {index:D2}", TestContext.Current.CancellationToken);
            await Storage.AddRequestAsync(factory, workspace, collection, null, "GET", TestContext.Current.CancellationToken);
        }

        var before = counter.Count;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/collections?pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);

        Assert.Equal(collections, page!.Items.Count(item => item.RequestCount == 1));
        return (counter.Count - before, page.Items.Count);
    }

    /// <summary>Counts the commands the context sends to the database.</summary>
    private sealed class StatementCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
