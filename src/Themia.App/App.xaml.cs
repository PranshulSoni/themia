using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Storage;

namespace Themia.App;

public partial class App : Application
{
    private const string Scope = "app";

    private AppHost? _host;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InstallLogSink();

#if DEBUG
        // The widget gallery runs instead of the app, never alongside it: no state file, no
        // single-instance hand-off, so it can render while the user's own copy keeps running.
        if (Shell.Gallery.WidgetGallery.TryRunFromCommandLine(this, Environment.GetCommandLineArgs()))
        {
            return;
        }
#endif

        // Themia has no tray entry and no window in the taskbar, so launching it again is the
        // gesture a user reaches for when they want at it. That has to mean "show me the copy I
        // already have", not "start a second desktop layer on top of it" — there is nothing on
        // screen that would tell the two apart.
        if (CommandWindow.TrySignalRunningInstance())
        {
            Log.Info(Scope, "already running; handed the request to that copy");
            Exit();
            return;
        }

        var store = new AppStateStore(AppStateStore.DefaultPath());
        var load = store.Load();

        if (load.Outcome == LoadOutcome.Recovered)
        {
            // Defaults are showing and the user's layout is not. That is worth saying out loud;
            // the previous file is preserved next to the state file.
            Log.Warn(Scope, $"started with default layout: {load.Detail}");
        }

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            Log.Error(Scope, "no dispatcher queue on the launch thread; cannot start");
            Exit();
            return;
        }

        _host = new AppHost(dispatcher, store, load.State);
        _host.Start();
    }

    /// <summary>
    /// Route the core log to the debugger and, in a release build, to a rolling file next to the
    /// state. Nothing is silent: an error the user cannot see is an error nobody fixes.
    /// </summary>
    private static void InstallLogSink()
    {
        Log.AddSink(static record =>
        {
            var line = $"[{record.At:HH:mm:ss}] {record.Level,-5} {record.Scope}: {record.Message}";
            if (record.Error is not null)
            {
                // Full ToString, not just the message: an AOT-only XAML or activation failure is
                // undiagnosable without the inner exception and the stack.
                line += Environment.NewLine + record.Error;
            }

            System.Diagnostics.Debug.WriteLine(line);

            if (record.Level < LogLevel.Warn)
            {
                return;
            }

            try
            {
                var path = Path.Combine(Path.GetDirectoryName(AppStateStore.DefaultPath())!, "themia.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // ignore: a log sink that throws on a full or locked disk would turn a warning into a crash
            }
            catch (UnauthorizedAccessException)
            {
                // ignore: as above
            }
        });
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(Scope, "unhandled exception on the UI thread", e.Exception);

        // Keep the widget layer alive. A throw while painting one widget should not take the whole
        // desktop down; the error is recorded and the surface carries on.
        e.Handled = true;
    }
}
