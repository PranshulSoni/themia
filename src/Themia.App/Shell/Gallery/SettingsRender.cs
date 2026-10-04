#if DEBUG
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Storage;
using Windows.Graphics;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// The debug-only settings render: <c>Themia.exe --settings-png &lt;Page&gt; &lt;out.png&gt; [waitMs]</c>
/// builds one settings page off-screen and writes it to a PNG, then exits. [S-499]
/// <para>
/// The gallery's counterpart for the settings window, so a page can be looked at without running
/// the app — no state file is read, no single-instance hand-off happens, and nothing is written:
/// the host is built over a demo state and a <see cref="AppStateStore.Detached"/> store, so a page
/// that saves as it is built still never touches the user's real state.
/// </para>
/// <para>
/// The page is drawn at the width the settings window gives its body and at its full height, so
/// rows below the fold are in the picture too.
/// </para>
/// </summary>
internal static class SettingsRender
{
    private const string Scope = "settings-render";

    /// <summary>The settings window's body width: 1060 wide, less the 280 navigation pane.</summary>
    private const double BodyWidth = 780;

    /// <summary>The body's own padding in the settings window.</summary>
    private static readonly Thickness BodyPadding = new(28, 16, 28, 28);

    /// <summary>
    /// Handle <c>--settings-png</c>. Returns false when the arguments are not for it, so the caller
    /// starts the app normally.
    /// </summary>
    public static bool TryRunFromCommandLine(Application app, string[] args)
    {
        var at = Array.IndexOf(args, "--settings-png");
        if (at < 0)
        {
            return false;
        }

        if (at + 2 >= args.Length)
        {
            Log.Error(Scope, "usage: --settings-png <General|Appearance|Layout|About|Accounts|Widget:<Kind>> <out.png> [waitMs]");
            app.Exit();
            return true;
        }

        var name = args[at + 1];
        var outPath = Path.GetFullPath(args[at + 2]);
        var wait = at + 3 < args.Length && int.TryParse(args[at + 3], out var ms) ? ms : 1500;

        // "Widget:<Kind>" is that kind's settings page, for the demo profile's widget of that kind.
        var widgetKind = name.StartsWith("Widget:", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<WidgetKind>(name["Widget:".Length..], ignoreCase: true, out var kind)
                ? kind
                : (WidgetKind?)null;

        if ((widgetKind is null ? Resolve(name) : new ConfigTarget.App()) is not { } target)
        {
            Log.Error(Scope, $"unknown settings page '{name}'");
            Console.Error.WriteLine($"settings: unknown page '{name}'");
            app.Exit();
            return true;
        }

        if (DispatcherQueue.GetForCurrentThread() is not { } dispatcher)
        {
            Log.Error(Scope, "no dispatcher on this thread; cannot render");
            app.Exit();
            return true;
        }

        _ = RenderAsync(app, dispatcher, target, widgetKind, outPath, wait);
        return true;
    }

    /// <summary>
    /// A page name to its target. The app pages are the <see cref="AppPage"/> members by name, so a
    /// page added there — Accounts included — renders without a change here.
    /// </summary>
    private static ConfigTarget? Resolve(string name)
        => Enum.TryParse<AppPage>(name, ignoreCase: true, out var page) && Enum.IsDefined(page)
            ? new ConfigTarget.App(page)
            : null;

    private static async Task RenderAsync(Application app, DispatcherQueue dispatcher, ConfigTarget target, WidgetKind? widgetKind, string outPath, int waitMs)
    {
        AppHost? host = null;
        Window? window = null;

        try
        {
            // A path of its own under %TEMP%, not the real one: the host keeps other files beside
            // the state file (accounts, screen time), and those must not be the user's either.
            var statePath = Path.Combine(Path.GetTempPath(), "themia-settings-render", "state.json");
            host = new AppHost(dispatcher, AppStateStore.Detached(statePath), DemoState());

            if (widgetKind is { } wanted)
            {
                var demo = host.State.Views.SelectMany(v => v.Widgets).FirstOrDefault(w => w.Kind == wanted);
                if (demo is null)
                {
                    demo = WidgetInstance.Create(wanted, new FracRect(0.6, 0.6, 0.2, 0.2));
                    host.State.Views[0].Widgets.Add(demo);
                }

                target = new ConfigTarget.Widget(demo.Id);
            }

            var page = ConfigPages.Build(host, target, _ => { });

            var body = new Border
            {
                Child = page,
                Width = BodyWidth,
                Padding = BodyPadding,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,

                // The app's own theme, not a forced one: the pages paint with brushes looked up
                // from the application's resources, which follow the app theme, so forcing this
                // element dark would draw dark text on light cards on a light-themed PC.
                Background = FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.Black),
            };

            // Measured up front so the window can be made tall enough for all of it: a page cut off
            // at the fold would hide exactly the rows most likely to be wrong.
            body.Measure(new Windows.Foundation.Size(BodyWidth, double.PositiveInfinity));
            var height = Math.Ceiling(Math.Max(body.DesiredSize.Height, 200));

            window = new Window { Title = "Themia settings render", Content = new ScrollViewer { Content = body } };

            // Off-screen and never activated, like the gallery's render: it must not take the user's
            // focus or appear on their desktop.
            var scale = 1.5;
            window.AppWindow.Resize(new SizeInt32((int)((BodyWidth + 40) * scale), (int)((height + 80) * scale)));
            window.AppWindow.Move(new PointInt32(-32000, -32000));
            window.AppWindow.Show(activateWindow: false);

            // Long enough for the logo bitmaps to decode; the page itself has nothing to fetch.
            await Task.Delay(waitMs).ConfigureAwait(true);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await WidgetGallery.SavePngAsync(body, outPath).ConfigureAwait(true);
            Console.WriteLine($"settings: wrote {outPath}");
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "rendering the settings page failed", ex);
        }
        finally
        {
            host?.Dispose();
            window?.Close();
            app.Exit();
        }
    }

    /// <summary>A believable profile: three views with a few widgets, defaults everywhere else.</summary>
    private static AppState DemoState()
    {
        var desktop = ViewState.Create("Desktop");
        desktop.Widgets.Add(WidgetInstance.Create(WidgetKind.Clock, new FracRect(0.40, 0.08, 0.20, 0.12)));
        desktop.Widgets.Add(WidgetInstance.Create(WidgetKind.WeatherNow, new FracRect(0.05, 0.08, 0.15, 0.18)));

        var work = ViewState.Create("Work");
        work.Widgets.Add(WidgetInstance.Create(WidgetKind.Calendar, new FracRect(0.05, 0.08, 0.20, 0.30)));
        work.Widgets.Add(WidgetInstance.Create(WidgetKind.Email, new FracRect(0.30, 0.08, 0.25, 0.30)));

        var evening = ViewState.Create("Evening");
        evening.Widgets.Add(WidgetInstance.Create(WidgetKind.Music, new FracRect(0.05, 0.70, 0.20, 0.12)));

        return new AppState
        {
            Views = [desktop, work, evening],
            ActiveViewId = desktop.Id,
        };
    }
}
#endif
