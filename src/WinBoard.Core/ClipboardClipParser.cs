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
    /// Leaf segments that must match on-disk names with <see cref="StringComparison.Ordinal"/>.
    /// The LocalAppData prefix (<c>Users</c>/<c>Frank</c>/<c>AppData</c>/<c>Local</c>) is
    /// <em>not</em> compared ordinally — Windows often stores those with different casing
    /// than <see cref="Environment.GetFolderPath"/>.
    /// </summary>
    public static readonly string[] ContractSuffixSegments = [FolderName, IntegrationFolder, FileName];

    /// <summary>
    /// True when the last three path segments are exactly
    /// <c>MyClipBoard\integration\clips.json</c> (ordinal). Prefix casing is ignored.
    /// </summary>
    public static bool HasExactContractSuffix(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string[] parts = path.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < ContractSuffixSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < ContractSuffixSegments.Length; i++)
        {
            string actual = parts[parts.Length - ContractSuffixSegments.Length + i];
            if (!string.Equals(actual, ContractSuffixSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Walks only <see cref="ContractSuffixSegments"/> from an already-resolved
    /// LocalAppData (or test) base directory. Prefix folders are not enumerated.
    /// </summary>
    public static string? TryWalkContractSuffix(string baseDirectory)
    {
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return null;
        }

        string current = Path.GetFullPath(baseDirectory);
        foreach (string part in ContractSuffixSegments)
        {
            string? match = Directory
                .EnumerateFileSystemEntries(current)
                .FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry), part, StringComparison.Ordinal));
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return File.Exists(current) ? current : null;
    }

    /// <summary>
    /// Resolves the contract file if it exists. Uses <see cref="File.Exists"/> (and
    /// directory existence) for the LocalAppData prefix — case-insensitive on Windows —
    /// and ordinal names only for <c>MyClipBoard</c>, <c>integration</c>, <c>clips.json</c>.
    /// </summary>
    public static string? TryResolveContractFile(string expectedPath)
    {
        try
        {
            if (string.IsNullOrEmpty(expectedPath))
            {
                return null;
            }

            string full = Path.GetFullPath(expectedPath);
            if (!HasExactContractSuffix(full) || !File.Exists(full))
            {
                return null;
            }

            string? baseDir = Path.GetDirectoryName(
                Path.GetDirectoryName(Path.GetDirectoryName(full)));
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
            {
                // Prefix could not be opened as a directory (casing/ACL) but
                // File.Exists already succeeded and the suffix string is exact.
                return full;
            }

            try
            {
                return TryWalkContractSuffix(baseDir);
            }
            catch
            {
                return full;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True when the contract file exists. Does not require ordinal casing on
    /// LocalAppData prefix segments. Rejects <c>MyClipboard</c> when the folder
    /// on disk is not <c>MyClipBoard</c>.
    /// </summary>
    public static bool ExistsWithExactCasing(string expectedPath) =>
        TryResolveContractFile(expectedPath) is not null;

    /// <summary>
    /// Map existence + parse outcome to a UI status. A file that exists but
    /// fails schema parse is <see cref="ClipFileStatus.Invalid"/>, never Missing.
    /// </summary>
    public static ClipFileStatus Classify(bool contractResolved, ParsedClipFile? parsed)
    {
        if (!contractResolved)
        {
            return ClipFileStatus.Missing;
        }

        if (parsed is null)
        {
            return ClipFileStatus.Invalid;
        }

        if (!parsed.Authorized)
        {
            return ClipFileStatus.Unauthorized;
        }

        return parsed.Clips.Count == 0 ? ClipFileStatus.Empty : ClipFileStatus.Ready;
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
