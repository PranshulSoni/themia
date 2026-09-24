using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

public class ViewPerDisplayTests
{
    private static readonly string[] TwoDisplays = ["\\\\.\\DISPLAY1", "\\\\.\\DISPLAY2"];

    [Fact]
    public void Each_display_ends_up_on_a_different_view()
    {
        // This is what makes dragging a widget between displays mean anything: while both showed
        // the active view, the widget was already on both and there was nothing to move.
        var state = AppState.CreateDefault();

        state.EnsureViewPerDisplay(TwoDisplays);

        var first = state.ViewForMonitor(TwoDisplays[0]);
        var second = state.ViewForMonitor(TwoDisplays[1]);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void The_existing_view_is_reused_before_a_new_one_is_made()
    {
        var state = AppState.CreateDefault();
        var original = state.ActiveView();

        var created = state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Equal(1, created);
        Assert.Equal(2, state.Views.Count);
        Assert.Contains(state.Views, v => v.Id == original.Id);
    }

    [Fact]
    public void The_seeded_widgets_are_not_duplicated_or_lost()
    {
        var state = AppState.CreateDefault();
        var seeded = state.ActiveView().Widgets.Count;

        state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Equal(seeded, state.Views.Sum(v => v.Widgets.Count));
    }

    [Fact]
    public void Running_it_again_changes_nothing()
    {
        var state = AppState.CreateDefault();
        state.EnsureViewPerDisplay(TwoDisplays);
        var before = state.MonitorViews.ToDictionary(kv => kv.Key, kv => kv.Value);

        var created = state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Equal(0, created);
        Assert.Equal(before, state.MonitorViews);
    }

    [Fact]
    public void An_existing_assignment_is_never_taken_from_its_display()
    {
        var state = AppState.CreateDefault();
        var chosen = ViewState.Create("Work");
        state.Views.Add(chosen);
        state.MonitorViews[TwoDisplays[1]] = chosen.Id;

        state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Equal(chosen.Id, state.ViewForMonitor(TwoDisplays[1]).Id);
        Assert.NotEqual(chosen.Id, state.ViewForMonitor(TwoDisplays[0]).Id);
    }

    [Fact]
    public void A_dangling_assignment_is_replaced_rather_than_honoured()
    {
        var state = AppState.CreateDefault();
        state.MonitorViews[TwoDisplays[0]] = "view-that-was-deleted";

        state.EnsureViewPerDisplay(TwoDisplays);

        Assert.NotNull(state.FindView(state.MonitorViews[TwoDisplays[0]]));
    }

    [Fact]
    public void Adding_a_third_display_leaves_the_first_two_alone()
    {
        var state = AppState.CreateDefault();
        state.EnsureViewPerDisplay(TwoDisplays);
        var before = state.MonitorViews.ToDictionary(kv => kv.Key, kv => kv.Value);

        string[] three = [.. TwoDisplays, "\\\\.\\DISPLAY3"];
        var created = state.EnsureViewPerDisplay(three);

        Assert.Equal(1, created);
        foreach (var (display, viewId) in before)
        {
            Assert.Equal(viewId, state.MonitorViews[display]);
        }
    }

    [Fact]
    public void Spare_views_are_handed_out_before_any_are_created()
    {
        var state = AppState.CreateDefault();
        state.Views.Add(ViewState.Create("Spare"));

        var created = state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Equal(0, created);
        Assert.Equal(2, state.Views.Count);
    }

    [Fact]
    public void No_displays_is_a_no_op_rather_than_a_reset()
    {
        var state = AppState.CreateDefault();

        var created = state.EnsureViewPerDisplay([]);

        Assert.Equal(0, created);
        Assert.Empty(state.MonitorViews);
    }

    [Fact]
    public void The_result_still_passes_validation()
    {
        var state = AppState.CreateDefault();
        state.EnsureViewPerDisplay(TwoDisplays);

        Assert.Empty(Themia.Core.Storage.AppStateStore.Validate(state));
    }
}
