using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class InputTargetPolicyTests
{
    [Fact]
    public void OtherProcessWindow_IsUsableTextTarget()
    {
        Assert.True(InputTargetPolicy.IsUsableTextTarget(isKeyboardTree: false, sameProcess: false));
    }

    [Fact]
    public void KeyboardOverlay_IsNotTextTarget()
    {
        Assert.False(InputTargetPolicy.IsUsableTextTarget(isKeyboardTree: true, sameProcess: true));
    }

    [Fact]
    public void DiagOrSettings_SameProcess_IsNotTextTarget()
    {
        Assert.False(InputTargetPolicy.IsUsableTextTarget(isKeyboardTree: false, sameProcess: true));
    }

    [Fact]
    public void StolenForeground_RestoredOnlyWhenOverlayHasFocusAndFingerIsUp()
    {
        Assert.True(InputTargetPolicy.ShouldRestoreStolenForeground(
            contactDown: false,
            foregroundMissing: false,
            foregroundIsKeyboardTree: true,
            foregroundIsUsableTextTarget: false));
        Assert.False(InputTargetPolicy.ShouldRestoreStolenForeground(
            contactDown: true,
            foregroundMissing: false,
            foregroundIsKeyboardTree: true,
            foregroundIsUsableTextTarget: false));
        Assert.False(InputTargetPolicy.ShouldRestoreStolenForeground(
            contactDown: false,
            foregroundMissing: false,
            foregroundIsKeyboardTree: false,
            foregroundIsUsableTextTarget: false));
    }

    [Fact]
    public void InjectRestore_SkippedWhileContactDown()
    {
        Assert.True(InputTargetPolicy.ShouldRestoreForInject(contactDown: false));
        Assert.False(InputTargetPolicy.ShouldRestoreForInject(contactDown: true));
    }
}
