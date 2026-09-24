using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The bodies of the config window.
/// <para>
/// Every page edits the live state and saves as it goes, rather than holding a draft behind an OK
/// button: the desktop is visible behind this window, so a setting the user cannot see take effect
/// is a setting they cannot judge. The exception is a widget's own config, which is a clone —
/// see <see cref="BuildWidget"/>.
/// </para>
/// </summary>
internal static class ConfigPages
{
    private const string Scope = "config";

    public static FrameworkElement Build(AppHost host, ConfigTarget target, Action<ConfigTarget> navigate) => target switch
    {
        ConfigTarget.App { Page: AppPage.General } => BuildGeneral(host),
        ConfigTarget.App { Page: AppPage.Appearance } => BuildAppearance(host),
        ConfigTarget.App { Page: AppPage.Layout } => BuildLayout(host),
        ConfigTarget.App { Page: AppPage.About } => BuildAbout(host),
        ConfigTarget.View view => BuildView(host, view.ViewId, navigate),
        ConfigTarget.Display display => BuildDisplay(host, display.MonitorId),
        ConfigTarget.Widget widget => BuildWidget(host, widget.WidgetId, navigate),
        ConfigTarget.Add add => BuildAdd(host, add.IntoViewId, navigate),
        _ => Page("Settings", []),
    };

    // -----------------------------------------------------------------------------------------
    // App
    // -----------------------------------------------------------------------------------------

    private static FrameworkElement BuildGeneral(AppHost host)
    {
        var rows = new List<FrameworkElement> { FluentLayout.Header("System") };

        var startup = FluentLayout.Toggle(StartupRegistration.IsEnabled(), value =>
        {
            // Reflect what the registry actually ended up at, so a failed write does not leave the
            // toggle claiming something untrue.
            var achieved = StartupRegistration.SetEnabled(value);
            host.Mutate(s => s.Design.RunAtStartup = achieved, relayout: false);
        });

        rows.Add(FluentLayout.Row(Glyphs.Startup, "Start with Windows", "Adds a per-user startup entry", startup));

        var editToggle = FluentLayout.Toggle(host.EditMode, host.SetEditMode);
        host.EditModeChanged += editing => editToggle.IsOn = editing;

        rows.Add(FluentLayout.Row(Glyphs.Edit, "Edit layout",
            "Show the drag handles and the snap grid on the desktop", editToggle));

        rows.Add(FluentLayout.Header("Data"));

        var openFolder = new Button { Content = "Open data folder" };
        openFolder.Click += (_, _) => OpenDataFolder();

        rows.Add(FluentLayout.Row(Glyphs.Info, "Settings and log",
            "Where the layout, settings and log file live", openFolder));

        return Page("General", rows);
    }

    private static FrameworkElement BuildAppearance(AppHost host)
    {
        var design = host.State.Design;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Header("Theme"),

            FluentLayout.Row(Glyphs.Theme, "Theme", "Follows Windows unless you pick one",
                FluentLayout.Choice(
                    [(AppTheme.System, "Use system setting"), (AppTheme.Light, "Light"), (AppTheme.Dark, "Dark")],
                    design.Theme,
                    value => host.Mutate(s => s.Design.Theme = value))),

            FluentLayout.Header("Colours"),

            ColorRow(host, Glyphs.Palette, "Accent", "Follows the Windows accent colour when empty",
                design.PrimaryColor, value => host.Mutate(s => s.Design.PrimaryColor = value)),

            ColorRow(host, Glyphs.Surface, "Widget background", "Uses the theme's surface colour when empty",
                design.WidgetBackgroundColor, value => host.Mutate(s => s.Design.WidgetBackgroundColor = value)),

            FluentLayout.Row(Glyphs.Opacity, "Widget opacity", null,
                FluentLayout.Slider(design.SurfaceOpacity * 100, 20, 100, 5, "%",
                    value => host.Mutate(s => s.Design.SurfaceOpacity = value / 100))),

            FluentLayout.Header("Shape"),

            FluentLayout.Row(Glyphs.Surface, "Corner radius", null,
                FluentLayout.Slider(design.CornerRadiusPx, 0, 32, 1, "px",
                    value => host.Mutate(s => s.Design.CornerRadiusPx = value))),

            FluentLayout.Row(Glyphs.Grid, "Border", null,
                FluentLayout.Choice(
                    [
                        (WidgetBorderStyle.None, "None"),
                        (WidgetBorderStyle.Solid, "Solid"),
                        (WidgetBorderStyle.Dashed, "Dashed"),
                        (WidgetBorderStyle.Dotted, "Dotted"),
                    ],
                    design.BorderStyle,
                    value => host.Mutate(s => s.Design.BorderStyle = value))),

            FluentLayout.Row(Glyphs.Grid, "Border width", null,
                FluentLayout.Slider(design.BorderWidthPx, 0, 8, 1, "px",
                    value => host.Mutate(s => s.Design.BorderWidthPx = value))),

            ColorRow(host, Glyphs.Grid, "Border colour", "Uses the theme's card stroke when empty",
                design.BorderColor, value => host.Mutate(s => s.Design.BorderColor = value)),

            FluentLayout.Row(Glyphs.Opacity, "Border opacity", null,
                FluentLayout.Slider(design.BorderOpacity * 100, 0, 100, 5, "%",
                    value => host.Mutate(s => s.Design.BorderOpacity = value / 100))),

            FluentLayout.Header("Text"),

            FluentLayout.Row(Glyphs.Font, "Font", "Any family installed on this PC",
                FontChoice(host, design.FontFamily)),

            FluentLayout.Row(Glyphs.TextSize, "Text size", "Multiplies every widget's computed text size",
                FluentLayout.Slider(design.FontScale * 100, 50, 200, 5, "%",
                    value => host.Mutate(s => s.Design.FontScale = value / 100))),

            FluentLayout.Row(Glyphs.Scale, "Widget scale", "Scales widget content without moving the widgets",
                FluentLayout.Slider(design.UiScale * 100, 50, 300, 5, "%",
                    value => host.Mutate(s => s.Design.UiScale = value / 100))),

            FluentLayout.Header("Motion"),

            FluentLayout.Row(Glyphs.Motion, "Intro animation", "Played once when a view appears",
                FluentLayout.Choice(
                    [
                        (IntroAnimation.None, "None"),
                        (IntroAnimation.Fade, "Fade"),
                        (IntroAnimation.Scale, "Scale"),
                        (IntroAnimation.Slide, "Slide"),
                    ],
                    design.IntroAnimation,
                    value => host.Mutate(s => s.Design.IntroAnimation = value))),

            FluentLayout.Row(Glyphs.Timer, "Animation length", null,
                FluentLayout.Slider(design.IntroAnimationMs, 0, 1500, 50, "ms",
                    value => host.Mutate(s => s.Design.IntroAnimationMs = value))),
        };

        return Page("Appearance", rows);
    }

    private static FrameworkElement BuildLayout(AppHost host)
    {
        var design = host.State.Design;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Grid, "Gap between widgets", null,
                FluentLayout.Slider(design.WidgetGapPx, 0, 48, 2, "px",
                    value => host.Mutate(s => s.Design.WidgetGapPx = value))),

            FluentLayout.Row(Glyphs.Grid, "Snap to grid",
                "Hold Alt while dragging to place freely",
                FluentLayout.Toggle(design.GridSnap, value => host.Mutate(s => s.Design.GridSnap = value))),
        };

        return Page("Layout", rows);
    }

    private static FrameworkElement BuildAbout(AppHost host)
    {
        var openFolder = new HyperlinkButton { Content = "Open data folder" };
        openFolder.Click += (_, _) => OpenDataFolder();

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Info, "Themia for WinUI",
                "A minimal native rebuild: WinUI 3 on .NET, compiled ahead of time.", openFolder),

            FluentLayout.Row(Glyphs.View, "Widgets", $"{WidgetCatalog.All.Count} available",
                new TextBlock { Text = string.Empty }),

            FluentLayout.Row(Glyphs.View, "Views", $"{host.State.Views.Count} configured",
                new TextBlock { Text = string.Empty }),
        };

        return Page("About", rows);
    }

    private static void OpenDataFolder()
    {
        var folder = Path.GetDirectoryName(Themia.Core.Storage.AppStateStore.DefaultPath());
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not open the data folder", ex);
        }
    }

    // -----------------------------------------------------------------------------------------
    // View / display
    // -----------------------------------------------------------------------------------------

    private static FrameworkElement BuildView(AppHost host, string viewId, Action<ConfigTarget> navigate)
    {
        var view = host.State.FindView(viewId);
        if (view is null)
        {
            return Page("View", [Note("This view no longer exists.")]);
        }

        var name = new TextBox { Text = view.Name, MinWidth = 220 };
        name.LostFocus += (_, _) => host.Mutate(s =>
        {
            if (s.FindView(viewId) is { } target && !string.IsNullOrWhiteSpace(name.Text))
            {
                target.Name = name.Text.Trim();
            }
        });

        var activate = new Button { Content = "Show this view" };
        activate.Click += (_, _) => host.SwitchView(viewId);

        var delete = new Button { Content = "Delete view", IsEnabled = host.State.Views.Count > 1 };
        delete.Click += (_, _) =>
        {
            host.DeleteView(viewId);
            navigate(new ConfigTarget.App());
        };

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Rename, "Name", null, name),
            FluentLayout.Row(Glyphs.View, "Active view",
                host.State.ActiveView().Id == viewId ? "This view is showing now" : "Switch the desktop to it",
                activate),
            FluentLayout.Header("Widgets"),
        };

        if (view.Widgets.Count == 0)
        {
            rows.Add(Note("No widgets in this view yet."));
        }

        foreach (var widget in view.Widgets)
        {
            var definition = WidgetCatalog.Definition(widget.Kind);
            var open = new Button { Content = "Settings" };
            var id = widget.Id;
            open.Click += (_, _) => navigate(new ConfigTarget.Widget(id));

            rows.Add(FluentLayout.Row(
                definition.Glyph,
                string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!,
                definition.Label,
                open));
        }

        var add = new Button { Content = "Add a widget" };
        add.Click += (_, _) => navigate(new ConfigTarget.Add(viewId));
        rows.Add(add);

        rows.Add(FluentLayout.Header("Danger"));
        rows.Add(FluentLayout.Row(Glyphs.Delete, "Delete this view",
            host.State.Views.Count > 1 ? "Its widgets go with it" : "The last view cannot be deleted",
            delete));

        return Page(view.Name, rows);
    }

    private static FrameworkElement BuildDisplay(AppHost host, string monitorId)
    {
        var surface = host.Surfaces.FirstOrDefault(s => s.Monitor.DeviceName == monitorId);
        if (surface is null)
        {
            return Page("Display", [Note("This display is no longer connected.")]);
        }

        var options = new List<(string Value, string Label)> { (string.Empty, "Follow the active view") };
        options.AddRange(host.State.Views.Select(v => (v.Id, v.Name)));

        var selected = host.State.MonitorViews.TryGetValue(monitorId, out var assigned) ? assigned : string.Empty;

        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (value, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        combo.SelectedIndex = Math.Max(0, options.FindIndex(o => o.Value == selected));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string id })
            {
                return;
            }

            host.Mutate(s =>
            {
                if (string.IsNullOrEmpty(id))
                {
                    s.MonitorViews.Remove(monitorId);
                }
                else
                {
                    s.MonitorViews[monitorId] = id;
                }
            });
        };

        var area = surface.Monitor.WorkArea;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.View, "Shows", "Which view this display renders", combo),
            FluentLayout.Row(Glyphs.Display, "Work area", $"{area.Width} x {area.Height} physical pixels",
                new TextBlock { Text = surface.Monitor.IsPrimary ? "Primary" : string.Empty }),
        };

        return Page(monitorId, rows);
    }

    // -----------------------------------------------------------------------------------------
    // Widget
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A widget's own settings.
    /// <para>
    /// Unlike the app pages these edit the live config directly and save on change, because the
    /// widget is visible on the desktop behind this window and the point is to see the effect.
    /// </para>
    /// </summary>
    private static FrameworkElement BuildWidget(AppHost host, string widgetId, Action<ConfigTarget> navigate)
    {
        if (host.Find(widgetId) is not var (view, widget) || widget is null)
        {
            return Page("Widget", [Note("This widget has been removed.")]);
        }

        var definition = WidgetCatalog.Definition(widget.Kind);
        var rows = new List<FrameworkElement>();

        void Save() => host.Mutate(_ => { });

        switch (widget.Config)
        {
            case ClockConfig clock:
                WidgetSettings.Clock(rows, clock, Save);
                break;

            case DiskConfig disk:
                WidgetSettings.Disk(rows, disk, Save);
                break;

            case BatteryConfig battery:
                WidgetSettings.Battery(rows, battery, Save);
                break;

            // Before GaugeConfig: Ping derives from it, and a base-type case placed first would
            // swallow the derived one.
            case PingConfig ping:
                WidgetSettings.Ping(rows, ping, Save);
                break;

            case GaugeConfig gauge:
                WidgetSettings.Gauge(rows, gauge, Save);
                break;

            case EmailConfig email:
                WidgetSettings.Email(rows, email, Save);
                break;

            case ProcessesConfig processes:
                WidgetSettings.Processes(rows, processes, Save);
                break;

            case ContactsConfig contacts:
                WidgetSettings.Contacts(rows, contacts, Save);
                break;

            case CalendarConfig calendar:
                WidgetSettings.Calendar(rows, calendar, Save);
                break;

            case TasksConfig tasks:
                WidgetSettings.Tasks(rows, tasks, Save);
                break;

            case NotesConfig notes:
                WidgetSettings.Notes(rows, notes, Save);
                break;

            case CountdownConfig countdown:
                WidgetSettings.Countdown(rows, countdown, Save);
                break;

            case PomodoroConfig pomodoro:
                WidgetSettings.Pomodoro(rows, pomodoro, Save);
                break;

            case CalculatorConfig calculator:
                WidgetSettings.Calculator(rows, calculator, Save);
                break;

            case SunMoonConfig sunMoon:
                WidgetSettings.SunMoon(rows, sunMoon, Save);
                break;

            case FolderConfig folder:
                WidgetSettings.Folder(rows, folder, Save);
                break;

            case VolumeConfig volume:
                WidgetSettings.Volume(rows, volume, Save);
                break;

            case PowerConfig power:
                WidgetSettings.Power(rows, power, Save);
                break;

            case BluetoothConfig bluetooth:
                WidgetSettings.Bluetooth(rows, bluetooth, Save);
                break;

            case MusicConfig music:
                WidgetSettings.Music(rows, music, Save);
                break;

            case WeatherNowConfig weatherNow:
                WidgetSettings.WeatherNow(rows, weatherNow, Save);
                break;

            case WeatherForecastConfig weatherForecast:
                WidgetSettings.WeatherForecast(rows, weatherForecast, Save);
                break;

            case StocksConfig stocks:
                WidgetSettings.Stocks(rows, stocks, Save);
                break;

            case RssConfig rss:
                WidgetSettings.Rss(rows, rss, Save);
                break;

            case AppsConfig apps:
                WidgetSettings.Apps(rows, apps, Save);
                break;

            case ShortcutsConfig shortcuts:
                WidgetSettings.Shortcuts(rows, shortcuts, Save);
                break;

            case WebSearchConfig webSearch:
                WidgetSettings.WebSearch(rows, webSearch, Save);
                break;

            case ClipboardConfig clipboard:
                WidgetSettings.Clipboard(rows, clipboard, Save);
                break;

            case ScratchpadConfig scratchpad:
                WidgetSettings.Scratchpad(rows, scratchpad, Save);
                break;

            case ColorSwatchConfig colorSwatch:
                WidgetSettings.ColorSwatch(rows, colorSwatch, Save);
                break;

            case QuoteConfig quote:
                WidgetSettings.Quote(rows, quote, Save);
                break;

            case GalleryConfig gallery:
                WidgetSettings.Gallery(rows, gallery, Save);
                break;

            case HabitTrackerConfig habitTracker:
                WidgetSettings.HabitTracker(rows, habitTracker, Save);
                break;

            case MoodTrackerConfig moodTracker:
                WidgetSettings.MoodTracker(rows, moodTracker, Save);
                break;

            case GroupConfig group:
                WidgetSettings.Group(rows, group, Save);
                break;

            case StackConfig stack:
                WidgetSettings.Stack(rows, stack, Save);
                break;

            case TabsConfig tabs:
                WidgetSettings.Tabs(rows, tabs, Save);
                break;

            default:
                break;
        }

        rows.Add(FluentLayout.Header("Appearance"));

        rows.Add(FluentLayout.Row(Glyphs.Surface, "Background", "How this widget's card is filled",
            FluentLayout.Choice(
                [
                    (WidgetSurface.Card, "Card"),
                    (WidgetSurface.Translucent, "Translucent"),
                    (WidgetSurface.Transparent, "None"),
                ],
                widget.Surface,
                value => host.Mutate(_ => widget.Surface = value))));

        var label = new TextBox
        {
            Text = widget.Label ?? string.Empty,
            MinWidth = 220,
            PlaceholderText = definition.Label,
        };

        label.LostFocus += (_, _) => host.Mutate(_ =>
            widget.Label = string.IsNullOrWhiteSpace(label.Text) ? null : label.Text.Trim());

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Title", "Shown in menus; leave blank for the default", label));

        rows.Add(FluentLayout.Header("Danger"));

        var delete = new Button { Content = "Delete widget" };
        delete.Click += (_, _) =>
        {
            host.Select(widgetId);
            host.DeleteSelected();
            navigate(new ConfigTarget.View(view.Id));
        };

        rows.Add(FluentLayout.Row(Glyphs.Delete, "Delete", $"Removes it from {view.Name}", delete));

        return Page(string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!, rows, definition.Description);
    }

    // -----------------------------------------------------------------------------------------
    // Add
    // -----------------------------------------------------------------------------------------

    private static FrameworkElement BuildAdd(AppHost host, string? intoViewId, Action<ConfigTarget> navigate)
    {
        var gallery = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
        };

        foreach (var definition in WidgetCatalog.All)
        {
            gallery.Items.Add(GalleryTile(definition));
        }

        gallery.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not FrameworkElement { Tag: WidgetKind kind })
            {
                return;
            }

            var id = host.AddWidget(kind, intoViewId);
            if (id is not null)
            {
                navigate(new ConfigTarget.Widget(id));
            }
        };

        var target = host.State.FindView(intoViewId) ?? host.State.ActiveView();

        return Page("Add a widget", [gallery], $"Lands in {target.Name}");
    }

    private static FrameworkElement GalleryTile(WidgetDefinition definition)
    {
        var stack = new StackPanel { Width = 150, Spacing = 4, Padding = new Thickness(4, 10, 4, 10) };

        stack.Children.Add(new FontIcon
        {
            Glyph = definition.Glyph,
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = definition.Label,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = definition.Description,
            FontSize = 11,
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Tag = definition.Kind;
        return stack;
    }

    // -----------------------------------------------------------------------------------------
    // Shared pieces
    // -----------------------------------------------------------------------------------------

    private static FrameworkElement Page(string title, IReadOnlyList<FrameworkElement> rows, string? subtitle = null)
    {
        var body = new StackPanel { Spacing = 4 };

        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            body.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
        else
        {
            body.Children.Add(new Border { Height = 8 });
        }

        foreach (var row in rows)
        {
            body.Children.Add(row);
        }

        return body;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };

    /// <summary>
    /// A colour override: a hex box, a live swatch, and a way back to the theme's own colour.
    /// <para>
    /// The reset matters more than it looks. Empty means "follow the theme", and without an explicit
    /// way back a user who once picked a colour can never return to one that tracks light and dark.
    /// </para>
    /// </summary>
    private static FrameworkElement ColorRow(
        AppHost host,
        string glyph,
        string title,
        string description,
        string value,
        Action<string> onChanged)
    {
        var swatch = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
        };

        var box = new TextBox { Text = value, PlaceholderText = "#RRGGBB", Width = 110 };

        var reset = new Button { Content = new FontIcon { Glyph = Glyphs.Reset, FontSize = 12 } };
        ToolTipService.SetToolTip(reset, "Use the theme colour");

        void Paint(string hex)
        {
            swatch.Background = HexColor.TryParse(hex, out var r, out var g, out var b)
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b))
                : FluentLayout.Themed("SolidBackgroundFillColorTertiaryBrush", Colors.Transparent);
        }

        Paint(value);

        box.LostFocus += (_, _) =>
        {
            // Normalise on the way in so a typo becomes "follow the theme" rather than a stored
            // value nothing can render.
            var normalised = HexColor.Normalise(box.Text);
            box.Text = normalised;
            Paint(normalised);
            onChanged(normalised);
        };

        reset.Click += (_, _) =>
        {
            box.Text = string.Empty;
            Paint(string.Empty);
            onChanged(string.Empty);
        };

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { swatch, box, reset },
        };

        return FluentLayout.Row(glyph, title, description, controls);
    }

    private static ComboBox FontChoice(AppHost host, string current)
    {
        var families = new List<string> { "system", "Segoe UI", "Cascadia Mono", "Consolas", "Georgia", "Arial" };

        if (!families.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            families.Insert(1, current);
        }

        var combo = new ComboBox { MinWidth = 180 };
        foreach (var family in families)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = family == "system" ? "System default" : family,
                Tag = family,
            });
        }

        combo.SelectedIndex = Math.Max(0, families.FindIndex(f => f.Equals(current, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string family })
            {
                host.Mutate(s => s.Design.FontFamily = family);
            }
        };

        return combo;
    }

}
