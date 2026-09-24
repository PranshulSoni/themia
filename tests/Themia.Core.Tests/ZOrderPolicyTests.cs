using Themia.Core.Shell;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The z-order decision. Every case here is a bug that actually shipped in the original app, which
/// is why the decision is a pure function in the first place.
/// </summary>
public sealed class ZOrderPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShowDesktopAlwaysPins(bool pinned, bool shellSurface)
        => Assert.Equal(ZAction.PinTopmost, ZOrderPolicy.Decide(true, pinned, shellSurface));

    [Fact]
    public void DismissedShowDesktopDropsTheHeldPin()
        => Assert.Equal(ZAction.DropToAnchor, ZOrderPolicy.Decide(false, pinnedTopmost: true, shellSurfaceForeground: false));

    /// <summary>
    /// Pressing the Windows key must not drop the pin: the desktop layer is still elevated, so
    /// dropping would bury the widgets, and dismissing Start hands the foreground to the taskbar
    /// rather than the desktop, so they would never come back.
    /// </summary>
    [Fact]
    public void ShellSurfaceHoldsTheHeldPin()
        => Assert.Equal(ZAction.HoldTopmost, ZOrderPolicy.Decide(false, pinnedTopmost: true, shellSurfaceForeground: true));

    [Fact]
    public void NothingHappeningSelfHeals()
        => Assert.Equal(ZAction.SelfHeal, ZOrderPolicy.Decide(false, pinnedTopmost: false, shellSurfaceForeground: false));

    /// <summary>
    /// A shell surface while nothing is pinned is an ordinary moment, not a reason to act: the user
    /// opening Start must not lift the widgets out of the desktop band.
    /// </summary>
    [Fact]
    public void ShellSurfaceWithoutAPinIsOrdinary()
        => Assert.Equal(ZAction.SelfHeal, ZOrderPolicy.Decide(false, pinnedTopmost: false, shellSurfaceForeground: true));
}
