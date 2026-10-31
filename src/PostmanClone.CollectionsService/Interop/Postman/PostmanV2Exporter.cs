using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Writes a collection as a Postman Collection v2.1 file (CONTRACTS §2). The output is deterministic: folders and requests
/// follow their sort orders and then their ids, and the collection id is the <c>_postman_id</c>. The reader in
/// <see cref="PostmanV2Importer"/> reads this file back into the same structure, which is what the round-trip tests check.
/// The query parameters are written into <c>raw</c> after the URL, and the <c>query</c> list carries the same rows, so Postman
/// and this importer both see them. A <c>{{variable}}</c> is written as it is.
/// </summary>
public static class PostmanV2Exporter
{
    public const string Schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    public static byte[] Export(Collection collection, IReadOnlyList<Folder> folders, IReadOnlyList<SavedRequest> requests)
    {
        var folderChildren = folders.ToLookup(folder => folder.ParentFolderId);
        var slotRequests = requests.ToLookup(request => request.FolderId);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("info");
            writer.WriteString("_postman_id", collection.Id);
            writer.WriteString("name", collection.Name);
            if (collection.Description is { } description)
            {
                writer.WriteString("description", description);
            }

            writer.WriteString("schema", Schema);
            writer.WriteEndObject();

            writer.WriteStartArray("item");
            WriteLevel(writer, null, folderChildren, slotRequests);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The folders of one level, each with its own contents, then the requests of the level.</summary>
    private static void WriteLevel(Utf8JsonWriter writer, Guid? parentId, ILookup<Guid?, Folder> folderChildren, ILookup<Guid?, SavedRequest> slotRequests)
    {
        foreach (var folder in folderChildren[parentId].OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.Id))
        {
            writer.WriteStartObject();
            writer.WriteString("name", folder.Name);
            writer.WriteStartArray("item");
            WriteLevel(writer, folder.Id, folderChildren, slotRequests);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        foreach (var request in slotRequests[parentId].OrderBy(request => request.SortOrder).ThenBy(request => request.Id))
        {
            WriteRequest(writer, request);
        }
    }

    private static void WriteRequest(Utf8JsonWriter writer, SavedRequest saved)
    {
        var definition = saved.Definition.RootElement.Deserialize<RequestDefinition>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("A stored request has no definition.");

        writer.WriteStartObject();
        writer.WriteString("name", saved.Name);
        if (saved.Description is { } description)
        {
            writer.WriteString("description", description);
        }

        writer.WriteStartObject("request");
        writer.WriteString("method", definition.Method);
        WriteRows(writer, "header", definition.Headers);
        WriteUrl(writer, definition);
        WriteBody(writer, definition.Body);
        WriteAuth(writer, definition.Auth);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteUrl(Utf8JsonWriter writer, RequestDefinition definition)
    {
        var enabled = (definition.Params ?? Array.Empty<KeyValue>()).Where(row => row.Enabled).ToList();
        var raw = definition.Url;
        if (enabled.Count > 0)
        {
            var separator = definition.Url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            raw = definition.Url + separator + string.Join("&", enabled.Select(row => row.Key + "=" + row.Value));
        }

        writer.WriteStartObject("url");
        writer.WriteString("raw", raw);
        WriteRows(writer, "query", definition.Params);
        writer.WriteEndObject();
    }

    private static void WriteBody(Utf8JsonWriter writer, RequestBody? body)
    {
        if (body is null)
        {
            return;
        }

        switch (body.Type)
        {
            case "raw":
                writer.WriteStartObject("body");
                writer.WriteString("mode", "raw");
                writer.WriteString("raw", body.Raw ?? string.Empty);
                writer.WriteStartObject("options");
                writer.WriteStartObject("raw");
                writer.WriteString("language", body.RawLanguage ?? "text");
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                break;

            case "formUrlEncoded":
                writer.WriteStartObject("body");
                writer.WriteString("mode", "urlencoded");
                WriteRows(writer, "urlencoded", body.Fields);
                writer.WriteEndObject();
                break;

            case "formData":
                writer.WriteStartObject("body");
                writer.WriteString("mode", "formdata");
                WriteRows(writer, "formdata", body.Fields, text: true);
                writer.WriteEndObject();
                break;
        }
    }

    private static void WriteAuth(Utf8JsonWriter writer, RequestAuth? auth)
    {
        var value = auth ?? new RequestAuth("none", null, null, null, null, null, null);
        writer.WriteStartObject("auth");
        switch (value.Type)
        {
            case "bearer":
                writer.WriteString("type", "bearer");
                writer.WriteStartArray("bearer");
                WriteValue(writer, "token", value.BearerToken, "string");
                writer.WriteEndArray();
                break;

            case "basic":
                writer.WriteString("type", "basic");
                writer.WriteStartArray("basic");
                WriteValue(writer, "username", value.Username, "string");
                WriteValue(writer, "password", value.Password, "string");
                writer.WriteEndArray();
                break;

            case "apiKey":
                writer.WriteString("type", "apikey");
                writer.WriteStartArray("apikey");
                WriteValue(writer, "key", value.ApiKeyName, "string");
                WriteValue(writer, "value", value.ApiKeyValue, "string");
                WriteValue(writer, "in", value.ApiKeyIn ?? "header", "string");
                writer.WriteEndArray();
                break;

            default:
                writer.WriteString("type", "noauth");
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, string key, string? value, string type)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteString("value", value ?? string.Empty);
        writer.WriteString("type", type);
        writer.WriteEndObject();
    }

    private static void WriteRows(Utf8JsonWriter writer, string name, IReadOnlyList<KeyValue>? rows, bool text = false)
    {
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var row in rows)
        {
            writer.WriteStartObject();
            writer.WriteString("key", row.Key);
            writer.WriteString("value", row.Value);
            if (text)
            {
                writer.WriteString("type", "text");
            }

            if (!row.Enabled)
            {
                writer.WriteBoolean("disabled", true);
            }

            if (row.Description is { } description)
            {
                writer.WriteString("description", description);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
