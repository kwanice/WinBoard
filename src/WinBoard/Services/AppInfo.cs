namespace WinBoard.Services;

/// <summary>Central place for the user-facing app version.</summary>
public static class AppInfo
{
    public const string Version = "0.8.1";

    public static string DisplayVersion => $"WinBoard v{Version}";
}
