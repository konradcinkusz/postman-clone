using System.Net;
using System.Text.Json.Nodes;

namespace PostmanClone.Seeder;

/// <summary>
/// The seeding flow (ADR-0030), written against the public APIs only. Every step first looks for what it would create and does
/// nothing when it is there, so a second run changes nothing. The output names what happened per step and never prints a token.
/// </summary>
internal sealed class SeedRunner(SeedOptions options, ApiClient api, Action<string> log, TimeProvider time)
{
    private static readonly HttpMethod Get = HttpMethod.Get, Post = HttpMethod.Post, Put = HttpMethod.Put, Delete = HttpMethod.Delete;

    public async Task<SeedSummary> SeedAsync(CancellationToken ct)
    {
        await WaitForEstateAsync(ct);

        var owner = await EnsureAccountAsync(DemoData.OwnerEmail, ct);
        var (org, orgCreated) = await EnsureOrganizationAsync(owner, ct);
        // A token issued before the workspace existed has no organization claim. A run that finds the workspace already there signs in
        // with a token that has it, so it needs no refresh (and spends nothing of authservice's sign-in window).
        if (orgCreated)
        {
            owner = await RefreshAsync(owner, ct);
        }

        var member = await EnsureAccountAsync(DemoData.MemberEmail, ct);
        member = await EnsureMemberAsync(owner, member, org, ct);

        var collections = await EnsureCollectionAsync(owner, ct);
        var environments = await EnsureEnvironmentAsync(owner, ct);

        // The member must see what the owner seeded: they share the workspace (CONTRACTS §0). A seed that cannot show this is a failure.
        var visible = await ListAsync(options.CollectionsBaseUrl, "/api/v1/collections?pageSize=100", member.Access, ct);
        if (!visible.Any(c => c["name"]?.GetValue<string>() == DemoData.CollectionName))
        {
            throw new InvalidOperationException("The demo member cannot see the demo collection. The two accounts are not in the same workspace.");
        }

        log("member sees the demo collection: yes");
        return new SeedSummary(org, collections, environments);
    }

    public async Task<ResetSummary> ResetAsync(CancellationToken ct)
    {
        await WaitForEstateAsync(ct);
        var login = await TryLoginAsync(DemoData.OwnerEmail, ct);
        if (login is null)
        {
            log("reset: the demo owner does not exist; nothing to remove");
            return new ResetSummary(0, 0);
        }

        var org = await FindDemoOrganizationAsync(login, ct);
        if (org is null)
        {
            log("reset: the demo owner has no demo workspace; nothing to remove");
            return new ResetSummary(0, 0);
        }

        // Only names inside the reserved namespace are removed, whatever else the workspace holds.
        var removedCollections = 0;
        foreach (var c in (await ListAsync(options.CollectionsBaseUrl, "/api/v1/collections?pageSize=100", login.Access, ct)).Where(c => DemoData.IsDemoName(c["name"]?.GetValue<string>())))
        {
            await api.SendAsync(Delete, options.CollectionsBaseUrl, $"/api/v1/collections/{c["id"]}", bearer: login.Access, cancellationToken: ct);
            removedCollections++;
        }

        var removedEnvironments = 0;
        foreach (var e in (await ListAsync(options.EnvironmentsBaseUrl, "/api/v1/environments?pageSize=100", login.Access, ct)).Where(e => DemoData.IsDemoName(e["name"]?.GetValue<string>())))
        {
            await api.SendAsync(Delete, options.EnvironmentsBaseUrl, $"/api/v1/environments/{e["id"]}", bearer: login.Access, cancellationToken: ct);
            removedEnvironments++;
        }

        log($"reset: removed {removedCollections} collection(s) from CollectionsService and {removedEnvironments} environment(s) from EnvironmentsService; accounts and the workspace are kept");
        return new ResetSummary(removedCollections, removedEnvironments);
    }

    // ── the estate ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task WaitForEstateAsync(CancellationToken ct)
    {
        var targets = new[]
        {
            (options.AuthBaseUrl, "/health/ready"),
            (options.CollectionsBaseUrl, "/health"),
            (options.EnvironmentsBaseUrl, "/health"),
        };
        var deadline = time.GetUtcNow() + options.WaitFor;
        foreach (var (baseUrl, path) in targets)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (status, _, _) = await api.TrySendAsync(Get, baseUrl, path, cancellationToken: ct);
                    if (status == HttpStatusCode.OK)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet.
                }

                if (time.GetUtcNow() >= deadline)
                {
                    throw new TimeoutException($"{baseUrl.Host}{path} did not answer 200 within {options.WaitFor.TotalSeconds:0} s.");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), time, ct);
            }
        }

        log("estate is healthy: authservice, CollectionsService, EnvironmentsService");
    }

    // ── accounts ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Account(string Email, string Access, string Refresh);

    private async Task<Account?> TryLoginAsync(string email, CancellationToken ct)
    {
        var (status, body, text) = await api.TrySendAsync(Post, options.AuthBaseUrl, "/api/v1/auth/login",
            new JsonObject { ["email"] = email, ["password"] = options.DemoPassword }, cancellationToken: ct);
        if (status == HttpStatusCode.OK && body?["accessToken"] is { } access)
        {
            return new Account(email, access.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
        }

        if (status == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        throw new SeedApiException("POST authservice /api/v1/auth/login", status, text.Length > 300 ? text[..300] : text);
    }

    private async Task<Account> EnsureAccountAsync(string email, CancellationToken ct)
    {
        var existing = await TryLoginAsync(email, ct);
        if (existing is not null)
        {
            log($"account {email}: present");
            return existing;
        }

        var versions = await api.SendAsync(Get, options.AuthBaseUrl, "/api/v1/auth/consents/versions", cancellationToken: ct);
        var (status, body, text) = await api.TrySendAsync(Post, options.AuthBaseUrl, "/api/v1/auth/register", new JsonObject
        {
            ["email"] = email,
            ["password"] = options.DemoPassword,
            ["acceptedTermsVersion"] = versions!["terms"]!.GetValue<string>(),
            ["acceptedPrivacyVersion"] = versions["privacy"]!.GetValue<string>(),
        }, cancellationToken: ct);
        if (status != HttpStatusCode.OK || body?["accessToken"] is not { } access)
        {
            throw new SeedApiException("POST authservice /api/v1/auth/register", status,
                status == HttpStatusCode.Accepted
                    ? "the account needs email verification; this estate has an email provider configured, which the seeder does not support"
                    : status == HttpStatusCode.BadRequest && text.Contains("already", StringComparison.OrdinalIgnoreCase)
                        ? $"{email} exists with a different password; remove it or set Seed__DemoPassword to its password"
                        : text.Length > 300 ? text[..300] : text);
        }

        log($"account {email}: created");
        return new Account(email, access.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    private async Task<Account> RefreshAsync(Account account, CancellationToken ct)
    {
        var body = await api.SendAsync(Post, options.AuthBaseUrl, "/api/v1/auth/refresh",
            new JsonObject { ["refreshToken"] = account.Refresh }, cancellationToken: ct);
        return new Account(account.Email, body!["accessToken"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    // ── the workspace ────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid Id, string Name)?> FindDemoOrganizationAsync(Account account, CancellationToken ct)
    {
        var me = await api.SendAsync(Get, options.AuthBaseUrl, "/api/v1/auth/me", bearer: account.Access, cancellationToken: ct);
        foreach (var org in me?["organizations"]?.AsArray() ?? [])
        {
            if (org?["name"]?.GetValue<string>() == DemoData.OrganizationName)
            {
                return (Guid.Parse(org["id"]!.GetValue<string>()), DemoData.OrganizationName);
            }
        }

        return null;
    }

    private async Task<(Guid Id, bool Created)> EnsureOrganizationAsync(Account owner, CancellationToken ct)
    {
        var found = await FindDemoOrganizationAsync(owner, ct);
        if (found is { } existing)
        {
            log($"workspace '{DemoData.OrganizationName}': present");
            return (existing.Id, false);
        }

        var created = await api.SendAsync(Post, options.AuthBaseUrl, "/api/v1/organizations",
            new JsonObject { ["name"] = DemoData.OrganizationName, ["description"] = "Demo data for a fresh start (seeded)." }, owner.Access, ct);
        log($"workspace '{DemoData.OrganizationName}': created");
        return (Guid.Parse(created!["id"]!.GetValue<string>()), true);
    }

    private async Task<Account> EnsureMemberAsync(Account owner, Account member, Guid orgId, CancellationToken ct)
    {
        if (await IsMemberAsync(member, orgId, ct))
        {
            log($"member {member.Email}: already in the workspace");
            return member;
        }

        // The token for this address may already be in the log from an earlier, interrupted run: the invitation is then pending and a
        // second invite would be refused ("already sent").
        var token = InvitationLog.TokenFor(options.AuthLogPath, member.Email);
        var pending = await PendingInvitationAsync(member, orgId, ct);
        if (pending is null)
        {
            await api.SendAsync(Post, options.AuthBaseUrl, $"/api/v1/organizations/{orgId}/invite",
                new JsonObject { ["email"] = member.Email, ["role"] = "Member" }, owner.Access, ct);
            log($"member {member.Email}: invited by the owner");
            token = null;
        }
        else if (token is null)
        {
            // A pending invitation whose token is not readable: ask authservice to send it again (it logs the token again).
            await api.SendAsync(Post, options.AuthBaseUrl, $"/api/v1/organizations/{orgId}/invitations/{pending}/resend", bearer: owner.Access, cancellationToken: ct);
            log($"member {member.Email}: invitation re-sent");
        }

        token = await WaitForTokenAsync(member.Email, token, ct);
        await api.SendAsync(Post, options.AuthBaseUrl, "/api/v1/organizations/invitations/accept", new JsonObject { ["token"] = token }, member.Access, ct);
        log($"member {member.Email}: accepted the invitation");
        return await RefreshAsync(member, ct);
    }

    private async Task<string> WaitForTokenAsync(string email, string? known, CancellationToken ct)
    {
        if (known is not null)
        {
            return known;
        }

        var deadline = time.GetUtcNow() + TimeSpan.FromSeconds(60);
        while (time.GetUtcNow() < deadline)
        {
            var token = InvitationLog.TokenFor(options.AuthLogPath, email);
            if (token is not null)
            {
                return token;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), time, ct);
        }

        throw new TimeoutException(
            "The invitation token for the demo member is not readable. authservice has no API that returns it: it writes it to its log in " +
            $"Development. Set Seed__AuthLogPath to that log ({(options.AuthLogPath is null ? "it is not set" : "it is set but holds no token")}) and run authservice with ASPNETCORE_ENVIRONMENT=Development.");
    }

    private async Task<bool> IsMemberAsync(Account account, Guid orgId, CancellationToken ct)
    {
        var me = await api.SendAsync(Get, options.AuthBaseUrl, "/api/v1/auth/me", bearer: account.Access, cancellationToken: ct);
        return (me?["organizations"]?.AsArray() ?? []).Any(o => o?["id"]?.GetValue<string>() is { } id && Guid.Parse(id) == orgId);
    }

    private async Task<Guid?> PendingInvitationAsync(Account member, Guid orgId, CancellationToken ct)
    {
        var list = await api.SendAsync(Get, options.AuthBaseUrl, "/api/v1/organizations/invitations", bearer: member.Access, cancellationToken: ct);
        foreach (var item in list?.AsArray() ?? [])
        {
            if (item?["organizationId"]?.GetValue<string>() is { } o && Guid.Parse(o) == orgId)
            {
                return Guid.Parse(item["id"]!.GetValue<string>());
            }
        }

        return null;
    }

    // ── collections ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> EnsureCollectionAsync(Account owner, CancellationToken ct)
    {
        var created = 0;
        var existing = (await ListAsync(options.CollectionsBaseUrl, "/api/v1/collections?pageSize=100", owner.Access, ct))
            .FirstOrDefault(c => c["name"]?.GetValue<string>() == DemoData.CollectionName);
        string collectionId;
        if (existing is null)
        {
            var made = await api.SendAsync(Post, options.CollectionsBaseUrl, "/api/v1/collections",
                new JsonObject { ["name"] = DemoData.CollectionName, ["description"] = "A few requests to try the product (seeded)." }, owner.Access, ct);
            collectionId = made!["id"]!.GetValue<string>();
            created++;
            log($"collection '{DemoData.CollectionName}': created");
        }
        else
        {
            collectionId = existing["id"]!.GetValue<string>();
            log($"collection '{DemoData.CollectionName}': present");
        }

        var tree = await api.SendAsync(Get, options.CollectionsBaseUrl, $"/api/v1/collections/{collectionId}", bearer: owner.Access, cancellationToken: ct);
        var folderId = tree!["folders"]!.AsArray().FirstOrDefault(f => f?["name"]?.GetValue<string>() == DemoData.FolderName)?["id"]?.GetValue<string>();
        if (folderId is null)
        {
            var folder = await api.SendAsync(Post, options.CollectionsBaseUrl, $"/api/v1/collections/{collectionId}/folders",
                new JsonObject { ["name"] = DemoData.FolderName }, owner.Access, ct);
            folderId = folder!["id"]!.GetValue<string>();
            created++;
        }

        var requests = tree["requests"]!.AsArray().Select(r => r?["name"]?.GetValue<string>()).ToHashSet();
        foreach (var (name, parent, definition) in new (string, string?, JsonObject)[]
                 {
                     (DemoData.GetRequestName, null, DemoData.GetDefinition()),
                     (DemoData.PostRequestName, folderId, DemoData.PostDefinition()),
                 })
        {
            if (requests.Contains(name))
            {
                continue;
            }

            await api.SendAsync(Post, options.CollectionsBaseUrl, $"/api/v1/collections/{collectionId}/requests",
                new JsonObject { ["name"] = name, ["folderId"] = parent, ["description"] = "Seeded demo request.", ["request"] = definition }, owner.Access, ct);
            created++;
            log($"request '{name}': created");
        }

        return created;
    }

    // ── environments ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> EnsureEnvironmentAsync(Account owner, CancellationToken ct)
    {
        var existing = (await ListAsync(options.EnvironmentsBaseUrl, "/api/v1/environments?pageSize=100", owner.Access, ct))
            .FirstOrDefault(e => e["name"]?.GetValue<string>() == DemoData.EnvironmentName);
        string id;
        var created = 0;
        var keepSecret = false;
        if (existing is null)
        {
            var made = await api.SendAsync(Post, options.EnvironmentsBaseUrl, "/api/v1/environments",
                new JsonObject { ["name"] = DemoData.EnvironmentName }, owner.Access, ct);
            id = made!["id"]!.GetValue<string>();
            created++;
        }
        else
        {
            id = existing["id"]!.GetValue<string>();
            var current = await api.SendAsync(Get, options.EnvironmentsBaseUrl, $"/api/v1/environments/{id}", bearer: owner.Access, cancellationToken: ct);
            var variables = current!["variables"]!.AsArray();
            var hasAll = new[] { DemoData.BaseUrlKey, DemoData.SecretKey }.All(k => variables.Any(v => v?["key"]?.GetValue<string>() == k));
            if (hasAll)
            {
                log($"environment '{DemoData.EnvironmentName}': present");
                return 0;
            }

            keepSecret = variables.Any(v => v?["key"]?.GetValue<string>() == DemoData.SecretKey && v["hasValue"]?.GetValue<bool>() == true);
        }

        await api.SendAsync(Put, options.EnvironmentsBaseUrl, $"/api/v1/environments/{id}",
            new JsonObject { ["name"] = DemoData.EnvironmentName, ["variables"] = DemoData.EnvironmentVariables(keepSecret) }, owner.Access, ct);
        log($"environment '{DemoData.EnvironmentName}': variables written ({DemoData.BaseUrlKey}, secret {DemoData.SecretKey})");
        return created + 1;
    }

    private async Task<List<JsonNode>> ListAsync(Uri baseUrl, string path, string bearer, CancellationToken ct)
    {
        var page = await api.SendAsync(Get, baseUrl, path, bearer: bearer, cancellationToken: ct);
        return (page?["items"]?.AsArray() ?? []).Where(i => i is not null).Select(i => i!).ToList();
    }
}

internal sealed record SeedSummary(Guid OrganizationId, int CollectionItemsCreated, int EnvironmentItemsCreated)
{
    public int TotalCreated => CollectionItemsCreated + EnvironmentItemsCreated;
}

internal sealed record ResetSummary(int CollectionsRemoved, int EnvironmentsRemoved);
