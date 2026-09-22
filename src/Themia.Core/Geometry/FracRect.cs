namespace Themia.Core.Geometry;

/// <summary>
/// A widget rect stored as <b>fractions of its screen</b>: <c>X</c>/<c>W</c> are fractions of
/// screen width, <c>Y</c>/<c>H</c> of screen height. Mirrors Themia [S-01]/[S-02]: geometry is
/// resolution independent, so the same layout renders proportionally on any monitor. There is
/// no stored pixel geometry and no stored grid.
/// </summary>
public readonly record struct FracRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;

    public static FracRect FromEdges(double left, double top, double right, double bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>
    /// [S-10] Convert a widget definition's default <b>pixel</b> size into a fraction of the screen
    /// it is dropped on, so a new widget has a consistent physical size on every monitor and
    /// scales per [S-02] thereafter.
    /// </summary>
    public static FracRect FromPixels(double x, double y, double wPx, double hPx, ScreenMetrics screen)
        => new(x, y, wPx / screen.Width, hPx / screen.Height);

    public bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(W) && double.IsFinite(H);
}

/// <summary>Logical size of the surface a layout renders into, in device-independent pixels.</summary>
public readonly record struct ScreenMetrics(double Width, double Height)
{
    public bool IsUsable => Width > 0 && Height > 0 && double.IsFinite(Width) && double.IsFinite(Height);
}

/// <summary>
/// [S-31] The eight resize handles. A corner drags one edge on each axis; a side handle drags
/// only its own edge.
/// </summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}
