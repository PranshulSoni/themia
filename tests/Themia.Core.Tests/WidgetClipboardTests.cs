using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

public class WidgetClipboardTests
{
    private static readonly ScreenMetrics Screen = new(1920, 1080);

    private static (AppState State, ViewState View, WidgetInstance Widget) Fixture()
    {
        var state = AppState.CreateDefault();
        var view = state.ActiveView();
        return (state, view, view.Widgets[0]);
    }

    [Fact]
    public void An_empty_clipboard_plans_nothing()
    {
        var clipboard = new WidgetClipboard();

        Assert.False(clipboard.HasContent);
        Assert.Null(clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false));
    }

    [Fact]
    public void A_pasted_copy_gets_a_new_id_so_it_can_coexist_with_the_original()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false);

        Assert.NotNull(plan);
        Assert.NotEqual(widget.Id, plan.Value.Widget.Id);
        Assert.Equal(widget.Kind, plan.Value.Widget.Kind);
    }

    [Fact]
    public void A_copy_carries_the_config_not_a_shared_reference()
    {
        var (_, view, widget) = Fixture();
        ((ClockConfig)widget.Config).ShowSeconds = true;
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        // Mutating the original afterwards must not reach into the clipboard entry.
        ((ClockConfig)widget.Config).ShowSeconds = false;

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false);

        Assert.True(((ClockConfig)plan!.Value.Widget.Config).ShowSeconds);
    }

    [Fact]
    public void A_copy_survives_the_original_being_deleted()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);
        view.Widgets.Clear();

        Assert.True(clipboard.HasContent);
        Assert.NotNull(clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false));
    }

    [Fact]
    public void A_copy_plan_removes_nothing()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;

        Assert.Null(plan.RemoveFromViewId);
        Assert.Null(plan.RemoveWidgetId);
    }

    [Fact]
    public void A_cut_plan_names_the_original_to_remove()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        clipboard.Cut(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;

        Assert.Equal(view.Id, plan.RemoveFromViewId);
        Assert.Equal(widget.Id, plan.RemoveWidgetId);
    }

    [Fact]
    public void A_cut_does_not_remove_anything_until_it_is_pasted()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        clipboard.Cut(widget, view.Id);

        // Marking only: changing your mind after a cut must cost nothing.
        Assert.Single(view.Widgets);
        Assert.Contains(view.Widgets, w => w.Id == widget.Id);
    }

    [Fact]
    public void A_cut_is_consumed_by_one_paste_but_a_copy_is_not()
    {
        var (_, view, widget) = Fixture();

        var cut = new WidgetClipboard();
        cut.Cut(widget, view.Id);
        cut.OnPasted();
        Assert.False(cut.HasContent);

        var copy = new WidgetClipboard();
        copy.Copy(widget, view.Id);
        copy.OnPasted();
        Assert.True(copy.HasContent);
    }

    [Fact]
    public void A_paste_is_centred_on_the_drop_point()
    {
        var (_, view, widget) = Fixture();
        widget.Rect = new FracRect(0.1, 0.1, 0.2, 0.1);
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;

        Assert.Equal(0.5, plan.Widget.Rect.X + (plan.Widget.Rect.W / 2), 6);
        Assert.Equal(0.5, plan.Widget.Rect.Y + (plan.Widget.Rect.H / 2), 6);
    }

    [Fact]
    public void A_paste_keeps_the_widget_size()
    {
        var (_, view, widget) = Fixture();
        widget.Rect = new FracRect(0.1, 0.1, 0.25, 0.15);
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(0.5, 0.5, 0, 0), Screen, snapEnabled: false)!.Value;

        Assert.Equal(0.25, plan.Widget.Rect.W, 6);
        Assert.Equal(0.15, plan.Widget.Rect.H, 6);
    }

    [Fact]
    public void A_paste_at_a_screen_corner_is_clamped_fully_on_screen()
    {
        var (_, view, widget) = Fixture();
        widget.Rect = new FracRect(0.1, 0.1, 0.3, 0.3);
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var plan = clipboard.PlanPaste(new FracRect(1.0, 1.0, 0, 0), Screen, snapEnabled: true)!.Value;

        Assert.True(plan.Widget.Rect.X >= 0);
        Assert.True(plan.Widget.Rect.Y >= 0);
        Assert.True(plan.Widget.Rect.Right <= 1.0000001);
        Assert.True(plan.Widget.Rect.Bottom <= 1.0000001);
    }

    [Fact]
    public void Pasting_onto_a_differently_shaped_display_still_lands_on_screen()
    {
        var (_, view, widget) = Fixture();
        widget.Rect = new FracRect(0.4, 0.4, 0.5, 0.5);
        var clipboard = new WidgetClipboard();
        clipboard.Copy(widget, view.Id);

        var portrait = new ScreenMetrics(1080, 1920);
        var plan = clipboard.PlanPaste(new FracRect(0.9, 0.9, 0, 0), portrait, snapEnabled: false)!.Value;

        Assert.True(plan.Widget.Rect.Right <= 1.0000001);
        Assert.True(plan.Widget.Rect.Bottom <= 1.0000001);
    }

    [Fact]
    public void Clearing_raises_Changed_once_and_only_when_there_was_content()
    {
        var (_, view, widget) = Fixture();
        var clipboard = new WidgetClipboard();
        var changes = 0;
        clipboard.Changed += () => changes++;

        clipboard.Clear();          // nothing to clear
        Assert.Equal(0, changes);

        clipboard.Copy(widget, view.Id);
        Assert.Equal(1, changes);

        clipboard.Clear();
        Assert.Equal(2, changes);
    }
}
