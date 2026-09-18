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
        Assert.False(SwipeInjectPolicy.AllowDecodedInject(
            pointerSessionActive: false, contactDown: true));
    }

    [Fact]
    public void SwipeEnd_CharacterInjectBlocked_WordInjectEnqueuedWhenIdle()
    {
        Assert.False(SwipeInjectPolicy.AllowCharacterInject(swipeLatched: true));
        Assert.True(SwipeInjectPolicy.AllowDecodedInject(
            pointerSessionActive: false, contactDown: false));
    }

    [Fact]
    public void Tap_CharacterInjectAllowedWhenNoSwipeLatched()
    {
        Assert.True(SwipeInjectPolicy.AllowCharacterInject(swipeLatched: false));
    }

    [Fact]
    public void SamePointerAfterSwipeEnd_MustNotStartLetterTap()
    {
        Assert.False(SwipeInjectPolicy.AllowLetterPress(pointerId: 5281, endedSwipePointerId: 5281));
        Assert.True(SwipeInjectPolicy.AllowLetterPress(pointerId: 5282, endedSwipePointerId: 5281));
        Assert.True(SwipeInjectPolicy.AllowLetterPress(pointerId: 5281, endedSwipePointerId: null));
    }

    [Fact]
    public void LetterKeyRepeat_DisabledForSwipeableLetters()
    {
        Assert.False(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: true));
        Assert.True(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: false));
    }

    [Fact]
    public void SwipeWordInject_IsUnicodeOnly_NoPerLetterVk()
    {
        Assert.True(SwipeInjectPolicy.SwipeWordInjectIsUnicodeOnly);
    }

    [Fact]
    public void StuckKeyUpVirtualKeys_CoverNotepadLastHitLettersAndModifiers()
    {
        IReadOnlyList<ushort> keys = SwipeInjectPolicy.StuckKeyUpVirtualKeys;
        Assert.Contains((ushort)0x53, keys); // S — vais last-hit
        Assert.Contains((ushort)0x45, keys); // E
        Assert.Contains((ushort)0x55, keys); // U
        Assert.Contains((ushort)0x49, keys); // I
        Assert.Contains((ushort)0x10, keys); // Shift
        Assert.Contains((ushort)0x11, keys); // Control
        Assert.DoesNotContain((ushort)0x08, keys); // Backspace stays a real VK inject
    }
}
