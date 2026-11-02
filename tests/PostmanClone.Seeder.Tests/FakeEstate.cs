using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PostmanClone.Seeder.Tests;

/// <summary>
/// An in-memory authservice, CollectionsService and EnvironmentsService, enough for the seeder's calls. It keeps the rules the seeder
/// depends on: a duplicate invitation is refused, a member sees only the workspace they belong to, an invitation token goes only to a
/// log file (as authservice v0.3.4 does), and every state-changing call is recorded so a test can assert that a second run writes nothing.
/// </summary>
internal sealed class FakeEstate : HttpMessageHandler
{
    private readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase); // email → password
    private readonly Dictionary<string, Guid> _orgByName = [];
    private readonly Dictionary<Guid, HashSet<string>> _members = [];
    private readonly Dictionary<Guid, (Guid OrgId, string Email, string Token)> _invitations = [];
    private readonly Dictionary<Guid, JsonObject> _collections = [];
    private readonly Dictionary<Guid, JsonObject> _environments = [];
    private int _tokenCounter;

    public FakeEstate(string logPath) => LogPath = logPath;

    public string LogPath { get; }
    public List<string> Writes { get; } = [];
    public bool WithholdTokens { get; set; }
    public IReadOnlyCollection<string> CollectionNames => _collections.Values.Select(c => c["name"]!.GetValue<string>()).ToList();
    public IReadOnlyCollection<string> EnvironmentNames => _environments.Values.Select(c => c["name"]!.GetValue<string>()).ToList();
    public JsonObject? Environment(string name) => _environments.Values.FirstOrDefault(e => e["name"]!.GetValue<string>() == name);

    public void AddUnrelatedData()
    {
        _collections[Guid.NewGuid()] = new JsonObject { ["name"] = "Customer API", ["folders"] = new JsonArray(), ["requests"] = new JsonArray() };
        _environments[Guid.NewGuid()] = new JsonObject { ["name"] = "Production", ["variables"] = new JsonArray() };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;
        var body = request.Content is null ? null : JsonNode.Parse(request.Content.ReadAsStringAsync(cancellationToken).Result);
        var user = request.Headers.Authorization?.Parameter is { } t && t.StartsWith("access:", StringComparison.Ordinal) ? t["access:".Length..] : null;
        if (request.Method != HttpMethod.Get && !path.Contains("/auth/login", StringComparison.Ordinal))
        {
            Writes.Add($"{request.Method} {request.RequestUri.Port}{path}");
        }

        return Task.FromResult(Route(request.Method.Method, request.RequestUri.Port, path, query, body, user));
    }

    private HttpResponseMessage Route(string method, int port, string path, string query, JsonNode? body, string? user)
    {
        if (path is "/health" or "/health/ready")
        {
            return Json(HttpStatusCode.OK, new JsonObject { ["status"] = "Healthy" });
        }

        if (port == 5300)
        {
            return Auth(method, path, body, user);
        }

        if (user is null || !MemberOfAnyOrg(user))
        {
            return Json(HttpStatusCode.Forbidden, new JsonObject { ["code"] = "no_active_workspace" });
        }

        return port == 5301 ? Collections(method, path, body) : Environments(method, path, body);
    }

    private bool MemberOfAnyOrg(string user) => _members.Values.Any(m => m.Contains(user));

    private HttpResponseMessage Auth(string method, string path, JsonNode? body, string? user)
    {
        switch (method, path)
        {
            case ("GET", "/api/v1/auth/consents/versions"):
                return Json(HttpStatusCode.OK, new JsonObject { ["terms"] = "t1", ["privacy"] = "p1" });
            case ("POST", "/api/v1/auth/login"):
            {
                var email = body!["email"]!.GetValue<string>();
                return _users.TryGetValue(email, out var pw) && pw == body["password"]!.GetValue<string>()
                    ? Tokens(email)
                    : Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "Invalid credentials" });
            }

            case ("POST", "/api/v1/auth/register"):
            {
                var email = body!["email"]!.GetValue<string>();
                if (!_users.TryAdd(email, body["password"]!.GetValue<string>()))
                {
                    return Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = "User already exists" });
                }

                return Tokens(email);
            }

            case ("POST", "/api/v1/auth/refresh"):
                return Tokens(body!["refreshToken"]!.GetValue<string>()["refresh:".Length..]);
            case ("GET", "/api/v1/auth/me"):
            {
                var orgs = new JsonArray();
                foreach (var (name, id) in _orgByName.Where(o => _members[o.Value].Contains(user!)))
                {
                    orgs.Add(new JsonObject { ["id"] = id.ToString(), ["name"] = name, ["role"] = "Member" });
                }

                return Json(HttpStatusCode.OK, new JsonObject { ["organizations"] = orgs });
            }

            case ("POST", "/api/v1/organizations"):
            {
                var id = Guid.NewGuid();
                _orgByName[body!["name"]!.GetValue<string>()] = id;
                _members[id] = [user!];
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = id.ToString() });
            }

            case ("GET", "/api/v1/organizations/invitations"):
            {
                var list = new JsonArray();
                foreach (var (id, inv) in _invitations.Where(i => string.Equals(i.Value.Email, user, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new JsonObject { ["id"] = id.ToString(), ["organizationId"] = inv.OrgId.ToString() });
                }

                return Json(HttpStatusCode.OK, list);
            }

            case ("POST", "/api/v1/organizations/invitations/accept"):
            {
                var found = _invitations.FirstOrDefault(i => i.Value.Token == body!["token"]!.GetValue<string>() && string.Equals(i.Value.Email, user, StringComparison.OrdinalIgnoreCase));
                if (found.Key == Guid.Empty)
                {
                    return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = "Invitation not found" });
                }

                _members[found.Value.OrgId].Add(user!);
                _invitations.Remove(found.Key);
                return Json(HttpStatusCode.OK, new JsonObject { ["organizationId"] = found.Value.OrgId.ToString() });
            }
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (method == "POST" && segments is ["api", "v1", "organizations", var orgId, "invite"])
        {
            var email = body!["email"]!.GetValue<string>();
            if (_invitations.Values.Any(i => string.Equals(i.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = "An invitation has already been sent to this email" });
            }

            var id = Guid.NewGuid();
            _invitations[id] = (Guid.Parse(orgId), email, "tok-" + ++_tokenCounter);
            LogToken(email, _invitations[id].Token);
            return Json(HttpStatusCode.OK, new JsonObject { ["email"] = email });
        }

        if (method == "POST" && segments is ["api", "v1", "organizations", _, "invitations", var invitationId, "resend"])
        {
            var inv = _invitations[Guid.Parse(invitationId)];
            LogToken(inv.Email, inv.Token);
            return Json(HttpStatusCode.OK, new JsonObject());
        }

        return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = "unknown " + method + " " + path });
    }

    private void LogToken(string email, string token) => File.AppendAllText(LogPath,
        $"warn: Email sending is not configured. Invitation email to {email} for organization Demo Workspace was not sent. Invitation token: {(WithholdTokens ? "[withheld]" : token)}. Share it.\n");

    private HttpResponseMessage Collections(string method, string path, JsonNode? body)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (method, segments)
        {
            case ("GET", ["api", "v1", "collections"]):
                return Json(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(_collections.Select(c => (JsonNode)new JsonObject { ["id"] = c.Key.ToString(), ["name"] = c.Value["name"]!.GetValue<string>() }).ToArray()) });
            case ("POST", ["api", "v1", "collections"]):
            {
                var id = Guid.NewGuid();
                _collections[id] = new JsonObject { ["name"] = body!["name"]!.GetValue<string>(), ["folders"] = new JsonArray(), ["requests"] = new JsonArray() };
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = id.ToString() });
            }

            case ("GET", ["api", "v1", "collections", var id]):
                return Json(HttpStatusCode.OK, Clone(_collections[Guid.Parse(id)]));
            case ("DELETE", ["api", "v1", "collections", var id]):
                _collections.Remove(Guid.Parse(id));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            case ("POST", ["api", "v1", "collections", var id, "folders"]):
            {
                var folderId = Guid.NewGuid().ToString();
                _collections[Guid.Parse(id)]["folders"]!.AsArray().Add(new JsonObject { ["id"] = folderId, ["name"] = body!["name"]!.GetValue<string>() });
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = folderId });
            }

            case ("POST", ["api", "v1", "collections", var id, "requests"]):
            {
                var requestId = Guid.NewGuid().ToString();
                _collections[Guid.Parse(id)]["requests"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = requestId,
                    ["name"] = body!["name"]!.GetValue<string>(),
                    ["folderId"] = body["folderId"]?.GetValue<string>(),
                });
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = requestId });
            }
        }

        return Json(HttpStatusCode.NotFound, new JsonObject());
    }

    private HttpResponseMessage Environments(string method, string path, JsonNode? body)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (method, segments)
        {
            case ("GET", ["api", "v1", "environments"]):
                return Json(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(_environments.Select(e => (JsonNode)new JsonObject { ["id"] = e.Key.ToString(), ["name"] = e.Value["name"]!.GetValue<string>() }).ToArray()) });
            case ("POST", ["api", "v1", "environments"]):
            {
                var id = Guid.NewGuid();
                _environments[id] = new JsonObject { ["name"] = body!["name"]!.GetValue<string>(), ["variables"] = new JsonArray() };
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = id.ToString() });
            }

            case ("GET", ["api", "v1", "environments", var id]):
                return Json(HttpStatusCode.OK, Clone(_environments[Guid.Parse(id)]));
            case ("PUT", ["api", "v1", "environments", var id]):
            {
                var variables = new JsonArray();
                foreach (var v in body!["variables"]!.AsArray())
                {
                    variables.Add(new JsonObject
                    {
                        ["key"] = v!["key"]!.GetValue<string>(),
                        ["isSecret"] = v["isSecret"]!.GetValue<bool>(),
                        ["value"] = v["isSecret"]!.GetValue<bool>() ? null : v["value"]?.GetValue<string>(),
                        ["hasValue"] = v["value"] is not null || v["keepExistingSecret"]!.GetValue<bool>(),
                    });
                }

                _environments[Guid.Parse(id)]["variables"] = variables;
                return Json(HttpStatusCode.OK, new JsonObject());
            }

            case ("DELETE", ["api", "v1", "environments", var id]):
                _environments.Remove(Guid.Parse(id));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return Json(HttpStatusCode.NotFound, new JsonObject());
    }

    private static JsonNode Clone(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    private static HttpResponseMessage Tokens(string email) =>
        Json(HttpStatusCode.OK, new JsonObject { ["accessToken"] = "access:" + email, ["refreshToken"] = "refresh:" + email });

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
