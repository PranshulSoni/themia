using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.Core.Diagnostics;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1600] The full-colour icons of the settings navigation, one per bundled SVG in
/// <c>Assets/NavIcons</c>. They are Microsoft's Fluent UI System Icons "Color" set (MIT, see
/// THIRD-PARTY-NOTICES.md), the family Windows 11 Settings draws its own navigation pane with.
/// Named after the icon, not the page, so the licence list maps one to one onto the files.
/// </summary>
internal enum NavIcon
{
    /// <summary>A gear: General.</summary>
    Settings,

    /// <summary>A paint brush, as Windows Settings' Personalization: Appearance.</summary>
    PaintBrush,

    /// <summary>Four tiles: Layout.</summary>
    Apps,

    /// <summary>A person, as Windows Settings' Accounts: Accounts.</summary>
    Person,

    /// <summary>A page of text: Logs.</summary>
    DocumentText,

    /// <summary>A question mark in a circle: About.</summary>
    QuestionCircle,

    /// <summary>A window laid out in panels: every view.</summary>
    ContentView,

    /// <summary>A screen, as Windows Settings' System: every display.</summary>
    Laptop,
}

internal static class NavIcons
{
    private const string Scope = "config";

    /// <summary>
    /// NavigationView draws an item's icon in a 16-DIP square, so the 16-DIP design of each icon is
    /// bundled: the Fluent sizes are drawn for their DIP size, and scale up cleanly with the display.
    /// </summary>
    private const double Size = 16;

    /// <summary>The bundled file for an icon, under <c>Assets/NavIcons</c>.</summary>
    public static string FileName(NavIcon icon) => icon switch
    {
        NavIcon.Settings => "ic_fluent_settings_16_color.svg",
        NavIcon.PaintBrush => "ic_fluent_paint_brush_16_color.svg",
        NavIcon.Apps => "ic_fluent_apps_16_color.svg",
        NavIcon.Person => "ic_fluent_person_16_color.svg",
        NavIcon.DocumentText => "ic_fluent_document_text_16_color.svg",
        NavIcon.QuestionCircle => "ic_fluent_question_circle_16_color.svg",
        NavIcon.ContentView => "ic_fluent_content_view_16_color.svg",
        NavIcon.Laptop => "ic_fluent_laptop_16_color.svg",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
    };

    /// <summary>
    /// [S-1601] A navigation icon, rasterised at exactly the pixels its 16-DIP box covers on the
    /// display the window is on (16 px at 100 %, 32 px at 200 %), so it is neither blurred by a
    /// down-scale nor soft from an up-scale. Moving the window to a display with another scale
    /// re-rasterises it. The colours are the icon's own in both themes, as in Windows Settings.
    /// </summary>
    public static IconElement Create(NavIcon icon)
    {
        var file = FileName(icon);
        var source = new SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "NavIcons", file)))
        {
            RasterizePixelWidth = Size,
            RasterizePixelHeight = Size,
        };

        source.OpenFailed += (_, args) => Log.Error(Scope, $"navigation icon {file} did not load: {args.Status}");

        var element = new ImageIcon { Source = source, Width = Size, Height = Size };
        XamlRoot? watched = null;

        void Rasterize()
        {
            var pixels = Math.Max(1, Math.Round(Size * (element.XamlRoot?.RasterizationScale ?? 1)));
            if (source.RasterizePixelWidth != pixels)
            {
                source.RasterizePixelWidth = pixels;
                source.RasterizePixelHeight = pixels;
            }
        }

        void OnRootChanged(XamlRoot root, XamlRootChangedEventArgs args) => Rasterize();

        // The scale is only known once the icon is in a window, and changes when the window moves
        // between displays; the subscription ends with the item (the menu is rebuilt, not reused).
        element.Loaded += (_, _) =>
        {
            Rasterize();
            if (watched is null && element.XamlRoot is { } root)
            {
                watched = root;
                root.Changed += OnRootChanged;
            }
        };

        element.Unloaded += (_, _) =>
        {
            if (watched is not null)
            {
                watched.Changed -= OnRootChanged;
                watched = null;
            }
        };

        return element;
    }
}
