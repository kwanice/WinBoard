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
}
