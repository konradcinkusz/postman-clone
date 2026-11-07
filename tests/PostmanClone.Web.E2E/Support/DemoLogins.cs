namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The seeded accounts and names (src/PostmanClone.Seeder, ADR-0030). The password is the published development value; it is
/// not a credential. The estate must have been started with the seed (scripts/run-local-stack.sh up, or scripts/compose-up.sh).
/// </summary>
public static class DemoLogins
{
    public const string OwnerEmail = "demo.owner@postmanclone.test";
    public const string AdminEmail = "demo.admin@postmanclone.test";
    public const string MemberEmail = "demo.member@postmanclone.test";
    public const string OtherEmail = "demo.other@postmanclone.test";
    public const string Password = "Demo-Passw0rd!"; // gitleaks:allow — the published local demo password.

    public const string Collection = "[demo] Getting started";
    public const string GetRequest = "Get example.com";
    public const string PostFolder = "Echo";
    public const string PostRequest = "Post a JSON note";
    public const string LocalRequest = "Local health (needs the local agent)";
    public const string OtherCollection = "[demo] Other workspace only";
    public const string Environment = "[demo] Local demo";
    public const string FakeSecret = "demo-not-a-real-key"; // gitleaks:allow — the obviously fake value of the demo secret variable.
}
