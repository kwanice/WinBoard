using System.Text.Json;

namespace WinBoard.Core;

/// <summary>One clipboard excerpt from MyClipboard's local integration file.</summary>
public sealed record ParsedClip(string Id, string Text, string Type, long UpdatedAtMs);

/// <summary>Schema version 1 document (<c>%LOCALAPPDATA%\MyClipBoard\integration\clips.json</c>).</summary>
public sealed record ParsedClipFile(
    int Version,
    long UpdatedAtMs,
    bool Authorized,
    IReadOnlyList<ParsedClip> Clips);

public enum ClipFileStatus
{
    Missing,
    Unauthorized,
    Empty,
    Invalid,
    Ready,
}

/// <summary>
/// Paths and protocol for the MyClipboard ↔ WinBoard contract (local file, no SQLite, no network).
/// WinBoard only reads; MyClipboard creates parent directories when it writes.
/// </summary>
public static class MyClipboardContract
{
    public const int SchemaVersion = 1;

    public const string ProtocolUri = "myclipboard://authorize-winboard";

    public const string FolderName = "MyClipBoard";

    public const string IntegrationFolder = "integration";

    public const string FileName = "clips.json";

    public static string GetPreferredPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName,
            IntegrationFolder,
            FileName);

    public static string GetPreferredDirectory() =>
        Path.GetDirectoryName(GetPreferredPath()) ?? GetPreferredPath();
}

/// <summary>
/// Parser for MyClipboard integration dumps (local file, no network).
/// Canonical schema is version 1; a few aliases remain so older dumps still parse.
/// </summary>
public static class ClipboardClipParser
{
    public static ParsedClipFile? ParseFile(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !LooksLikeJson(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                if (root.ValueKind == JsonValueKind.Array)
                {
                    return new ParsedClipFile(0, 0, Authorized: false, ParseClipArray(root, maxCount: 64));
                }

                return null;
            }

            int version = (int)ReadInt64(root, "version", "Version");
            long updatedAtMs = ReadInt64(root, "updatedAtMs", "UpdatedAtMs");
            bool authorized = ReadAuthorized(root);

            if (!TryGetArray(root, "clips", out JsonElement array)
                && !TryGetArray(root, "Clips", out array)
                && !TryGetArray(root, "items", out array)
                && !TryGetArray(root, "Items", out array))
            {
                return new ParsedClipFile(version, updatedAtMs, authorized, []);
            }

            IReadOnlyList<ParsedClip> clips = ParseClipArray(array, maxCount: 64);
            if (updatedAtMs == 0 && clips.Count > 0)
            {
                updatedAtMs = clips.Max(c => c.UpdatedAtMs);
            }

            return new ParsedClipFile(version, updatedAtMs, authorized, clips);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ParsedClip> Parse(string json, int maxCount)
    {
        ParsedClipFile? file = ParseFile(json);
        if (file is null)
        {
            return [];
        }

        return file.Clips.Take(Math.Max(0, maxCount)).ToArray();
    }

    public static bool LooksLikeJson(string json)
    {
        ReadOnlySpan<char> trim = json.AsSpan().Trim();
        return trim.Length > 0 && trim[0] is '{' or '[';
    }

    private static IReadOnlyList<ParsedClip> ParseClipArray(JsonElement array, int maxCount)
    {
        var clips = new List<ParsedClip>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string raw = item.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    clips.Add(new ParsedClip(Guid.NewGuid().ToString("n"), raw, "text", 0));
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
            string type = ReadString(item, "type", "Type") ?? "text";
            long updatedAtMs = ReadInt64(item, "updatedAtMs", "UpdatedAtMs");
            if (updatedAtMs == 0)
            {
                string? ts = ReadString(item, "timestamp", "Timestamp", "time", "Time", "created", "Created");
                if (ts is not null && DateTimeOffset.TryParse(ts, out DateTimeOffset parsed))
                {
                    updatedAtMs = parsed.ToUnixTimeMilliseconds();
                }
            }

            clips.Add(new ParsedClip(id, text, type, updatedAtMs));
        }

        return clips
            .OrderByDescending(c => c.UpdatedAtMs)
            .Take(maxCount)
            .ToArray();
    }

    private static bool ReadAuthorized(JsonElement root)
    {
        if (!root.TryGetProperty("authorized", out JsonElement el)
            && !root.TryGetProperty("Authorized", out el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when el.TryGetInt64(out long n) => n != 0,
            JsonValueKind.String when bool.TryParse(el.GetString(), out bool flag) => flag,
            _ => false,
        };
    }

    private static long ReadInt64(JsonElement obj, params string[] names)
    {
        foreach (string name in names)
        {
            if (!obj.TryGetProperty(name, out JsonElement el))
            {
                continue;
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long n))
            {
                return n;
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double d))
            {
                return (long)d;
            }

            if (el.ValueKind == JsonValueKind.String
                && long.TryParse(el.GetString(), out long parsed))
            {
                return parsed;
            }
        }

        return 0;
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
