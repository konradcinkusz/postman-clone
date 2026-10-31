using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// Fails every insert of a saved request, after the collection and folders of the same save were written. A transaction
/// test uses it to show that the failed save left none of those rows behind.
/// </summary>
public sealed class FailingSavedRequestInsert : DbCommandInterceptor
{
    private const string Marker = "INSERT INTO \"SavedRequests\"";

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Refuse(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Refuse(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(command);
        return ValueTask.FromResult(result);
    }

    private static void Refuse(DbCommand command)
    {
        if (command.CommandText.Contains(Marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A planted failure of the saved-request insert.");
        }
    }
}
