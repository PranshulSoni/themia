using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>Multi-select and the one placement session ([S-1580]..[S-1593]).</summary>
public sealed class MultiSelectPlacementTests
{
    private static readonly ScreenMetrics Screen = new(1000, 1000);

    private static WidgetInstance At(WidgetKind kind, string id, double x, double y, double w, double h, string? parent = null)
    {
        var widget = WidgetInstance.Create(kind, new FracRect(x, y, w, h));
        widget.Id = id;
        widget.ParentId = parent;
        return widget;
    }

    private static ViewState View(params WidgetInstance[] widgets)
    {
        var view = ViewState.Create("v");
        view.Widgets.AddRange(widgets);
        return view;
    }

    // ---- [S-1580] the selection ----------------------------------------------------------------

    [Fact]
    public void S1580_ctrl_click_toggles_and_the_last_picked_is_primary()
    {
        var selection = new WidgetSelection();

        Assert.True(selection.Toggle("v", "a"));
        Assert.True(selection.Toggle("v", "b"));
        Assert.Equal(["a", "b"], selection.Ids);
        Assert.Equal("b", selection.Primary);

        Assert.True(selection.Toggle("v", "a"));
        Assert.Equal(["b"], selection.Ids);

        Assert.True(selection.Toggle("v", "b"));
        Assert.True(selection.IsEmpty);
        Assert.Null(selection.ViewId);
    }

    [Fact]
    public void S1580_picking_in_another_view_starts_over_rather_than_mixing_views()
    {
        var selection = new WidgetSelection();
        selection.Replace("left", ["a", "b"]);

        selection.Toggle("right", "c");

        Assert.Equal("right", selection.ViewId);
        Assert.Equal(["c"], selection.Ids);
    }

    [Fact]
    public void S1580_a_plain_click_selects_exactly_one_and_null_clears()
    {
        var selection = new WidgetSelection();
        selection.Replace("v", ["a", "b"]);

        Assert.True(selection.Set("v", "b"));
        Assert.Equal(["b"], selection.Ids);
        Assert.False(selection.Set("v", "b"));

        Assert.True(selection.Set(null, null));
        Assert.True(selection.IsEmpty);
        Assert.False(selection.Clear());
    }

    [Fact]
    public void S1580_replace_drops_duplicates_and_reports_no_change_for_the_same_set()
    {
        var selection = new WidgetSelection();

        Assert.True(selection.Replace("v", ["a", "a", "b", ""]));
        Assert.Equal(["a", "b"], selection.Ids);
        Assert.False(selection.Replace("v", ["a", "b"]));
    }

    [Fact]
    public void S1583_pruning_drops_deleted_widgets_and_a_vanished_view()
    {
        var state = AppState.CreateDefault();
        var view = state.ActiveView();
        view.Widgets.Clear();
        view.Widgets.AddRange([At(WidgetKind.Clock, "a", 0, 0, 0.1, 0.1), At(WidgetKind.Cpu, "b", 0.2, 0, 0.1, 0.1)]);
        var selection = new WidgetSelection();
        selection.Replace(view.Id, ["a", "b"]);

        view.Widgets.RemoveAll(w => w.Id == "a");
        Assert.True(selection.Prune(state));
        Assert.Equal(["b"], selection.Ids);
        Assert.False(selection.Prune(state));

        state.Views.Clear();
        Assert.True(selection.Prune(state));
        Assert.True(selection.IsEmpty);
    }

    // ---- [S-1581] / [S-1582] rubber band and Ctrl+A -----------------------------------------------

    [Fact]
    public void S1581_the_band_selects_every_widget_it_touches_in_view_order()
    {
        var view = View(
            At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1),
            At(WidgetKind.Cpu, "b", 0.5, 0.5, 0.1, 0.1),
            At(WidgetKind.Memory, "c", 0.18, 0.3, 0.1, 0.1));

        // Touches a (corner) and c (edge), not b.
        var hits = SelectionRules.Lasso(view, new FracRect(0.15, 0.15, 0.1, 0.2));

        Assert.Equal(["a", "c"], hits);
    }

    [Fact]
    public void S1581_the_band_skips_widgets_a_stack_lays_out_and_hidden_ones()
    {
        var view = View(
            At(WidgetKind.Stack, "s", 0.5, 0.5, 0.3, 0.3),
            At(WidgetKind.Clock, "child", 0.1, 0.1, 0.1, 0.1, "s"),
            At(WidgetKind.Group, "g", 0, 0, 0.4, 0.4),
            At(WidgetKind.Cpu, "member", 0.2, 0.2, 0.1, 0.1, "g"),
            At(WidgetKind.Memory, "hidden", 0.05, 0.05, 0.05, 0.05, "g"));

        var hits = SelectionRules.Lasso(view, new FracRect(0, 0, 0.45, 0.45), new HashSet<string> { "hidden" });

        // The stack's child keeps a return rect under the band, but the stack draws it elsewhere.
        Assert.Equal(["g", "member"], hits);
    }

    [Fact]
    public void S1581_a_band_of_no_area_touches_nothing()
    {
        var view = View(At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1));

        Assert.Empty(SelectionRules.Lasso(view, new FracRect(0.15, 0.15, 0, 0)));
        Assert.Empty(SelectionRules.Lasso(view, new FracRect(double.NaN, 0, 1, 1)));
    }

    [Fact]
    public void S1582_select_all_takes_every_grid_placed_widget()
    {
        var view = View(
            At(WidgetKind.Tabs, "t", 0.5, 0.5, 0.3, 0.3),
            At(WidgetKind.Clock, "tab", 0.1, 0.1, 0.1, 0.1, "t"),
            At(WidgetKind.Cpu, "c", 0, 0, 0.1, 0.1));

        Assert.Equal(["t", "c"], SelectionRules.All(view));
    }

    [Fact]
    public void S1588_top_level_drops_selected_members_of_a_selected_container()
    {
        var view = View(
            At(WidgetKind.Group, "g", 0, 0, 0.5, 0.5),
            At(WidgetKind.Stack, "s", 0.1, 0.1, 0.2, 0.2, "g"),
            At(WidgetKind.Clock, "deep", 0.1, 0.1, 0.1, 0.1, "s"),
            At(WidgetKind.Cpu, "free", 0.7, 0.7, 0.1, 0.1));

        Assert.Equal(["g", "free"], SelectionRules.TopLevel(view, ["deep", "free", "g", "missing"]));
    }

    [Fact]
    public void S1584_a_drag_moves_a_group_and_its_selected_members_but_not_stack_children()
    {
        var view = View(
            At(WidgetKind.Group, "g", 0, 0, 0.5, 0.5),
            At(WidgetKind.Cpu, "member", 0.1, 0.1, 0.1, 0.1, "g"),
            At(WidgetKind.Stack, "s", 0.6, 0.6, 0.2, 0.2),
            At(WidgetKind.Clock, "child", 0.6, 0.6, 0.1, 0.1, "s"));

        Assert.Equal(["g", "member", "s"], SelectionRules.Movable(view, ["s", "child", "member", "g"]));
    }

    // ---- [S-1584] block geometry ------------------------------------------------------------------

    [Fact]
    public void S1584_a_block_moves_by_one_translation_keeping_offsets()
    {
        FracRect[] start = [new(0.1, 0.1, 0.1, 0.1), new(0.3, 0.2, 0.2, 0.1)];

        var delta = BlockGeometry.Move(start, 0.05, 0.1, Screen, snapEnabled: false);
        var moved = start.Select(r => BlockGeometry.Apply(r, delta, Screen)).ToList();

        Assert.Equal(0.15, moved[0].X, 9);
        Assert.Equal(0.35, moved[1].X, 9);
        Assert.Equal(moved[1].X - moved[0].X, start[1].X - start[0].X, 9);
        Assert.Equal(moved[1].Y - moved[0].Y, start[1].Y - start[0].Y, 9);
        Assert.Equal(0.2, moved[1].W, 9);
    }

    [Fact]
    public void S1584_a_block_is_clamped_as_a_whole_so_offsets_survive_the_edge()
    {
        FracRect[] start = [new(0.6, 0.1, 0.1, 0.1), new(0.8, 0.1, 0.1, 0.1)];

        // Far past the right edge: the block stops with its right side on the edge.
        var delta = BlockGeometry.Move(start, 0.5, 0, Screen, snapEnabled: false);
        var moved = start.Select(r => BlockGeometry.Apply(r, delta, Screen)).ToList();

        Assert.Equal(1.0, moved[1].Right, 9);
        Assert.Equal(0.2, moved[1].X - moved[0].X, 9);
    }

    [Fact]
    public void S1584_a_block_is_kept_inside_the_work_area()
    {
        var screen = new ScreenMetrics(1000, 1000) { Allowed = new FracRect(0, 0, 1, 0.9) };
        FracRect[] start = [new(0.1, 0.5, 0.1, 0.1), new(0.1, 0.7, 0.1, 0.1)];

        var delta = BlockGeometry.Move(start, 0, 0.5, screen, snapEnabled: false);

        Assert.Equal(0.9, BlockGeometry.Apply(start[1], delta, screen).Bottom, 9);
        Assert.Equal(0.6, BlockGeometry.Apply(start[0], delta, screen).Y, 9);
    }

    [Fact]
    public void S1584_a_block_snaps_at_its_corner_and_no_widget_changes_size()
    {
        // 1000 DIPs → 100 snap cells of 0.01.
        FracRect[] start = [new(0.1, 0.1, 0.123, 0.1), new(0.3, 0.1, 0.1, 0.1)];

        var delta = BlockGeometry.Move(start, 0.0042, 0.0061, Screen, snapEnabled: true);
        var moved = start.Select(r => BlockGeometry.Apply(r, delta, Screen)).ToList();

        Assert.Equal(0.10, moved[0].X, 9);
        Assert.Equal(0.11, moved[0].Y, 9);
        Assert.Equal(0.123, moved[0].W, 9);
        Assert.Equal(0.2, moved[1].X - moved[0].X, 9);
    }

    [Fact]
    public void S1584_a_block_bigger_than_the_screen_sits_at_the_top_left_and_each_widget_stays_on_it()
    {
        FracRect[] start = [new(0.0, 0.0, 0.6, 0.2), new(0.7, 0.0, 0.6, 0.2)];

        var delta = BlockGeometry.Move(start, 0.3, 0, Screen, snapEnabled: false);
        var moved = start.Select(r => BlockGeometry.Apply(r, delta, Screen)).ToList();

        Assert.Equal(0.0, moved[0].X, 9);
        Assert.True(moved[1].Right <= 1.0000001);
    }

    [Fact]
    public void S1584_non_finite_input_does_not_move_anything()
    {
        var delta = BlockGeometry.Move([new FracRect(0.1, 0.1, 0.1, 0.1)], double.NaN, 0, Screen, snapEnabled: true);

        Assert.Equal(default, delta);
    }

    // ---- [S-1585] / [S-1586] the payload's ghost and landing ---------------------------------------

    [Fact]
    public void S1586_a_payload_is_centred_on_the_pointer_as_a_block()
    {
        var view = View(At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1), At(WidgetKind.Cpu, "b", 0.3, 0.1, 0.1, 0.1));
        var payload = PlacementPayload.FromView(view, ["a", "b"], PlacementMode.Copy)!;

        var outlines = payload.Outlines(0.5, 0.5, Screen, snapEnabled: false);

        // Bounding box 0.1..0.4 x 0.1..0.2, centred on (0.5, 0.5).
        Assert.Equal(0.35, outlines[0].Rect.X, 9);
        Assert.Equal(0.55, outlines[1].Rect.X, 9);
        Assert.Equal(0.45, outlines[0].Rect.Y, 9);
    }

    [Fact]
    public void S1586_a_hand_off_centres_the_held_widget_on_the_pointer()
    {
        var view = View(At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1), At(WidgetKind.Cpu, "b", 0.3, 0.1, 0.1, 0.1));
        var payload = PlacementPayload.FromView(view, ["a", "b"], PlacementMode.Move, anchorId: "b")!;

        var plan = payload.Plan(0.5, 0.5, Screen, snapEnabled: false);

        var b = plan.Added.Single(w => w.Id == "b");
        Assert.Equal(0.5, b.X + (b.W / 2), 9);
        Assert.Equal(0.25, plan.Added.Single(w => w.Id == "a").X, 9);
    }

    [Fact]
    public void S1585_the_ghost_is_exactly_where_the_plan_lands_and_skips_what_a_stack_draws()
    {
        var view = View(
            At(WidgetKind.Stack, "s", 0.1, 0.1, 0.3, 0.3),
            At(WidgetKind.Clock, "child", 0.1, 0.1, 0.1, 0.1, "s"),
            At(WidgetKind.Cpu, "c", 0.5, 0.1, 0.1, 0.1));
        var payload = PlacementPayload.FromView(view, ["s", "c"], PlacementMode.Copy)!;

        var outlines = payload.Outlines(0.6, 0.7, Screen, snapEnabled: true);
        var plan = payload.Plan(0.6, 0.7, Screen, snapEnabled: true);

        Assert.Equal([WidgetKind.Stack, WidgetKind.Cpu], outlines.Select(o => o.Kind));
        Assert.Equal(plan.Added.Single(w => w.Kind == WidgetKind.Stack).Rect, outlines[0].Rect);
        Assert.Equal(plan.Added.Single(w => w.Kind == WidgetKind.Cpu).Rect, outlines[1].Rect);
    }

    [Fact]
    public void S1587_a_new_widget_payload_lands_one_widget_of_its_kind_centred_and_counts_one()
    {
        var payload = PlacementPayload.ForNew(WidgetKind.Clock, 200, 100);

        var plan = payload.Plan(0.5, 0.5, Screen, snapEnabled: false);
        var widget = Assert.Single(plan.Added);

        Assert.Equal(WidgetKind.Clock, widget.Kind);
        Assert.Equal(new FracRect(0.4, 0.45, 0.2, 0.1), widget.Rect);
        Assert.Equal([widget.Id], plan.Roots);
        Assert.Empty(plan.RemoveIds);
        Assert.Equal(1, payload.Creates);
        Assert.Equal(WidgetKind.Clock, payload.SingleKind);
    }

    [Fact]
    public void S1587_a_new_widget_on_the_taskbar_lands_just_above_it()
    {
        var screen = new ScreenMetrics(1000, 1000) { Allowed = new FracRect(0, 0, 1, 0.95) };

        var rect = PlacementPayload.ForNew(WidgetKind.Clock, 200, 100).Plan(0.5, 0.99, screen, snapEnabled: false).Added[0].Rect;

        Assert.Equal(0.95, rect.Bottom, 9);
    }

    [Fact]
    public void S1587_only_creating_payloads_count_against_the_limit_before_the_ghost_appears()
    {
        var view = View(At(WidgetKind.Group, "g", 0, 0, 0.5, 0.5), At(WidgetKind.Clock, "m", 0.1, 0.1, 0.1, 0.1, "g"));

        Assert.Equal(2, PlacementPayload.FromView(view, ["g"], PlacementMode.Copy)!.Creates);
        Assert.Equal(0, PlacementPayload.FromView(view, ["g"], PlacementMode.Move)!.Creates);
    }

    [Fact]
    public void S1588_a_payload_holds_the_picked_containers_with_everything_inside_them()
    {
        var view = View(
            At(WidgetKind.Group, "g", 0, 0, 0.5, 0.5),
            At(WidgetKind.Clock, "m", 0.1, 0.1, 0.1, 0.1, "g"),
            At(WidgetKind.Cpu, "other", 0.7, 0.7, 0.1, 0.1));

        var payload = PlacementPayload.FromView(view, ["m", "g"], PlacementMode.Copy)!;

        Assert.Equal(["g"], payload.RootIds);
        Assert.Equal(["g", "m"], payload.Widgets.Select(w => w.Id));
        Assert.Equal(1, payload.Count);
        Assert.True(payload.Holds("m"));
        Assert.False(payload.Holds("other"));
        Assert.Null(PlacementPayload.FromView(view, ["missing"], PlacementMode.Copy));
    }

    [Fact]
    public void S1589_every_landing_of_a_copy_gets_fresh_ids_and_a_root_lands_on_the_desktop()
    {
        var view = View(
            At(WidgetKind.Group, "g", 0, 0, 0.5, 0.5),
            At(WidgetKind.Clock, "m", 0.1, 0.1, 0.1, 0.1, "g"));
        var payload = PlacementPayload.FromView(view, ["m"], PlacementMode.Copy)!;

        var first = payload.Plan(0.5, 0.5, Screen, snapEnabled: false).Added.Single();
        var second = payload.Plan(0.5, 0.5, Screen, snapEnabled: false).Added.Single();

        Assert.NotEqual("m", first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Null(first.ParentId);
    }

    [Fact]
    public void S1590_a_move_keeps_ids_and_remaps_nothing()
    {
        var view = View(
            At(WidgetKind.Stack, "s", 0.1, 0.1, 0.3, 0.3),
            At(WidgetKind.Clock, "child", 0.1, 0.1, 0.1, 0.1, "s"));
        var plan = PlacementPayload.FromView(view, ["s"], PlacementMode.Move)!.Plan(0.5, 0.5, Screen, snapEnabled: false);

        Assert.Equal(["s", "child"], plan.Added.Select(w => w.Id));
        Assert.Equal("s", plan.Added[1].ParentId);
        Assert.Equal(["s"], plan.RemoveIds);
        Assert.Equal(["s"], plan.Roots);
    }

    [Fact]
    public void S1590_a_move_across_views_lands_the_whole_selection_once()
    {
        var state = AppState.CreateDefault();
        var left = state.ActiveView();
        left.Widgets.Clear();
        var right = ViewState.Create("right");
        state.Views.Add(right);
        left.Widgets.AddRange([At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1), At(WidgetKind.Cpu, "b", 0.3, 0.1, 0.1, 0.1), At(WidgetKind.Memory, "stay", 0.6, 0.6, 0.1, 0.1)]);

        var plan = PlacementPayload.FromView(left, ["a", "b"], PlacementMode.Move)!.Plan(0.5, 0.5, Screen, snapEnabled: false);
        Assert.Equal(0, plan.NetAdded(state));
        state.ApplyPlacement(plan, right.Id);

        Assert.Equal(["stay"], left.Widgets.Select(w => w.Id));
        Assert.Equal(["a", "b"], right.Widgets.Select(w => w.Id));
        Assert.Equal(0.2, right.Find("b")!.X - right.Find("a")!.X, 9);
    }
}
