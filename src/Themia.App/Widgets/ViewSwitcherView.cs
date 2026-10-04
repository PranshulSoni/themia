using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A row of buttons, one per view; a click puts that view on the display the widget is on. [S-487]
/// <para>
/// The display, not the app: every display has a view of its own ([S-18]), so "switch the view"
/// only means something once it says where. The widget asks its card for the display it sits on
/// (<see cref="IWidgetSurface.Display"/>) and switches that one through the same path as the
/// toolbar's View menu ([S-19]). Copy it to another screen and it switches that screen instead.
/// </para>
/// <para>
/// The card is the track. The original drew a segmented control on the header-coloured surface; a
/// track with its own fill inside a card that already has one is a box inside a box, so the buttons
/// sit straight on the card, inset by one step so their corners nest inside the card's.
/// </para>
/// </summary>
internal sealed partial class ViewSwitcherView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetContextActions
{
    private readonly Grid _track = new()
    {
        Padding = new Thickness(Fluent.Space1),
        RowSpacing = Fluent.Space1,
        ColumnSpacing = Fluent.Space1,
    };

    private readonly StackPanel _empty;

    private ViewSwitcherConfig _config = new();
    private IWidgetSurface? _surface;
    private IReadOnlyList<ViewChoice> _choices = [];
    private string _built = string.Empty;
    private bool _detached;

    public ViewSwitcherView()
    {
        _empty = Fluent.EmptyState(Glyphs.View, new TextBlock { Text = "Place it on the desktop to switch views" }, out _);
        _empty.Visibility = Visibility.Collapsed;

        Children.Add(_track);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        ApplyConfig(config);
    }

    /// <summary>
    /// Re-read the views as well as the config. The card calls this on every state change it
    /// survives, which is exactly when a view may have been renamed, added or deleted — so the
    /// switcher follows the settings window without subscribing to anything of its own.
    /// </summary>
    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ViewSwitcherConfig ?? new ViewSwitcherConfig();
        Refresh();
    }

    /// <summary>[S-492] The card menu's one widget command: names on the buttons, or initials alone.</summary>
    public IReadOnlyList<WidgetAction> ContextActions =>
    [
        new WidgetAction(
            _config.IconOnly ? Glyphs.Rename : Glyphs.Hidden,
            _config.IconOnly ? "Show names" : "Initials only",
            () =>
            {
                _config.IconOnly = !_config.IconOnly;
                _surface?.RequestSave();
                Rebuild(force: true);
            }),
    ];

    private void Refresh()
    {
        if (_detached)
        {
            return;
        }

        if (_surface?.Display is not { } display)
        {
            _choices = [];
            _track.Visibility = Visibility.Collapsed;
            _empty.Visibility = Visibility.Visible;
            return;
        }

        _track.Visibility = Visibility.Visible;
        _empty.Visibility = Visibility.Collapsed;
        _choices = ViewSwitcherRules.Choices(display.Views, _config.VisibleViews, display.ShownViewId);
        Rebuild(force: false);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Rebuild(force: false);

    /// <summary>
    /// Build the buttons for the views on offer and the room there is. Skipped when nothing that
    /// shows has changed, because the card calls in on every state change on the desktop and a
    /// rebuild would drop the pointer-over state under the cursor.
    /// </summary>
    private void Rebuild(bool force)
    {
        if (_detached || _choices.Count == 0)
        {
            return;
        }

        var plan = ViewSwitcherRules.Plan(ActualWidth, ActualHeight, _choices.Count, _config.IconOnly);
        var signature = $"{plan}|{string.Join("|", _choices)}";

        if (!force && signature == _built)
        {
            return;
        }

        _built = signature;
        _track.Children.Clear();
        _track.RowDefinitions.Clear();
        _track.ColumnDefinitions.Clear();

        // Big buttons get a step up the ramp; a strip of compact buttons stays at body size.
        var cellHeight = plan.Vertical
            ? (ActualHeight - (Fluent.Space1 * (_choices.Count + 1))) / _choices.Count
            : ActualHeight - (Fluent.Space1 * 2);
        var cellWidth = plan.Vertical
            ? ActualWidth - (Fluent.Space1 * 2)
            : (ActualWidth - (Fluent.Space1 * (_choices.Count + 1))) / _choices.Count;
        var large = cellHeight >= 72 && cellWidth >= 64;

        for (var i = 0; i < _choices.Count; i++)
        {
            var button = BuildButton(_choices[i], plan, large);

            if (plan.Vertical)
            {
                _track.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                SetRow(button, i);
            }
            else
            {
                _track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                SetColumn(button, i);
            }

            _track.Children.Add(button);
        }
    }

    private Button BuildButton(ViewChoice choice, SwitcherLayout plan, bool large)
    {
        var initials = Fluent.Text(
            plan.ShowNames ? FluentType.Strong(FluentType.Caption) : large ? FluentType.Subtitle : FluentType.BodyStrong,
            choice.Shown ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        initials.Text = choice.Initials;
        initials.TextTrimming = TextTrimming.None;
        initials.VerticalAlignment = VerticalAlignment.Center;

        FrameworkElement content = initials;

        if (plan.ShowNames)
        {
            var name = Fluent.Text(
                choice.Shown ? FluentType.Strong(large ? FluentType.BodyLarge : FluentType.Body) : large ? FluentType.BodyLarge : FluentType.Body,
                choice.Shown ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            name.Text = choice.Name;
            name.VerticalAlignment = VerticalAlignment.Center;

            // A Grid rather than a horizontal StackPanel, so the name learns its width and trims
            // instead of running out of the button.
            var row = new Grid
            {
                ColumnSpacing = Fluent.Space2,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
                Children = { initials, name },
            };

            SetColumn(name, 1);
            content = row;
        }

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = plan.Vertical && plan.ShowNames ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(plan.ShowNames ? Fluent.Space3 : 0, 0, plan.ShowNames ? Fluent.Space3 : 0, 0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            BorderThickness = new Thickness(Fluent.Hairline),
        };

        // A grid column hands the content its share of the width only when the content is told to
        // stretch; centred, the name never learns how much room it has.
        if (plan.ShowNames && !plan.Vertical)
        {
            content.HorizontalAlignment = HorizontalAlignment.Center;
        }

        Paint(button, choice.Shown);

        ToolTipService.SetToolTip(button, choice.Shown ? $"{choice.Name} (showing)" : $"Show {choice.Name} on this display");
        AutomationProperties.SetName(button, choice.Name);
        AutomationProperties.SetItemStatus(button, choice.Shown ? "Showing" : string.Empty);

        var id = choice.Id;
        button.Click += (_, _) => Switch(id);
        return button;
    }

    /// <summary>
    /// Lightweight styling: the stock button template, with the fills it reads swapped for the ones
    /// a segmented choice uses. The shown view is a raised control on the card; the rest are subtle
    /// until the pointer is over them, the way a Windows 11 selector reads.
    /// </summary>
    private static void Paint(Button button, bool shown)
    {
        void Set(string key, string brush) => button.Resources[key] = FluentLayout.Themed(brush, Colors.Transparent);

        if (shown)
        {
            Set("ButtonBackground", "ControlFillColorDefaultBrush");
            Set("ButtonBackgroundPointerOver", "ControlFillColorSecondaryBrush");
            Set("ButtonBackgroundPressed", "ControlFillColorTertiaryBrush");
            Set("ButtonBorderBrush", "ControlStrokeColorDefaultBrush");
            Set("ButtonBorderBrushPointerOver", "ControlStrokeColorDefaultBrush");
            Set("ButtonBorderBrushPressed", "ControlStrokeColorDefaultBrush");
        }
        else
        {
            Set("ButtonBackground", "SubtleFillColorTransparentBrush");
            Set("ButtonBackgroundPointerOver", "SubtleFillColorSecondaryBrush");
            Set("ButtonBackgroundPressed", "SubtleFillColorTertiaryBrush");
            Set("ButtonBorderBrush", "SubtleFillColorTransparentBrush");
            Set("ButtonBorderBrushPointerOver", "SubtleFillColorTransparentBrush");
            Set("ButtonBorderBrushPressed", "SubtleFillColorTransparentBrush");
        }
    }

    /// <summary>
    /// Show a view on this widget's display. On the desktop the switch replaces this card with the
    /// other view's widgets; the refresh afterwards is for a display that keeps this card — the
    /// gallery's pretend one — and does nothing on a card that has gone.
    /// </summary>
    private void Switch(string viewId)
    {
        if (_detached || _surface?.Display is not { } display || viewId == display.ShownViewId)
        {
            return;
        }

        display.ShowView(viewId);
        Refresh();
    }

    public void Detach()
    {
        _detached = true;
        SizeChanged -= OnSizeChanged;
        _track.Children.Clear();
    }
}
