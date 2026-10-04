using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-842]..[S-847] Display identity, the display pass plan, the per-display switch and new displays.</summary>
public class DisplayPlanTests
{
    private const string PathA = @"\\?\display#del40f6#5&2b2a8e3c&0&uid4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathB = @"\\?\display#gsm5b7f#5&2b2a8e3c&0&uid4358#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static DisplayInfo Display(string id, bool primary = false, double x = 0, double taskbar = 48, bool resolved = true)
        => new(id, resolved, new PixelRect(x, 0, 1920, 1080), new PixelRect(x, 0, 1920, 1080 - taskbar), primary);

    private static readonly DisplayInfo A = Display(PathA, primary: true);
    private static readonly DisplayInfo B = Display(PathB, x: 1920);

    private static Dictionary<string, DisplayInfo> Surfaces(params DisplayInfo[] displays)
        => displays.ToDictionary(d => d.Id, StringComparer.Ordinal);

    // ---- identity -------------------------------------------------------------------------------

    [Fact]
    public void S842_the_device_path_is_the_id_lower_cased()
    {
        var (id, resolved) = DisplayIdentity.Resolve(@"\\?\DISPLAY#DEL40F6#5&2B2A8E3C&0&UID4357#{E6F07B5F}", @"\\.\DISPLAY2");

        Assert.True(resolved);
        Assert.Equal(@"\\?\display#del40f6#5&2b2a8e3c&0&uid4357#{e6f07b5f}", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\?\")]
    [InlineData("garbage")]
    public void S843_without_a_device_path_the_ordinal_is_used_and_flagged_unresolved(string? path)
    {
        var (id, resolved) = DisplayIdentity.Resolve(path, @"\\.\DISPLAY3");

        Assert.False(resolved);
        Assert.Equal(@"\\.\DISPLAY3", id);
    }

    [Fact]
    public void S843_an_empty_gdi_name_still_gives_a_non_empty_id()
    {
        Assert.False(string.IsNullOrEmpty(DisplayIdentity.Resolve(null, "").Id));
    }

    // ---- the pass ------------------------------------------------------------------------------

    [Fact]
    public void S844_startup_creates_one_surface_per_display()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(), [A, B], []);

        Assert.Equal([PathA, PathB], pass.Create.Select(d => d.Id));
        Assert.Empty(pass.Close);
        Assert.Empty(pass.Update);
    }

    [Fact]
    public void S844_a_settled_topology_is_a_fixed_point()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [B, A], []);

        Assert.True(pass.IsEmpty);
    }

    [Fact]
    public void S844_surfaces_follow_the_panel_not_the_enumeration_order()
    {
        // The two panels come back in the other order (link-training order after a power cycle).
        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [B, A], []);

        Assert.True(pass.IsEmpty);
    }

    [Fact]
    public void S844_an_unplugged_display_closes_its_surface_and_a_new_one_gets_one()
    {
        var c = Display(@"\\?\display#aaa#1#{x}", x: -1920);

        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [A, c], []);

        Assert.Equal([PathB], pass.Close);
        Assert.Equal([c.Id], pass.Create.Select(d => d.Id));
    }

    [Fact]
    public void S840_S844_a_taskbar_change_updates_the_surface_and_does_not_rebuild_it()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [A with { WorkArea = new PixelRect(0, 0, 1920, 1000) }, B], []);

        Assert.Empty(pass.Create);
        Assert.Empty(pass.Close);
        Assert.Equal([PathA], pass.Update.Select(d => d.Id));
    }

    [Fact]
    public void S844_a_resolution_change_updates_the_surface()
    {
        var bigger = A with { Bounds = new PixelRect(0, 0, 2560, 1440), WorkArea = new PixelRect(0, 0, 2560, 1392) };

        var pass = DisplayPlan.Reconcile(Surfaces(A), [bigger], []);

        Assert.Equal([bigger], pass.Update);
    }

    [Fact]
    public void S843_a_panel_whose_path_resolves_later_gets_a_surface_under_its_real_id()
    {
        var waking = Display(@"\\.\DISPLAY2", x: 1920, resolved: false);

        var pass = DisplayPlan.Reconcile(Surfaces(A, waking), [A, B], []);

        Assert.Equal([@"\\.\DISPLAY2"], pass.Close);
        Assert.Equal([PathB], pass.Create.Select(d => d.Id));
    }

    [Fact]
    public void S846_an_empty_enumeration_builds_nothing_and_closes_nothing()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [], []);

        Assert.True(pass.IsEmpty);
        Assert.Empty(pass.Enabled);
    }

    [Fact]
    public void S844_a_repeated_id_gets_one_surface()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(), [A, A with { Bounds = new PixelRect(1920, 0, 1920, 1080) }], []);

        Assert.Single(pass.Create);
        Assert.Equal(A, pass.Create[0]);
    }

    // ---- per-display switch --------------------------------------------------------------------

    [Fact]
    public void S845_a_switched_off_display_gets_no_surface_and_loses_the_one_it_had()
    {
        var pass = DisplayPlan.Reconcile(Surfaces(A, B), [A, B], [PathB]);

        Assert.Equal([PathB], pass.Close);
        Assert.Equal([PathA], pass.Enabled.Select(d => d.Id));
    }

    [Fact]
    public void S845_the_last_enabled_display_cannot_be_switched_off()
    {
        Assert.True(DisplayPlan.CanDisable([A, B], [], PathA));
        Assert.False(DisplayPlan.CanDisable([A, B], [PathB], PathA));
        Assert.False(DisplayPlan.CanDisable([A], [], PathA));
    }

    [Fact]
    public void S845_when_every_connected_display_is_off_the_primary_is_shown_anyway()
    {
        // B was switched off, then A — the only one on — was unplugged and replaced by C.
        var c = Display(@"\\?\display#ccc#1#{x}", primary: true, x: 0);

        var enabled = DisplayPlan.Enabled([B, c], [PathB, c.Id]);

        Assert.Equal([c.Id], enabled.Select(d => d.Id));
    }

    [Fact]
    public void S845_with_no_primary_among_them_the_first_display_is_shown()
    {
        var enabled = DisplayPlan.Enabled([B], [PathB]);

        Assert.Equal([PathB], enabled.Select(d => d.Id));
    }

    // ---- views -----------------------------------------------------------------------------------

    [Fact]
    public void S843_an_unresolved_display_is_never_given_a_new_view()
    {
        var waking = Display(@"\\.\DISPLAY2", x: 1920, resolved: false);

        var keys = DisplayPlan.ViewKeys([A, waking], new Dictionary<string, string>());

        Assert.Equal([PathA], keys);
    }

    [Fact]
    public void S843_a_permanently_unresolved_display_keeps_the_view_it_already_has()
    {
        var remote = Display(@"\\.\DISPLAY9", resolved: false);

        var keys = DisplayPlan.ViewKeys([remote], new Dictionary<string, string> { [@"\\.\DISPLAY9"] = "v1" });

        Assert.Equal([@"\\.\DISPLAY9"], keys);
    }

    [Fact]
    public void S847_a_new_display_shows_the_chosen_view_shared_not_a_new_one()
    {
        var state = AppState.CreateDefault();
        var chosen = state.Views[0];
        state.DefaultViewForNewDisplays = chosen.Id;

        var created = state.EnsureViewPerDisplay([PathA, PathB]);

        Assert.Equal(0, created);
        Assert.Equal(chosen.Id, state.MonitorViews[PathA]);
        Assert.Equal(chosen.Id, state.MonitorViews[PathB]);
    }

    [Fact]
    public void S847_a_display_that_already_has_a_view_keeps_it()
    {
        var state = AppState.CreateDefault();
        var other = ViewState.Create("Second");
        state.Views.Add(other);
        state.MonitorViews[PathB] = other.Id;
        state.DefaultViewForNewDisplays = state.Views[0].Id;

        state.EnsureViewPerDisplay([PathA, PathB]);

        Assert.Equal(other.Id, state.MonitorViews[PathB]);
    }

    [Fact]
    public void S847_without_a_choice_each_new_display_gets_a_view_of_its_own()
    {
        var state = AppState.CreateDefault();

        var created = state.EnsureViewPerDisplay([PathA, PathB]);

        Assert.Equal(1, created);
        Assert.NotEqual(state.MonitorViews[PathA], state.MonitorViews[PathB]);
    }
}
