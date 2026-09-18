using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class SwipeInjectPolicyTests
{
    [Fact]
    public void DecodedInject_AllowedAfterTerminalReleaseWhenIdle()
    {
        Assert.True(SwipeInjectPolicy.AllowDecodedInject(
            pointerSessionActive: false, contactDown: false));
    }

    [Fact]
    public void DecodedInject_BlockedWhilePointerSessionActive()
    {
        Assert.False(SwipeInjectPolicy.AllowDecodedInject(
            pointerSessionActive: true, contactDown: false));
    }

    [Fact]
    public void DecodedInject_BlockedInFalseIdleGap_WhenNextContactAlreadyDown()
    {
        // Teardown nulled the session before the next finger was registered.
        // Inject / HWND restore here killed the trail and left last-key repeats.
        Assert.False(SwipeInjectPolicy.AllowDecodedInject(
            pointerSessionActive: false, contactDown: true));
    }

    [Fact]
    public void LetterKeyRepeat_DisabledForSwipeableLetters()
    {
        Assert.False(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: true));
        Assert.True(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: false));
    }
}
