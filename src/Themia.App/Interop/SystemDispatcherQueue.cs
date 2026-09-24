using Themia.App.Interop;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// Ensures the UI thread has a <b>system</b> dispatcher queue.
/// <para>
/// WinUI gives the thread a <c>Microsoft.UI.Dispatching.DispatcherQueue</c>, which is a different
/// thing from the <c>Windows.System</c> one that <c>Windows.UI.Composition.Compositor</c> requires.
/// Constructing a system compositor without it fails with "The caller must initialize DispatcherQueue
/// on this thread before this operation" — and that compositor is the only source of the transparent
/// brush the widget surface needs, because the backdrop interface is projected in terms of the system
/// composition types.
/// </para>
/// <para>
/// The controller is created once and deliberately never shut down: it lives as long as the UI
/// thread, and tearing it down would invalidate every brush created from its compositor.
/// </para>
/// </summary>
internal static class SystemDispatcherQueue
{
    private const string Scope = "dispatcher";

    private static nint _controller;
    private static bool _attempted;

    /// <summary>Create the system dispatcher queue for this thread if it does not have one. Idempotent.</summary>
    public static bool EnsureOnCurrentThread()
    {
        if (_controller != 0)
        {
            return true;
        }

        if (_attempted)
        {
            return false; // already failed once; do not retry on every window
        }

        _attempted = true;

        var options = new Win32.DispatcherQueueOptions
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.DispatcherQueueOptions>(),
            ThreadType = Win32.DqTypeThreadCurrent,
            ApartmentType = Win32.DqTatComStA,
        };

        var hr = Win32.CreateDispatcherQueueController(options, out var controller);
        if (hr < 0 || controller == 0)
        {
            Log.Error(Scope, $"CreateDispatcherQueueController failed (hr 0x{hr:X8})");
            return false;
        }

        _controller = controller;
        return true;
    }
}
