using System.Text;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// The Postman files the tests import. The realistic ones are committed under <c>Fixtures/postman</c>. The hostile ones are
/// built here, because some are 10 MB or more and a committed file that size would only slow the repository down. The literal
/// secrets are built here too, so that no secret-shaped value sits in a committed file (ADR-0006).
/// </summary>
public static class PostmanFixtures
{
    /// <summary>The realistic fixtures, each one a file under <c>Fixtures/postman</c>.</summary>
    public static readonly string[] Realistic =
    [
        "minimal.json",
        "nested-folders.json",
        "body-modes.json",
        "auth-types.json",
        "scripts-and-variables.json",
    ];

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "postman", name));

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private const string SchemaV21 = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    /// <summary>The file with a given body of items, the rest of the envelope being valid.</summary>
    public static byte[] Collection(string itemsJson) =>
        Utf8("{\"info\":{\"name\":\"Generated\",\"schema\":\"" + SchemaV21 + "\"},\"item\":" + itemsJson + "}");

    /// <summary>A JSON array nested <paramref name="depth"/> levels deep, each level an object with one array member.</summary>
    public static byte[] NestedFolders(int depth)
    {
        var builder = new StringBuilder();
        builder.Append("{\"info\":{\"name\":\"Deep\"},\"item\":");
        for (var level = 0; level < depth; level++)
        {
            builder.Append("[{\"name\":\"Level ").Append(level).Append("\",\"item\":");
        }

        builder.Append("[]");
        for (var level = 0; level < depth; level++)
        {
            builder.Append("}]");
        }

        builder.Append('}');
        return Utf8(builder.ToString());
    }

    /// <summary>A file of <paramref name="count"/> one-line requests, each a small valid request. Its size is about 35 bytes an item.</summary>
    public static byte[] ManyRequests(int count)
    {
        var builder = new StringBuilder();
        builder.Append("{\"info\":{\"name\":\"Many\"},\"item\":[");
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"name\":\"r\",\"request\":\"https://x.invalid\"}");
        }

        builder.Append("]}");
        return Utf8(builder.ToString());
    }

    /// <summary>A file of at least <paramref name="bytes"/> bytes, which is refused by size before it is read.</summary>
    public static byte[] Oversized(int bytes)
    {
        var padding = new string(' ', bytes);
        return Utf8("{\"info\":{\"name\":\"Big\"},\"item\":[]}" + padding);
    }

    /// <summary>A request whose URL is <paramref name="length"/> characters long, which the validator refuses (CONTRACTS §1).</summary>
    public static byte[] GiantUrl(int length) =>
        Collection("[{\"name\":\"Giant\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/" + new string('a', length) + "\"}}]");

    /// <summary>A request whose name is <paramref name="length"/> characters long. A name is cut to its limit, not refused.</summary>
    public static byte[] GiantName(int length) =>
        Collection("[{\"name\":\"" + new string('n', length) + "\",\"request\":\"https://example.invalid\"}]");
}
