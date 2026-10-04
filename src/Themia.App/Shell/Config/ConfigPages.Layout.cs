using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The Layout page: the gap between widgets and snap to grid.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildLayout(AppHost host)
    {
        var design = host.State.Design;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Grid, Str.Shell_Layout_Gap.Text(), null,
                FluentLayout.Slider(design.WidgetGapPx, 0, 48, 2, "px",
                    value => host.Mutate(s => s.Design.WidgetGapPx = value))),

            FluentLayout.Row(Glyphs.Grid, Str.Settings_Style_GridSnap_Label.Text(),
                Str.Shell_Layout_SnapDesc.Text(),
                FluentLayout.Toggle(design.GridSnap, value => host.Mutate(s => s.Design.GridSnap = value))),
        };

        return Page(Str.Shell_Settings_Nav_Layout.Text(), rows);
    }
}
