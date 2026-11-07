using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PostmanClone.Web.Hosting;

/// <summary>
/// The development hint on the sign-in page (ADR-0030): the seeded demo accounts and their published password, so a developer who has
/// just started the estate can sign in at once. It is shown only when the Web runs in Development AND the estate was started with the demo
/// seed (<c>Seed:Enabled</c> is exactly "true", which the AppHost, compose and the local script set only while they seed). In any other
/// environment the block is absent from the page, whatever the setting says.
/// </summary>
public static class DevSignInHint
{
    /// <summary>The published development password of every seeded account. It is not a credential: those accounts exist only in a Development estate.</summary>
    public const string Password = "Demo-Passw0rd!"; // gitleaks:allow — the published local demo password.

    public sealed record Row(string Email, string Role, string Workspace);

    public static IReadOnlyList<Row> Accounts { get; } =
    [
        new("demo.owner@postmanclone.test", "Owner", "Demo Workspace"),
        new("demo.admin@postmanclone.test", "Admin", "Demo Workspace"),
        new("demo.member@postmanclone.test", "Member", "Demo Workspace"),
        new("demo.other@postmanclone.test", "Owner", "Other Workspace"),
    ];

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && string.Equals(configuration["Seed:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
}
