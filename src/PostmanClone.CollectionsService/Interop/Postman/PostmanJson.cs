using System.Text.Json;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Reads one member of a JSON object in the file. A missing member and a null member are both absent. A member of the
/// wrong JSON type is refused with <c>import_invalid</c>, naming its path, so the walker never guesses what a value meant
/// (ADR-0016). Callers pass only objects here; the refusal covers every other kind.
/// </summary>
internal static class PostmanJson
{
    /// <summary>The path of a member, without a leading dot at the top of the file.</summary>
    public static string Child(string path, string name) => path.Length == 0 ? name : path + "." + name;

    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>A text member: a string, or a number or boolean as its JSON text. Absent or null gives null.</summary>
    public static string? Text(JsonElement obj, string name, string path)
    {
        return TryGet(obj, name, out var value) ? Scalar(value, Child(path, name)) : null;
    }

    /// <summary>A string, or a number or boolean as its JSON text. Any other kind is refused.</summary>
    public static string Scalar(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw ImportRefusal.Invalid(path, "This value must be text."),
    };

    /// <summary>A true-or-false member. Absent or null is false.</summary>
    public static bool Flag(JsonElement obj, string name, string path)
    {
        if (!TryGet(obj, name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw ImportRefusal.Invalid(Child(path, name), "This value must be true or false."),
        };
    }

    /// <summary>An object member, or null when it is absent. Any other kind is refused.</summary>
    public static JsonElement? Object(JsonElement obj, string name, string path)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw ImportRefusal.Invalid(Child(path, name), "This value must be an object.");
    }

    /// <summary>An array member, or null when it is absent. Any other kind is refused.</summary>
    public static JsonElement? Array(JsonElement obj, string name, string path)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw ImportRefusal.Invalid(Child(path, name), "This value must be an array.");
    }

    /// <summary>
    /// A member Postman allows as text or as a list of text parts (a host or a path). The parts are joined with
    /// <paramref name="separator"/>. Absent gives the empty text.
    /// </summary>
    public static string Parts(JsonElement obj, string name, string path, string separator)
    {
        if (!TryGet(obj, name, out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return Scalar(value, Child(path, name));
        }

        var parts = new List<string>();
        var index = 0;
        foreach (var part in value.EnumerateArray())
        {
            parts.Add(Scalar(part, Child(path, name) + $"[{index}]"));
            index++;
        }

        return string.Join(separator, parts);
    }

    /// <summary>
    /// A description: a string, or the <c>content</c> of Postman's description object. Any other shape is dropped, because a
    /// description carries no meaning the service keeps.
    /// </summary>
    public static string? Description(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                => content.GetString(),
            _ => null,
        };
    }
}
