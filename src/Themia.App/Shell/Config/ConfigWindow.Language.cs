using Themia.Core.Diagnostics;
using Themia.Core.Localization;

namespace Themia.App.Shell.Config;

/// <summary>[S-1110] Following a language switch while the settings window is open.</summary>
internal sealed partial class ConfigWindow
{
    /// <summary>
    /// Redraw in the new language: the navigation tree, the open page and the search index are all
    /// built from strings, so all three are rebuilt (nothing else changed, so nothing else is).
    /// <para>
    /// Queued rather than done inline because the usual sender is the language box on the General
    /// page: replacing that page inside the box's own SelectionChanged would tear the control down
    /// mid-event. The queue runs it straight after, in order — no timing involved.
    /// </para>
    /// </summary>
    private void OnLanguageChanged(Locale locale)
    {
        if (_closed)
        {
            return;
        }

        var queued = _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
            {
                return;
            }

            ApplyDevBanner();
            RebuildNavigation();
            _index.Invalidate();
            RenderBody();
        });

        if (!queued)
        {
            Log.Warn(Scope, $"could not queue the redraw for language {LanguageResolver.Code(locale)}");
        }
    }
}
