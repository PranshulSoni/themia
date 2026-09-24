using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

public class WidgetTransferTests
{
    private static readonly ScreenMetrics Screen = new(1920, 1080);

    private static (AppState State, ViewState Left, ViewState Right, WidgetInstance Widget) TwoViews()
    {
        var state = AppState.CreateDefault();
        var left = state.ActiveView();
        var right = ViewState.Create("Second");
        state.Views.Add(right);
        return (state, left, right, left.Widgets[0]);
    }

    // ---- hand-off decision ------------------------------------------------------------------

    [Fact]
    public void A_drop_on_the_same_display_never_hands_off()
    {
        Assert.False(WidgetTransfer.ShouldHandOff(droppedOnThisDisplay: true, "view-a", "view-b"));
    }

    [Fact]
    public void A_drop_on_a_display_showing_the_same_view_does_not_hand_off()
    {
        // Both displays already render that widget, so moving it would be a no-op that rewrote its
        // position against the wrong screen.
        Assert.False(WidgetTransfer.ShouldHandOff(droppedOnThisDisplay: false, "view-a", "view-a"));
    }

    [Fact]
    public void A_drop_outside_every_display_does_not_hand_off()
    {
        Assert.False(WidgetTransfer.ShouldHandOff(droppedOnThisDisplay: false, "view-a", null));
    }

    [Fact]
    public void A_drop_on_a_display_showing_another_view_hands_off()
    {
        Assert.True(WidgetTransfer.ShouldHandOff(droppedOnThisDisplay: false, "view-a", "view-b"));
    }

    // ---- moving between views ---------------------------------------------------------------

    [Fact]
    public void Moving_a_widget_transfers_it_without_duplicating_it()
    {
        var (state, left, right, widget) = TwoViews();

        var moved = state.MoveWidget(widget.Id, left.Id, right.Id, new FracRect(0.1, 0.2, 0.3, 0.2));

        Assert.True(moved);
        Assert.Empty(left.Widgets);
        Assert.Single(right.Widgets);
        Assert.Equal(widget.Id, right.Widgets[0].Id);
    }

    [Fact]
    public void A_moved_widget_keeps_its_identity_and_config()
    {
        var (state, left, right, widget) = TwoViews();
        ((ClockConfig)widget.Config).ShowSeconds = true;
        widget.Label = "Bedside";

        state.MoveWidget(widget.Id, left.Id, right.Id, new FracRect(0.1, 0.2, 0.3, 0.2));

        var moved = right.Widgets[0];
        Assert.Equal(widget.Id, moved.Id);
        Assert.Equal("Bedside", moved.Label);
        Assert.True(((ClockConfig)moved.Config).ShowSeconds);
    }

    [Fact]
    public void A_moved_widget_lands_at_the_requested_rect()
    {
        var (state, left, right, widget) = TwoViews();

        state.MoveWidget(widget.Id, left.Id, right.Id, new FracRect(0.11, 0.22, 0.33, 0.25));

        Assert.Equal(new FracRect(0.11, 0.22, 0.33, 0.25), right.Widgets[0].Rect);
    }

    [Fact]
    public void Moving_within_one_view_is_refused_rather_than_removing_the_widget()
    {
        var (state, left, _, widget) = TwoViews();

        var moved = state.MoveWidget(widget.Id, left.Id, left.Id, new FracRect(0.1, 0.1, 0.2, 0.2));

        Assert.False(moved);
        Assert.Single(left.Widgets);
    }

    [Fact]
    public void Moving_an_unknown_widget_or_view_changes_nothing()
    {
        var (state, left, right, widget) = TwoViews();

        Assert.False(state.MoveWidget("nope", left.Id, right.Id, new FracRect(0, 0, 0.2, 0.2)));
        Assert.False(state.MoveWidget(widget.Id, left.Id, "nope", new FracRect(0, 0, 0.2, 0.2)));
        Assert.Single(left.Widgets);
        Assert.Empty(right.Widgets);
    }

    // ---- paste across views -----------------------------------------------------------------

    [Fact]
    public void Pasting_a_copy_into_another_view_leaves_the_original_alone()
    {
        var (state, left, right, widget) = TwoViews();
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, left.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;
        state.ApplyPaste(plan, right.Id);

        Assert.Single(left.Widgets);
        Assert.Single(right.Widgets);
        Assert.NotEqual(left.Widgets[0].Id, right.Widgets[0].Id);
    }

    [Fact]
    public void Pasting_a_cut_into_another_view_moves_it()
    {
        var (state, left, right, widget) = TwoViews();
        var clipboard = new WidgetClipboard();
        clipboard.Cut(widget, left.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;
        state.ApplyPaste(plan, right.Id);

        Assert.Empty(left.Widgets);
        Assert.Single(right.Widgets);
    }

    [Fact]
    public void Pasting_a_cut_back_into_its_own_view_keeps_exactly_one_widget()
    {
        // The remove and the add both target the same list; doing them in the wrong order would
        // either lose the widget or leave two.
        var (state, left, _, widget) = TwoViews();
        var clipboard = new WidgetClipboard();
        clipboard.Cut(widget, left.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.7, 0.7, 0, 0), Screen, snapEnabled: false)!.Value;
        state.ApplyPaste(plan, left.Id);

        Assert.Single(left.Widgets);
        Assert.NotEqual(widget.Id, left.Widgets[0].Id);
    }

    [Fact]
    public void Pasting_into_a_view_that_was_deleted_falls_back_to_the_active_view()
    {
        var (state, left, right, widget) = TwoViews();
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, left.Id);
        state.Views.Remove(right);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;
        state.ApplyPaste(plan, right.Id);

        Assert.Equal(2, state.ActiveView().Widgets.Count);
    }

    [Fact]
    public void A_cut_whose_source_view_vanished_still_pastes()
    {
        var (state, left, right, widget) = TwoViews();
        var clipboard = new WidgetClipboard();
        clipboard.Cut(widget, left.Id);

        // The source view is gone by the time the paste happens.
        state.Views.Remove(left);
        state.ActiveViewId = right.Id;

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;
        state.ApplyPaste(plan, right.Id);

        Assert.Single(right.Widgets);
    }

    [Fact]
    public void A_transferred_state_still_passes_validation()
    {
        var (state, left, right, widget) = TwoViews();
        state.MoveWidget(widget.Id, left.Id, right.Id, new FracRect(0.1, 0.2, 0.3, 0.2));

        var repairs = Themia.Core.Storage.AppStateStore.Validate(state);

        Assert.Empty(repairs);
    }
}
