using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>
/// Stands in for EnvironmentsService behind the Web's <c>environments</c> client (WP21, CONTRACTS §3). It keeps one workspace's
/// environments in memory and answers the routes with the statuses and codes of §0 and §3, including the rules that matter to
/// the page: a secret's value is never returned, <c>keepExistingSecret</c> needs a stored secret, keys are unique, a workspace
/// holds 50 environments, and a foreign id is <c>404</c>. The request bodies are kept as sent, so a test reads what the page sent.
/// </summary>
public sealed class FakeEnvironmentsService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly Regex Route = new(@"^/api/v1/environments(?:/(?<id>[0-9a-fA-F-]{36}))?$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyShape = new(@"^[A-Za-z0-9_.\-]{1,100}$", RegexOptions.CultureInvariant);

    private readonly Dictionary<Guid, Stored> _environments = [];
    private readonly Dictionary<Guid, Stored> _foreign = [];
    private readonly List<string> _calls = [];
    private readonly List<string> _bodies = [];

    public FakeEnvironmentsService()
    {
        Handler = new FakeHttpMessageHandler(Respond);
    }

    public FakeHttpMessageHandler Handler { get; }

    /// <summary>Every call as "METHOD path", in order.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>The JSON body of each write (POST and PUT), exactly as the page sent it.</summary>
    public IReadOnlyList<string> Bodies => _bodies;

    /// <summary>When set, every call answers 503 with no code, as a stopped service does.</summary>
    public bool Unavailable { get; set; }

    /// <summary>When set, a write that carries a new secret value answers 503 <c>secrets_unavailable</c> (ADR-0006, CONTRACTS §3).</summary>
    public bool SecretsUnavailable { get; set; }

    /// <summary>When set, a delete answers 403: the caller is neither the creator nor an Admin or Owner.</summary>
    public bool ForbidDelete { get; set; }

    /// <summary>The number of environments in the workspace.</summary>
    public int Count => _environments.Count;

    /// <summary>Adds an environment to the workspace. A variable's value is plain, or secret with <c>secret</c> set to true.</summary>
    public Guid AddEnvironment(string name, params FakeVariable[] variables)
    {
        var id = Guid.NewGuid();
        _environments[id] = Build(id, name, variables);
        return id;
    }

    /// <summary>Adds an environment to another workspace. The page must see it as absent (CONTRACTS §0).</summary>
    public Guid AddForeignEnvironment(string name, params FakeVariable[] variables)
    {
        var id = Guid.NewGuid();
        _foreign[id] = Build(id, name, variables);
        return id;
    }

    /// <summary>Removes an environment from the workspace, as another user's delete would: the page's list is then stale.</summary>
    public void RemoveEnvironment(Guid environmentId) => _environments.Remove(environmentId);

    /// <summary>The stored state of one variable: whether a secret value is held, never the value itself.</summary>
    public bool StoredHasSecret(Guid environmentId, string key) =>
        _environments[environmentId].Variables.Any(variable => variable.Key == key && variable.Secret is not null);

    /// <summary>The plain value the service holds for a variable (for asserting a save).</summary>
    public string? StoredPlainValue(Guid environmentId, string key) =>
        _environments[environmentId].Variables.Single(variable => variable.Key == key).Value;

    /// <summary>The name the service holds.</summary>
    public string StoredName(Guid environmentId) => _environments[environmentId].Name;

    private static Stored Build(Guid id, string name, FakeVariable[] variables) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Variables = variables.Select(variable => new StoredVariable
        {
            Key = variable.Key,
            IsSecret = variable.IsSecret,
            Enabled = variable.Enabled,
            Value = variable.IsSecret ? null : variable.Value,
            Secret = variable.IsSecret ? variable.Value : null,
        }).ToList(),
    };

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        _calls.Add($"{request.Method} {path}");
        if (Unavailable)
        {
            return Problem(HttpStatusCode.ServiceUnavailable, "The service is unavailable.", null);
        }

        var match = Route.Match(path);
        if (!match.Success)
        {
            return Problem(HttpStatusCode.NotFound, "No such route.", "not_found");
        }

        var id = match.Groups["id"].Success ? Guid.Parse(match.Groups["id"].Value) : (Guid?)null;
        var method = request.Method.Method;
        if (id is null)
        {
            return method switch
            {
                "GET" => List(request),
                "POST" => Create(request),
                _ => Problem(HttpStatusCode.MethodNotAllowed, "Not allowed.", null),
            };
        }

        if (!_environments.TryGetValue(id.Value, out var environment))
        {
            // A foreign environment and an absent one are the same answer (CONTRACTS §0).
            return Problem(HttpStatusCode.NotFound, "The environment was not found.", "not_found");
        }

        return method switch
        {
            "GET" => Json(HttpStatusCode.OK, Dto(environment)),
            "PUT" => Update(request, environment),
            "DELETE" => Delete(environment),
            _ => Problem(HttpStatusCode.MethodNotAllowed, "Not allowed.", null),
        };
    }

    private HttpResponseMessage List(HttpRequestMessage request)
    {
        var items = _environments.Values
            .OrderBy(environment => environment.Name, StringComparer.Ordinal)
            .Select(environment => new
            {
                id = environment.Id,
                name = environment.Name,
                variableCount = environment.Variables.Count,
                updatedAt = environment.UpdatedAt,
            })
            .ToList();
        var page = int.Parse(QueryValue(request, "page") ?? "1");
        var pageSize = int.Parse(QueryValue(request, "pageSize") ?? "50");
        return Json(HttpStatusCode.OK, new { items, page, pageSize, total = items.Count });
    }

    private HttpResponseMessage Create(HttpRequestMessage request)
    {
        var body = ReadBody(request);
        var name = body?["name"]?.GetValue<string>() ?? string.Empty;
        if (_environments.Count >= 50)
        {
            // The service answers a bare 409 here, with no code (EnvironmentEndpoints.CreateAsync).
            return Problem(HttpStatusCode.Conflict, "This workspace already has the maximum number of environments.", null);
        }

        var id = Guid.NewGuid();
        _environments[id] = new Stored { Id = id, Name = name, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch };
        return Json(HttpStatusCode.Created, Dto(_environments[id]));
    }

    private HttpResponseMessage Update(HttpRequestMessage request, Stored environment)
    {
        var raw = ReadRaw(request);
        _bodies.Add(raw);
        var body = JsonNode.Parse(raw)!.AsObject();
        var name = body["name"]?.GetValue<string>() ?? string.Empty;
        var variables = body["variables"]!.AsArray().Select(node => node!.AsObject()).ToList();

        var errors = new List<string>();
        var keys = variables.Select(variable => variable["key"]!.GetValue<string>()).ToList();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
        {
            return Problem(HttpStatusCode.Conflict, "Variable keys must be unique within an environment.", null);
        }

        var planned = new List<StoredVariable>();
        foreach (var variable in variables)
        {
            var key = variable["key"]!.GetValue<string>();
            var value = variable["value"]?.GetValue<string>();
            var isSecret = variable["isSecret"]!.GetValue<bool>();
            var enabled = variable["enabled"]?.GetValue<bool>() ?? true;
            var keep = variable["keepExistingSecret"]?.GetValue<bool>() ?? false;
            if (!KeyShape.IsMatch(key))
            {
                errors.Add("The key may use only letters, digits, '_', '.' and '-'.");
                continue;
            }

            var stored = environment.Variables.FirstOrDefault(candidate => candidate.Key == key);
            if (!isSecret)
            {
                if (value is null)
                {
                    errors.Add($"{key}: a plain variable needs a value.");
                    continue;
                }

                planned.Add(new StoredVariable { Key = key, IsSecret = false, Enabled = enabled, Value = value });
                continue;
            }

            if (value is not null)
            {
                if (SecretsUnavailable)
                {
                    return Problem(HttpStatusCode.ServiceUnavailable, "Secret variables are unavailable.", "secrets_unavailable");
                }

                planned.Add(new StoredVariable { Key = key, IsSecret = true, Enabled = enabled, Secret = value });
                continue;
            }

            if (!keep)
            {
                errors.Add($"{key}: a secret needs a value, or keepExistingSecret when one is already stored.");
                continue;
            }

            if (stored is not { IsSecret: true, Secret: not null })
            {
                errors.Add($"{key}: there is no stored secret to keep; supply a value.");
                continue;
            }

            planned.Add(new StoredVariable { Key = key, IsSecret = true, Enabled = enabled, Secret = stored.Secret });
        }

        if (errors.Count > 0)
        {
            return Problem(HttpStatusCode.BadRequest, "One or more validation errors occurred.", "validation_failed", new Dictionary<string, string[]> { ["variables"] = errors.ToArray() });
        }

        environment.Name = name;
        environment.Variables = planned;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        return Json(HttpStatusCode.OK, Dto(environment));
    }

    private HttpResponseMessage Delete(Stored environment)
    {
        if (ForbidDelete)
        {
            return Problem(HttpStatusCode.Forbidden, "Only the creator, an Admin or an Owner can delete this environment.", "forbidden");
        }

        _environments.Remove(environment.Id);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    /// <summary>The environment as the service returns it: a secret's value is null, and HasValue says whether one is held.</summary>
    private static object Dto(Stored environment) => new
    {
        id = environment.Id,
        name = environment.Name,
        createdBy = "owner",
        createdAt = environment.CreatedAt,
        updatedAt = environment.UpdatedAt,
        variables = environment.Variables
            .OrderBy(variable => variable.Key, StringComparer.Ordinal)
            .Select(variable => new
            {
                key = variable.Key,
                value = variable.IsSecret ? null : variable.Value,
                isSecret = variable.IsSecret,
                hasValue = variable.IsSecret ? variable.Secret is not null : variable.Value is not null,
                enabled = variable.Enabled,
            })
            .ToList(),
    };

    private static JsonNode? ReadBody(HttpRequestMessage request) => JsonNode.Parse(ReadRaw(request));

    private static string ReadRaw(HttpRequestMessage request) =>
        request.Content is null ? "{}" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static string? QueryValue(HttpRequestMessage request, string name)
    {
        var query = request.RequestUri!.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair[0] == name)
            {
                return Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty);
            }
        }

        return null;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Options), new MediaTypeHeaderValue("application/json")),
    };

    /// <summary>An RFC 7807 problem with the service's <c>code</c> and, for 400, its <c>errors</c> map (CONTRACTS §0).</summary>
    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string? code, IDictionary<string, string[]>? errors = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["status"] = (int)status,
        };
        if (code is not null)
        {
            body["code"] = code;
        }

        if (errors is not null)
        {
            body["errors"] = errors;
        }

        return Json(status, body);
    }

    /// <summary>A variable for <see cref="AddEnvironment"/>: its key, its value (the secret's value when <see cref="IsSecret"/>), and its flags.</summary>
    public sealed record FakeVariable(string Key, string Value, bool IsSecret = false, bool Enabled = true);

    private sealed class Stored
    {
        public Guid Id { get; init; }

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; set; }

        public List<StoredVariable> Variables { get; set; } = [];
    }

    private sealed class StoredVariable
    {
        public string Key { get; init; } = string.Empty;

        public bool IsSecret { get; init; }

        public bool Enabled { get; init; }

        public string? Value { get; init; }

        /// <summary>The secret's stored value. Stands for the ciphertext: it is never returned.</summary>
        public string? Secret { get; init; }
    }
}
