using Themia.App.Services.Hearth;

namespace Themia.App.Services;

/// <summary>
/// The few services a widget view needs but cannot be handed.
/// <para>
/// Widget views are built by the catalogue from a <c>WidgetKind</c> alone, so there is no place to
/// inject into. The ticker is passed through <c>IWidgetView.Attach</c> because every widget needs
/// it; the hearth connection is here because only one widget does, and threading it through every
/// signature for that would be worse than one explicit lookup.
/// </para>
/// </summary>
internal static class AppServices
{
    /// <summary>The app's single hearth connection, or null before the host has started.</summary>
    public static HearthMailService? Mail { get; internal set; }
}
