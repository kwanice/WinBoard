namespace WinBoard.Services;

/// <summary>Central place for the user-facing app version.</summary>
public static class AppInfo
{
    public const string Version = "0.6.4";

    public static string DisplayVersion => $"WinBoard v{Version}";
}
