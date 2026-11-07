namespace PostmanClone.Seeder.Tests;

public sealed class InvitationLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "seed-log-" + Guid.NewGuid().ToString("N") + ".log");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void The_newest_token_for_the_address_is_returned()
    {
        File.WriteAllText(_path, string.Join('\n',
            "warn: Email sending is not configured. Invitation email to a@x.test for organization Demo was not sent. Invitation token: first-token. Share it.",
            "warn: Email sending is not configured. Invitation email to other@x.test for organization Demo was not sent. Invitation token: other-token. Share it.",
            "warn: Email sending is not configured. Invitation email to A@X.test for organization Demo was not sent. Invitation token: second-token. Share it."));

        Assert.Equal("second-token", InvitationLog.TokenFor(_path, "a@x.test"));
        Assert.Equal("other-token", InvitationLog.TokenFor(_path, "other@x.test"));
    }

    [Fact]
    public void A_withheld_token_is_not_a_token()
    {
        File.WriteAllText(_path, "warn: Invitation email to a@x.test for organization Demo was not sent. Invitation token: [withheld]. Share it.");

        Assert.Null(InvitationLog.TokenFor(_path, "a@x.test"));
    }

    [Fact]
    public void A_missing_log_or_no_path_gives_no_token()
    {
        Assert.Null(InvitationLog.TokenFor(null, "a@x.test"));
        Assert.Null(InvitationLog.TokenFor(_path, "a@x.test"));
    }
}
