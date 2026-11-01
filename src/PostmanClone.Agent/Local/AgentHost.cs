using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Remote;
using PostmanClone.Agent.Send;

namespace PostmanClone.Agent.Local;

/// <summary>What the host needs: the port it listens on, the store, and the server it calls.</summary>
/// <param name="LoggerProviders">Extra logging providers. The tests use one to capture every line the agent writes.</param>
public sealed record AgentHostOptions(int Port, AgentConfigStore Store, IAgentServer Server, IEnumerable<Microsoft.Extensions.Logging.ILoggerProvider>? LoggerProviders = null);

/// <summary>
/// The local listener (CONTRACTS A2.6). Kestrel is bound to the loopback address only, and every request passes
/// <see cref="Guard"/> first: the <c>Host</c> check (421), the <c>Origin</c> check (403), and the CORS answer for an allowed
/// origin. Refused requests get no allow headers. Nothing else in the process listens.
/// </summary>
public static class AgentHost
{
    public static WebApplication Build(AgentHostOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        foreach (var provider in options.LoggerProviders ?? [])
        {
            builder.Logging.AddProvider(provider);
        }
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // 127.0.0.1 only: never 0.0.0.0 and never ::. A second listener on the port fails to start (CONTRACTS A2.6).
            kestrel.Listen(IPAddress.Loopback, options.Port);
            kestrel.Limits.MaxRequestBodySize = LocalProtocol.MaxBodyBytes;
            kestrel.AddServerHeader = false;
        });

        builder.Services.AddSingleton(options.Store);
        builder.Services.AddSingleton(options.Server);
        builder.Services.AddSingleton(new AgentRuntime(options.Store));
        builder.Services.AddSingleton<AgentSender>();
        builder.Services.AddSingleton(new AgentListener(options.Port));

        var app = builder.Build();
        app.Use(Guard);
        app.MapGet("/v1/status", StatusAsync);
        app.MapPost("/v1/pair", PairAsync);
        app.MapPost("/v1/send", SendAsync);
        return app;
    }

    /// <summary>The JSON the local protocol uses: camelCase, enums as camelCase words, as the server's results are.</summary>
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Host, then Origin, then the CORS headers for an allowed origin, then the preflight answer. Runs before any route.</summary>
    private static async Task Guard(HttpContext context, RequestDelegate next)
    {
        var port = context.RequestServices.GetRequiredService<AgentListener>().Port;
        if (!LocalProtocol.HostAllowed(context.Request.Host.Value, port))
        {
            await Problem(context, StatusCodes.Status421MisdirectedRequest, "host_not_allowed", "The Host header does not name the agent.");
            return;
        }

        var config = context.RequestServices.GetRequiredService<AgentRuntime>().Load();
        var origin = context.Request.Headers.Origin.ToString();
        if (!LocalProtocol.OriginAllowed(origin, config.AllowedWebOrigins))
        {
            // No Access-Control-Allow-Origin is set for a refused origin, so the browser gets nothing it can read.
            await Problem(context, StatusCodes.Status403Forbidden, "origin_not_allowed", "The page is not allowed to use this agent.");
            return;
        }

        // The exact origin is echoed, never "*", and credentials are never allowed (CONTRACTS A2.6).
        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers.Append("Vary", "Origin");

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.Headers["Access-Control-Allow-Methods"] = LocalProtocol.AllowMethods;
            context.Response.Headers["Access-Control-Allow-Headers"] = LocalProtocol.AllowHeaders;
            context.Response.Headers["Access-Control-Max-Age"] = LocalProtocol.MaxAge;
            if (string.Equals(context.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            }

            return;
        }

        await next(context);
    }

    private static IResult StatusAsync(AgentRuntime runtime) =>
        Results.Json(new { agent = AgentInfo.Name, protocol = AgentInfo.Protocol, version = AgentInfo.Version, paired = runtime.Load().IsPaired }, Json);

    /// <summary>Exchanges the code at the server and stores the agent token and the hash of the new pairing token (CONTRACTS A2.6).</summary>
    private static async Task<IResult> PairAsync(HttpContext context, AgentRuntime runtime, IAgentServer server, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!LocalProtocol.IsJson(context.Request.ContentType))
        {
            return Refused(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "The body must be application/json.");
        }

        var body = await ReadBodyAsync<AgentPairBody>(context, cancellationToken);
        if (body.Failure is not null)
        {
            return body.Failure;
        }

        var code = body.Value?.Code;
        if (!LocalProtocol.PairingCodeShapeValid(code))
        {
            return Refused(StatusCodes.Status400BadRequest, "pairing_code_invalid", "The pairing code is not valid.");
        }

        var config = runtime.Load();
        if (string.IsNullOrEmpty(config.ServerBaseUrl))
        {
            return Refused(StatusCodes.Status409Conflict, "agent_not_paired", "The agent has no server. Run its setup command first.");
        }

        var reply = await server.PairAsync(config.ServerBaseUrl!, code!, AgentInfo.Name, AgentInfo.Platform, AgentInfo.Version, cancellationToken);
        if (reply.Status == StatusCodes.Status400BadRequest)
        {
            return Refused(StatusCodes.Status400BadRequest, "pairing_code_invalid", "The pairing code is not valid, has been used, or has expired.");
        }

        if (!reply.Ok)
        {
            return Refused(StatusCodes.Status502BadGateway, "upstream_unavailable", "The server could not complete the pairing.");
        }

        var paired = reply.Body!;
        var pairingToken = PairingTokens.New();
        await runtime.UpdateAsync(current =>
        {
            current.AgentId = paired.AgentId;
            current.WorkspaceId = paired.WorkspaceId;
            current.AgentToken = paired.AgentToken;
            current.AgentTokenExpiresAt = paired.ExpiresAt;
            current.PairingTokenHash = PairingTokens.Hash(pairingToken);
            // The server's list is what the pages may use. An empty list keeps the origins that were set up.
            if (paired.AllowedWebOrigins.Count > 0)
            {
                current.AllowedWebOrigins = paired.AllowedWebOrigins.ToList();
            }
        });

        loggers.CreateLogger("PostmanClone.Agent.Pairing").LogInformation("Paired agent {AgentId}.", paired.AgentId);
        return Results.Json(new
        {
            pairingToken,
            agentId = paired.AgentId,
            expiresAt = paired.ExpiresAt,
            workspaceId = paired.WorkspaceId,
        }, Json, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Checks the pairing token, the pairing and the body, then runs the send (CONTRACTS A2.6).</summary>
    private static async Task<IResult> SendAsync(HttpContext context, AgentRuntime runtime, AgentSender sender, AgentListener listener, CancellationToken cancellationToken)
    {
        if (!LocalProtocol.IsJson(context.Request.ContentType))
        {
            return Refused(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "The body must be application/json.");
        }

        var config = runtime.Load();
        if (!PairingTokens.Matches(context.Request.Headers[PairingTokens.HeaderName].ToString(), config.PairingTokenHash))
        {
            return Refused(StatusCodes.Status401Unauthorized, "agent_token_invalid", "The pairing token is missing or wrong.");
        }

        if (!config.IsPaired)
        {
            return Refused(StatusCodes.Status409Conflict, "agent_not_paired", "The agent is not paired. Pair it again from the Web.");
        }

        if (context.Request.ContentLength is > LocalProtocol.MaxBodyBytes)
        {
            return Refused(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The request is larger than the agent accepts.");
        }

        var body = await ReadBodyAsync<AgentSendRequest>(context, cancellationToken);
        if (body.Failure is not null)
        {
            return body.Failure;
        }

        if (body.Value?.Request is null)
        {
            return Refused(StatusCodes.Status400BadRequest, "validation_failed", "The request is required.");
        }

        var reply = await sender.SendAsync(config, body.Value, listener.Port, cancellationToken);
        return reply.Result is not null
            ? Results.Json(reply.Result, Json, statusCode: StatusCodes.Status200OK)
            : Refused(reply.Status, reply.Code!, reply.Message!);
    }

    /// <summary>Reads the JSON body. An oversize body is 413, a malformed one is 400, and neither reaches the handler.</summary>
    private static async Task<(T? Value, IResult? Failure)> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return (await context.Request.ReadFromJsonAsync<T>(Json, cancellationToken), null);
        }
        catch (BadHttpRequestException failure) when (failure.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (default, Refused(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The request is larger than the agent accepts."));
        }
        catch (JsonException)
        {
            return (default, Refused(StatusCodes.Status400BadRequest, "validation_failed", "The request body is not valid JSON for this route."));
        }
    }

    private static IResult Refused(int status, string code, string message) =>
        Results.Json(new { type = "about:blank", title = message, status, code }, Json, statusCode: status, contentType: "application/problem+json");

    private static Task Problem(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { type = "about:blank", title = message, status, code }, Json));
    }
}

/// <summary>The port the listener is bound to. The guard reads it, so a test can start the agent on any free port.</summary>
public sealed record AgentListener(int Port);
