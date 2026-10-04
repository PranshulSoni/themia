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
}

/// <summary>
/// The app-wide pages. A value only becomes a page in the window once it has an entry in
/// <see cref="ConfigPages.AppPages"/>; until then it is reserved for the package building it.
/// </summary>
internal enum AppPage
{
    General,
    Appearance,
    Layout,
    Accounts,
    About,

    /// <summary>Intro, view-transition and unlock animations, and the startup sound.</summary>
    Animations,

    /// <summary>Microsoft Store licence state.</summary>
    License,

    /// <summary>Log files, verbose logging and developer mode.</summary>
    Logs,
}

/// <summary>Where an app page sits in the settings navigation.</summary>
internal enum AppPageSlot
{
    /// <summary>Under the "Themia" heading at the top.</summary>
    Main,

    /// <summary>Pinned to the foot of the menu.</summary>
    Footer,
}

/// <summary>One app-wide settings page: its menu entry and the code that builds its body.</summary>
internal sealed record AppPageEntry(
    AppPage Page,
    string Title,
    string Glyph,
    AppPageSlot Slot,
    Func<Services.AppHost, Microsoft.UI.Xaml.FrameworkElement> Build);
