using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBoard.Core;

namespace WinBoard.Input;

/// <summary>
/// Injects keystrokes into the currently focused window via SendInput.
/// Unicode characters use KEYEVENTF_UNICODE so they do not depend on the
/// target app's keyboard layout. Navigation keys (Backspace) use virtual-key codes.
/// </summary>
public static class KeyboardInjector
{
    public static void InjectCharacter(char character)
    {
        SendUnicode(character);
    }

    public static void InjectText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Dispatch(BuildUnicodeInputs(text));
    }

    /// <summary>
    /// Swipe-word inject: Unicode text only, with letter/modifier KEYUPs so
    /// Notepad cannot auto-repeat a stuck last-hit VK. Restores the remembered
    /// other-process HWND (not Diag) once, then sends one batch.
    /// </summary>
    public static void InjectSwipeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Debug.Assert(SwipeInjectPolicy.SwipeWordInjectIsUnicodeOnly);

        IReadOnlyList<ushort> flush = SwipeInjectPolicy.StuckKeyUpVirtualKeys;
        INPUT[] unicode = BuildUnicodeInputs(text);
        var inputs = new INPUT[flush.Count + unicode.Length + flush.Count];
        int i = 0;
        AppendKeyUps(inputs, ref i, flush);
        Array.Copy(unicode, 0, inputs, i, unicode.Length);
        i += unicode.Length;
        AppendKeyUps(inputs, ref i, flush);

        InputTargetGuard.RestoreForInject();
        Dispatch(inputs, restoreTarget: false);
    }

    public static void InjectBackspace()
    {
        SendVirtualKey(NativeMethods.VkBack);
    }

    /// <summary>One SendInput batch of Backspace key-downs/ups (swipe-unit undo).</summary>
    public static void InjectBackspaces(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[(i * 2) + 0] = CreateVirtualKeyInput(NativeMethods.VkBack, keyUp: false);
            inputs[(i * 2) + 1] = CreateVirtualKeyInput(NativeMethods.VkBack, keyUp: true);
        }

        Dispatch(inputs);
    }

    public static void InjectEnter()
    {
        SendVirtualKey(NativeMethods.VkReturn);
    }

    public static void InjectLeft()
    {
        SendVirtualKey(NativeMethods.VkLeft, extended: true);
    }

    public static void InjectRight()
    {
        SendVirtualKey(NativeMethods.VkRight, extended: true);
    }

    /// <summary>
    /// Deletes the previous word (Ctrl+Backspace), used by the Gboard-style
    /// backspace swipe-to-delete-words gesture.
    /// </summary>
    public static void InjectDeleteWord()
    {
        INPUT[] inputs =
        [
            CreateVirtualKeyInput(NativeMethods.VkControl, keyUp: false),
            CreateVirtualKeyInput(NativeMethods.VkBack, keyUp: false),
            CreateVirtualKeyInput(NativeMethods.VkBack, keyUp: true),
            CreateVirtualKeyInput(NativeMethods.VkControl, keyUp: true),
        ];

        Dispatch(inputs);
    }

    private static void SendUnicode(char character)
    {
        INPUT[] inputs =
        [
            CreateUnicodeInput(character, keyUp: false),
            CreateUnicodeInput(character, keyUp: true),
        ];

        Dispatch(inputs);
    }

    private static void SendVirtualKey(ushort virtualKey, bool extended = false)
    {
        uint extra = extended ? NativeMethods.KeyeventfExtendedKey : 0;
        INPUT[] inputs =
        [
            CreateVirtualKeyInput(virtualKey, keyUp: false, extra),
            CreateVirtualKeyInput(virtualKey, keyUp: true, extra),
        ];

        Dispatch(inputs);
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new INPUT
        {
            Type = NativeMethods.InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = character,
                    Flags = NativeMethods.KeyeventfUnicode | (keyUp ? NativeMethods.KeyeventfKeyup : 0),
                    Time = 0,
                    ExtraInfo = 0,
                },
            },
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp, uint extraFlags = 0)
    {
        return new INPUT
        {
            Type = NativeMethods.InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = virtualKey,
                    Scan = 0,
                    Flags = extraFlags | (keyUp ? NativeMethods.KeyeventfKeyup : 0),
                    Time = 0,
                    ExtraInfo = 0,
                },
            },
        };
    }

    private static INPUT[] BuildUnicodeInputs(string text)
    {
        var inputs = new INPUT[text.Length * 2];
        int i = 0;
        foreach (char character in text)
        {
            inputs[i++] = CreateUnicodeInput(character, keyUp: false);
            inputs[i++] = CreateUnicodeInput(character, keyUp: true);
        }

        return inputs;
    }

    private static void AppendKeyUps(INPUT[] inputs, ref int index, IReadOnlyList<ushort> virtualKeys)
    {
        for (int i = 0; i < virtualKeys.Count; i++)
        {
            ushort vk = virtualKeys[i];
            uint extra = IsExtendedVirtualKey(vk) ? NativeMethods.KeyeventfExtendedKey : 0;
            inputs[index++] = CreateVirtualKeyInput(vk, keyUp: true, extra);
        }
    }

    private static bool IsExtendedVirtualKey(ushort virtualKey) =>
        virtualKey is NativeMethods.VkLeft or NativeMethods.VkRight or 0x5B or 0x5C;

    private static void Dispatch(INPUT[] inputs, bool restoreTarget = true)
    {
        if (restoreTarget)
        {
            InputTargetGuard.RestoreForInject();
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            Debug.WriteLine($"SendInput injected {sent}/{inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
        }
    }
}
