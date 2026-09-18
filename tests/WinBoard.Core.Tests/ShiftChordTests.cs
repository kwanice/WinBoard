using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class ShiftChordTests
{
    [Fact]
    public void Tap_CyclesOffToStickyToCapsToOff()
    {
        var shift = new ShiftChord();
        Assert.Equal(ShiftState.Off, shift.Latch);
        Assert.False(shift.IsUpper);

        shift.CycleLatch();
        Assert.Equal(ShiftState.Shift, shift.Latch);
        Assert.True(shift.IsUpper);

        shift.CycleLatch();
        Assert.Equal(ShiftState.CapsLock, shift.Latch);
        Assert.True(shift.IsUpper);

        shift.CycleLatch();
        Assert.Equal(ShiftState.Off, shift.Latch);
        Assert.False(shift.IsUpper);
    }

    [Fact]
    public void StickyShift_ConsumeLatch_ReturnsToOff_CapsStays()
    {
        var shift = new ShiftChord();
        shift.CycleLatch();
        Assert.True(shift.ConsumeLatch());
        Assert.Equal(ShiftState.Off, shift.Latch);

        shift.CycleLatch();
        shift.CycleLatch();
        Assert.Equal(ShiftState.CapsLock, shift.Latch);
        Assert.False(shift.ConsumeLatch());
        Assert.Equal(ShiftState.CapsLock, shift.Latch);
    }

    [Fact]
    public void HoldPlusLetter_DoesNotCycle_AndDoesNotConsumeWhileHeld()
    {
        var shift = new ShiftChord();
        shift.BeginHold();
        Assert.True(shift.Held);
        Assert.True(shift.IsUpper);
        Assert.Equal(ShiftState.Off, shift.Latch);

        shift.MarkModifierUsed();
        Assert.False(shift.ConsumeLatch());
        Assert.Equal(ShiftState.Off, shift.Latch);

        Assert.False(shift.EndHold(commitTap: true));
        Assert.False(shift.Held);
        Assert.Equal(ShiftState.Off, shift.Latch);
        Assert.False(shift.IsUpper);
    }

    [Fact]
    public void HoldReleasedWithoutChord_CyclesLikeATap()
    {
        var shift = new ShiftChord();
        shift.BeginHold();
        Assert.True(shift.EndHold(commitTap: true));
        Assert.Equal(ShiftState.Shift, shift.Latch);
        Assert.True(shift.IsUpper);
    }

    [Fact]
    public void HoldCanceled_DoesNotCycle()
    {
        var shift = new ShiftChord();
        shift.BeginHold();
        shift.CancelHold();
        Assert.False(shift.Held);
        Assert.Equal(ShiftState.Off, shift.Latch);
    }

    [Fact]
    public void CapsPlusHoldPlusLetter_KeepsCaps()
    {
        var shift = new ShiftChord();
        shift.CycleLatch();
        shift.CycleLatch();
        Assert.Equal(ShiftState.CapsLock, shift.Latch);

        shift.BeginHold();
        shift.MarkModifierUsed();
        Assert.False(shift.EndHold(commitTap: true));
        Assert.Equal(ShiftState.CapsLock, shift.Latch);
        Assert.True(shift.IsUpper);
    }

    [Fact]
    public void StickyPlusHoldPlusLetter_ConsumesStickyOnRelease()
    {
        var shift = new ShiftChord();
        shift.CycleLatch();
        Assert.Equal(ShiftState.Shift, shift.Latch);

        shift.BeginHold();
        shift.MarkModifierUsed();
        Assert.True(shift.EndHold(commitTap: true));
        Assert.Equal(ShiftState.Off, shift.Latch);
    }

    [Fact]
    public void Resolve_LettersUppercase_IgnoreSecondary()
    {
        Assert.Equal('a', ShiftChord.Resolve('a', "%", shifted: false));
        Assert.Equal('A', ShiftChord.Resolve('a', "%", shifted: true));
        Assert.Equal('É', ShiftChord.Resolve('é', null, shifted: true));
    }

    [Fact]
    public void Resolve_NonLettersUseSecondaryGlyph()
    {
        Assert.Equal('1', ShiftChord.Resolve('1', "¹", shifted: false));
        Assert.Equal('¹', ShiftChord.Resolve('1', "¹", shifted: true));
        Assert.Equal('?', ShiftChord.Resolve('\'', "?", shifted: true));
        Assert.Equal(',', ShiftChord.Resolve(',', null, shifted: true));
    }

    [Fact]
    public void SecondFingerWhileShiftHeld_IsPrimaryNotCancel()
    {
        Assert.Equal(
            KeyPressAction.BeginShiftHold,
            KeyPointerPolicy.OnPressed(1, isShiftKey: true, null, null, swiping: false));
        Assert.Equal(
            KeyPressAction.BeginPrimary,
            KeyPointerPolicy.OnPressed(2, isShiftKey: false, null, shiftHoldPointerId: 1, swiping: false));
        Assert.Equal(
            KeyPressAction.Ignore,
            KeyPointerPolicy.OnPressed(3, isShiftKey: false, primaryPointerId: 2, shiftHoldPointerId: 1, swiping: false));
    }

    [Fact]
    public void SecondFingerWithoutShift_CancelsPrimary()
    {
        Assert.Equal(
            KeyPressAction.BeginPrimary,
            KeyPointerPolicy.OnPressed(1, isShiftKey: false, null, null, swiping: false));
        Assert.Equal(
            KeyPressAction.CancelPrimaryThenBegin,
            KeyPointerPolicy.OnPressed(2, isShiftKey: false, primaryPointerId: 1, null, swiping: false));
    }

    [Fact]
    public void ExtraFingerDuringSwipe_CommitsThenBeginsNextWord()
    {
        Assert.Equal(
            KeyPressAction.CommitPrimaryThenBegin,
            KeyPointerPolicy.OnPressed(2, isShiftKey: false, primaryPointerId: 1, null, swiping: true));
        Assert.Equal(
            KeyPressAction.Ignore,
            KeyPointerPolicy.OnPressed(2, isShiftKey: true, primaryPointerId: 1, null, swiping: true));
    }

    [Fact]
    public void ShiftWhileLetterIsDown_StartsHoldWithoutCancel()
    {
        Assert.Equal(
            KeyPressAction.BeginShiftHold,
            KeyPointerPolicy.OnPressed(2, isShiftKey: true, primaryPointerId: 1, null, swiping: false));
    }

    [Fact]
    public void SwipeLatch_DisabledWhileShiftHeld()
    {
        Assert.True(KeyPointerPolicy.AllowSwipeLatch(shiftHeld: false));
        Assert.False(KeyPointerPolicy.AllowSwipeLatch(shiftHeld: true));
    }
}
