using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// Stands in for CollectionsService behind the Web's <c>collections</c> client (WP18). It keeps one workspace's collections,
/// folders and requests in memory and answers the routes of CONTRACTS §2 with the statuses and problem codes of §0: a foreign
/// or absent id is <c>404 not_found</c>, an <c>If-Match</c> mismatch is <c>409</c>, a cascade delete removes the subtree.
/// Tests seed the data, switch failures on, and read the calls it received.
/// </summary>
public sealed class FakeCollectionsService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<Guid, CollectionDto> _collections = [];
    private readonly Dictionary<Guid, FolderDto> _folders = [];
    private readonly Dictionary<Guid, Saved> _requests = [];
    private readonly List<string> _calls = [];

    public FakeCollectionsService(Guid currentUser)
    {
        CurrentUser = currentUser;
        Handler = new FakeHttpMessageHandler(Respond);
    }

    public FakeHttpMessageHandler Handler { get; }

    /// <summary>The user the service attributes new collections to. The tests use it for the creator rule.</summary>
    public Guid CurrentUser { get; }

    /// <summary>Every call as "METHOD path", in order.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>The bearer the last call carried (CONTRACTS §0: the workspace comes from it).</summary>
    public string? LastBearer { get; private set; }

    /// <summary>When set, every call answers 503, as a stopped service does.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Collections whose tree read answers 503, so a single tree can fail while the list works.</summary>
    public HashSet<Guid> FailingTrees { get; } = [];

    /// <summary>When set, deleting a collection answers <c>403 forbidden</c>, as for a Member who did not create it.</summary>
    public bool ForbidCollectionDelete { get; set; }

    public Guid AddCollection(string name, Guid? createdBy = null, string? description = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _collections[id] = new CollectionDto(id, name, description, createdBy ?? CurrentUser, now, now);
        return id;
    }

    public Guid AddFolder(Guid collectionId, string name, Guid? parent = null, int sortOrder = 0)
    {
        var id = Guid.NewGuid();
        _folders[id] = new FolderDto(id, collectionId, parent, name, sortOrder);
        return id;
    }

    public Guid AddRequest(Guid collectionId, string name, Guid? folder = null, string method = "GET", int sortOrder = 0)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _requests[id] = new Saved(id, collectionId, folder, name, sortOrder, method, Version: 1, now);
        return id;
    }

    public bool HasCollection(Guid id) => _collections.ContainsKey(id);

    public bool HasFolder(Guid id) => _folders.ContainsKey(id);

    public bool HasRequest(Guid id) => _requests.ContainsKey(id);

    /// <summary>The stored name of a request, or null when it is gone.</summary>
    public string? RequestName(Guid id) => _requests.TryGetValue(id, out var saved) ? saved.Name : null;

    /// <summary>The folder a request sits in, or null for the top of its collection.</summary>
    public Guid? RequestFolder(Guid id) => _requests.TryGetValue(id, out var saved) ? saved.Folder : null;

    public int RequestVersion(Guid id) => _requests[id].Version;

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        var method = request.Method.Method;
        _calls.Add($"{method} {path}");
        LastBearer = request.Headers.Authorization?.Parameter;

        if (Unavailable)
        {
            return Problem(HttpStatusCode.ServiceUnavailable, "upstream_unavailable", "The service is down.");
        }

        var route = path.Split('?')[0];
        var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : string.Empty;

        if (method == "GET" && route == "/api/v1/collections")
        {
            return ListCollections(query);
        }

        if (method == "POST" && route == "/api/v1/collections")
        {
            var body = Read<CreateCollectionBody>(request);
            var id = AddCollection(body.Name, description: body.Description);
            return Json201(_collections[id]);
        }

        if (Match(route, @"^/api/v1/collections/(?<id>[0-9a-f-]{36})$", out var collectionId))
        {
            return CollectionRoute(method, collectionId, request);
        }

        if (Match(route, @"^/api/v1/collections/(?<id>[0-9a-f-]{36})/folders$", out var parentCollection) && method == "POST")
        {
            return CreateFolder(parentCollection, request);
        }

        if (Match(route, @"^/api/v1/folders/(?<id>[0-9a-f-]{36})$", out var folderId))
        {
            return FolderRoute(method, folderId, request);
        }

        if (Match(route, @"^/api/v1/requests/(?<id>[0-9a-f-]{36})$", out var requestId))
        {
            return RequestRoute(method, requestId, request);
        }

        if (Match(route, @"^/api/v1/requests/(?<id>[0-9a-f-]{36})/duplicate$", out var duplicateId) && method == "POST")
        {
            return Duplicate(duplicateId);
        }

        if (Match(route, @"^/api/v1/requests/(?<id>[0-9a-f-]{36})/move$", out var moveId) && method == "POST")
        {
            return Move(moveId, request);
        }

        return Problem(HttpStatusCode.NotFound, "not_found", "No such route.");
    }

    private HttpResponseMessage ListCollections(string query)
    {
        var page = IntParameter(query, "page", 1);
        var pageSize = Math.Clamp(IntParameter(query, "pageSize", 50), 1, 100);
        var ordered = _collections.Values.OrderBy(collection => collection.CreatedAt).ToList();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(collection => new CollectionSummary(
                collection.Id,
                collection.Name,
                collection.Description,
                collection.CreatedBy,
                collection.CreatedAt,
                collection.UpdatedAt,
                _requests.Values.Count(saved => saved.Collection == collection.Id)))
            .ToList();
        return Json(HttpStatusCode.OK, new PageResult<CollectionSummary>(items, page, pageSize, ordered.Count));
    }

    private HttpResponseMessage CollectionRoute(string method, Guid id, HttpRequestMessage request)
    {
        if (!_collections.TryGetValue(id, out var collection))
        {
            return NotFound();
        }

        switch (method)
        {
            case "GET":
                if (FailingTrees.Contains(id))
                {
                    return Problem(HttpStatusCode.ServiceUnavailable, "upstream_unavailable", "The service is down.");
                }

                return Json(HttpStatusCode.OK, Tree(collection));
            case "PUT":
                var update = Read<UpdateCollectionBody>(request);
                _collections[id] = collection with { Name = update.Name, Description = update.Description, UpdatedAt = DateTimeOffset.UtcNow };
                return Json(HttpStatusCode.OK, _collections[id]);
            case "DELETE":
                if (ForbidCollectionDelete)
                {
                    return Problem(HttpStatusCode.Forbidden, "forbidden", "Only the creator or an Owner or Admin may delete this.");
                }

                _collections.Remove(id);
                foreach (var folder in _folders.Values.Where(folder => folder.CollectionId == id).ToList())
                {
                    _folders.Remove(folder.Id);
                }

                foreach (var saved in _requests.Values.Where(saved => saved.Collection == id).ToList())
                {
                    _requests.Remove(saved.Id);
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            default:
                return Problem(HttpStatusCode.MethodNotAllowed, "not_found", "No such method.");
        }
    }

    private HttpResponseMessage CreateFolder(Guid collectionId, HttpRequestMessage request)
    {
        if (!_collections.ContainsKey(collectionId))
        {
            return NotFound();
        }

        var body = Read<CreateFolderBody>(request);
        if (body.ParentFolderId is { } parent && (!_folders.TryGetValue(parent, out var parentFolder) || parentFolder.CollectionId != collectionId))
        {
            return NotFound();
        }

        var id = AddFolder(collectionId, body.Name, body.ParentFolderId);
        return Json201(_folders[id]);
    }

    private HttpResponseMessage FolderRoute(string method, Guid id, HttpRequestMessage request)
    {
        if (!_folders.TryGetValue(id, out var folder))
        {
            return NotFound();
        }

        if (method == "PUT")
        {
            var body = Read<RenameFolderBody>(request);
            _folders[id] = folder with { Name = body.Name };
            return Json(HttpStatusCode.OK, _folders[id]);
        }

        if (method == "DELETE")
        {
            RemoveFolderTree(id);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return Problem(HttpStatusCode.MethodNotAllowed, "not_found", "No such method.");
    }

    private HttpResponseMessage RequestRoute(string method, Guid id, HttpRequestMessage request)
    {
        if (!_requests.TryGetValue(id, out var saved))
        {
            return NotFound();
        }

        switch (method)
        {
            case "GET":
                return SavedJson(HttpStatusCode.OK, saved);
            case "PUT":
                if (request.Headers.TryGetValues("If-Match", out var values) && values.Single() != $"\"{saved.Version}\"")
                {
                    return Problem(HttpStatusCode.Conflict, "conflict", "The request changed since it was read.");
                }

                var body = Read<UpdateSavedRequestBody>(request);
                _requests[id] = saved with { Name = body.Name, Version = saved.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
                return SavedJson(HttpStatusCode.OK, _requests[id]);
            case "DELETE":
                _requests.Remove(id);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            default:
                return Problem(HttpStatusCode.MethodNotAllowed, "not_found", "No such method.");
        }
    }

    private HttpResponseMessage Duplicate(Guid id)
    {
        if (!_requests.TryGetValue(id, out var source))
        {
            return NotFound();
        }

        var copyId = Guid.NewGuid();
        _requests[copyId] = source with { Id = copyId, Name = $"{source.Name} copy", Version = 1, UpdatedAt = DateTimeOffset.UtcNow };
        return SavedJson(HttpStatusCode.Created, _requests[copyId]);
    }

    private HttpResponseMessage Move(Guid id, HttpRequestMessage request)
    {
        if (!_requests.TryGetValue(id, out var saved))
        {
            return NotFound();
        }

        var body = Read<MoveRequestBody>(request);
        if (body.FolderId is { } folder && (!_folders.TryGetValue(folder, out var target) || target.CollectionId != saved.Collection))
        {
            return NotFound();
        }

        _requests[id] = saved with { Folder = body.FolderId, UpdatedAt = DateTimeOffset.UtcNow };
        return SavedJson(HttpStatusCode.OK, _requests[id]);
    }

    private void RemoveFolderTree(Guid folderId)
    {
        foreach (var child in _folders.Values.Where(folder => folder.ParentFolderId == folderId).ToList())
        {
            RemoveFolderTree(child.Id);
        }

        foreach (var saved in _requests.Values.Where(saved => saved.Folder == folderId).ToList())
        {
            _requests.Remove(saved.Id);
        }

        _folders.Remove(folderId);
    }

    private CollectionTree Tree(CollectionDto collection) => new(
        collection,
        _folders.Values.Where(folder => folder.CollectionId == collection.Id).OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.Name).ToList(),
        _requests.Values.Where(saved => saved.Collection == collection.Id)
            .OrderBy(saved => saved.SortOrder).ThenBy(saved => saved.Name)
            .Select(saved => new RequestSummary(saved.Id, saved.Collection, saved.Folder, saved.Name, saved.Method, saved.SortOrder, saved.UpdatedAt))
            .ToList());

    private HttpResponseMessage SavedJson(HttpStatusCode status, Saved saved)
    {
        var response = Json(status, new SavedRequestDto(
            saved.Id,
            saved.Collection,
            saved.Folder,
            saved.Name,
            Description: null,
            new RequestDefinition(saved.Method, "https://api.example.test/items", [], [], Body: null, Auth: null),
            saved.SortOrder,
            saved.Version,
            CurrentUser,
            saved.UpdatedAt,
            saved.UpdatedAt));
        response.Headers.ETag = new EntityTagHeaderValue($"\"{saved.Version}\"");
        return response;
    }

    private static T Read<T>(HttpRequestMessage request) =>
        JsonSerializer.Deserialize<T>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), Options)!;

    private static bool Match(string route, string pattern, out Guid id)
    {
        var match = Regex.Match(route, pattern);
        id = match.Success ? Guid.Parse(match.Groups["id"].Value) : Guid.Empty;
        return match.Success;
    }

    private static int IntParameter(string query, string name, int fallback)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name && int.TryParse(parts.ElementAtOrDefault(1), out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static HttpResponseMessage NotFound() => Problem(HttpStatusCode.NotFound, "not_found", "Not found.");

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string title)
    {
        var body = JsonSerializer.Serialize(new { status = (int)status, title, code }, Options);
        return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/problem+json") };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body, options: Options) };

    private static HttpResponseMessage Json201<T>(T body) => Json(HttpStatusCode.Created, body!);

    /// <summary>A saved request as the fake stores it: the fields the tree and the actions read.</summary>
    private sealed record Saved(
        Guid Id,
        Guid Collection,
        Guid? Folder,
        string Name,
        int SortOrder,
        string Method,
        int Version,
        DateTimeOffset UpdatedAt);
}
