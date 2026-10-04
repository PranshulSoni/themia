using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>Containers, from outside the desktop surface: the settings window's "Add widget" ([S-870]).</summary>
internal sealed partial class AppHost
{
    /// <summary>
    /// [S-870] Open the widget picker on the display showing <paramref name="containerId"/>, with that
    /// container chosen. False when no display shows it (its view is not on screen).
    /// </summary>
    public bool AddInside(string containerId)
    {
        foreach (var surface in _windows)
        {
            if (surface.CardFor(containerId) is { } card)
            {
                surface.RequestAddInside(card);
                return true;
            }
        }

        Log.Info(Scope, $"container '{containerId}' is not on any display; nowhere to open the picker");
        return false;
    }
}
