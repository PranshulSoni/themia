using System.Diagnostics;
using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>One graphics adapter as DXGI lists it.</summary>
/// <param name="Index">Its place in DXGI's list: the persisted selection.</param>
/// <param name="Luid">The token its performance counters are filed under.</param>
internal sealed record GpuAdapter(int Index, string Name, string Luid, ulong DedicatedBytes, ulong SharedBytes);

/// <summary>One sample of every adapter. Readings line up with <see cref="Adapters"/>.</summary>
/// <param name="HasLoad">
/// False when no adapter has a load figure yet — the first sample after the counters were opened,
/// on a machine with no NVIDIA card. Each reading says so for itself too; the view shows "--" for
/// it rather than a 0% that never happened.
/// </param>
internal sealed record GpuSnapshot(IReadOnlyList<GpuAdapter> Adapters, IReadOnlyList<GpuReading> Readings, bool HasLoad);

/// <summary>
/// [S-431] Graphics load and memory for every adapter, sampled once for however many GPU widgets
/// are on the desktop.
/// <para>
/// <b>Where the numbers come from.</b> The original read adapter names and memory sizes from the
/// display class key in the registry, and load and memory use from the GPU performance counters
/// through PDH. The counters are kept — they are the only system-wide source, they are what Task
/// Manager reads, and PDH is a plain C API that <c>LibraryImport</c> binds without any runtime
/// marshalling, so it is AOT-clean. The registry is replaced by DXGI: its numbered subkeys include
/// adapters that are no longer present and stop at the first gap, while DXGI lists exactly the
/// adapters there are and hands over each one's LUID — the key the counters are filed under — so
/// nothing has to be parsed back out of a registry blob. DXGI is two COM calls through a vtable,
/// the same pattern as <see cref="AudioEndpoint"/>. Not WMI, which Native AOT cannot use, and not
/// <c>PerformanceCounter</c>, which is not trim-safe.
/// </para>
/// <para>
/// <b>NVIDIA.</b> When the driver's management library is present it is asked too ([S-435]): its
/// load figure is the true one for a borderless full-screen game, where the 3D counter reads near
/// zero, and it is the only source of temperature, power, fan, clocks and the video engines. It is
/// loaded with <see cref="NativeLibrary"/> and called through function pointers, so a machine
/// without it costs one failed <c>LoadLibrary</c> and nothing else.
/// </para>
/// <para>
/// <b>Shared and lazy.</b> One query, opened when the first GPU widget appears and closed when the
/// last one goes. Widgets ask at their own cadence; a request that lands within half a second of the
/// last sample gets that sample, and a request while one is being taken waits for it rather than
/// starting another. Sampling runs on the thread pool: a counter array of a few hundred engine
/// instances is not something to walk on the UI thread.
/// </para>
/// </summary>
internal static partial class GpuSampler
{
    private const string Scope = "gpu";

    /// <summary>A sample younger than this is handed out again rather than taken afresh.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMilliseconds(500);

    private static readonly Lock Gate = new();

    private static int _users;
    private static Session? _session;
    private static Task<GpuSnapshot>? _inFlight;
    private static GpuSnapshot? _last;
    private static long _lastAt;

    /// <summary>Start sharing the sampler. Pair every call with <see cref="Release"/>.</summary>
    public static void Acquire()
    {
        lock (Gate)
        {
            _users++;
        }
    }

    /// <summary>
    /// Stop sharing it. The last widget to leave closes the counters and lets go of the driver
    /// library, on the pool, after any sample still being taken has finished.
    /// </summary>
    public static void Release()
    {
        Session? closing = null;
        Task? pending;

        lock (Gate)
        {
            _users = Math.Max(0, _users - 1);

            if (_users > 0)
            {
                return;
            }

            closing = _session;
            pending = _inFlight;
            _session = null;
            _inFlight = null;
            _last = null;
        }

        if (closing is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            if (pending is not null)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // ignore: a failed last sample has already been logged by the sample itself
                    Log.Debug(Scope, $"last sample before closing failed: {ex.Message}");
                }
            }

            closing.Dispose();
        });
    }

    /// <summary>
    /// A sample of every adapter: the last one when it is fresh, the one being taken when there is
    /// one, otherwise a new one. Never faults: a failure is logged and reads as no adapters.
    /// </summary>
    public static Task<GpuSnapshot> SampleAsync()
    {
        lock (Gate)
        {
            if (_last is not null && Stopwatch.GetElapsedTime(_lastAt) < Fresh)
            {
                return Task.FromResult(_last);
            }

            if (_inFlight is not null)
            {
                return _inFlight;
            }

            _session ??= new Session();
            var session = _session;
            var task = Task.Run(() => Take(session));
            _inFlight = task;
            return task;
        }
    }

    private static GpuSnapshot Take(Session session)
    {
        GpuSnapshot snapshot;

        try
        {
            snapshot = session.Sample();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not sample the graphics adapters", ex);
            snapshot = new GpuSnapshot([], [], false);
        }

        lock (Gate)
        {
            if (ReferenceEquals(_session, session))
            {
                _last = snapshot;
                _lastAt = Stopwatch.GetTimestamp();
                _inFlight = null;
            }
        }

        return snapshot;
    }

    /// <summary>The adapters on this machine, for the settings picker. Cheap; safe on any thread.</summary>
    public static IReadOnlyList<GpuAdapter> Adapters() => Dxgi.Enumerate();

    /// <summary>
    /// The open counters and driver library. Used by one sample at a time — <see cref="SampleAsync"/>
    /// never runs two — and disposed only once no sample can still be using it.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private readonly IReadOnlyList<GpuAdapter> _adapters;
        private readonly PdhQuery? _counters;
        private readonly Nvml? _nvml;
        private bool _baselined;

        public Session()
        {
            _adapters = Dxgi.Enumerate();
            _counters = PdhQuery.Open();
            _nvml = Nvml.TryOpen();
        }

        public GpuSnapshot Sample()
        {
            var engines = _counters?.Engines() ?? [];
            var dedicated = _counters?.Dedicated() ?? [];
            var shared = _counters?.Shared() ?? [];

            // The query collected once when it opened, so every sample after the first has a
            // second reading to make a rate from.
            var baselined = _baselined;
            _baselined = true;
            var anyLoad = false;

            var readings = new List<GpuReading>(_adapters.Count);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var adapter in _adapters)
            {
                var ordinal = seen.GetValueOrDefault(adapter.Name);
                seen[adapter.Name] = ordinal + 1;

                var driver = _nvml?.Read(adapter.Name, ordinal);

                var counters = GpuRules.Utilization(engines, adapter.Luid);

                readings.Add(new GpuReading(
                    adapter.Name,
                    GpuRules.ChooseUtilization(driver?.Utilization, counters),
                    GpuRules.Bytes(dedicated, adapter.Luid),
                    adapter.DedicatedBytes,
                    GpuRules.Bytes(shared, adapter.Luid),
                    adapter.SharedBytes)
                {
                    // The driver's load needs no baseline, so an NVIDIA card has a real figure at once.
                    HasLoad = baselined || driver?.Utilization is not null,
                    TemperatureC = driver?.TemperatureC,
                    PowerWatts = driver?.PowerWatts,
                    FanPercent = driver?.FanPercent,
                    CoreClockMhz = driver?.CoreClockMhz,
                    MemoryClockMhz = driver?.MemoryClockMhz,
                    EncoderPercent = driver?.EncoderPercent,
                    DecoderPercent = driver?.DecoderPercent,
                });

                anyLoad |= readings[^1].HasLoad;
            }

            return new GpuSnapshot(_adapters, readings, anyLoad);
        }

        public void Dispose()
        {
            _counters?.Dispose();
            _nvml?.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // DXGI: which adapters there are
    // ---------------------------------------------------------------------------------------------

    private static unsafe partial class Dxgi
    {
        /// <summary>IID_IDXGIFactory1.</summary>
        private static readonly Guid FactoryInterface = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

        private const int SlotRelease = 2;
        private const int SlotEnumAdapters1 = 12;
        private const int SlotGetDesc1 = 10;

        /// <summary>DXGI_ERROR_NOT_FOUND: the end of the list.</summary>
        private const int NotFound = unchecked((int)0x887A0002);

        /// <summary>DXGI_ADAPTER_FLAG_SOFTWARE: the Basic Render Driver, which has no counters worth drawing.</summary>
        private const uint SoftwareAdapter = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterDesc1
        {
            public fixed char Description[128];
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public nuint DedicatedVideoMemory;
            public nuint DedicatedSystemMemory;
            public nuint SharedSystemMemory;
            public uint LuidLow;
            public int LuidHigh;
            public uint Flags;
        }

        public static IReadOnlyList<GpuAdapter> Enumerate()
        {
            var adapters = new List<GpuAdapter>();
            nint factory = 0;

            try
            {
                var iid = FactoryInterface;
                var created = CreateDXGIFactory1(&iid, &factory);

                if (created < 0)
                {
                    Log.Warn(Scope, $"could not open DXGI: 0x{created:X8}");
                    return adapters;
                }

                for (uint i = 0; ; i++)
                {
                    nint adapter = 0;
                    var found = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(factory, SlotEnumAdapters1))(factory, i, &adapter);

                    if (found == NotFound)
                    {
                        break;
                    }

                    if (found < 0)
                    {
                        Log.Warn(Scope, $"could not list adapter {i}: 0x{found:X8}");
                        break;
                    }

                    try
                    {
                        AdapterDesc1 desc;
                        var described = ((delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)Slot(adapter, SlotGetDesc1))(adapter, &desc);

                        if (described < 0 || (desc.Flags & SoftwareAdapter) != 0)
                        {
                            continue;
                        }

                        var name = new string(desc.Description).TrimEnd('\0').Trim();

                        adapters.Add(new GpuAdapter(
                            adapters.Count,
                            name.Length > 0 ? name : "GPU",
                            GpuRules.LuidKey(unchecked((uint)desc.LuidHigh), desc.LuidLow),
                            desc.DedicatedVideoMemory,
                            desc.SharedSystemMemory));
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }

            return adapters;
        }

        private static void Release(nint com)
        {
            if (com != 0)
            {
                ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, SlotRelease))(com);
            }
        }

        private static nint Slot(nint com, int index) => (*(nint**)com)[index];

        [LibraryImport("dxgi.dll")]
        private static partial int CreateDXGIFactory1(Guid* riid, nint* factory);
    }

    // ---------------------------------------------------------------------------------------------
    // PDH: what the adapters are doing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The three GPU counter sets in one query. Owns the query handle and closes it exactly once;
    /// the counter handles belong to the query and go with it.
    /// </summary>
    private sealed unsafe partial class PdhQuery : IDisposable
    {
        private const uint FormatDouble = 0x00000200;
        private const uint FormatLarge = 0x00000400;
        private const uint MoreData = 0x800007D2;

        /// <summary>PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA; anything above is a bad reading.</summary>
        private const uint LastGoodStatus = 1;

        private readonly nint _query;
        private readonly nint _engines;
        private readonly nint _dedicated;
        private readonly nint _shared;
        private bool _collected;
        private bool _disposed;

        private PdhQuery(nint query, nint engines, nint dedicated, nint shared)
        {
            _query = query;
            _engines = engines;
            _dedicated = dedicated;
            _shared = shared;
        }

        /// <summary>The query, collected once so the next collection has something to rate against. Null when PDH refuses.</summary>
        public static PdhQuery? Open()
        {
            var opened = PdhOpenQuery(0, 0, out var query);
            if (opened != 0)
            {
                Log.Warn(Scope, $"could not open the GPU counters: 0x{opened:X8}");
                return null;
            }

            // English paths, so a German or Japanese Windows finds the same counters.
            if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", 0, out var engines) != 0)
            {
                // No engine counters means no GPU counters at all — an old driver model or a VM.
                Log.Warn(Scope, "this machine has no GPU engine counters");
                PdhCloseQuery(query);
                return null;
            }

            // Memory is optional: a load meter is still worth drawing without it.
            if (PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out var dedicated) != 0)
            {
                dedicated = 0;
            }

            if (PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Shared Usage", 0, out var shared) != 0)
            {
                shared = 0;
            }

            var counters = new PdhQuery(query, engines, dedicated, shared);
            counters.Collect();
            return counters;
        }

        /// <summary>Per-instance engine load, after collecting the query for this sample.</summary>
        public List<(string Instance, double Percent)> Engines()
        {
            // One collection per sample, and the engine read is always the first of the three.
            Collect();

            var result = new List<(string, double)>();
            Read(_engines, FormatDouble, (name, value) => result.Add((name, value.Double)));
            return result;
        }

        public List<(string Instance, long Bytes)> Dedicated() => Memory(_dedicated);

        public List<(string Instance, long Bytes)> Shared() => Memory(_shared);

        private List<(string Instance, long Bytes)> Memory(nint counter)
        {
            var result = new List<(string, long)>();
            if (counter != 0)
            {
                Read(counter, FormatLarge, (name, value) => result.Add((name, value.Large)));
            }

            return result;
        }

        private void Collect()
        {
            if (_disposed)
            {
                return;
            }

            var status = PdhCollectQueryData(_query);

            // The very first collection of a rate counter reports "no data yet"; anything else is
            // worth a line, once.
            if (status != 0 && _collected)
            {
                Log.Debug(Scope, $"collecting the GPU counters returned 0x{status:X8}");
            }

            _collected = true;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Value
        {
            [FieldOffset(0)]
            public double Double;

            [FieldOffset(0)]
            public long Large;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Item
        {
            public char* Name;
            public uint Status;
            public Value Value;
        }

        private void Read(nint counter, uint format, Action<string, Value> each)
        {
            if (_disposed)
            {
                return;
            }

            // The instance list can grow between asking for the size and reading — a process
            // starts a GPU context — so the size is asked again a few times rather than once.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                uint size = 0;
                var sized = PdhGetFormattedCounterArray(counter, format, ref size, out var count, 0);

                if (sized != MoreData || size == 0 || count == 0)
                {
                    return;
                }

                var buffer = NativeMemory.Alloc(size);
                try
                {
                    var read = PdhGetFormattedCounterArray(counter, format, ref size, out count, (nint)buffer);

                    if (read == MoreData)
                    {
                        continue;
                    }

                    if (read != 0)
                    {
                        return;
                    }

                    var items = (Item*)buffer;
                    for (var i = 0; i < count; i++)
                    {
                        if (items[i].Status <= LastGoodStatus && items[i].Name is not null)
                        {
                            each(new string(items[i].Name), items[i].Value);
                        }
                    }

                    return;
                }
                finally
                {
                    NativeMemory.Free(buffer);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PdhCloseQuery(_query);
        }

        [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
        private static partial uint PdhOpenQuery(nint dataSource, nint userData, out nint query);

        [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

        [LibraryImport("pdh.dll")]
        private static partial uint PdhCollectQueryData(nint query);

        [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
        private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

        [LibraryImport("pdh.dll")]
        private static partial uint PdhCloseQuery(nint query);
    }

    // ---------------------------------------------------------------------------------------------
    // NVML: what only an NVIDIA driver will say
    // ---------------------------------------------------------------------------------------------

    /// <summary>What the NVIDIA driver reported for one card. Each field null when that call failed.</summary>
    private sealed record DriverReading(
        double? Utilization,
        double? TemperatureC,
        double? PowerWatts,
        int? FanPercent,
        int? CoreClockMhz,
        int? MemoryClockMhz,
        double? EncoderPercent,
        double? DecoderPercent);

    /// <summary>
    /// [S-435] The NVIDIA management library, if this machine has it. Initialised once per session
    /// and shut down with it; a failure at any point leaves every driver-only row hidden.
    /// </summary>
    private sealed unsafe class Nvml : IDisposable
    {
        private const int Success = 0;
        private const int TemperatureGpu = 0;
        private const int ClockGraphics = 0;
        private const int ClockMemory = 2;

        private readonly nint _library;
        private readonly delegate* unmanaged<int> _shutdown;
        private readonly delegate* unmanaged<uint*, int> _count;
        private readonly delegate* unmanaged<uint, nint*, int> _handle;
        private readonly delegate* unmanaged<nint, byte*, uint, int> _name;
        private readonly delegate* unmanaged<nint, uint*, int> _utilization;
        private readonly delegate* unmanaged<nint, int, uint*, int> _temperature;
        private readonly delegate* unmanaged<nint, uint*, int> _power;
        private readonly delegate* unmanaged<nint, uint*, int> _fan;
        private readonly delegate* unmanaged<nint, int, uint*, int> _clock;
        private readonly delegate* unmanaged<nint, uint*, uint*, int> _encoder;
        private readonly delegate* unmanaged<nint, uint*, uint*, int> _decoder;
        private bool _disposed;

        private Nvml(nint library)
        {
            _library = library;
            _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
            _count = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
            _handle = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
            _name = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
            _utilization = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");
            _temperature = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
            _power = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerUsage");
            _fan = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetFanSpeed");
            _clock = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetClockInfo");
            _encoder = (delegate* unmanaged<nint, uint*, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetEncoderUtilization");
            _decoder = (delegate* unmanaged<nint, uint*, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetDecoderUtilization");
        }

        /// <summary>The library, initialised, or null on any machine without a working NVIDIA driver.</summary>
        public static Nvml? TryOpen()
        {
            // System32 is where every current driver installs it; the NVSMI folder is the old home.
            var system = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvml.dll");

            if (!NativeLibrary.TryLoad(system, out var library) && !NativeLibrary.TryLoad(legacy, out library))
            {
                return null;
            }

            try
            {
                var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
                var status = init();

                if (status != Success)
                {
                    Log.Info(Scope, $"the NVIDIA driver library would not start (status {status}); using the counters only");
                    NativeLibrary.Free(library);
                    return null;
                }

                return new Nvml(library);
            }
            catch (EntryPointNotFoundException ex)
            {
                Log.Warn(Scope, "the NVIDIA driver library is missing an entry point; using the counters only", ex);
                NativeLibrary.Free(library);
                return null;
            }
        }

        /// <summary>
        /// The card DXGI calls <paramref name="name"/>, or null when it is not an NVIDIA card or cannot
        /// be told apart. Matched by name; <paramref name="ordinal"/> — how many adapters of the same
        /// name came before it — picks between identical cards.
        /// </summary>
        public DriverReading? Read(string name, int ordinal)
        {
            if (_disposed || !name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (Device(name, ordinal) is not { } device)
            {
                return null;
            }

            uint a, b;
            var utilization = stackalloc uint[2];

            return new DriverReading(
                _utilization(device, utilization) == Success ? utilization[0] : null,
                _temperature(device, TemperatureGpu, &a) == Success ? a : null,
                _power(device, &a) == Success ? a / 1000.0 : null,
                _fan(device, &a) == Success ? (int)a : null,
                _clock(device, ClockGraphics, &a) == Success ? (int)a : null,
                _clock(device, ClockMemory, &a) == Success ? (int)a : null,
                _encoder(device, &a, &b) == Success ? a : null,
                _decoder(device, &a, &b) == Success ? a : null);
        }

        private nint? Device(string name, int ordinal)
        {
            uint count;
            if (_count(&count) != Success || count == 0)
            {
                return null;
            }

            nint? only = null;
            var sameName = 0;
            var name8 = stackalloc byte[96];

            for (uint i = 0; i < count; i++)
            {
                nint handle;
                if (_handle(i, &handle) != Success)
                {
                    continue;
                }

                only = count == 1 ? handle : null;

                if (_name(handle, name8, 96) != Success)
                {
                    continue;
                }

                var deviceName = Marshal.PtrToStringUTF8((nint)name8) ?? string.Empty;

                // Two identical cards: the n-th DXGI one of that name is the n-th driver one.
                if (string.Equals(deviceName.Trim(), name, StringComparison.OrdinalIgnoreCase) && sameName++ == ordinal)
                {
                    return handle;
                }
            }

            // No name matched. With a single card there is no way to pick the wrong one; with
            // several, a guess would put one card's temperature under another card's name.
            return only;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown();
            NativeLibrary.Free(_library);
        }
    }
}
