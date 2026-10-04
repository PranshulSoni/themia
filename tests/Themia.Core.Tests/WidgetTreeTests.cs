using Themia.Core.Geometry;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Container membership. Every case here is either something the UI cannot produce but a hand-edited
/// state file can, or something that is painful to reproduce by dragging widgets around.
/// </summary>
public sealed class WidgetTreeTests
{
    private static ViewState View(params WidgetInstance[] widgets)
    {
        var view = ViewState.Create("Desktop");
        view.Widgets.AddRange(widgets);
        return view;
    }

    private static WidgetInstance Widget(string id, WidgetKind kind = WidgetKind.Clock, string? parent = null)
        => new()
        {
            Id = id,
            Kind = kind,
            Config = WidgetConfig.CreateDefault(kind),
            ParentId = parent,
            X = 0,
            Y = 0,
            W = 0.1,
            H = 0.1,
        };

    // ---- what is a container ---------------------------------------------------------------

    [Theory]
    [InlineData(WidgetKind.Group, ContainerKind.Group)]
    [InlineData(WidgetKind.Stack, ContainerKind.Stack)]
    [InlineData(WidgetKind.Tabs, ContainerKind.Tabs)]
    [InlineData(WidgetKind.Clock, ContainerKind.None)]
    [InlineData(WidgetKind.Folder, ContainerKind.None)]
    public void S220_ContainerKindsAreAClosedSet(WidgetKind kind, ContainerKind expected)
        => Assert.Equal(expected, WidgetTree.ContainerOf(kind));

    /// <summary>A group leaves its children where they were; a stack and a tab strip do not.</summary>
    [Fact]
    public void S220_OnlyStackAndTabsOwnTheirChildrensGeometry()
    {
        Assert.False(WidgetTree.OwnsChildGeometry(ContainerKind.Group));
        Assert.True(WidgetTree.OwnsChildGeometry(ContainerKind.Stack));
        Assert.True(WidgetTree.OwnsChildGeometry(ContainerKind.Tabs));
    }

    // ---- membership ------------------------------------------------------------------------

    [Fact]
    public void S221_ChildrenAndRootsPartitionTheView()
    {
        var view = View(
            Widget("g", WidgetKind.Group),
            Widget("a", parent: "g"),
            Widget("b", parent: "g"),
            Widget("c"));

        Assert.Equal(["a", "b"], view.ChildrenOf("g").ConvertAll(w => w.Id));
        Assert.Equal(["g", "c"], view.Roots().ConvertAll(w => w.Id));
    }

    /// <summary>An orphan is on the desktop, not in limbo: a parent that no longer exists is no parent.</summary>
    [Fact]
    public void S221_AWidgetWhoseParentIsGoneCountsAsARoot()
    {
        var view = View(Widget("a", parent: "vanished"));

        Assert.Equal(["a"], view.Roots().ConvertAll(w => w.Id));
        Assert.Equal(0, view.DepthOf("a"));
    }

    [Fact]
    public void S221_DepthCountsContainersNotWidgets()
    {
        var view = View(
            Widget("g", WidgetKind.Group),
            Widget("s", WidgetKind.Stack, parent: "g"),
            Widget("a", parent: "s"));

        Assert.Equal(0, view.DepthOf("g"));
        Assert.Equal(1, view.DepthOf("s"));
        Assert.Equal(2, view.DepthOf("a"));
    }

    // ---- cycles ----------------------------------------------------------------------------

    /// <summary>
    /// The case that matters: putting a container inside itself detaches the whole branch into a ring
    /// that nothing can reach and no layout pass can terminate on.
    /// </summary>
    [Fact]
    public void S222_AContainerCannotBePutInsideItself()
    {
        var view = View(Widget("g", WidgetKind.Group));
        Assert.False(view.CanReparent("g", "g"));
        Assert.False(view.Reparent("g", "g"));
        Assert.Null(view.Find("g")!.ParentId);
    }

    [Fact]
    public void S222_AContainerCannotBePutInsideItsOwnDescendant()
    {
        var view = View(
            Widget("outer", WidgetKind.Group),
            Widget("inner", WidgetKind.Stack, parent: "outer"));

        Assert.False(view.CanReparent("outer", "inner"));
        Assert.Equal("outer", view.Find("inner")!.ParentId);
    }

    [Fact]
    public void S222_ACycleAlreadyOnDiskIsRepairedRatherThanHanging()
    {
        var view = View(
            Widget("a", WidgetKind.Group, parent: "b"),
            Widget("b", WidgetKind.Group, parent: "a"));

        var repaired = view.RepairTree();

        // The ring is broken, not flattened: what is left is a legal tree, and a second pass finds
        // nothing to do. Demanding that every widget end up on the desktop would be demanding that
        // the repair throw away a parent it did not have to.
        Assert.True(repaired > 0);
        Assert.Equal(0, view.RepairTree());
        Assert.All(view.Widgets, w => Assert.True(view.DepthOf(w.Id) <= WidgetTree.MaxDepth));
    }

    // ---- what may hold what ----------------------------------------------------------------

    [Fact]
    public void S223_OnlyAContainerMayBeAParent()
    {
        var view = View(Widget("clock"), Widget("a"));
        Assert.False(view.CanReparent("a", "clock"));
    }

    [Fact]
    public void S223_MovingBackToTheDesktopIsAlwaysAllowed()
    {
        var view = View(Widget("g", WidgetKind.Group), Widget("a", parent: "g"));

        Assert.True(view.Reparent("a", null));
        Assert.Null(view.Find("a")!.ParentId);
    }

    [Fact]
    public void S223_NestingStopsAtTheDepthLimit()
    {
        // Fill every legal level: g0 sits on the desktop at depth 0, so the deepest legal widget is
        // at depth MaxDepth.
        var view = View(Widget("g0", WidgetKind.Group));
        var previous = "g0";

        for (var level = 1; level <= WidgetTree.MaxDepth; level++)
        {
            var id = $"g{level}";
            view.Widgets.Add(Widget(id, WidgetKind.Group, previous));
            previous = id;
        }

        Assert.Equal(WidgetTree.MaxDepth, view.DepthOf(previous));

        view.Widgets.Add(Widget("one-too-deep"));
        Assert.False(view.CanReparent("one-too-deep", previous));
    }

    /// <summary>A branch moves as a whole, so the tallest thing inside it has to fit too.</summary>
    [Fact]
    public void S223_ABranchIsMeasuredByItsOwnHeight()
    {
        var view = View(
            Widget("outer", WidgetKind.Group),
            Widget("target", WidgetKind.Group, parent: "outer"),
            Widget("deep", WidgetKind.Group),
            Widget("deeper", WidgetKind.Group, parent: "deep"),
            Widget("deepest", WidgetKind.Group, parent: "deeper"));

        // target is at depth 1 and the branch is 2 tall, so landing it there would put "deepest" at
        // depth 4. The branch is refused as a whole rather than clipped.
        Assert.Equal(2, view.HeightOf("deep"));
        Assert.Equal(1, view.DepthOf("target"));
        Assert.False(view.CanReparent("deep", "target"));

        // One level shallower and the same branch fits.
        Assert.True(view.CanReparent("deep", "outer"));
    }

    // ---- deletion --------------------------------------------------------------------------

    /// <summary>
    /// Removing the box must not remove the things in it. One click, no undo, a dozen widgets.
    /// </summary>
    [Fact]
    public void S224_DissolvingAContainerKeepsItsChildren()
    {
        var view = View(
            Widget("g", WidgetKind.Group),
            Widget("a", parent: "g"),
            Widget("b", parent: "g"));

        view.Dissolve("g");

        Assert.Null(view.Find("a")!.ParentId);
        Assert.Null(view.Find("b")!.ParentId);
        Assert.Equal(3, view.Widgets.Count);
    }

    [Fact]
    public void S224_DissolvingANestedContainerPromotesToItsOwnParent()
    {
        var view = View(
            Widget("outer", WidgetKind.Group),
            Widget("inner", WidgetKind.Stack, parent: "outer"),
            Widget("a", parent: "inner"));

        view.Dissolve("inner");

        Assert.Equal("outer", view.Find("a")!.ParentId);
    }

    // ---- loading ---------------------------------------------------------------------------

    [Fact]
    public void S225_AParentThatIsNotAContainerIsRepaired()
    {
        var view = View(Widget("clock"), Widget("a", parent: "clock"));

        Assert.Equal(1, view.RepairTree());
        Assert.Null(view.Find("a")!.ParentId);
    }

    [Fact]
    public void S225_AHealthyTreeIsLeftAlone()
    {
        var view = View(
            Widget("g", WidgetKind.Group),
            Widget("a", parent: "g"));

        Assert.Equal(0, view.RepairTree());
        Assert.Equal("g", view.Find("a")!.ParentId);
    }

    /// <summary>A clone is a new widget in the same place in the tree, not a new root.</summary>
    [Fact]
    public void S226_CloningKeepsTheParentAndTheHeaderChoice()
    {
        var original = Widget("a", parent: "g");
        original.Header = HeaderPlacement.Bottom;

        var copy = original.Clone();

        Assert.Equal("g", copy.ParentId);
        Assert.Equal(HeaderPlacement.Bottom, copy.Header);
        Assert.NotEqual(original.Id, copy.Id);
    }

    [Fact]
    public void S226_ANewWidgetStartsOnTheDesktopWithATopHeader()
    {
        // Not the Clock: it starts headerless since [S-700].
        var widget = WidgetInstance.Create(WidgetKind.Cpu, new FracRect(0, 0, 0.2, 0.2));

        Assert.Null(widget.ParentId);
        Assert.Equal(HeaderPlacement.Top, widget.Header);
    }
}
