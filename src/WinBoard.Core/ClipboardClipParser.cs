using System.Text.Json;

namespace WinBoard.Core;

/// <summary>One clipboard excerpt parsed from a local JSON dump.</summary>
public sealed record ParsedClip(string Id, string Text, DateTimeOffset Timestamp);

public enum ClipFileStatus
{
    Missing,
    Empty,
    Invalid,
    Ready,
}

/// <summary>
/// Schema-tolerant parser for MyClipboard dumps (local file, no network).
/// Accepts <c>{ "clips": [...] }</c>, <c>{ "items": [...] }</c>, or a root array.
/// Item text may be <c>text</c>, <c>Text</c>, <c>content</c>, or <c>Content</c>.
/// </summary>
public static class ClipboardClipParser
{
    public static IReadOnlyList<ParsedClip> Parse(string json, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(json) || maxCount <= 0)
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && (TryGetArray(root, "clips", out array)
                         || TryGetArray(root, "Clips", out array)
                         || TryGetArray(root, "items", out array)
                         || TryGetArray(root, "Items", out array)))
            {
                // array set
            }
            else
            {
                return [];
            }

            var clips = new List<ParsedClip>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string raw = item.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        clips.Add(new ParsedClip(Guid.NewGuid().ToString("n"), raw, DateTimeOffset.MinValue));
                    }

                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string text = ReadText(item);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string id = ReadString(item, "id", "Id") ?? Guid.NewGuid().ToString("n");
                DateTimeOffset timestamp = DateTimeOffset.MinValue;
                string? ts = ReadString(item, "timestamp", "Timestamp", "time", "Time", "created", "Created");
                if (ts is not null && DateTimeOffset.TryParse(ts, out DateTimeOffset parsed))
                {
                    timestamp = parsed;
                }

                clips.Add(new ParsedClip(id, text, timestamp));
            }

            return clips
                .OrderByDescending(c => c.Timestamp)
                .Take(maxCount)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool LooksLikeJson(string json)
    {
        ReadOnlySpan<char> trim = json.AsSpan().Trim();
        return trim.Length > 0 && trim[0] is '{' or '[';
    }

    private static bool TryGetArray(JsonElement obj, string name, out JsonElement array)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Array)
        {
            array = el;
            return true;
        }

        array = default;
        return false;
    }

    private static string ReadText(JsonElement item) =>
        ReadString(item, "text", "Text", "content", "Content", "clip", "Clip", "value", "Value")
        ?? string.Empty;

    private static string? ReadString(JsonElement item, params string[] names)
    {
        foreach (string name in names)
        {
            if (item.TryGetProperty(name, out JsonElement el)
                && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }

        return null;
    }
}
