using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-860]..[S-879] Lease key routing, containers (Group, Stack, Tabs) and the card's shell hooks.</summary>
public sealed class ContainersShellTests
{
    // ---- [S-860] lease keys reach the holder before a ScrollViewer -----------------------------

    [Theory]
    [InlineData(WidgetKey.Up)]
    [InlineData(WidgetKey.Down)]
    [InlineData(WidgetKey.PageUp)]
    [InlineData(WidgetKey.PageDown)]
    [InlineData(WidgetKey.Home)]
    [InlineData(WidgetKey.End)]
    [InlineData(WidgetKey.Enter)]
    [InlineData(WidgetKey.Tab)]
    public void S860_navigation_keys_go_to_the_holder_in_the_tunnel_before_any_control(WidgetKey key)
        => Assert.Equal(LeaseKeyRoute.ToHolder, LeaseKeyRouting.Decide(KeyPhase.Preview, key, textFocused: false));

    [Theory]
    [InlineData(WidgetKey.Up)]
    [InlineData(WidgetKey.Home)]
    [InlineData(WidgetKey.Space)]
    [InlineData(WidgetKey.Delete)]
    public void S860_a_focused_text_editor_keeps_its_own_keys(WidgetKey key)
    {
        Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Preview, key, textFocused: true));
        Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Bubble, key, textFocused: true));
    }

    [Fact]
    public void S860_keys_are_offered_once_not_again_in_the_bubble()
        => Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Bubble, WidgetKey.Down, textFocused: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void S860_escape_ends_the_lease_only_after_the_controls_had_it(bool textFocused)
    {
        Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Preview, WidgetKey.Escape, textFocused));
        Assert.Equal(LeaseKeyRoute.EndLease, LeaseKeyRouting.Decide(KeyPhase.Bubble, WidgetKey.Escape, textFocused));
    }

    [Fact]
    public void S860_a_key_no_widget_answers_passes()
    {
        Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Preview, null, textFocused: false));
        Assert.Equal(LeaseKeyRoute.Pass, LeaseKeyRouting.Decide(KeyPhase.Bubble, null, textFocused: false));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static WidgetInstance W(WidgetKind kind, string id, string? parent = null)
    {
        var widget = WidgetInstance.Create(kind, new FracRect(0.1, 0.1, 0.2, 0.2));
        widget.Id = id;
        widget.ParentId = parent;
        return widget;
    }

    private static ViewState View(params WidgetInstance[] widgets)
    {
        var view = ViewState.Create("test");
        view.Widgets.AddRange(widgets);
        return view;
    }

    private static string[] Ids(IEnumerable<WidgetInstance> widgets) => [.. widgets.Select(w => w.Id)];

    // ---- [S-862] side headers are a group's -----------------------------------------------------

    [Theory]
    [InlineData(WidgetKind.Group, HeaderPlacement.Left, true)]
    [InlineData(WidgetKind.Group, HeaderPlacement.Right, true)]
    [InlineData(WidgetKind.Clock, HeaderPlacement.Left, false)]
    [InlineData(WidgetKind.Stack, HeaderPlacement.Right, false)]
    [InlineData(WidgetKind.Clock, HeaderPlacement.Bottom, true)]
    [InlineData(WidgetKind.Tasks, HeaderPlacement.Hidden, true)]
    public void S862_only_a_group_may_put_its_header_on_a_side(WidgetKind kind, HeaderPlacement placement, bool allowed)
    {
        Assert.Equal(allowed, HeaderRules.Allows(kind, placement));
        Assert.Equal(allowed, HeaderRules.Choices(kind).Contains(placement));
    }

    [Fact]
    public void S862_a_side_header_on_another_widget_is_repaired_to_the_top_on_load()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "views": [
                {
                  "id": "v1",
                  "name": "Desktop",
                  "widgets": [
                    { "id": "clock", "kind": "Clock", "x": 0.1, "y": 0.1, "w": 0.2, "h": 0.2, "header": "Left", "config": { "kind": "clock" } },
                    { "id": "group", "kind": "Group", "x": 0.4, "y": 0.1, "w": 0.2, "h": 0.2, "header": "Right", "config": { "kind": "group" } }
                  ]
                }
              ],
              "activeViewId": "v1",
              "monitorViews": {},
              "design": { "widgetGapPx": 8 }
            }
            """;

        var result = Themia.Core.Storage.AppStateStore.Parse(json);
        var view = result.State!.Views[0];

        Assert.Equal(HeaderPlacement.Top, view.Find("clock")!.Header);
        Assert.Equal(HeaderPlacement.Right, view.Find("group")!.Header);
    }

    // ---- [S-863] the group's fill ---------------------------------------------------------------

    [Fact]
    public void S863_no_fill_is_the_theme_wash_and_junk_is_too()
    {
        Assert.Null(GroupRules.Fill(new GroupConfig()));
        Assert.Null(GroupRules.Fill(new GroupConfig { Fill = "teal" }));
        Assert.Equal(((byte)0x60, (byte)0xCD, (byte)0xFF, 0.3), GroupRules.Fill(new GroupConfig { Fill = "#60cdff", FillOpacity = 0.3 }));
        Assert.Equal(GroupConfig.DefaultFillOpacity, GroupRules.Fill(new GroupConfig { Fill = "#60cdff", FillOpacity = 7 })!.Value.Opacity);
    }

    [Fact]
    public void S863_repair_drops_a_bad_fill_opacity_and_icon()
    {
        var config = new GroupConfig { Fill = "nope", FillOpacity = -1, Icon = "two glyphs" };

        Assert.Equal(3, config.Repair().Count);
        Assert.Equal(string.Empty, config.Fill);
        Assert.Equal(GroupConfig.DefaultFillOpacity, config.FillOpacity);
        Assert.Null(config.Icon);
    }

    // ---- [S-864] a stack: nothing open, toggles, stale ids --------------------------------------

    private static readonly string[] Three = ["a", "b", "c"];

    [Fact]
    public void S864_every_child_starts_closed()
        => Assert.Equal(-1, ContainerRules.OpenIndex(Three, null));

    [Fact]
    public void S864_a_stale_open_id_is_nothing_open()
        => Assert.Equal(-1, ContainerRules.OpenIndex(Three, "gone"));

    [Fact]
    public void S864_a_click_opens_that_child_and_closes_the_other()
    {
        Assert.Equal("b", ContainerRules.Toggle(Three, null, 1));
        Assert.Equal("c", ContainerRules.Toggle(Three, "b", 2));
    }

    [Fact]
    public void S864_a_click_on_the_open_row_closes_it()
        => Assert.Null(ContainerRules.Toggle(Three, "b", 1));

    [Fact]
    public void S864_a_click_outside_the_rows_changes_nothing()
        => Assert.Equal("b", ContainerRules.Toggle(Three, "b", 7));

    // ---- [S-868] tabs: member wins, else the first ------------------------------------------------

    [Fact]
    public void S868_the_selected_tab_resolves_without_being_rewritten()
    {
        Assert.Equal(2, ContainerRules.SelectedIndex(Three, "c"));
        Assert.Equal(0, ContainerRules.SelectedIndex(Three, "gone"));
        Assert.Equal(0, ContainerRules.SelectedIndex(Three, null));
        Assert.Equal(-1, ContainerRules.SelectedIndex([], "a"));
    }

    // ---- [S-866] moving a child among its siblings ------------------------------------------------

    [Fact]
    public void S866_moving_a_child_swaps_it_with_its_sibling_and_nothing_else()
    {
        var view = View(
            W(WidgetKind.Stack, "s"),
            W(WidgetKind.Clock, "a", "s"),
            W(WidgetKind.Cpu, "x"),
            W(WidgetKind.Memory, "b", "s"));

        Assert.True(view.MoveChild("b", -1));
        Assert.Equal(["s", "b", "x", "a"], Ids(view.Widgets));
        Assert.Equal(["b", "a"], Ids(view.ChildrenOf("s")));
    }

    [Fact]
    public void S866_a_child_does_not_move_past_either_end()
    {
        var view = View(W(WidgetKind.Tabs, "t"), W(WidgetKind.Clock, "a", "t"), W(WidgetKind.Cpu, "b", "t"));

        Assert.False(view.CanMoveChild("a", -1));
        Assert.False(view.CanMoveChild("b", 1));
        Assert.True(view.CanMoveChild("a", 1));
        Assert.False(view.MoveChild("t", 1));
        Assert.Equal(["t", "a", "b"], Ids(view.Widgets));
    }

    // ---- [S-871]/[S-872] collapse to the bar, and the hit region follows paint --------------------

    private static readonly PixelRect Cell = new(100, 50, 400, 300);

    [Theory]
    [InlineData(HeaderPlacement.Top, 100, 50, 400, 40)]
    [InlineData(HeaderPlacement.Hidden, 100, 50, 400, 40)]
    [InlineData(HeaderPlacement.Bottom, 100, 310, 400, 40)]
    [InlineData(HeaderPlacement.Left, 100, 50, 40, 300)]
    [InlineData(HeaderPlacement.Right, 460, 50, 40, 300)]
    public void S871_a_collapsed_card_is_its_bar_on_the_header_edge(HeaderPlacement header, double x, double y, double w, double h)
        => Assert.Equal(new PixelRect(x, y, w, h), CardFrame.Collapsed(Cell, header, 40));

    [Fact]
    public void S871_the_bar_never_exceeds_the_cell()
        => Assert.Equal(new PixelRect(0, 0, 30, 20), CardFrame.Collapsed(new PixelRect(0, 0, 30, 20), HeaderPlacement.Left, 40));

    [Fact]
    public void S872_a_card_that_draws_nothing_takes_no_clicks()
        => Assert.Null(CardFrame.Hit(Cell, new CardHit(Shown: false, BandOnly: false, HeaderPlacement.Top, 40)));

    [Fact]
    public void S872_an_ordinary_card_takes_its_painted_rect()
        => Assert.Equal(Cell, CardFrame.Hit(Cell, new CardHit(true, false, HeaderPlacement.Top, 40)));

    [Theory]
    [InlineData(HeaderPlacement.Top, 100, 50, 400, 40)]
    [InlineData(HeaderPlacement.Right, 460, 50, 40, 300)]
    public void S872_a_see_through_group_takes_only_its_band(HeaderPlacement header, double x, double y, double w, double h)
        => Assert.Equal(new PixelRect(x, y, w, h), CardFrame.Hit(Cell, new CardHit(true, true, header, 40)));

    [Fact]
    public void S872_a_see_through_group_with_no_header_takes_nothing()
        => Assert.Null(CardFrame.Hit(Cell, new CardHit(true, true, HeaderPlacement.Hidden, 40)));

    [Fact]
    public void S872_band_only_follows_what_the_area_paints()
    {
        // The wash paints unless the background is None; a colour paints unless its opacity is 0.
        Assert.False(GroupRules.BandOnly(new GroupConfig(), WidgetSurface.Card, 2));
        Assert.True(GroupRules.BandOnly(new GroupConfig(), WidgetSurface.Transparent, 2));
        Assert.False(GroupRules.BandOnly(new GroupConfig { Fill = "#112233", FillOpacity = 0.5 }, WidgetSurface.Transparent, 2));
        Assert.True(GroupRules.BandOnly(new GroupConfig { Fill = "#112233", FillOpacity = 0 }, WidgetSurface.Card, 2));
    }

    [Fact]
    public void S872_an_empty_or_collapsed_group_is_never_band_only()
    {
        Assert.False(GroupRules.BandOnly(new GroupConfig(), WidgetSurface.Transparent, 0));
        Assert.False(GroupRules.BandOnly(new GroupConfig { Collapsed = true }, WidgetSurface.Transparent, 2));
    }

    // ---- [S-874] bring to front / send to back, and the paint order --------------------------------

    [Fact]
    public void S874_bring_to_front_moves_the_widget_to_the_end_of_the_view()
    {
        var view = View(W(WidgetKind.Clock, "a"), W(WidgetKind.Cpu, "b"), W(WidgetKind.Memory, "c"));

        Assert.True(view.BringToFront("a"));
        Assert.Equal(["b", "c", "a"], Ids(view.Widgets));
        Assert.False(view.CanBringToFront("a"));
    }

    [Fact]
    public void S874_send_to_back_moves_the_widget_to_the_start()
    {
        var view = View(W(WidgetKind.Clock, "a"), W(WidgetKind.Cpu, "b"), W(WidgetKind.Memory, "c"));

        Assert.True(view.SendToBack("c"));
        Assert.Equal(["c", "a", "b"], Ids(view.Widgets));
        Assert.False(view.CanSendToBack("c"));
        Assert.False(view.SendToBack("missing"));
    }

    [Fact]
    public void S874_a_container_moves_with_everything_inside_it()
    {
        var view = View(
            W(WidgetKind.Stack, "s"),
            W(WidgetKind.Clock, "a", "s"),
            W(WidgetKind.Cpu, "x"),
            W(WidgetKind.Memory, "b", "s"));

        Assert.True(view.BringToFront("s"));
        Assert.Equal(["x", "s", "a", "b"], Ids(view.Widgets));
    }

    [Fact]
    public void S874_a_group_paints_under_its_lowest_member_and_stack_children_not_at_all()
    {
        var view = View(
            W(WidgetKind.Clock, "m1", "g"),
            W(WidgetKind.Cpu, "x"),
            W(WidgetKind.Group, "g"),
            W(WidgetKind.Stack, "s"),
            W(WidgetKind.Memory, "inStack", "s"),
            W(WidgetKind.Group, "inner", "g"),
            W(WidgetKind.Disk, "m2", "inner"));

        Assert.Equal(["g", "m1", "x", "s", "inner", "m2"], Ids(view.PaintOrder()));
    }

    [Fact]
    public void S874_sending_a_member_back_keeps_its_group_under_it()
    {
        var view = View(W(WidgetKind.Cpu, "x"), W(WidgetKind.Group, "g"), W(WidgetKind.Clock, "m", "g"));

        view.SendToBack("m");

        Assert.Equal(["g", "m", "x"], Ids(view.PaintOrder()));
    }

    // ---- [S-865] the stack's ladder ---------------------------------------------------------------

    [Fact]
    public void S865_xs_rows_are_names_only_and_a_sliver_is_not_drawn()
    {
        var plan = ContainerLayout.Plan(new WidgetArea(120, 120), 3, anyOpen: true, 40);

        Assert.False(plan.Icons);
        Assert.False(plan.BodyShown);
        Assert.Equal(StackArrangement.Rows, plan.Arrangement);
    }

    [Fact]
    public void S865_a_tall_stack_gives_the_open_child_what_the_rows_leave()
        => Assert.Equal(new StackPlan(StackArrangement.Rows, true, true, 280), ContainerLayout.Plan(new WidgetArea(200, 400), 3, anyOpen: true, 40));

    [Fact]
    public void S865_a_wide_strip_puts_the_rows_side_by_side()
        => Assert.Equal(new StackPlan(StackArrangement.Strip, true, true, 120), ContainerLayout.Plan(new WidgetArea(480, 160), 3, anyOpen: true, 40));

    [Fact]
    public void S865_nothing_open_is_rows_only()
        => Assert.False(ContainerLayout.Plan(new WidgetArea(560, 420), 3, anyOpen: false, 40).BodyShown);

    // ---- [S-869] the tabs' ladder ----------------------------------------------------------------

    [Fact]
    public void S869_tabs_shed_from_icon_and_name_to_name_to_icon()
    {
        double[] names = [50, 60, 70];

        Assert.Equal(TabLabels.IconAndText, ContainerLayout.Tabs(560, SizeClass.Expanded, names, 16, 24));
        Assert.Equal(TabLabels.Text, ContainerLayout.Tabs(320, SizeClass.Regular, names, 16, 24));
        Assert.Equal(TabLabels.Icon, ContainerLayout.Tabs(200, SizeClass.Regular, names, 16, 24));
    }

    // ---- [S-866] drag-reorder lands a child at an index ------------------------------------------

    [Fact]
    public void S866_move_child_to_an_index_is_clamped_and_keeps_the_others_in_order()
    {
        var view = View(W(WidgetKind.Stack, "s"), W(WidgetKind.Clock, "a", "s"), W(WidgetKind.Cpu, "b", "s"), W(WidgetKind.Disk, "c", "s"));

        Assert.True(view.MoveChildTo("a", 9));
        Assert.Equal(["b", "c", "a"], Ids(view.ChildrenOf("s")));
        Assert.True(view.MoveChildTo("a", 0));
        Assert.Equal(["a", "b", "c"], Ids(view.ChildrenOf("s")));
        Assert.False(view.MoveChildTo("a", 0));
    }

    // ---- [S-875] drop to join, drop to leave ------------------------------------------------------

    private static WidgetInstance At(WidgetKind kind, string id, double x, double y, double w, double h, string? parent = null)
    {
        var widget = W(kind, id, parent);
        widget.Rect = new FracRect(x, y, w, h);
        return widget;
    }

    [Fact]
    public void S875_dropping_onto_a_container_joins_it()
    {
        var view = View(At(WidgetKind.Stack, "s", 0.1, 0.1, 0.3, 0.3), At(WidgetKind.Clock, "c", 0.6, 0.6, 0.1, 0.1));

        Assert.Equal("s", view.DropTarget("c", 0.2, 0.2));
        Assert.Null(view.DropTarget("c", 0.8, 0.8));
    }

    [Fact]
    public void S875_the_topmost_container_under_the_pointer_wins()
    {
        var view = View(
            At(WidgetKind.Group, "g", 0.0, 0.0, 0.6, 0.6),
            At(WidgetKind.Tabs, "t", 0.1, 0.1, 0.3, 0.3),
            At(WidgetKind.Clock, "c", 0.7, 0.7, 0.1, 0.1));

        Assert.Equal("t", view.DropTarget("c", 0.2, 0.2));
        Assert.Equal("g", view.DropTarget("c", 0.5, 0.5));
    }

    [Fact]
    public void S875_dropped_outside_its_group_a_member_leaves_and_inside_it_stays()
    {
        var view = View(At(WidgetKind.Group, "g", 0.0, 0.0, 0.5, 0.5), At(WidgetKind.Clock, "c", 0.1, 0.1, 0.1, 0.1, "g"));

        Assert.Null(view.DropTarget("c", 0.2, 0.2));
        Assert.Equal(string.Empty, view.DropTarget("c", 0.8, 0.8));
    }

    [Fact]
    public void S875_a_container_is_never_dropped_into_itself_or_a_collapsed_group()
    {
        var group = At(WidgetKind.Group, "g", 0.0, 0.0, 0.5, 0.5);
        ((GroupConfig)group.Config).Collapsed = true;
        var view = View(group, At(WidgetKind.Stack, "s", 0.1, 0.1, 0.2, 0.2), At(WidgetKind.Clock, "c", 0.7, 0.7, 0.1, 0.1));

        Assert.Null(view.DropTarget("s", 0.15, 0.15));
        Assert.Null(view.DropTarget("c", 0.4, 0.4));
    }

    // ---- [S-876] a container is copied, cut and moved with what it holds ------------------------

    private static readonly ScreenMetrics Screen = new(1000, 1000);

    [Fact]
    public void S876_copying_a_group_copies_its_members_shifted_with_it()
    {
        var group = At(WidgetKind.Group, "g", 0.1, 0.1, 0.4, 0.4);
        var member = At(WidgetKind.Clock, "m", 0.2, 0.2, 0.1, 0.1, "g");
        var view = View(group, member);
        var clipboard = new WidgetClipboard();
        clipboard.Copy(view, [group.Id]);

        var plan = clipboard.Payload!.Plan(0.5, 0.5, Screen, snapEnabled: false);
        var pastedGroup = plan.Added.Single(w => w.Kind == WidgetKind.Group);
        var copy = plan.Added.Single(w => w.Kind == WidgetKind.Clock);

        Assert.NotEqual("m", copy.Id);
        Assert.Equal(pastedGroup.Id, copy.ParentId);
        Assert.Equal([pastedGroup.Id], plan.Roots);
        Assert.Equal(member.X + (pastedGroup.X - group.X), copy.X, 6);
        Assert.Equal(member.Y + (pastedGroup.Y - group.Y), copy.Y, 6);

        // A second paste of the same copy gets ids of its own.
        var again = clipboard.Payload!.Plan(0.5, 0.5, Screen, snapEnabled: false);
        Assert.DoesNotContain(again.Added, w => w.Id == copy.Id);
    }

    [Fact]
    public void S876_pasting_a_cut_container_removes_the_original_with_its_members()
    {
        var state = AppState.CreateDefault();
        var view = state.ActiveView();
        var stack = At(WidgetKind.Stack, "s", 0.1, 0.1, 0.3, 0.3);
        var child = At(WidgetKind.Clock, "a", 0.1, 0.1, 0.1, 0.1, "s");
        view.Widgets.AddRange([stack, child, At(WidgetKind.Cpu, "x", 0.6, 0.6, 0.1, 0.1)]);

        var clipboard = new WidgetClipboard();
        clipboard.Cut(view, [stack.Id]);
        state.ApplyPlacement(clipboard.Payload!.Plan(0.5, 0.5, Screen, snapEnabled: false), view.Id);

        // Moved, not duplicated: one stack, the same one, still holding its child.
        Assert.Same(stack, view.Widgets.Single(w => w.Kind == WidgetKind.Stack));
        Assert.Equal("s", view.Find("a")!.ParentId);
        Assert.Equal(3, view.Widgets.Count(w => w.Id is "s" or "a" or "x"));
    }

    [Fact]
    public void S876_moving_a_container_to_another_view_takes_its_members()
    {
        var state = AppState.CreateDefault();
        var left = state.ActiveView();
        var right = ViewState.Create("right");
        state.Views.Add(right);
        var group = At(WidgetKind.Group, "g", 0.1, 0.1, 0.4, 0.4);
        var member = At(WidgetKind.Clock, "m", 0.2, 0.2, 0.1, 0.1, "g");
        left.Widgets.AddRange([group, member]);

        var payload = PlacementPayload.FromView(left, ["g"], PlacementMode.Move)!;
        state.ApplyPlacement(payload.Plan(0.5, 0.5, Screen, snapEnabled: false), right.Id);

        Assert.DoesNotContain(left.Widgets, w => w.Id is "g" or "m");
        Assert.Equal("g", right.Find("m")!.ParentId);
        Assert.Equal(0.4, right.Find("m")!.X, 6);
    }

    [Theory]
    [InlineData(0, 5.0, 0)]
    [InlineData(0, 95.0, 2)]
    [InlineData(2, 5.0, 0)]
    [InlineData(1, 45.0, 1)]
    [InlineData(1, 95.0, 2)]
    public void S866_a_dragged_row_lands_after_the_rows_whose_centres_it_passed(int from, double at, int slot)
        => Assert.Equal(slot, ContainerLayout.DropSlot([20.0, 60.0, 90.0], from, at));
}
