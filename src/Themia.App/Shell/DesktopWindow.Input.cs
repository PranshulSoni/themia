using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Shell.Config;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;
using Windows.Graphics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Themia.App.Shell;

/// <summary>
/// Keyboard: the edit-mode shortcuts, and when the surface may take focus for a widget being
/// typed into.
/// </summary>
internal sealed partial class DesktopWindow
{
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!EditMode)
        {
            return;
        }

        var ctrl = InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (args.Key)
        {
            case Windows.System.VirtualKey.C when ctrl:
                _host.CopySelected();
                break;

            case Windows.System.VirtualKey.X when ctrl:
                _host.CutSelected();
                break;

            case Windows.System.VirtualKey.V when ctrl:
                // [S-74] The display under the pointer, not the one holding the keyboard. Those
                // differ exactly when it matters: copy on one screen, point at the other, paste.
                _host.PasteAtCursor();
                break;

            case Windows.System.VirtualKey.Delete:
                _host.DeleteSelected();
                break;

            case Windows.System.VirtualKey.Escape:
                // Escape backs out one step at a time: out of placing first, out of editing second.
                if (_host.PlacingKind is not null)
                {
                    _host.CancelPlacement();
                }
                else
                {
                    _host.SetEditMode(false);
                }

                break;

            default:
                return;
        }

        args.Handled = true;
    }

    /// <summary>
    /// A widget asked for the keyboard, or gave it up.
    /// <para>
    /// The surface carries <c>WS_EX_NOACTIVATE</c> so that clicking a widget never pulls focus off
    /// the user's work — which is right for every widget except the ones you type into, where it
    /// means the caret blinks and the letters go somewhere else. Rather than a second mechanism,
    /// this feeds the same <c>interactive</c> flag edit mode already uses: the surface becomes
    /// activatable for exactly as long as some widget wants typing, and the z-order controller keeps
    /// it on the desktop regardless, so it still never rises above another window.
    /// </para>
    /// </summary>
    public void RequestTextInput(WidgetCard card, bool wanted)
    {
        if (_closed)
        {
            return;
        }

        var changed = wanted ? _textInputWanted.Add(card) : _textInputWanted.Remove(card);
        if (!changed)
        {
            return;
        }

        ApplyInteractivity();

        // Asking is not enough: the surface has to actually be the foreground window before Windows
        // routes a keystroke to it.
        if (wanted && _textInputWanted.Count == 1 && !_host.EditMode)
        {
            Win32.SetForegroundWindow(_hwnd);
        }
    }

    /// <summary>
    /// The one place that decides whether the surface takes focus. Edit mode and a widget being
    /// typed into are two reasons for the same thing, so they share the flag rather than each
    /// setting the style themselves and overwriting each other.
    /// </summary>
    private void ApplyInteractivity()
    {
        DesktopLayer.Apply(_hwnd, interactive: _host.EditMode || _textInputWanted.Count > 0);
        ApplyChromeless();
    }
}
