#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Geometry;
using Themia.Core.Model;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-877] Group, stack and tabs with members, for <c>--gallery-png Group+fixture</c>. The gallery
/// has no surface to arrange containers, so this does what <c>DesktopWindow.ArrangeContainers</c>
/// does for one card: builds the member cards, hands a stack or tab strip its children, and lays a
/// group's members over its area (they are grid tiles of their own, drawn above it).
/// <para>
/// Debug builds only. <c>THEMIA_GALLERY_HEADER=Left|Right|Bottom</c> puts the group's header on that
/// edge and <c>THEMIA_GALLERY_COLLAPSED=1</c> collapses it, so the turned and collapsed bars can be
/// looked at too.
/// </para>
/// </summary>
internal static class ContainerFixture
{
    private const string First = "fixture-clock";

    public static bool Has(WidgetKind kind) => kind is WidgetKind.Group or WidgetKind.Stack or WidgetKind.Tabs;

    public static WidgetConfig? Create(WidgetKind kind) => kind switch
    {
        WidgetKind.Group => new GroupConfig { Collapsed = Environment.GetEnvironmentVariable("THEMIA_GALLERY_COLLAPSED") == "1" },
        WidgetKind.Stack => new StackConfig { OpenChildId = First },
        WidgetKind.Tabs => new TabsConfig { SelectedChildId = First },
        _ => null,
    };

    /// <summary>The fixture's header placement for the container itself (a group may be turned).</summary>
    public static HeaderPlacement Header(WidgetKind kind, HeaderPlacement fallback)
        => kind == WidgetKind.Group
            && Enum.TryParse<HeaderPlacement>(Environment.GetEnvironmentVariable("THEMIA_GALLERY_HEADER"), out var side)
            && HeaderRules.Allows(kind, side)
                ? side
                : fallback;

    /// <summary>
    /// Build the members for <paramref name="container"/>, drawn at (<paramref name="left"/>,
    /// <paramref name="top"/>) at <paramref name="width"/>×<paramref name="height"/>, and add every
    /// card made to <paramref name="cards"/> so the gallery detaches them with the rest.
    /// </summary>
    public static void Attach(WidgetCard container, IWidgetCardHost host, Canvas page, List<WidgetCard> cards,
        double left, double top, double width, double height)
    {
        var kind = container.Model.Kind;
        WidgetKind[] kinds = kind == WidgetKind.Group
            ? [WidgetKind.Clock, WidgetKind.Memory]
            : [WidgetKind.Clock, WidgetKind.Memory, WidgetKind.Cpu];

        var members = new List<WidgetCard>();
        for (var i = 0; i < kinds.Length; i++)
        {
            var model = WidgetInstance.Create(kinds[i], new FracRect(0, 0, 0.1, 0.1));
            model.Id = i == 0 ? First : $"fixture-{i}";
            model.ParentId = container.Model.Id;

            var card = new WidgetCard(host, model);
            card.ApplyGap(0);
            members.Add(card);
            cards.Add(card);
        }

        if (container.View is not IWidgetContainerView view)
        {
            return;
        }

        if (kind != WidgetKind.Group)
        {
            foreach (var member in members)
            {
                member.SuppressHeader(kind == WidgetKind.Stack);
            }

            view.SetChildren(members);
            return;
        }

        // A group's members keep their own places: two tiles side by side in the area under the bar.
        // Out of the bar's way, on whichever edge it is.
        var bar = WidgetCard.BarThickness;
        var side = HeaderRules.IsSide(container.Model.Header);
        var barLeft = container.Model.Header == HeaderPlacement.Left ? bar : 0;
        var barTop = side ? 0 : bar;
        var pad = Fluent.Space3;
        var tileWidth = Math.Max(0, (width - (side ? bar : 0) - (3 * pad)) / 2);
        var tileHeight = Math.Max(0, height - barTop - (2 * pad));
        view.SetChildren(members);

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (tileWidth < 60 || tileHeight < 60)
            {
                member.Visibility = Visibility.Collapsed;
                continue;
            }

            member.Width = tileWidth;
            member.Height = tileHeight;
            Canvas.SetLeft(member, left + barLeft + pad + (i * (tileWidth + pad)));
            Canvas.SetTop(member, top + barTop + pad);
            page.Children.Add(member);
        }
    }
}
#endif
