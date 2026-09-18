namespace WinBoard.Core;

/// <summary>
/// Decode/inject is a queue separate from pointer ownership. It must not
/// steal, commit, or recapture a live finger. Letters never key-repeat
/// (0.8.17 safety).
/// </summary>
public static class SwipeInjectPolicy
{
    /// <summary>
    /// Inject a decoded word only after a confirmed terminal release when no
    /// contact is active. A teardown that already nulled the session while the
    /// next finger is physically down is still contact-down: do not inject.
    /// </summary>
    public static bool AllowDecodedInject(bool pointerSessionActive, bool contactDown) =>
        !pointerSessionActive && !contactDown;

    /// <summary>
    /// A latched swipe must never fall through to a single-letter tap
    /// (<c>PerformTap</c> / <c>InjectCharacterKey</c>) on pointer-up.
    /// The decoded word is enqueued instead.
    /// </summary>
    public static bool AllowCharacterInject(bool swipeLatched) => !swipeLatched;

    /// <summary>
    /// After capture ends, WinUI can deliver a new <c>PointerPressed</c> on
    /// the last hit-key for the same pointerId. That leftover must not start
    /// a letter tap (field <c>ssss</c>/<c>eeeee</c>) and must not look like a
    /// live session that would skip <c>InjectSwipeWord</c>.
    /// A different pointerId (the next chained word) is allowed.
    /// </summary>
    public static bool AllowLetterPress(uint pointerId, uint? endedSwipePointerId) =>
        endedSwipePointerId != pointerId;

    /// <summary>
    /// Swipe letters must never key-repeat. Holding <c>s</c> after a glide
    /// produced <c>ssss</c> in the target while diag still showed the word.
    /// Digits / Backspace still repeat.
    /// </summary>
    public static bool AllowLetterKeyRepeat(bool isSwipeableLetter) => !isSwipeableLetter;

    /// <summary>
    /// Swipe words are injected as <c>KEYEVENTF_UNICODE</c> text only.
    /// Never synthesize per-letter VK KEYDOWN for a decoded word.
    /// </summary>
    public static bool SwipeWordInjectIsUnicodeOnly => true;

    /// <summary>
    /// Virtual keys that must be KEYUP'd around a swipe Unicode SendInput.
    /// Notepad's EDIT control auto-repeats if a letter VK is left down
    /// (AttachThreadInput / leftover KEYDOWN). Other apps typically ignore it.
    /// </summary>
    public static IReadOnlyList<ushort> StuckKeyUpVirtualKeys { get; } = BuildStuckKeyUpVirtualKeys();

    private static ushort[] BuildStuckKeyUpVirtualKeys()
    {
        var keys = new List<ushort>(64);
        for (ushort vk = 0x41; vk <= 0x5A; vk++)
        {
            keys.Add(vk); // A–Z (covers last-hit s/e/u/i spam)
        }

        for (ushort vk = 0x30; vk <= 0x39; vk++)
        {
            keys.Add(vk); // 0–9
        }

        ushort[] modifiersAndOem =
        [
            0x10, 0x11, 0x12, 0x14, // Shift, Control, Menu, Capital
            0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, // L/R Shift, Ctrl, Alt
            0x5B, 0x5C, // LWin, RWin
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, // OEM 1 + - . / `
            0xDB, 0xDC, 0xDD, 0xDE, 0xE2, // OEM 4 5 6 7 102 (AZERTY)
        ];
        keys.AddRange(modifiersAndOem);
        return [.. keys];
    }
}
