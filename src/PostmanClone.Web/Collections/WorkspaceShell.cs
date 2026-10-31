using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Collections;

/// <summary>Who the shell is for: the workspace's name and the signed-in user's address and id.</summary>
public sealed record ShellIdentity(string WorkspaceName, string? Email, Guid? UserId);

/// <summary>
/// The workspace shell's calls to CollectionsService (WP18). Each call asks <see cref="CircuitSession"/> for the session's
/// access token and passes it on, so the circuit holds no token of its own; the workspace is the token's, never a parameter.
/// The class runs in the circuit's scope. The page calls it and renders what comes back; it decides nothing about the tree.
/// </summary>
public sealed class WorkspaceShell(WorkspaceState workspace, CircuitSession session, CollectionsClient collections, AuthServiceClient authservice)
{
    /// <summary>The most pages the list reads. At 100 a page this covers the 200-collection limit (CONTRACTS §2) with room to spare.</summary>
    private const int MaximumPages = 4;

    public bool HasWorkspace => workspace.Resolution.IsActive;

    /// <summary>The caller's role in the workspace, or null when there is no single workspace.</summary>
    public string? Role => workspace.Resolution.IsActive ? workspace.Resolution.Role : null;

    /// <summary>The name and the signed-in user, from authservice's profile. Null when the session has ended or authservice did not answer.</summary>
    public async Task<ShellIdentity?> IdentityAsync(CancellationToken cancellationToken)
    {
        if (!HasWorkspace || workspace.Resolution.WorkspaceId is not { } workspaceId)
        {
            return null;
        }

        var accessToken = await session.AccessTokenAsync(cancellationToken);
        if (accessToken is null)
        {
            return null;
        }

        var user = await authservice.MeAsync(accessToken, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var name = user.Organizations.Find(organization => Guid.TryParse(organization.Id, out var id) && id == workspaceId)?.Name ?? "Workspace";
        return new ShellIdentity(name, user.Email, CollectionRules.UserIdOf(user));
    }

    /// <summary>Every collection of the workspace, read page by page. The service's list is bounded, so the loop is too.</summary>
    public async Task<CollectionAnswer<IReadOnlyList<CollectionSummary>>> ListCollectionsAsync(CancellationToken cancellationToken)
    {
        var accessToken = await CurrentTokenAsync(cancellationToken);
        if (accessToken.Failure is { } failure)
        {
            return new CollectionAnswer<IReadOnlyList<CollectionSummary>>(failure, null, null);
        }

        var all = new List<CollectionSummary>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            var answer = await collections.ListCollectionsAsync(accessToken.Token!, page, CollectionsClient.MaximumPageSize, cancellationToken);
            if (answer.Value is not { } result)
            {
                return new CollectionAnswer<IReadOnlyList<CollectionSummary>>(answer.Outcome, null, answer.Code);
            }

            all.AddRange(result.Items);
            if (result.Items.Count == 0 || all.Count >= result.Total)
            {
                break;
            }
        }

        return new CollectionAnswer<IReadOnlyList<CollectionSummary>>(CollectionOutcome.Succeeded, all, null);
    }

    public Task<CollectionAnswer<CollectionTree>> LoadTreeAsync(Guid collectionId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.GetCollectionTreeAsync(token, collectionId, ct), cancellationToken);

    public Task<CollectionAnswer<CollectionDto>> CreateCollectionAsync(string name, string? description, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.CreateCollectionAsync(token, new CreateCollectionBody(name, description), ct), cancellationToken);

    public Task<CollectionAnswer<CollectionDto>> RenameCollectionAsync(Guid collectionId, string name, string? description, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.UpdateCollectionAsync(token, collectionId, new UpdateCollectionBody(name, description), ct), cancellationToken);

    public Task<CollectionAnswer> DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.DeleteCollectionAsync(token, collectionId, ct), cancellationToken);

    public Task<CollectionAnswer<FolderDto>> CreateFolderAsync(Guid collectionId, string name, Guid? parentFolderId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.CreateFolderAsync(token, collectionId, new CreateFolderBody(name, parentFolderId), ct), cancellationToken);

    public Task<CollectionAnswer<FolderDto>> RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.RenameFolderAsync(token, folderId, new RenameFolderBody(name), ct), cancellationToken);

    public Task<CollectionAnswer> DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.DeleteFolderAsync(token, folderId, ct), cancellationToken);

    /// <summary>
    /// Renames a request. The service's rename is a full replacement (CONTRACTS §2), so the request is read first and the
    /// write carries the version it read in <c>If-Match</c>: a change made meanwhile is refused, not overwritten.
    /// </summary>
    public async Task<CollectionAnswer<SavedRequestDto>> RenameRequestAsync(Guid requestId, string name, CancellationToken cancellationToken)
    {
        var current = await WithTokenAsync((token, ct) => collections.GetRequestAsync(token, requestId, ct), cancellationToken);
        if (current.Value is not { } saved)
        {
            return new CollectionAnswer<SavedRequestDto>(current.Outcome, null, current.Code);
        }

        return await WithTokenAsync(
            (token, ct) => collections.UpdateRequestAsync(token, requestId, new UpdateSavedRequestBody(name, saved.Description, saved.Request), saved.Version, ct),
            cancellationToken);
    }

    public Task<CollectionAnswer<SavedRequestDto>> DuplicateRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.DuplicateRequestAsync(token, requestId, ct), cancellationToken);

    /// <summary>Moves a request into a folder of its own collection, or to the top of it when <paramref name="folderId"/> is null.</summary>
    public Task<CollectionAnswer<SavedRequestDto>> MoveRequestAsync(Guid requestId, Guid? folderId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.MoveRequestAsync(token, requestId, new MoveRequestBody(CollectionId: null, folderId, SortOrder: null), ct), cancellationToken);

    public Task<CollectionAnswer> DeleteRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => collections.DeleteRequestAsync(token, requestId, ct), cancellationToken);

    private async Task<(string? Token, CollectionOutcome? Failure)> CurrentTokenAsync(CancellationToken cancellationToken)
    {
        if (!HasWorkspace)
        {
            return (null, CollectionOutcome.NoWorkspace);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        if (token is null)
        {
            return (null, CollectionOutcome.SessionEnded);
        }

        return (token, null);
    }

    /// <summary>Runs a call with the session's token. No workspace or no session is an outcome, so the page can say which.</summary>
    private async Task<CollectionAnswer<T>> WithTokenAsync<T>(Func<string, CancellationToken, Task<CollectionAnswer<T>>> call, CancellationToken cancellationToken)
    {
        var (token, failure) = await CurrentTokenAsync(cancellationToken);
        return failure is { } outcome
            ? new CollectionAnswer<T>(outcome, default, null)
            : await call(token!, cancellationToken);
    }

    private async Task<CollectionAnswer> WithTokenAsync(Func<string, CancellationToken, Task<CollectionAnswer>> call, CancellationToken cancellationToken)
    {
        var (token, failure) = await CurrentTokenAsync(cancellationToken);
        return failure is { } outcome
            ? new CollectionAnswer(outcome, null)
            : await call(token!, cancellationToken);
    }
}
