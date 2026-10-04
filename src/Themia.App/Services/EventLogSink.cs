using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// [S-986] Errors mirrored into the Windows Application event log, under source <c>Themia</c>,
/// event id 1000.
/// <para>
/// A user who reports "it just closed" will not find <c>%LOCALAPPDATA%\Themia\logs</c>, and a
/// hard crash shows up in Event Viewer only as a WER "Application Error" with a module offset.
/// Mirroring our own errors next to it puts both halves in one place support can ask for by name.
/// </para>
/// <para>
/// The source is registered by the installer (it needs HKLM). Without that registration Windows
/// still files the event under the Application log, wrapped in a "description cannot be found"
/// preamble — degraded, still readable, never depended on. Everything here is best-effort and
/// silent on failure: the caller is the logging pipeline, which must never throw or recurse, and
/// the file already has the record.
/// </para>
/// <para>
/// Privacy: the Application log is readable by any local administrator, the files only by the
/// user. That is why only errors go there, why they are capped, and why they are redacted first
/// (<see cref="LogRedactor"/>, applied before any sink).
/// </para>
/// </summary>
internal static partial class EventLogSink
{
    private const string SourceName = "Themia";
    private const uint EventIdError = 1000;
    private const ushort EventLogErrorType = 0x0001;

    private static readonly Lazy<SourceHandle?> Source = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _seen;

    public static void Report(LogRecord record)
    {
        if (record.Level != LogLevel.Error)
        {
            return;
        }

        var seenBefore = Interlocked.Increment(ref _seen) - 1;
        var text = LogPolicy.EventDecision(seenBefore) switch
        {
            EventLogEmit.Record => LogPolicy.ForEventLog(LogFormat.Record(record)),
            EventLogEmit.FinalNotice => LogPolicy.SuppressionNotice(LogFiles.Folder),
            _ => null,
        };

        if (text is not null)
        {
            Write(text);
        }
    }

    private static unsafe void Write(string text)
    {
        if (Source.Value is not { IsInvalid: false } source)
        {
            return;
        }

        var added = false;
        try
        {
            source.DangerousAddRef(ref added);
            fixed (char* chars = text)
            {
                var strings = stackalloc char*[1];
                strings[0] = chars;

                // ignore: best-effort mirror; the file log is the record of truth, and there is
                // nowhere to report this failure without recursing into the log.
                _ = ReportEvent(source.DangerousGetHandle(), EventLogErrorType, 0, EventIdError, 0, 1, 0, strings, 0);
            }
        }
        catch (ObjectDisposedException)
        {
            // ignore: the handle went with the process shutting down.
        }
        finally
        {
            if (added)
            {
                source.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Once per process, on the first error. Null when the Event Log service is unavailable — that is
    /// decided once, not retried per record.
    /// </summary>
    private static SourceHandle? Register()
    {
        var handle = RegisterEventSource(null, SourceName);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            System.Diagnostics.Debug.WriteLine($"[themia] no event log source (win32 {Marshal.GetLastPInvokeError()}); errors stay in the file only");
            return null;
        }

        return handle;
    }

    /// <summary>The registered source; deregistered when released (the process keeps it to the end).</summary>
    private sealed class SourceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SourceHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => DeregisterEventSource(handle);
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegisterEventSourceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SourceHandle RegisterEventSource(string? uncServerName, string sourceName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeregisterEventSource(nint eventLog);

    [LibraryImport("advapi32.dll", EntryPoint = "ReportEventW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReportEvent(
        nint eventLog,
        ushort type,
        ushort category,
        uint eventId,
        nint userSid,
        ushort numStrings,
        uint dataSize,
        char** strings,
        nint rawData);
}
