using System.Diagnostics;
using Microsoft.Win32;
using WinBoard.Core;

namespace WinBoard.Input;

/// <summary>
/// Opens MyClipboard so the user can authorize WinBoard. Local only: protocol
/// or a discovered desktop EXE — never a network call.
/// </summary>
internal static class MyClipboardAccess
{
    public enum LaunchKind
    {
        Protocol,
        Executable,
        Unavailable,
    }

    private static readonly string[] ExeNames =
    [
        "MyClipBoard.exe",
        "MyClipboard.exe",
    ];

    public static LaunchKind TryRequestAccess()
    {
        if (IsProtocolRegistered() && TryStart(MyClipboardContract.ProtocolUri, args: null))
        {
            return LaunchKind.Protocol;
        }

        string? exe = FindExecutable();
        if (exe is not null && TryStart(exe, args: MyClipboardContract.ProtocolUri))
        {
            return LaunchKind.Executable;
        }

        if (exe is not null && TryStart(exe, args: null))
        {
            return LaunchKind.Executable;
        }

        return LaunchKind.Unavailable;
    }

    internal static bool IsProtocolRegistered()
    {
        try
        {
            using RegistryKey? hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Classes\myclipboard");
            if (hkcu is not null)
            {
                return true;
            }

            using RegistryKey? hklm = Registry.ClassesRoot.OpenSubKey("myclipboard");
            return hklm is not null;
        }
        catch
        {
            return false;
        }
    }

    internal static string? FindExecutable()
    {
        foreach (string candidate in EnumerateExeCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExeCandidates()
    {
        foreach (string name in ExeNames)
        {
            string? fromPath = ReadAppPath(Registry.CurrentUser, name)
                ?? ReadAppPath(Registry.LocalMachine, name);
            if (!string.IsNullOrEmpty(fromPath))
            {
                yield return fromPath;
            }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] roots =
        [
            Path.Combine(local, "Programs", "MyClipBoard"),
            Path.Combine(local, "Programs", "MyClipboard"),
            Path.Combine(local, "MyClipBoard"),
            Path.Combine(local, "MyClipboard"),
            Path.Combine(programs, "MyClipBoard"),
            Path.Combine(programs, "MyClipboard"),
            Path.Combine(programsX86, "MyClipBoard"),
            Path.Combine(programsX86, "MyClipboard"),
        ];

        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            foreach (string name in ExeNames)
            {
                yield return Path.Combine(root, name);
            }
        }
    }

    private static string? ReadAppPath(RegistryKey hive, string exeName)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName);
            object? value = key?.GetValue(null);
            string? path = value as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryStart(string fileName, string? args)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(args))
            {
                info.Arguments = args;
            }

            Process.Start(info);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
