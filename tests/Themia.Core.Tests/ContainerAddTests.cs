using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>Creating a widget straight inside a container from the picker.</summary>
public sealed class ContainerAddTests
{
    private static ViewState View(params WidgetInstance[] widgets)
    {
        var view = ViewState.Create("Desktop");
        view.Widgets.AddRange(widgets);
        return view;
    }

    private static WidgetInstance Widget(string id, WidgetKind kind, FracRect rect, string? parent = null)
        => new()
        {
            Id = id,
            Kind = kind,
            Config = WidgetConfig.CreateDefault(kind),
            ParentId = parent,
            X = rect.X,
            Y = rect.Y,
            W = rect.W,
            H = rect.H,
        };

    private static readonly FracRect Area = new(0.2, 0.2, 0.4, 0.5);

    [Fact]
    public void S953_StackChildTakesTheContainersRectAndGoesLast()
    {
        var view = View(
            Widget("s", WidgetKind.Stack, Area),
            Widget("a", WidgetKind.Clock, new FracRect(0, 0, 0.1, 0.1), "s"));

        var added = view.AddInto("s", WidgetKind.Cpu, 0.1, 0.1);

        Assert.NotNull(added);
        Assert.Equal("s", added.ParentId);
        Assert.Equal(Area, added.Rect);
        Assert.Equal(WidgetKind.Cpu, added.Kind);
        Assert.Equal(["a", added.Id], view.ChildrenOf("s").Select(w => w.Id));
    }

    [Fact]
    public void S953_TabsChildTakesTheContainersRect()
    {
        var view = View(Widget("t", WidgetKind.Tabs, Area));
        var added = view.AddInto("t", WidgetKind.Notes, 0.3, 0.3);
        Assert.Equal(Area, added?.Rect);
    }

    [Fact]
    public void S952_GroupMemberGetsItsDefaultSizeInsideTheArea()
    {
        var view = View(Widget("g", WidgetKind.Group, Area));

        var added = view.AddInto("g", WidgetKind.Clock, 0.1, 0.08, topInset: 0.04);

        Assert.NotNull(added);
        var r = added.Rect;
        Assert.Equal(0.1, r.W, 9);
        Assert.Equal(0.08, r.H, 9);
        Assert.Equal(Area.X + (Area.W * ContainerAdd.AreaPad), r.X, 9);
        Assert.Equal(Area.Y + 0.04 + (Area.H * ContainerAdd.AreaPad), r.Y, 9);
    }

    [Fact]
    public void S952_GroupMembersCascadeAndRestartAfterFiveSteps()
    {
        var first = ContainerAdd.AreaChildRect(Area, 0.1, 0.1, 0);
        var second = ContainerAdd.AreaChildRect(Area, 0.1, 0.1, 1);
        var sixth = ContainerAdd.AreaChildRect(Area, 0.1, 0.1, 5);

        Assert.True(second.X > first.X);
        Assert.True(second.Y > first.Y);
        Assert.Equal(first, sixth);
    }

    [Theory]
    [InlineData(0.9, 0.9, 0)]
    [InlineData(0.1, 0.1, 4)]
    [InlineData(0.39, 0.49, 3)]
    public void S952_GroupMemberAlwaysStaysInsideTheArea(double w, double h, int index)
    {
        var r = ContainerAdd.AreaChildRect(Area, w, h, index, topInset: 0.05);

        Assert.True(r.X >= Area.X - 1e-9);
        Assert.True(r.Y >= Area.Y + 0.05 - 1e-9);
        Assert.True(r.Right <= Area.Right + 1e-9);
        Assert.True(r.Bottom <= Area.Bottom + 1e-9);
        Assert.True(r.W >= 0 && r.H >= 0);
    }

    [Fact]
    public void S952_NonFiniteSizeNeverEscapesTheArea()
    {
        var r = ContainerAdd.AreaChildRect(Area, double.NaN, double.PositiveInfinity, 0, double.NaN);
        Assert.True(r.IsFinite);
        Assert.True(r.Right <= Area.Right + 1e-9 && r.Bottom <= Area.Bottom + 1e-9);
    }

    [Fact]
    public void S951_RefusesAMissingTargetOrOneThatIsNotAContainer()
    {
        var view = View(Widget("c", WidgetKind.Clock, Area));

        Assert.Null(view.AddInto("c", WidgetKind.Cpu, 0.1, 0.1));
        Assert.Null(view.AddInto("gone", WidgetKind.Cpu, 0.1, 0.1));
        Assert.Single(view.Widgets);
    }

    [Fact]
    public void S951_RefusesNestingDeeperThanTheTreeAllows()
    {
        // Three containers deep already: a fourth level does not fit under MaxDepth.
        var view = View(
            Widget("g1", WidgetKind.Group, Area),
            Widget("s2", WidgetKind.Stack, Area, "g1"),
            Widget("t3", WidgetKind.Tabs, Area, "s2"),
            Widget("s4", WidgetKind.Stack, Area, "t3"));

        Assert.True(view.CanAddInto("t3"));
        Assert.False(view.CanAddInto("s4"));
        Assert.Null(view.AddInto("s4", WidgetKind.Clock, 0.1, 0.1));
        Assert.Equal(4, view.Widgets.Count);
    }

    [Fact]
    public void S951_AContainerMayBeCreatedInsideAContainer()
    {
        var view = View(Widget("g", WidgetKind.Group, Area));
        var added = view.AddInto("g", WidgetKind.Stack, 0.2, 0.2);
        Assert.Equal("g", added?.ParentId);
    }
}
