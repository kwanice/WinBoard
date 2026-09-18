using WinBoard.Core;

namespace WinBoard.Services;

/// <summary>One clipboard item exposed to WinBoard (local only — never uploaded).</summary>
public sealed record ClipboardClip(string Id, string Text, string Type, long UpdatedAtMs)
{
    public string Preview
    {
        get
        {
            string oneLine = Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return oneLine.Length <= 48 ? oneLine : oneLine[..45] + "…";
        }
    }
}

public sealed record ClipSnapshot(
    ClipFileStatus Status,
    string PreferredPath,
    string? ResolvedPath,
    bool FileExists,
    string? ParseHint,
    long UpdatedAtMs,
    IReadOnlyList<ClipboardClip> Clips)
{
    public string Fingerprint =>
        $"{Status}|{UpdatedAtMs}|{Clips.Count}|{string.Join('\u001f', Clips.Select(c => c.Id + '=' + c.Text.Length))}";

    /// <summary>One-line debug for empty states: resolved path + File.Exists.</summary>
    public string DebugExistenceLine =>
        $"File.Exists={(FileExists ? "oui" : "non")}  {(ResolvedPath ?? PreferredPath)}";
}

/// <summary>
/// Local clip source. Implementations must stay on-device (file), never cloud.
/// </summary>
public interface IClipboardClipSource
{
    string DisplayName { get; }

    string PreferredPath { get; }

    string? ResolvedPath { get; }

    ClipFileStatus Status { get; }

    ClipSnapshot GetSnapshot(int maxCount);

    IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount) => GetSnapshot(maxCount).Clips;
}

/// <summary>
/// Reads MyClipboard Desktop's integration JSON (schema version 1).
/// Exact path: <c>%LOCALAPPDATA%\MyClipBoard\integration\clips.json</c>
/// (ordinal casing on the MyClipBoard suffix only — not MyClipboard).
/// LocalAppData prefix casing is resolved with File.Exists. WinBoard never writes this file.
/// </summary>
public sealed class FileClipboardClipSource : IClipboardClipSource
{
    public FileClipboardClipSource()
    {
        PreferredPath = MyClipboardContract.GetPreferredPath();
    }

    public string DisplayName => "MyClipboard";

    public string PreferredPath { get; }

    public string? ResolvedPath { get; private set; }

    public ClipFileStatus Status { get; private set; } = ClipFileStatus.Missing;

    public IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount) => GetSnapshot(maxCount).Clips;

    public ClipSnapshot GetSnapshot(int maxCount)
    {
        string path = PreferredPath;
        bool fileExists = File.Exists(path);
        string? resolved = MyClipboardContract.TryResolveContractFile(path);
        ResolvedPath = resolved;
        if (resolved is null)
        {
            Status = ClipFileStatus.Missing;
            return new ClipSnapshot(Status, PreferredPath, null, fileExists, null, 0, []);
        }

        try
        {
            string json = ReadAllShared(resolved);
            ParsedClipFile? parsed = string.IsNullOrWhiteSpace(json)
                ? null
                : ClipboardClipParser.ParseFile(json);
            Status = MyClipboardContract.Classify(contractResolved: true, parsed);
            if (parsed is null)
            {
                return new ClipSnapshot(
                    Status,
                    PreferredPath,
                    resolved,
                    fileExists,
                    "JSON illisible (schéma version 1 camelCase attendu).",
                    0,
                    []);
            }

            if (Status == ClipFileStatus.Unauthorized)
            {
                return new ClipSnapshot(
                    Status, PreferredPath, resolved, fileExists, null, parsed.UpdatedAtMs, []);
            }

            IReadOnlyList<ClipboardClip> clips = parsed.Clips
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .Take(Math.Max(0, maxCount))
                .Select(c => new ClipboardClip(c.Id, c.Text, c.Type, c.UpdatedAtMs))
                .ToArray();

            Status = clips.Count == 0 ? ClipFileStatus.Empty : ClipFileStatus.Ready;
            return new ClipSnapshot(
                Status, PreferredPath, resolved, fileExists, null, parsed.UpdatedAtMs, clips);
        }
        catch (Exception)
        {
            Status = ClipFileStatus.Invalid;
            return new ClipSnapshot(
                Status,
                PreferredPath,
                resolved,
                fileExists,
                "Lecture impossible (fichier verrouillé ou illisible).",
                0,
                []);
        }
    }

    private static string ReadAllShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
