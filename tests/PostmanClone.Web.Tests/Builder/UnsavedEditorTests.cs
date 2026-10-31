using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;
using PostmanClone.Web.Tests.History;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>
/// A draft opened from history is not a saved request (WP22): it has no version and no collection, it is dirty from the start so its
/// tab asks before it is lost, and it keeps the definition it was opened with. A saved request is clean when it is read.
/// </summary>
public sealed class UnsavedEditorTests
{
    private static RequestDefinition Definition(string url) => new(
        "POST",
        url,
        [],
        [new KeyValue("Accept", "application/json")],
        new RequestBody("raw", "json", "{\"a\":1}", []),
        new RequestAuth("none", null, null, null, null, null, null));

    [Fact]
    public void An_unsaved_draft_is_not_a_saved_request_and_is_dirty_from_the_start()
    {
        var localId = Guid.NewGuid();

        var editor = RequestEditor.Unsaved(localId, "From history", Definition("https://api.example.test/items"));

        Assert.False(editor.IsSaved);
        Assert.True(editor.IsDirty);
        Assert.Equal(localId, editor.RequestId);
        Assert.Equal(Guid.Empty, editor.CollectionId);
        Assert.Equal("From history", editor.Name);
    }

    [Fact]
    public void An_unsaved_draft_keeps_the_definition_it_was_opened_with()
    {
        var definition = Definition("https://api.example.test/items/{{itemId}}");

        var editor = RequestEditor.Unsaved(Guid.NewGuid(), "From history", definition);

        Assert.Equal("https://api.example.test/items/{{itemId}}", editor.Draft.ToDefinition().Url);
        Assert.Equal("POST", editor.Draft.ToDefinition().Method);
    }

    [Fact]
    public void A_saved_request_is_clean_when_it_is_read()
    {
        var saved = new SavedRequestDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Items",
            null,
            Definition("https://api.example.test/items"),
            0,
            3,
            Guid.NewGuid(),
            default,
            default);

        var editor = new RequestEditor(saved);

        Assert.True(editor.IsSaved);
        Assert.False(editor.IsDirty);
        Assert.Equal(3, editor.Version);
    }
}
