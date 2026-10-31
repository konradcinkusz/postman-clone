using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The Copy as cURL page (WP23 acceptance, bUnit): the command the service renders is shown as text in a read-only block, and a
/// request that is not in this workspace is the same "no longer exists" answer as an absent one (CONTRACTS §0).
/// </summary>
public sealed class CollectionCurlPageTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_command_is_shown_as_text_in_a_read_only_block_and_its_markup_is_escaped()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var request = fake.AddRequest(collection, "Hostile", method: "GET");
        fake.CurlText = "curl '<script>alert(1)</script>' https://api.example.test/items";
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<CollectionCurl>(parameters => parameters.Add(page => page.RequestId, request));

        cut.WaitForState(() => cut.FindAll("[data-testid='curl-command']").Count == 1);
        Assert.Equal(fake.CurlText, cut.Find("[data-testid='curl-command']").TextContent);
        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;", cut.Find("[data-testid='curl-command']").InnerHtml);
    }

    [Fact]
    public async Task Scoping_GET_requests_id_curl_is_not_found_for_a_request_of_another_workspace()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var ours = new FakeCollectionsService(Guid.NewGuid());
        ours.AddCollection("Billing");
        var theirs = new FakeCollectionsService(Guid.NewGuid());
        var theirCollection = theirs.AddCollection("Their billing");
        var theirRequest = theirs.AddRequest(theirCollection, "Theirs");
        using var context = CollectionsPageHarness.Create(harness, principal, ours);

        var cut = context.Render<CollectionCurl>(parameters => parameters.Add(page => page.RequestId, theirRequest));

        cut.WaitForState(() => cut.FindAll("[data-testid='curl-error']").Count == 1);
        Assert.Contains("no longer exists", cut.Find("[data-testid='curl-error']").TextContent);
        Assert.Equal("alert", cut.Find("[data-testid='curl-error']").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[data-testid='curl-command']"));
    }

    [Fact]
    public async Task An_unavailable_service_is_said_in_words_and_no_command_is_shown()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid()) { Unavailable = true };
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<CollectionCurl>(parameters => parameters.Add(page => page.RequestId, Guid.NewGuid()));

        cut.WaitForState(() => cut.FindAll("[data-testid='curl-error']").Count == 1);
        Assert.Contains("did not answer", cut.Find("[data-testid='curl-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='curl-command']"));
    }
}
