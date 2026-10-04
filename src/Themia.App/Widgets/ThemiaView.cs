using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The Themia mark, the name and the version, centred. [S-484]
/// <para>
/// Nothing ticks and nothing is fetched: the version is the running build's own, read once by
/// <see cref="AppInfo"/>. The original read it asynchronously from Tauri and showed a bare "v" until
/// it arrived; here it is known before the first frame, so there is no empty state to design.
/// </para>
/// </summary>
internal sealed partial class ThemiaView : Grid, IWidgetView
{
    /// <summary>The mark is decoded at its drawn size rounded up to this, so a resize drag does not decode it per pixel.</summary>
    private const int DecodeStep = 32;

    private static readonly Uri LogoUri = new(Path.Combine(AppContext.BaseDirectory, "Assets", "themia-logo.png"));

    private readonly Image _logo = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _name = Fluent.Text(FluentType.BodyStrong);
    private readonly TextBlock _version = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly StackPanel _stack;

    private int _decodedAt;
    private bool _detached;

    public ThemiaView()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_logo, "Themia logo");

        _name.Text = "Themia";
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _version.Text = $"v{AppInfo.Version}";
        _version.HorizontalAlignment = HorizontalAlignment.Center;

        _logo.HorizontalAlignment = HorizontalAlignment.Center;

        _stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _logo, _name, _version },
        };

        Children.Add(_stack);
        SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config) => _detached = false;

    public void ApplyConfig(WidgetConfig config)
    {
        // ignore: the Themia widget has no settings
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout(args.NewSize.Width, args.NewSize.Height);

    /// <summary>
    /// Fit the mark to the tile, then decide what text fits under it. The text gives way before the
    /// mark does: on a tile too small for all three, a recognisable logo says more than a clipped
    /// version number.
    /// </summary>
    private void ApplyLayout(double width, double height)
    {
        if (_detached || width <= 0 || height <= 0)
        {
            return;
        }

        var inner = Math.Max(0, Math.Min(width, height) - (Fluent.Space4 * 2));

        // Name and version step up together on a big tile, and the version is dropped first.
        var nameStep = inner >= 240 ? FluentType.Title : inner >= 140 ? FluentType.Subtitle : FluentType.BodyStrong;
        var versionStep = inner >= 240 ? FluentType.Body : FluentType.Caption;
        _name.Apply(nameStep);
        _version.Apply(versionStep);

        var gap = inner >= 140 ? Fluent.Space3 : Fluent.Space2;
        var textHeight = nameStep.LineHeight + versionStep.LineHeight;
        var available = height - (Fluent.Space4 * 2);

        var showVersion = available - textHeight >= 40;
        var showName = available - nameStep.LineHeight >= 32;
        _version.Visibility = showVersion ? Visibility.Visible : Visibility.Collapsed;
        _name.Visibility = showName ? Visibility.Visible : Visibility.Collapsed;

        var text = (showName ? nameStep.LineHeight : 0) + (showVersion ? versionStep.LineHeight : 0);
        var logo = Math.Floor(Math.Min(width - (Fluent.Space4 * 2), available - text - (showName ? gap : 0)));
        logo = Math.Clamp(logo, 16, 160);

        _logo.Width = logo;
        _logo.Height = logo;
        _logo.Margin = new Thickness(0, 0, 0, showName ? gap : 0);

        Decode(logo);
    }

    /// <summary>
    /// Decode the mark at the size it is drawn, in DIPs: logical decoding lets WIC scale for the
    /// display, so the same widget is sharp at 200% without holding a 256px bitmap at 100%.
    /// </summary>
    private void Decode(double logoDip)
    {
        var target = (int)Math.Ceiling(logoDip / DecodeStep) * DecodeStep;
        if (target == _decodedAt)
        {
            return;
        }

        _decodedAt = target;
        _logo.Source = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = target,
            UriSource = LogoUri,
        };
    }

    public void Detach()
    {
        _detached = true;
        SizeChanged -= OnSizeChanged;
    }
}
