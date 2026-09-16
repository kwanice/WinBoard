using WinBoard.Core;

namespace WinBoard.Services;

/// <summary>One clipboard item exposed to WinBoard (local only — never uploaded).</summary>
public sealed record ClipboardClip(string Id, string Text, DateTimeOffset Timestamp)
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

/// <summary>
/// Local clip source. Implementations must stay on-device (file / IPC), never cloud.
/// </summary>
public interface IClipboardClipSource
{
    string DisplayName { get; }

    string PreferredPath { get; }

    string? ResolvedPath { get; }

    ClipFileStatus Status { get; }

    IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount);
}

/// <summary>
/// Reads clips from MyClipboard's local JSON dump.
///
/// Contract (see README « Connexion MyClipboard ») :
/// <c>%LOCALAPPDATA%\MyClipBoard\clips.json</c> (also accepts MyClipboard / Roaming).
/// The public repo has no IPC API yet — when the file is missing the panel
/// explains how to connect rather than looking like a WinBoard crash.
/// </summary>
public sealed class FileClipboardClipSource : IClipboardClipSource
{
    private static readonly string[] FolderNames = ["MyClipBoard", "MyClipboard"];

    public FileClipboardClipSource()
    {
        PreferredPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyClipBoard",
            "clips.json");
    }

    public string DisplayName => "MyClipboard";

    public string PreferredPath { get; }

    public string? ResolvedPath { get; private set; }

    public ClipFileStatus Status { get; private set; } = ClipFileStatus.Missing;

    public IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount)
    {
        string? path = FindExistingFile();
        ResolvedPath = path;
        if (path is null)
        {
            Status = ClipFileStatus.Missing;
            return [];
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                Status = ClipFileStatus.Empty;
                return [];
            }

            if (!ClipboardClipParser.LooksLikeJson(json))
            {
                Status = ClipFileStatus.Invalid;
                return [];
            }

            IReadOnlyList<ParsedClip> parsed = ClipboardClipParser.Parse(json, maxCount);
            if (parsed.Count == 0)
            {
                Status = ClipboardClipParser.LooksLikeJson(json) ? ClipFileStatus.Empty : ClipFileStatus.Invalid;
                return [];
            }

            Status = ClipFileStatus.Ready;
            return parsed.Select(c => new ClipboardClip(c.Id, c.Text, c.Timestamp)).ToArray();
        }
        catch (Exception)
        {
            Status = ClipFileStatus.Invalid;
            return [];
        }
    }

    private static string? FindExistingFile()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };

        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            foreach (string folder in FolderNames)
            {
                string path = Path.Combine(root, folder, "clips.json");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }
}
