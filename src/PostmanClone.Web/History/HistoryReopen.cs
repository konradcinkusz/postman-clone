using System.Text.Json;
using System.Text.Json.Nodes;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.History;

/// <summary>
/// The rules for re-opening a history entry as a new draft (WP22, ADR-0015). A stored request is the masked template: a sensitive
/// value is <c>••••••</c>, and the secret itself was never stored. Re-opening keeps the mask where it was, so the page can see it
/// and refuse to send or save the mask as if it were the value. Plain logic, tested without a renderer.
/// </summary>
public static class HistoryReopen
{
    /// <summary>The mask ExecutionService writes in place of a secret (CONTRACTS §4, SecretMasker.Mask). Shown as text only.</summary>
    public const string Mask = "••••••";

    /// <summary>The longest tab title, in characters. A longer URL is cut with an ellipsis.</summary>
    public const int MaximumTitleLength = 80;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// How many string values of the definition still hold the mask, in any field: the URL, a row, the body or the auth helper.
    /// Zero means the draft is free of masked values. The walk is iterative, so a deep definition cannot exhaust the stack.
    /// </summary>
    public static int MaskedValueCount(RequestDefinition definition)
    {
        var count = 0;
        var pending = new Stack<JsonNode?>();
        pending.Push(JsonSerializer.SerializeToNode(definition, Json));
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject obj:
                    foreach (var child in obj.Select(pair => pair.Value))
                    {
                        pending.Push(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        pending.Push(child);
                    }

                    break;
                case JsonValue value when value.TryGetValue<string>(out var text) && text.Contains(Mask, StringComparison.Ordinal):
                    count++;
                    break;
            }
        }

        return count;
    }

    /// <summary>The tab title for a draft opened from history: its method and URL, cut to <see cref="MaximumTitleLength"/>.</summary>
    public static string TitleFor(string method, string url)
    {
        var title = $"{method} {url}";
        return title.Length <= MaximumTitleLength ? title : string.Concat(title.AsSpan(0, MaximumTitleLength - 1), "…");
    }
}
