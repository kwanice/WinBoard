using System.Diagnostics;
using System.Text.Json;

namespace WinBoard.Services;

/// <summary>
/// Loads and persists <see cref="KeyboardSettings"/>. Because WinBoard runs
/// unpackaged (no MSIX identity), it stores JSON under
/// <c>%LOCALAPPDATA%\WinBoard\settings.json</c> instead of
/// <c>Windows.Storage.ApplicationData</c> (which requires package identity).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinBoard");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    /// <summary>Raised after settings are changed and saved.</summary>
    public event EventHandler? Changed;

    /// <summary>Process-wide instance so the keyboard and the settings window share one file.</summary>
    public static SettingsService Shared { get; } = new();

    public KeyboardSettings Current { get; private set; }

    /// <summary>Applies mutations to a working copy, persists, and optionally notifies listeners.</summary>
    public void Update(Action<KeyboardSettings> mutate, bool notify = true)
    {
        KeyboardSettings working = Current.Clone();
        mutate(working);
        working.Normalize();
        Current = working;
        Save();
        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private KeyboardSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                KeyboardSettings? loaded = JsonSerializer.Deserialize<KeyboardSettings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinBoard: failed to load settings ({ex.Message}); using defaults.");
        }

        var defaults = new KeyboardSettings();
        defaults.Normalize();
        return defaults;
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinBoard: failed to save settings ({ex.Message}).");
        }
    }
}
