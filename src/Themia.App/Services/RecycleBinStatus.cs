using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-1389] How full the Recycle Bin is, for the badge on a shortcut that points at it (original
/// S-75): one process-wide answer, so any number of widgets showing the bin cost one query.
/// <para>
/// <c>SHQueryRecycleBin</c> over every drive walks each drive's <c>$Recycle.Bin</c>, which is disk
/// I/O, so it runs on the thread pool. A refresh is single-flight (callers share the query in
/// flight) and throttled: an answer younger than five seconds is reused rather than asked again, so
/// several widgets waking on the same tick ask once. A failed query keeps the last answer and never
/// invents "empty". Only widgets that pin the bin ask at all (every 15 s, on the shared ticker, which
/// pauses while the desktop cannot be seen).
/// </para>
/// </summary>
internal static partial class RecycleBinStatus
{
    private const string Scope = "recycle-bin";

    /// <summary>How often a widget showing the bin asks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>An answer younger than this is reused rather than asked again.</summary>
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(5);

    private static Task? _inFlight;
    private static DateTimeOffset _answeredAt = DateTimeOffset.MinValue;

    /// <summary>The last answer, or null when none has come back yet. UI thread only.</summary>
    public static RecycleBinState? Current { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Ask again, unless an answer is fresh or a query is already running. UI thread only; never
    /// throws.
    /// </summary>
    public static void Refresh()
    {
        // IsCompleted, not null: a query that finished before its task was stored must not read as
        // one still running.
        if (_inFlight is { IsCompleted: false } || DateTimeOffset.UtcNow - _answeredAt < FreshFor)
        {
            return;
        }

        _inFlight = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        RecycleBinState? answer;

        try
        {
            answer = await Task.Run(Query).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "asking the Recycle Bin how full it is threw", ex);
            answer = null;
        }
        finally
        {
            _inFlight = null;
        }

        if (answer is not { } state)
        {
            return; // the last answer stands; a failed query is not "empty".
        }

        _answeredAt = DateTimeOffset.UtcNow;

        if (Current == state)
        {
            return;
        }

        Current = state;

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "a Recycle Bin listener threw", ex);
        }
    }

    private static unsafe RecycleBinState? Query()
    {
        var info = new QueryInfo { Size = (uint)sizeof(QueryInfo) };

        var result = SHQueryRecycleBin(null, &info);

        if (result < 0)
        {
            Log.Debug(Scope, $"SHQueryRecycleBin failed (0x{result:X8})");
            return null;
        }

        return new RecycleBinState(Math.Max(0, info.Items), Math.Max(0, info.Bytes));
    }

    /// <summary>SHQUERYRBINFO, as laid out on x64 (cbSize, then two 8-byte-aligned 64-bit counts).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHQueryRecycleBin(string? rootPath, QueryInfo* info);
}
