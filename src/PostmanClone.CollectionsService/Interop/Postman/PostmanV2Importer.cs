using System.Text;
using System.Text.Json;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Services;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Reads a Postman Collection v2 file into an <see cref="ImportPlan"/> (ADR-0016). The file is untrusted: nothing in it is run,
/// fetched or evaluated, and no network is used. The size is checked by the caller before this runs; here the UTF-8 text,
/// the JSON nesting (64), the folder nesting (20), the item count (5500) and every request rule are checked, and a broken rule
/// refuses the whole file. The result is a plan only; the service writes it in one transaction.
/// </summary>
public static class PostmanV2Importer
{
    private const int CollectionNameMaxLength = 100;

    private const int FolderNameMaxLength = 100;

    private const int RequestNameMaxLength = 200;

    private const string SchemaMarker = "getpostman.com/json/collection/v2";

    public static Outcome<ImportPlan> Parse(ReadOnlyMemory<byte> file)
    {
        try
        {
            return Read(file);
        }
        catch (ImportRefusal refusal)
        {
            return refusal.Failure;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
        {
            // A defence in depth: a member the walker did not expect. The answer is the refusal, never a server error.
            return ImportRefusal.Invalid("$", "The file could not be read as a Postman collection.").Failure;
        }
    }

    private static Outcome<ImportPlan> Read(ReadOnlyMemory<byte> file)
    {
        var bytes = StripByteOrderMark(file);
        if (!IsUtf8(bytes.Span))
        {
            throw ImportRefusal.Invalid("$", "The file is not UTF-8 text.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = PostmanImportLimits.MaxJsonDepth });
        }
        catch (JsonException)
        {
            throw ImportRefusal.Invalid("$", "The file is not valid JSON, or it nests too deeply.");
        }

        using (document)
        {
            return Build(document.RootElement);
        }
    }

    private static Outcome<ImportPlan> Build(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw ImportRefusal.Invalid("$", "The file must hold a JSON object.");
        }

        var session = new ImportSession();
        var info = PostmanJson.Object(root, "info", string.Empty);
        var schema = info is { } infoObject ? PostmanJson.Text(infoObject, "schema", "info") : null;
        if (schema is not null && !schema.Contains(SchemaMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw ImportRefusal.Invalid("info.schema", "Only Postman Collection v2 files can be imported.");
        }

        var name = session.Name(
            info is { } named ? PostmanJson.Text(named, "name", "info") : null,
            "info.name",
            "Imported collection",
            CollectionNameMaxLength);
        var description = session.Description(
            info is { } described ? PostmanJson.Description(described, "description") : null,
            "info.description");

        var items = PostmanJson.Array(root, "item", string.Empty)
            ?? throw ImportRefusal.Invalid("item", "The collection needs an item array.");

        NoteScriptsAndVariables(root, string.Empty, session);
        NoteOwnAuth(root, string.Empty, session, "collection");
        WalkItems(items, "item", 0, null, session);
        return Outcome<ImportPlan>.Ok(session.Finish(name, description));
    }

    /// <summary>The folders and requests of one level. <paramref name="level"/> is the folder level the level sits in (0 at the top).</summary>
    private static void WalkItems(JsonElement items, string arrayPath, int level, int? parentKey, ImportSession session)
    {
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var itemPath = $"{arrayPath}[{index}]";
            index++;
            session.CountItem();
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw ImportRefusal.Invalid(itemPath, "Each item must be an object.");
            }

            if (PostmanJson.Array(item, "item", itemPath) is { } children)
            {
                WalkFolder(item, children, itemPath, level, parentKey, session);
            }
            else if (PostmanJson.TryGet(item, "request", out var request))
            {
                WalkRequest(item, request, itemPath, parentKey, session);
            }
            else
            {
                session.Warn("item_skipped", "This item holds neither a folder nor a request, so it was skipped.", itemPath);
            }
        }
    }

    private static void WalkFolder(JsonElement item, JsonElement children, string itemPath, int level, int? parentKey, ImportSession session)
    {
        var childLevel = level + 1;
        if (childLevel > PostmanImportLimits.MaxFolderNesting)
        {
            throw ImportRefusal.Invalid(itemPath, $"Folders nest at most {PostmanImportLimits.MaxFolderNesting} levels deep.");
        }

        var name = session.Name(PostmanJson.Text(item, "name", itemPath), $"{itemPath}.name", "Folder", FolderNameMaxLength);
        NoteScriptsAndVariables(item, itemPath, session);
        NoteOwnAuth(item, itemPath, session, "folder");

        // A folder deeper than the model allows is not created: its contents go into the folder above it (the level the model allows).
        var folderKey = parentKey;
        if (childLevel <= CollectionLimits.MaxFolderDepth)
        {
            folderKey = session.AddFolder(parentKey, name);
        }
        else
        {
            session.Warn(
                "depth_flattened",
                $"This folder is nested deeper than {CollectionLimits.MaxFolderDepth} levels, so its contents were imported into the folder above it.",
                itemPath);
        }

        WalkItems(children, $"{itemPath}.item", childLevel, folderKey, session);
    }

    private static void WalkRequest(JsonElement item, JsonElement request, string itemPath, int? folderKey, ImportSession session)
    {
        NoteScriptsAndVariables(item, itemPath, session);
        var name = session.Name(PostmanJson.Text(item, "name", itemPath), $"{itemPath}.name", "Untitled request", RequestNameMaxLength);
        var described = PostmanJson.Description(item, "description");
        if (described is null && request.ValueKind == JsonValueKind.Object)
        {
            described = PostmanJson.Description(request, "description");
        }

        var description = session.Description(described, $"{itemPath}.description");
        var definition = new PostmanRequestReader(session).Read(request, $"{itemPath}.request", name);
        session.AddRequest(folderKey, name, description, definition, itemPath);
    }

    /// <summary>Scripts and variables are never run or stored. Each one is named by a warning, so nothing is dropped silently.</summary>
    private static void NoteScriptsAndVariables(JsonElement item, string path, ImportSession session)
    {
        if (PostmanJson.TryGet(item, "event", out _))
        {
            session.Warn("scripts_ignored", "Scripts are not imported, and they are never run.", Join(path, "event"));
        }

        if (PostmanJson.TryGet(item, "variable", out _))
        {
            session.Warn("variables_ignored", "Variables are not imported. Use environment variables instead.", Join(path, "variable"));
        }
    }

    /// <summary>Auth set on a folder or on the collection is not applied; the warning says so, and the value is never read.</summary>
    private static void NoteOwnAuth(JsonElement item, string path, ImportSession session, string scope)
    {
        if (!PostmanJson.TryGet(item, "auth", out var auth) || auth.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var authPath = Join(path, "auth");
        var type = PostmanJson.Text(auth, "type", authPath) ?? "noauth";
        if (type != "noauth")
        {
            session.Warn("unsupported_auth", $"Auth set on the {scope} is not imported. Set it on each request.", authPath);
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static ReadOnlyMemory<byte> StripByteOrderMark(ReadOnlyMemory<byte> file) =>
        file.Length >= 3 && file.Span[0] == 0xEF && file.Span[1] == 0xBB && file.Span[2] == 0xBF ? file[3..] : file;

    private static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
