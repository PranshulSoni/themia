namespace Themia.App.Shell.Config;

/// <summary>Which page of the config window to show.</summary>
internal abstract record ConfigTarget
{
    /// <summary>App-wide settings.</summary>
    public sealed record App(AppPage Page = AppPage.General) : ConfigTarget;

    /// <summary>One widget's settings.</summary>
    public sealed record Widget(string WidgetId) : ConfigTarget;

    /// <summary>One view's settings.</summary>
    public sealed record View(string ViewId) : ConfigTarget;

    /// <summary>One display's settings.</summary>
    public sealed record Display(string MonitorId) : ConfigTarget;

    /// <summary>The widget gallery. <paramref name="IntoViewId"/> is the view a new widget lands in.</summary>
    public sealed record Add(string? IntoViewId = null) : ConfigTarget;
}

internal enum AppPage
{
    General,
    Appearance,
    Layout,
    About,
}
