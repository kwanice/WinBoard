namespace WinBoard.Core;

/// <summary>
/// Decode/inject is a queue separate from pointer ownership. It must not
/// steal, commit, or recapture a live finger. Letters never key-repeat
/// (0.8.17 safety). Global swipe-only / pending letter-inject gates from
/// 0.8.16 are gone — they created false-idle gaps and wrong inject timing.
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
    /// Swipe letters must never key-repeat. Holding <c>s</c> after a glide
    /// produced <c>ssss</c> in the target while diag still showed the word.
    /// Digits / Backspace still repeat.
    /// </summary>
    public static bool AllowLetterKeyRepeat(bool isSwipeableLetter) => !isSwipeableLetter;
}
