using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Membership;

namespace PostmanClone.Web.Tests.Membership;

/// <summary>The rules of the members page and the invitation flow, as plain xUnit (BLAZOR-WEB-APP §10, no renderer).</summary>
public sealed class InvitationRulesTests
{
    [Theory]
    [InlineData(WorkspaceRoles.Owner, true)]
    [InlineData(WorkspaceRoles.Admin, true)]
    [InlineData(WorkspaceRoles.Member, false)]
    [InlineData(null, false)]
    public void Only_an_owner_or_an_admin_may_invite(string? role, bool expected) =>
        Assert.Equal(expected, InvitationRules.CanInvite(role));

    [Fact]
    public void The_web_offers_admin_and_member_and_never_owner()
    {
        Assert.Equal([WorkspaceRoles.Member, WorkspaceRoles.Admin], InvitationRules.InvitableRoles);
        Assert.False(InvitationRules.IsInvitableRole(WorkspaceRoles.Owner));
        Assert.False(InvitationRules.IsInvitableRole("member"));
        Assert.False(InvitationRules.IsInvitableRole(null));
    }

    [Theory]
    [InlineData("  person@example.test  ", "person@example.test")]
    [InlineData("a@b.test", "a@b.test")]
    public void A_well_formed_address_is_kept_trimmed(string given, string expected) =>
        Assert.Equal(expected, InvitationRules.NormalizeEmail(given));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    public void A_missing_or_malformed_address_is_refused(string? given) =>
        Assert.Null(InvitationRules.NormalizeEmail(given));

    [Fact]
    public void An_address_over_the_authservice_limit_is_refused()
    {
        var local = new string('a', InvitationRules.MaximumEmailLength);

        Assert.Null(InvitationRules.NormalizeEmail($"{local}@example.test"));
    }

    [Fact]
    public void A_refused_invite_maps_to_the_reason_authservice_gave()
    {
        Assert.Equal(MembershipCodes.Forbidden, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(403, null)));
        Assert.Equal(MembershipCodes.AlreadyMember, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(400, "User is already a member of this organization")));
        Assert.Equal(MembershipCodes.Pending, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(400, "An invitation has already been sent to this email")));
        Assert.Equal(MembershipCodes.Invalid, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(400, null)));
        Assert.Equal(MembershipCodes.Unavailable, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(0, null)));
        Assert.Equal(MembershipCodes.Unavailable, InvitationRules.InviteFailureCode(Answer<InvitationSummary>(503, null)));
    }

    [Fact]
    public void A_refused_accept_maps_to_the_reason_authservice_gave()
    {
        Assert.Equal(MembershipCodes.VerifyEmail, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(403, "Verify your email address before accepting an organization invitation.")));
        Assert.Equal(MembershipCodes.NotFound, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(404, "Invitation not found")));
        Assert.Equal(MembershipCodes.Expired, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(400, "Invitation has expired")));
        Assert.Equal(MembershipCodes.Used, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(400, "Invitation has already been accepted")));
        Assert.Equal(MembershipCodes.Invalid, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(400, "something else")));
        Assert.Equal(MembershipCodes.Unavailable, InvitationRules.AcceptFailureCode(Answer<AcceptedInvitation>(500, null)));
    }

    [Fact]
    public void Every_code_a_redirect_can_carry_has_a_message()
    {
        var codes = typeof(MembershipCodes).GetFields().Select(field => (string)field.GetRawConstantValue()!).ToArray();

        foreach (var code in codes)
        {
            Assert.NotNull(MembershipMessages.For(code, code) ?? MembershipMessages.For(code, null) ?? MembershipMessages.For(null, code));
        }
    }

    [Fact]
    public void Error_codes_are_alerts_and_notice_codes_are_not()
    {
        Assert.Equal(("Only an Owner or an Admin can invite members.", true), MembershipMessages.For(MembershipCodes.Forbidden, null));
        Assert.Equal(("You have joined the workspace.", false), MembershipMessages.For(null, MembershipCodes.Joined));
        Assert.Null(MembershipMessages.For(null, null));
        Assert.Null(MembershipMessages.For("not-a-code", "not-a-notice"));
    }

    private static OrganizationAnswer<T> Answer<T>(int status, string? error) =>
        new(status is >= 200 and < 300 ? AuthOutcome.Succeeded : AuthOutcome.Rejected, status, default, error);
}
