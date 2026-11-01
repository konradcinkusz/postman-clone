using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// WP24 and WP18 together: an owner invites a member, the member accepts, and the member sees the owner's collection in the same
/// workspace. The invitation token reaches the test through authservice's log, which holds it only when the stack runs with
/// AUTHSERVICE_ENVIRONMENT=Development (CI and scripts/run-local-stack.sh set it for the E2E run; docs/TESTING.md §1).
/// </summary>
public sealed class SharedCollectionJourneys : Journey
{
    [Fact]
    public async Task A_member_who_accepts_an_invitation_sees_the_owners_collection_in_the_workspace()
    {
        var owner = await TestUsers.CreateAsync();
        // The invitee has no organization yet: the invitation is what gives it one.
        var invitee = await TestUsers.CreateAsync(organizations: 0);
        var collection = $"Shared {Guid.NewGuid():N}";

        var ownerPage = await NewPageAsync();
        await Ui.SignInAsync(ownerPage, owner.Email, owner.Password);
        await Workspace.CreateCollectionAsync(ownerPage, collection);

        await Ui.OpenAsync(ownerPage, "/workspace");
        await ownerPage.GetByTestId("invite-email").FillAsync(invitee.Email);
        await ownerPage.GetByTestId("invite-role").SelectOptionAsync("Member");
        await ownerPage.GetByTestId("invite-submit").ClickAsync();
        await Expect(ownerPage.GetByTestId("invitations-table")).ToContainTextAsync(invitee.Email);

        var token = await Poll.UntilAsync(
            () => Task.FromResult(InvitationLog.TokenFor(invitee.Email)),
            "the invitation token in authservice's log (start the stack with AUTHSERVICE_ENVIRONMENT=Development)",
            TestContext.Current.CancellationToken);

        var inviteePage = await NewPageAsync();
        await Ui.SignInAsync(inviteePage, invitee.Email, invitee.Password);
        await Ui.OpenAsync(inviteePage, "/accept-invitation?token=" + Uri.EscapeDataString(token));
        await inviteePage.GetByTestId("accept-submit").ClickAsync();
        // The answer is a redirect: a success goes to the workspace with notice=joined, a failure back to the page with error=.
        await inviteePage.WaitForURLAsync(new Regex("[?&](notice|error)="));
        Assert.Contains("notice=joined", inviteePage.Url, StringComparison.Ordinal);

        // The invitee's session was signed in before the membership existed: the Web refreshes it on accept, so the home page
        // now shows the workspace and its collections.
        await Ui.OpenAsync(inviteePage, "/");
        await Expect(inviteePage.GetByTestId("tree")).ToContainTextAsync(collection);
    }
}
