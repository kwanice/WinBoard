using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class TopmostPolicyTests
{
    [Fact]
    public void PreserveExStyle_AddsTopmostNoActivateToolWindow()
    {
        uint preserved = TopmostPolicy.PreserveExStyle(0);
        Assert.Equal(
            TopmostPolicy.WsExTopmost | TopmostPolicy.WsExNoActivate | TopmostPolicy.WsExToolWindow,
            preserved);
        Assert.True(TopmostPolicy.HasTopmost(new nint(preserved)));
        Assert.True(TopmostPolicy.IsExStyleIndex(TopmostPolicy.GwlExStyle));
        Assert.False(TopmostPolicy.IsExStyleIndex(-16));
    }

    [Fact]
    public void Rewrite_ForcesTopmostWhenDemotedOrMissingBit()
    {
        Assert.True(TopmostPolicy.TryRewriteWindowPos(
            0, TopmostPolicy.HwndNoTopmost, alreadyTopmost: true, isTopLevel: true,
            out nint after, out uint flags));
        Assert.Equal(TopmostPolicy.HwndTopmost, after);
        Assert.Equal(TopmostPolicy.SwpNoActivate, flags);

        Assert.True(TopmostPolicy.TryRewriteWindowPos(
            TopmostPolicy.SwpNoZOrder, nint.Zero, alreadyTopmost: false, isTopLevel: true,
            out after, out flags));
        Assert.Equal(TopmostPolicy.HwndTopmost, after);
        Assert.Equal(TopmostPolicy.SwpNoActivate, flags);
        Assert.Equal(0u, flags & TopmostPolicy.SwpNoZOrder);

        Assert.True(TopmostPolicy.TryRewriteWindowPos(
            0, TopmostPolicy.HwndTop, alreadyTopmost: false, isTopLevel: true,
            out after, out _));
        Assert.Equal(TopmostPolicy.HwndTopmost, after);
    }

    [Fact]
    public void Rewrite_LeavesHideAndSiblingOrderAlone()
    {
        Assert.False(TopmostPolicy.TryRewriteWindowPos(
            TopmostPolicy.SwpHideWindow, TopmostPolicy.HwndNoTopmost,
            alreadyTopmost: false, isTopLevel: true, out _, out _));
        Assert.False(TopmostPolicy.TryRewriteWindowPos(
            0, TopmostPolicy.HwndNoTopmost,
            alreadyTopmost: true, isTopLevel: false, out _, out _));
        Assert.False(TopmostPolicy.TryRewriteWindowPos(
            TopmostPolicy.SwpNoZOrder, nint.Zero,
            alreadyTopmost: true, isTopLevel: true, out _, out _));
        Assert.False(TopmostPolicy.TryRewriteWindowPos(
            0, TopmostPolicy.HwndTopmost,
            alreadyTopmost: true, isTopLevel: true, out _, out _));

        nint settings = new(0x1234);
        Assert.False(TopmostPolicy.TryRewriteWindowPos(
            0, settings, alreadyTopmost: true, isTopLevel: true, out nint after, out _));
        Assert.Equal(settings, after);
    }
}
