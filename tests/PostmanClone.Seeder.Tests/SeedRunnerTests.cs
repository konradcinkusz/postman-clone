using Microsoft.Extensions.Time.Testing;

namespace PostmanClone.Seeder.Tests;

public sealed class SeedRunnerTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), "seed-runner-" + Guid.NewGuid().ToString("N") + ".log");
    private readonly List<string> _lines = [];

    public void Dispose() => File.Delete(_log);

    private (SeedRunner Runner, FakeEstate Estate) Create(FakeEstate estate) => Create(withLog: true, estate);

    private (SeedRunner Runner, FakeEstate Estate) Create(bool withLog = true, FakeEstate? existing = null)
    {
        var estate = existing ?? new FakeEstate(_log);
        var options = SeedOptions.Read(key => key switch
        {
            "Seed__Enabled" => "true",
            "DOTNET_ENVIRONMENT" => "Development",
            "Seed__AuthLogPath" => withLog ? _log : null,
            "Seed__WaitSeconds" => "5",
            _ => null,
        });
        var time = new FakeTimeProvider();
        // The fake clock advances on every wait, so a missing token ends in a timeout at once instead of after real seconds.
        var runner = new SeedRunner(options, new ApiClient(new HttpClient(estate)), _lines.Add, new AdvancingTime(time));
        return (runner, estate);
    }

    [Fact]
    public async Task A_fresh_estate_gets_two_accounts_in_one_workspace_and_the_demo_data()
    {
        var (runner, estate) = Create();

        var summary = await runner.SeedAsync(CancellationToken.None);

        Assert.Equal(["[demo] Getting started", "[demo] Other workspace only"], estate.CollectionNames.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["[demo] Local demo"], estate.EnvironmentNames);
        // collection, folder, three requests, the environment and its variables, and the other workspace's collection with its request
        Assert.Equal(9, summary.TotalCreated);
        Assert.Contains(_lines, l => l.Contains("member sees the demo collection: yes", StringComparison.Ordinal));
        Assert.Contains(_lines, l => l.Contains("admin sees the demo collection: yes", StringComparison.Ordinal));
        Assert.Contains(_lines, l => l.Contains("other workspace's account sees the demo collection: no", StringComparison.Ordinal));

        var variables = estate.Environment(DemoData.EnvironmentName)!["variables"]!.AsArray();
        Assert.Equal(2, variables.Count);
        var secret = variables.Single(v => v!["key"]!.GetValue<string>() == DemoData.SecretKey)!;
        Assert.True(secret["isSecret"]!.GetValue<bool>());
        Assert.True(secret["hasValue"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_credentials_table_lists_every_persona_with_the_password()
    {
        var (runner, _) = Create();

        await runner.SeedAsync(CancellationToken.None);

        var table = string.Join('\n', _lines);
        Assert.Contains("DEMO LOGINS", table, StringComparison.Ordinal);
        foreach (var persona in DemoData.Cast)
        {
            Assert.Contains(persona.Email, table, StringComparison.Ordinal);
            Assert.Contains(persona.Workspace, table, StringComparison.Ordinal);
        }

        Assert.Contains(DemoData.DefaultPassword, table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cast_has_an_owner_an_admin_and_a_member_in_one_workspace_and_one_account_in_another()
    {
        var (runner, estate) = Create();
        await runner.SeedAsync(CancellationToken.None);

        Assert.Equal(
            ["Owner:Demo Workspace", "Admin:Demo Workspace", "Member:Demo Workspace", "Owner:Other Workspace"],
            DemoData.Cast.Select(p => p.Role + ":" + p.Workspace).ToArray());
        Assert.Contains(estate.Writes, w => w.EndsWith("/invite", StringComparison.Ordinal));
        Assert.Equal(["Admin", "Member"], estate.InvitedRoles.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var (runner, estate) = Create();
        await runner.SeedAsync(CancellationToken.None);
        estate.Writes.Clear();

        var second = await runner.SeedAsync(CancellationToken.None);

        Assert.Equal(0, second.TotalCreated);
        Assert.Empty(estate.Writes);
        Assert.Equal(2, estate.CollectionNames.Count);
        Assert.Single(estate.EnvironmentNames);
    }

    [Fact]
    public async Task A_partly_seeded_estate_is_completed_without_repeating_what_exists()
    {
        var (runner, estate) = Create();
        await runner.SeedAsync(CancellationToken.None);
        // Lose the environment only (for instance a reset that stopped half way).
        await runner.ResetAsync(CancellationToken.None);
        estate.Writes.Clear();

        var again = await runner.SeedAsync(CancellationToken.None);

        Assert.Equal(9, again.TotalCreated);
        Assert.DoesNotContain(estate.Writes, w => w.Contains("/auth/register", StringComparison.Ordinal) || w.Contains("/invite", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reset_removes_only_names_in_the_reserved_namespace_and_keeps_the_accounts()
    {
        var (runner, estate) = Create();
        await runner.SeedAsync(CancellationToken.None);
        estate.AddUnrelatedData();

        var reset = await runner.ResetAsync(CancellationToken.None);

        Assert.Equal(2, reset.CollectionsRemoved); // the demo collection and the other workspace's
        Assert.Equal(1, reset.EnvironmentsRemoved);
        Assert.Equal(["Customer API"], estate.CollectionNames);
        Assert.Equal(["Production"], estate.EnvironmentNames);
        Assert.Contains(_lines, l => l.Contains("removed 2 collection(s) from CollectionsService and 1 environment(s) from EnvironmentsService", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reset_on_an_estate_that_was_never_seeded_removes_nothing_and_registers_nobody()
    {
        var (runner, estate) = Create();

        var reset = await runner.ResetAsync(CancellationToken.None);

        Assert.Equal(new ResetSummary(0, 0), reset);
        Assert.Empty(estate.Writes);
    }

    [Fact]
    public async Task An_interrupted_run_that_left_a_pending_invitation_is_finished_from_the_logged_token()
    {
        // The first run cannot read the log, so it stops after sending the invitation.
        var (first, estate) = Create(withLog: false);
        await Assert.ThrowsAsync<TimeoutException>(() => first.SeedAsync(CancellationToken.None));

        // The second run is given the log. The invitation is pending, so it must not invite again (authservice would refuse).
        var (second, _) = Create(estate);
        estate.Writes.Clear();
        await second.SeedAsync(CancellationToken.None);

        // The admin's invitation was the pending one; only the member, not yet invited, gets a new invitation.
        Assert.Single(estate.Writes, w => w.EndsWith("/invite", StringComparison.Ordinal));
        Assert.Contains(estate.Writes, w => w.Contains("/invitations/accept", StringComparison.Ordinal));
        Assert.Contains(_lines, l => l.Contains("accepted the invitation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pending_invitation_whose_token_is_not_in_the_log_is_sent_again()
    {
        var (first, estate) = Create(withLog: false);
        await Assert.ThrowsAsync<TimeoutException>(() => first.SeedAsync(CancellationToken.None));
        File.Delete(_log); // the log was rotated away

        var (second, _) = Create(estate);
        estate.Writes.Clear();
        await second.SeedAsync(CancellationToken.None);

        Assert.Contains(estate.Writes, w => w.Contains("/resend", StringComparison.Ordinal));
        Assert.Single(estate.Writes, w => w.EndsWith("/invite", StringComparison.Ordinal)); // the member's, never the admin's again
    }

    private static async Task Register(FakeEstate estate, string email, string password)
    {
        using var http = new HttpClient(estate);
        await new ApiClient(http).SendAsync(HttpMethod.Post, new Uri("http://localhost:5300"), "/api/v1/auth/register",
            new System.Text.Json.Nodes.JsonObject { ["email"] = email, ["password"] = password });
    }

    [Fact]
    public async Task Without_a_readable_token_the_run_fails_with_a_message_that_names_the_cause()
    {
        var (runner, estate) = Create(withLog: false);

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => runner.SeedAsync(CancellationToken.None));

        Assert.Contains("authservice has no API that returns it", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Seed__AuthLogPath", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(estate);
    }

    [Fact]
    public async Task A_withheld_token_in_the_log_is_reported_as_unreadable()
    {
        var (runner, estate) = Create();
        estate.WithholdTokens = true;

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => runner.SeedAsync(CancellationToken.None));

        Assert.Contains("holds no token", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_account_that_exists_with_another_password_is_reported_and_not_overwritten()
    {
        var (runner, estate) = Create();
        await Register(estate, DemoData.OwnerEmail, "Other-Passw0rd!"); // not the seed's password

        var failure = await Assert.ThrowsAsync<SeedApiException>(() => runner.SeedAsync(CancellationToken.None));

        Assert.Contains("different password", failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>A clock whose delays finish at once and move the time forward by the delay, so waits are bounded without sleeping.</summary>
internal sealed class AdvancingTime(FakeTimeProvider inner) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        inner.Advance(dueTime);
        callback(state);
        return inner.CreateTimer((_) => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}
