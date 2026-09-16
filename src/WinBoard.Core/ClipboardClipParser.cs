using System.Text.Json;

namespace WinBoard.Core;

/// <summary>One clipboard excerpt from MyClipboard Desktop's integration file.</summary>
public sealed record ParsedClip(string Id, string Text, string Type, long UpdatedAtMs);

/// <summary>
/// Schema version 1 document at
/// <c>%LOCALAPPDATA%\MyClipBoard\integration\clips.json</c> (exact casing).
/// </summary>
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
/// Frozen MyClipboard Desktop ↔ WinBoard contract (MCB_App PR #6).
/// Local file only: no SQLite, no network, no alternate folder spellings.
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

    /// <summary>
    /// True only if the file exists and every path segment matches
    /// <paramref name="expectedPath"/> with ordinal casing (rejects
    /// <c>MyClipboard</c> when the contract folder is <c>MyClipBoard</c>).
    /// </summary>
    public static bool ExistsWithExactCasing(string expectedPath)
    {
        try
        {
            if (string.IsNullOrEmpty(expectedPath) || !File.Exists(expectedPath))
            {
                return false;
            }

            string full = Path.GetFullPath(expectedPath);
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            string current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (current.Length == 0)
            {
                current = root;
            }

            string remainder = full[root.Length..];
            foreach (string part in remainder.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                string? match = Directory
                    .EnumerateFileSystemEntries(current)
                    .FirstOrDefault(entry =>
                        string.Equals(Path.GetFileName(entry), part, StringComparison.Ordinal));
                if (match is null)
                {
                    return false;
                }

                current = match;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Strict parser for MyClipboard Desktop integration dumps (schema v1, camelCase only).
/// </summary>
public static class ClipboardClipParser
{
    public static ParsedClipFile? ParseFile(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !LooksLikeObject(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!TryReadInt(root, "version", out int version)
                || version != MyClipboardContract.SchemaVersion
                || !TryReadInt64(root, "updatedAtMs", out long updatedAtMs)
                || !TryReadBool(root, "authorized", out bool authorized)
                || !root.TryGetProperty("clips", out JsonElement clipsEl)
                || clipsEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            IReadOnlyList<ParsedClip> clips = authorized
                ? ParseClipArray(clipsEl)
                : [];

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

    private static bool LooksLikeObject(string json)
    {
        ReadOnlySpan<char> trim = json.AsSpan().Trim();
        return trim.Length > 0 && trim[0] == '{';
    }

    private static IReadOnlyList<ParsedClip> ParseClipArray(JsonElement array)
    {
        var clips = new List<ParsedClip>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryReadString(item, "id", out string id)
                || !TryReadString(item, "text", out string text)
                || !TryReadString(item, "type", out string type)
                || !TryReadInt64(item, "updatedAtMs", out long updatedAtMs)
                || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            clips.Add(new ParsedClip(id, text, type, updatedAtMs));
        }

        return clips
            .OrderByDescending(c => c.UpdatedAtMs)
            .ToArray();
    }

    private static bool TryReadBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (!obj.TryGetProperty(name, out JsonElement el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (el.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryReadInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!TryReadInt64(obj, name, out long n) || n < int.MinValue || n > int.MaxValue)
        {
            return false;
        }

        value = (int)n;
        return true;
    }

    private static bool TryReadInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement el) || el.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (el.TryGetInt64(out value))
        {
            return true;
        }

        if (el.TryGetDouble(out double d))
        {
            value = (long)d;
            return true;
        }

        return false;
    }

    private static bool TryReadString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = el.GetString() ?? string.Empty;
        return true;
    }
}
