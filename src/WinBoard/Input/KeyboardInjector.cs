using System.Diagnostics;
using System.Runtime.InteropServices;

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

        // One SendInput batch so ZWJ sequences / flags stay a single cluster.
        var inputs = new INPUT[text.Length * 2];
        int i = 0;
        foreach (char character in text)
        {
            inputs[i++] = CreateUnicodeInput(character, keyUp: false);
            inputs[i++] = CreateUnicodeInput(character, keyUp: true);
        }

        Dispatch(inputs);
    }

    public static void InjectBackspace()
    {
        SendVirtualKey(NativeMethods.VkBack);
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
                    ExtraInfo = (nuint)NativeMethods.GetMessageExtraInfo(),
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
                    ExtraInfo = (nuint)NativeMethods.GetMessageExtraInfo(),
                },
            },
        };
    }

    private static void Dispatch(INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            Debug.WriteLine($"SendInput injected {sent}/{inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
        }
    }
}
