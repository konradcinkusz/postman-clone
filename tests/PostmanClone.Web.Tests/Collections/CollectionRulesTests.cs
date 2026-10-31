using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>The creator-or-Owner-or-Admin rule, the user id reading, the page's words for each outcome, and the issue note.</summary>
public sealed class CollectionRulesTests
{
    [Theory]
    [InlineData(WorkspaceRoles.Owner, false)]
    [InlineData(WorkspaceRoles.Admin, false)]
    [InlineData(WorkspaceRoles.Member, true)]
    public void The_creator_may_delete_and_an_owner_or_admin_may_delete_anything(string role, bool creatorOnly)
    {
        var creator = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        Assert.Equal(!creatorOnly, CollectionRules.CanDeleteCollection(role, creator, stranger));
        Assert.True(CollectionRules.CanDeleteCollection(role, creator, creator));
    }

    [Fact]
    public void Without_a_known_user_nobody_is_the_creator()
    {
        Assert.False(CollectionRules.CanDeleteCollection(WorkspaceRoles.Member, Guid.NewGuid(), userId: null));
        Assert.False(CollectionRules.CanDeleteCollection(null, Guid.NewGuid(), userId: null));
    }

    [Fact]
    public void The_user_id_is_read_as_a_guid_and_a_value_that_is_not_one_matches_no_creator()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, CollectionRules.UserIdOf(new UserInfoResponse(id.ToString(), "a@example.test", [])));
        Assert.Null(CollectionRules.UserIdOf(new UserInfoResponse("not-a-guid", "a@example.test", [])));
        Assert.Null(CollectionRules.UserIdOf(null));
    }

    [Theory]
    [InlineData(CollectionOutcome.Unavailable, null, "did not answer")]
    [InlineData(CollectionOutcome.NotFound, "not_found", "no longer exists")]
    [InlineData(CollectionOutcome.Forbidden, "forbidden", "do not have permission")]
    [InlineData(CollectionOutcome.Conflict, "conflict", "limit was reached")]
    [InlineData(CollectionOutcome.Invalid, "depth_exceeded", "eight levels deep")]
    [InlineData(CollectionOutcome.Invalid, "cycle_detected", "into itself")]
    [InlineData(CollectionOutcome.Invalid, "validation_failed", "not valid")]
    [InlineData(CollectionOutcome.RateLimited, "rate_limited", "Too many changes")]
    [InlineData(CollectionOutcome.Refused, "other", "was not saved")]
    public void Each_outcome_has_a_plain_message_that_names_no_server_detail(CollectionOutcome outcome, string? code, string expected)
    {
        var text = CollectionMessages.For(outcome, code);

        Assert.Contains(expected, text);
        Assert.DoesNotContain("upstream", text);
        Assert.DoesNotContain("_", text);
    }

    [Fact]
    public void The_issue_note_says_how_many_were_moved_and_how_many_were_left_out()
    {
        var issues = new List<TreeIssue>
        {
            new(TreeIssueKind.Orphan, Guid.NewGuid(), "a"),
            new(TreeIssueKind.Cycle, Guid.NewGuid(), "b"),
            new(TreeIssueKind.Foreign, Guid.NewGuid(), "c"),
        };

        var note = CollectionMessages.IssueNote(issues, "Billing");

        Assert.Contains("2 items are shown at the top of \"Billing\"", note);
        Assert.Contains("1 item was left out", note);
    }

    [Fact]
    public void A_single_moved_item_is_said_in_the_singular()
    {
        var note = CollectionMessages.IssueNote([new TreeIssue(TreeIssueKind.Orphan, Guid.NewGuid(), "a")], "Billing");

        Assert.Contains("1 item is shown at the top", note);
    }
}
