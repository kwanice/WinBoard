using System.Text.Json;

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

    bool IsAvailable { get; }

    IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount);
}

/// <summary>
/// Reads clips from MyClipboard's local JSON dump.
///
/// Contract (see README « Connexion MyClipboard ») :
/// <c>%LOCALAPPDATA%\MyClipBoard\clips.json</c>
/// <code>
/// { "clips": [ { "id": "…", "text": "…", "timestamp": "2026-09-16T08:00:00Z" } ] }
/// </code>
/// The public MyClipBoard repo currently has no IPC API, so WinBoard only
/// consumes this file. When MyClipboard is not running / the file is absent,
/// <see cref="IsAvailable"/> is false and the panel shows an empty state.
/// </summary>
public sealed class FileClipboardClipSource : IClipboardClipSource
{
    private readonly string _filePath;

    public FileClipboardClipSource()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyClipBoard",
            "clips.json");
    }

    public string DisplayName => "MyClipboard";

    public bool IsAvailable => File.Exists(_filePath);

    public IReadOnlyList<ClipboardClip> GetRecentClips(int maxCount)
    {
        if (!IsAvailable)
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("clips", out JsonElement array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var clips = new List<ClipboardClip>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                string text = item.TryGetProperty("text", out JsonElement t)
                    ? t.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string id = item.TryGetProperty("id", out JsonElement idEl)
                    ? idEl.GetString() ?? Guid.NewGuid().ToString("n")
                    : Guid.NewGuid().ToString("n");
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;
                if (item.TryGetProperty("timestamp", out JsonElement ts)
                    && ts.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(ts.GetString(), out DateTimeOffset parsed))
                {
                    timestamp = parsed;
                }

                clips.Add(new ClipboardClip(id, text, timestamp));
                if (clips.Count >= maxCount)
                {
                    break;
                }
            }

            return clips;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
