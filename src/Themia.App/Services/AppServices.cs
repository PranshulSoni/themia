using Themia.App.Services.Hearth;

namespace Themia.App.Services;

/// <summary>
/// The few services a widget view needs but cannot be handed.
/// <para>
/// Widget views are built by the catalogue from a <c>WidgetKind</c> alone, so there is no place to
/// inject into. The ticker is passed through <c>IWidgetView.Attach</c> because every widget needs
/// it; the hearth services are here because only a few widgets do, and threading them through every
/// signature for that would be worse than one explicit lookup.
/// </para>
/// <para>
/// <see cref="Mail"/> and <see cref="Data"/> are two views of <b>one</b> connection, not two
/// connections: <see cref="Data"/> is constructed over <see cref="Mail"/>, so there is a single
/// pipe and a single consent prompt however many hearth widgets are on the desktop.
/// </para>
/// </summary>
internal static class AppServices
{
    /// <summary>
    /// The app's single hearth connection, or null before the host has started. Mail reads through
    /// it directly; everything else goes via <see cref="Data"/>.
    /// </summary>
    public static HearthHub? Hub { get; internal set; }

    /// <summary>
    /// Contacts, calendar and tasks, read over the same connection as <see cref="Mail"/>. Null
    /// before the host has started.
    /// </summary>
    public static HearthDataService? Data { get; internal set; }
}
