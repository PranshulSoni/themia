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
        Config = WidgetConfig.CreateDefault(kind),
        X = rect.X,
        Y = rect.Y,
        W = rect.W,
        H = rect.H,
    };

    public WidgetInstance Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = Kind,
        Label = Label,
        Surface = Surface,
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

    public List<WidgetInstance> Widgets { get; set; } = [];

    public static ViewState Create(string name) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
    };
}

/// <summary>Global look-and-feel. Kept deliberately small; everything here is a "look", not an accessibility choice.</summary>
public sealed class DesignState
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>[S-04] Gap between widgets, in device-independent pixels.</summary>
    public double WidgetGapPx { get; set; } = 8;

    /// <summary>[S-06] Snap-to-grid aid for drag, resize and placement.</summary>
    public bool GridSnap { get; set; } = true;

    /// <summary>Corner radius of a widget card, in device-independent pixels.</summary>
    public double CornerRadiusPx { get; set; } = 8;

    /// <summary>Opacity applied to every widget card, 0.2 to 1.0.</summary>
    public double SurfaceOpacity { get; set; } = 1.0;

    /// <summary>Launch with Windows, via the per-user Run key.</summary>
    public bool RunAtStartup { get; set; }

    public DesignState Clone() => new()
    {
        Theme = Theme,
        WidgetGapPx = WidgetGapPx,
        GridSnap = GridSnap,
        CornerRadiusPx = CornerRadiusPx,
        SurfaceOpacity = SurfaceOpacity,
        RunAtStartup = RunAtStartup,
    };
}

/// <summary>
/// The whole persisted document. <see cref="SchemaVersion"/> is the load-time trust boundary: a
/// document from the future is refused rather than half-read.
/// </summary>
public sealed class AppState
{
    /// <summary>Bumped whenever the shape changes in a way older builds cannot read.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ViewState> Views { get; set; } = [];

    public string? ActiveViewId { get; set; }

    /// <summary>Per-monitor view assignment: monitor stable id to view id. Missing means "use the active view".</summary>
    public Dictionary<string, string> MonitorViews { get; set; } = [];

    public DesignState Design { get; set; } = new();

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
}
