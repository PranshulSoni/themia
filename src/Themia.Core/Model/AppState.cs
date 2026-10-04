using System.Text.Json.Serialization;
using Themia.Core.Geometry;

namespace Themia.Core.Model;

/// <summary>One placed widget. Geometry is fractional ([S-01]); config is typed ([S-13]).</summary>
public sealed class WidgetInstance
{
    public required string Id { get; set; }

    public required WidgetKind Kind { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; }

    public double H { get; set; }

    /// <summary>Optional user-set title shown in the widget header. Null means "use the catalogue label".</summary>
    public string? Label { get; set; }

    public WidgetSurface Surface { get; set; } = WidgetSurface.Card;

    /// <summary>
    /// The container this widget belongs to, or null for one sitting directly on the desktop.
    /// <para>
    /// A parent id rather than a list of children on the container: membership is then stated in
    /// exactly one place, so a widget cannot be in two containers at once and a container cannot
    /// list a widget that no longer exists. The cost is that finding a container's children is a
    /// scan of the view, which at a desktop's worth of widgets is nothing.
    /// </para>
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>Where the widget's header sits. A per-widget choice, like the surface.</summary>
    public HeaderPlacement Header { get; set; } = HeaderPlacement.Top;

    /// <summary>Never null after a successful load; a missing or mismatched config is repaired to the kind's default.</summary>
    public required WidgetConfig Config { get; set; }

    [JsonIgnore]
    public FracRect Rect
    {
        get => new(X, Y, W, H);
        set
        {
            X = value.X;
            Y = value.Y;
            W = value.W;
            H = value.H;
        }
    }

    public static WidgetInstance Create(WidgetKind kind, FracRect rect) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = kind,
        Header = DefaultHeader(kind),
        Config = WidgetConfig.CreateDefault(kind),
        X = rect.X,
        Y = rect.Y,
        W = rect.W,
        H = rect.H,
    };

    /// <summary>
    /// [S-485] Where a new widget's header goes. Top for everything except the two widgets that are
    /// themselves a piece of chrome: the Themia mark is its own label, and a view switcher is a row
    /// of buttons whose names already say what it is. A header on either is the same word twice.
    /// Only the default: the user can still give either one a header.
    /// <para>
    /// [S-700] Calculator, Clock, Colour swatch and Web search start headerless too, as they did in
    /// the original: each is a single readout or field whose content says what it is, and at the
    /// small sizes they are usually placed at, a header would take a third of the card. Widgets
    /// already saved keep the placement they were saved with; this is only read on creation.
    /// </para>
    /// </summary>
    public static HeaderPlacement DefaultHeader(WidgetKind kind) => kind switch
    {
        WidgetKind.Themia or WidgetKind.ViewSwitcher => HeaderPlacement.Hidden,
        WidgetKind.Calculator or WidgetKind.Clock or WidgetKind.ColorSwatch or WidgetKind.WebSearch => HeaderPlacement.Hidden,

        // [S-867] A stack's rows and a tab strip are their own chrome (the original drew them with
        // no widget header): a "Stack" title over a list of names is a header over headers.
        WidgetKind.Stack or WidgetKind.Tabs => HeaderPlacement.Hidden,
        _ => HeaderPlacement.Top,
    };

    public WidgetInstance Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = Kind,
        Label = Label,
        Surface = Surface,
        ParentId = ParentId,
        Header = Header,
        Config = Config.Clone(),
        X = X,
        Y = Y,
        W = W,
        H = H,
    };
}

/// <summary>A named desktop layout. Views are switched as a whole; each holds its own widgets.</summary>
public sealed class ViewState
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// [S-703] A Segoe Fluent glyph shown for the view in switchers and the view bar, or null for
    /// the name's initials.
    /// </summary>
    public string? Icon { get; set; }

    public List<WidgetInstance> Widgets { get; set; } = [];

    public static ViewState Create(string name) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
    };
}

/// <summary>
/// Global look-and-feel, mirroring the original app's design options minus anything tied to an
/// account. Colours are hex strings; <b>empty means "use the theme's own"</b>, which is what lets a
/// user override one colour without pinning all the others.
/// </summary>
public sealed class DesignState
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Scales every widget's content. The user's accessibility choice, not a look.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>A font-family preset, or the name of any installed family.</summary>
    public string FontFamily { get; set; } = "system";

    /// <summary>Multiplies the computed font sizes.</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>[S-04] Gap between widgets, in device-independent pixels.</summary>
    public double WidgetGapPx { get; set; } = 8;

    /// <summary>[S-06] Snap-to-grid aid for drag, resize and placement.</summary>
    public bool GridSnap { get; set; } = true;

    /// <summary>Corner radius of a widget card, in device-independent pixels.</summary>
    public double CornerRadiusPx { get; set; } = 8;

    /// <summary>Widget card fill, as <c>#RRGGBB</c>. Empty uses the theme's surface colour.</summary>
    public string WidgetBackgroundColor { get; set; } = string.Empty;

    /// <summary>Opacity applied to every widget card, 0.2 to 1.0.</summary>
    public double SurfaceOpacity { get; set; } = 1.0;

    /// <summary>Accent override, as <c>#RRGGBB</c>. Empty follows the Windows accent colour.</summary>
    public string PrimaryColor { get; set; } = string.Empty;

    public WidgetBorderStyle BorderStyle { get; set; } = WidgetBorderStyle.Solid;

    /// <summary>Border thickness in device-independent pixels, 0 to 8.</summary>
    public double BorderWidthPx { get; set; } = 1;

    /// <summary>Border colour, as <c>#RRGGBB</c>. Empty uses the theme's card stroke.</summary>
    public string BorderColor { get; set; } = string.Empty;

    public double BorderOpacity { get; set; } = 1.0;

    /// <summary>The one-shot animation widgets play when a view appears.</summary>
    public IntroAnimation IntroAnimation { get; set; } = IntroAnimation.Scale;

    /// <summary>Duration of that animation, in milliseconds.</summary>
    public double IntroAnimationMs { get; set; } = 500;

    /// <summary>[S-704] The header strip's fill, as <c>#RRGGBB</c>. Empty uses the widget's own fill.</summary>
    public string HeaderBackgroundColor { get; set; } = string.Empty;

    /// <summary>Opacity of the header strip, 0 to 1. Independent of the body, as in the original.</summary>
    public double HeaderOpacity { get; set; } = 1.0;

    /// <summary>[S-704] The animation widgets play when the user switches views.</summary>
    public IntroAnimation TransitionAnimation { get; set; } = IntroAnimation.Scale;

    public double TransitionAnimationMs { get; set; } = 500;

    /// <summary>[S-704] The animation replayed when the session is unlocked. None turns the replay off.</summary>
    public IntroAnimation UnlockAnimation { get; set; } = IntroAnimation.Scale;

    public double UnlockAnimationMs { get; set; } = 500;

    /// <summary>Play a short sound with the startup animation. Opt-in, as in the original.</summary>
    public bool StartupSound { get; set; }

    /// <summary>Launch with Windows, via the per-user Run key.</summary>
    public bool RunAtStartup { get; set; }

    public DesignState Clone() => new()
    {
        Theme = Theme,
        UiScale = UiScale,
        FontFamily = FontFamily,
        FontScale = FontScale,
        WidgetGapPx = WidgetGapPx,
        GridSnap = GridSnap,
        CornerRadiusPx = CornerRadiusPx,
        WidgetBackgroundColor = WidgetBackgroundColor,
        SurfaceOpacity = SurfaceOpacity,
        PrimaryColor = PrimaryColor,
        BorderStyle = BorderStyle,
        BorderWidthPx = BorderWidthPx,
        BorderColor = BorderColor,
        BorderOpacity = BorderOpacity,
        IntroAnimation = IntroAnimation,
        IntroAnimationMs = IntroAnimationMs,
        HeaderBackgroundColor = HeaderBackgroundColor,
        HeaderOpacity = HeaderOpacity,
        TransitionAnimation = TransitionAnimation,
        TransitionAnimationMs = TransitionAnimationMs,
        UnlockAnimation = UnlockAnimation,
        UnlockAnimationMs = UnlockAnimationMs,
        StartupSound = StartupSound,
        RunAtStartup = RunAtStartup,
    };
}

/// <summary>
/// The whole persisted document. <see cref="SchemaVersion"/> is the load-time trust boundary: a
/// document from the future is refused rather than half-read.
/// </summary>
public sealed class AppState
{
    /// <summary>Bumped whenever the shape changes. Only this version is read; there is no upgrade path.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ViewState> Views { get; set; } = [];

    public string? ActiveViewId { get; set; }

    /// <summary>Per-monitor view assignment: monitor stable id to view id. Missing means "use the active view".</summary>
    public Dictionary<string, string> MonitorViews { get; set; } = [];

    public DesignState Design { get; set; } = new();

    /// <summary>[S-702] How the app behaves: language, game mode, network guard, developer switches.</summary>
    public AppPreferences Preferences { get; set; } = new();

    /// <summary>
    /// [S-705] The view a display shows the first time it is seen, or null for the active view.
    /// Repaired to null when the view is deleted.
    /// </summary>
    public string? DefaultViewForNewDisplays { get; set; }

    /// <summary>[S-705] Displays the user turned Themia off on, keyed like <see cref="MonitorViews"/>. No surface is built there.</summary>
    public List<string> DisabledMonitors { get; set; } = [];

    /// <summary>
    /// [S-705] Per-display widget scale, keyed like <see cref="MonitorViews"/>, as a factor from 0.5 to 2. Missing means 1, so
    /// only displays the user changed are stored.
    /// </summary>
    public Dictionary<string, double> MonitorScales { get; set; } = [];

    /// <summary>A fresh profile: one view holding one clock, roughly centred.</summary>
    public static AppState CreateDefault()
    {
        var view = ViewState.Create("Desktop");
        view.Widgets.Add(WidgetInstance.Create(WidgetKind.Clock, new FracRect(0.40, 0.08, 0.20, 0.12)));

        return new AppState
        {
            Views = [view],
            ActiveViewId = view.Id,
        };
    }

    public ViewState? FindView(string? id)
        => id is null ? null : Views.FirstOrDefault(v => v.Id == id);

    /// <summary>The active view, repairing a dangling or missing id to the first view.</summary>
    public ViewState ActiveView()
    {
        var found = FindView(ActiveViewId);
        if (found is not null)
        {
            return found;
        }

        if (Views.Count == 0)
        {
            var seeded = ViewState.Create("Desktop");
            Views.Add(seeded);
        }

        ActiveViewId = Views[0].Id;
        return Views[0];
    }

    /// <summary>The view a given monitor shows, falling back to the active view.</summary>
    public ViewState ViewForMonitor(string monitorId)
    {
        if (MonitorViews.TryGetValue(monitorId, out var viewId))
        {
            var view = FindView(viewId);
            if (view is not null)
            {
                return view;
            }

            // Dangling assignment (the view was deleted): drop it rather than keep resolving it.
            MonitorViews.Remove(monitorId);
        }

        return ActiveView();
    }

    /// <summary>
    /// [S-19] Show a view on one display. Returns false, changing nothing, when the view does not exist.
    /// <para>
    /// The display's own assignment is what is changed, not just the active view. Every display is
    /// given an assignment at startup ([S-18]), and an assignment always wins over the active view,
    /// so switching only the active view — which is what switching did — changed nothing on screen.
    /// The active view follows too, so a display that has no assignment of its own, such as one
    /// plugged in later, starts on the view the user last chose.
    /// </para>
    /// </summary>
    public bool ShowViewOn(string monitorId, string viewId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);

        if (FindView(viewId) is null)
        {
            return false;
        }

        MonitorViews[monitorId] = viewId;
        ActiveViewId = viewId;
        return true;
    }
}
