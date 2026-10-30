using System.Net.Http.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Collections;

/// <summary>
/// <c>GET /collections?q=</c> matches a name case-insensitively, and the characters <c>%</c>, <c>_</c> and <c>\</c> match
/// themselves (CONTRACTS §2 and the WP08 acceptance): they are not LIKE wildcards or escapes.
/// </summary>
public sealed class CollectionSearchTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Search_matches_a_name_regardless_of_case(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = await SeedAsync(tokens, client, "Payments", "Refunds");

        var names = await SearchAsync(client, token, "pAyMeNts");

        Assert.Equal(new[] { "Payments" }, names);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Search_treats_percent_as_a_literal_character(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = await SeedAsync(tokens, client, "100% sure", "100 sure", "Payments");

        var names = await SearchAsync(client, token, "%");

        Assert.Equal(new[] { "100% sure" }, names);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Search_treats_underscore_as_a_literal_character(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = await SeedAsync(tokens, client, "a_b", "axb", "Payments");

        var names = await SearchAsync(client, token, "a_b");

        Assert.Equal(new[] { "a_b" }, names);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Search_treats_backslash_as_a_literal_character(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = await SeedAsync(tokens, client, @"C:\temp", "Ctemp", "Payments");

        var names = await SearchAsync(client, token, @"\");

        Assert.Equal(new[] { @"C:\temp" }, names);
    }

    /// <summary>A workspace with the given collections; returns its token.</summary>
    private static async Task<string> SeedAsync(TestTokens tokens, HttpClient client, params string[] names)
    {
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        foreach (var name in names)
        {
            await CollectionsCalls.CreateCollectionAsync(client, token, name, TestContext.Current.CancellationToken);
        }

        return token;
    }

    private static async Task<string[]> SearchAsync(HttpClient client, string token, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/collections?q={Uri.EscapeDataString(query)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);
        return page!.Items.Select(collection => collection.Name).Order().ToArray();
    }
}
