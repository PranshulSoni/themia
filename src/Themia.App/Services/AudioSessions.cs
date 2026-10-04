using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>What the mixer can see right now.</summary>
/// <param name="Available">False when there is no output device or Core Audio would not answer.</param>
/// <param name="Sessions">Every live session of the default output device, ungrouped.</param>
/// <param name="Master">The device's own level and mute, or null when it could not be read.</param>
internal sealed record MixerSnapshot(bool Available, IReadOnlyList<AudioSessionInfo> Sessions, (double Level, bool Muted)? Master)
{
    public static MixerSnapshot Unavailable { get; } = new(false, [], null);
}

/// <summary>
/// [S-440] The audio sessions of the default output device, kept current by Core Audio's own
/// notifications rather than by polling.
/// <para>
/// <b>One worker thread, in the multithreaded apartment.</b> Session notifications are only
/// delivered to a session manager used from the MTA, and every call here is a blocking COM call
/// that has no business on the UI thread. So the whole conversation with Core Audio happens on one
/// dedicated thread: it binds to the default device, registers for the device changing, sessions
/// appearing, each session's volume, state and disconnect, and the master volume; and it sleeps
/// until one of those — or a write from a slider — wakes it. A wake reads every session once and
/// publishes one snapshot, however many notifications arrived meanwhile.
/// </para>
/// <para>
/// <b>Callbacks are four static COM objects.</b> Core Audio calls back through interfaces we must
/// implement, and Native AOT cannot build a COM wrapper at runtime, so each is a vtable of
/// <see cref="UnmanagedCallersOnlyAttribute"/> function pointers over a block of native memory,
/// built once and never freed. Reference counting is a no-op on them — they are immortal — which is
/// what makes a late callback during teardown harmless. A callback only sets a flag and wakes the
/// worker; it never calls back into Core Audio (which the documentation forbids from inside a
/// notification) and never lets an exception escape.
/// </para>
/// <para>
/// <b>Started by the first mixer and stopped by the last.</b> Nothing is registered and no thread
/// exists while no mixer is on the desktop.
/// </para>
/// </summary>
internal static unsafe partial class AudioSessions
{
    private const string Scope = "mixer";

    private static readonly Lock Gate = new();
    private static readonly List<Subscriber> Subscribers = [];
    private static volatile Worker? _worker;

    /// <summary>
    /// Receive every snapshot on the calling thread's dispatcher, starting with the current one.
    /// Dispose the result to stop; the last subscriber to leave stops the worker.
    /// </summary>
    public static IDisposable Subscribe(Action<MixerSnapshot> onSnapshot)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);

        var dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("subscribe from a thread with a dispatcher");

        var subscriber = new Subscriber(dispatcher, onSnapshot);
        MixerSnapshot? current;

        lock (Gate)
        {
            Subscribers.Add(subscriber);

            if (_worker is null)
            {
                _worker = new Worker();
                _worker.Start();
            }

            current = _worker.Last;
        }

        if (current is not null)
        {
            subscriber.Deliver(current);
        }

        return subscriber;
    }

    /// <summary>[S-446] Queue a level for a row; the newest value per key wins.</summary>
    public static void SetLevel(string key, double level) => _worker?.PostLevel(key, MixerRules.ClampLevel(level));

    /// <summary>[S-447] Queue a mute for a row.</summary>
    public static void SetMute(string key, bool muted) => _worker?.PostMute(key, muted);

    private static void Unsubscribe(Subscriber subscriber)
    {
        Worker? stopping = null;

        lock (Gate)
        {
            if (!Subscribers.Remove(subscriber) || Subscribers.Count > 0)
            {
                return;
            }

            stopping = _worker;
            _worker = null;
        }

        stopping?.Stop();
    }

    private static void Publish(MixerSnapshot snapshot)
    {
        Subscriber[] targets;
        lock (Gate)
        {
            targets = [.. Subscribers];
        }

        foreach (var target in targets)
        {
            target.Deliver(snapshot);
        }
    }

    /// <summary>A notification arrived: wake the worker that is current, whichever one that is.</summary>
    private static void Notify(Change change) => _worker?.Signal(change);

    [Flags]
    private enum Change
    {
        None = 0,
        Sessions = 1,
        Device = 2,
    }

    private sealed class Subscriber(DispatcherQueue dispatcher, Action<MixerSnapshot> onSnapshot) : IDisposable
    {
        private volatile bool _disposed;

        public void Deliver(MixerSnapshot snapshot)
        {
            if (_disposed)
            {
                return;
            }

            dispatcher.TryEnqueue(() =>
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    onSnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    Log.Error(Scope, "a mixer could not show the sessions", ex);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unsubscribe(this);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The worker
    // ---------------------------------------------------------------------------------------------

    /// <summary>One session the worker holds and listens to, until it expires or the device changes.</summary>
    private sealed class Tracked
    {
        public nint Control;
        public nint Volume;
        public string GroupKey = string.Empty;
        public bool Seen;
    }

    private sealed class Worker
    {
        private readonly AutoResetEvent _wake = new(false);
        private readonly LatestWrites<double> _levels = new();
        private readonly LatestWrites<bool> _mutes = new();
        private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);
        private Thread? _thread;
        private int _changes = (int)(Change.Device | Change.Sessions);
        private volatile bool _stopping;

        private nint _enumerator;
        private nint _manager;
        private nint _endpoint;
        private bool _clientRegistered;

        private volatile MixerSnapshot? _last;

        /// <summary>The newest snapshot, for a mixer that subscribes after it was published.</summary>
        public MixerSnapshot? Last => _last;

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "themia-mixer" };
            _thread.Start();
        }

        public void Stop()
        {
            _stopping = true;
            Wake();
        }

        public void Signal(Change change)
        {
            int seen, next;
            do
            {
                seen = _changes;
                next = seen | (int)change;
            }
            while (Interlocked.CompareExchange(ref _changes, next, seen) != seen);

            Wake();
        }

        public void PostLevel(string key, double level)
        {
            _levels.Post(key, level);
            Wake();
        }

        public void PostMute(string key, bool muted)
        {
            _mutes.Post(key, muted);
            Wake();
        }

        private void Wake()
        {
            try
            {
                _wake.Set();
            }
            catch (ObjectDisposedException)
            {
                // ignore: a notification or a write that raced the worker's own shutdown; there is
                // nothing left to wake, and the next mixer starts a new worker.
            }
        }

        private void Run()
        {
            var initialised = CoInitializeEx(0, CoInitMultithreaded);

            // RPC_E_CHANGED_MODE would mean this thread is already single-threaded, where session
            // notifications never arrive. A fresh thread cannot be, but the log would say why if so.
            if (initialised < 0)
            {
                Log.Error(Scope, $"could not enter the multithreaded apartment: 0x{initialised:X8}");
                Publish(MixerSnapshot.Unavailable);
                return;
            }

            try
            {
                while (!_stopping)
                {
                    var changes = (Change)Interlocked.Exchange(ref _changes, 0);

                    try
                    {
                        Cycle(changes);
                    }
                    catch (Exception ex)
                    {
                        // One bad cycle must not end the thread: the next notification retries.
                        Log.Error(Scope, "a mixer refresh failed", ex);
                    }

                    if (_stopping)
                    {
                        break;
                    }

                    _wake.WaitOne();
                }
            }
            finally
            {
                Unbind();
                Release(ref _enumerator, unregisterClient: true);
                CoUninitialize();
                _wake.Dispose();
            }
        }

        private void Cycle(Change changes)
        {
            if (changes.HasFlag(Change.Device) || _manager == 0)
            {
                Unbind();
                Bind();
            }

            var wrote = ApplyWrites();

            if (changes != Change.None || wrote)
            {
                var snapshot = Read();
                _last = snapshot;
                Publish(snapshot);
            }
        }

        // ---- binding to the device ----------------------------------------------------------

        private void Bind()
        {
            if (_enumerator == 0)
            {
                var clsid = Iids.DeviceEnumeratorClass;
                var iid = Iids.DeviceEnumerator;
                nint enumerator;

                var created = CoCreateInstance(&clsid, 0, ClsCtxInprocServer, &iid, &enumerator);
                if (created < 0)
                {
                    Note(created, "could not open the audio device enumerator");
                    return;
                }

                _enumerator = enumerator;
            }

            if (!_clientRegistered)
            {
                // [S-440] Follow the default device: a headset plugged in moves every app's sound.
                var registered = Call(_enumerator, Slots.EnumeratorRegisterClient, Callbacks.DeviceClient);
                _clientRegistered = registered >= 0;
            }

            nint device;
            var found = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(_enumerator, Slots.EnumeratorGetDefault))(
                _enumerator, DataFlowRender, RoleConsole, &device);

            if (found < 0)
            {
                // E_NOTFOUND is a machine with nothing to play sound out of: not a fault.
                Note(found, found == NotFound ? null : "no default output device");
                return;
            }

            try
            {
                var managerIid = Iids.SessionManager2;
                nint manager;
                var activated = ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Slot(device, Slots.DeviceActivate))(
                    device, &managerIid, ClsCtxInprocServer, 0, &manager);

                if (activated < 0)
                {
                    Note(activated, "could not open the session manager");
                    return;
                }

                _manager = manager;
                Call(_manager, Slots.ManagerRegisterSessionNotification, Callbacks.SessionCreated);

                var endpointIid = Iids.EndpointVolume;
                nint endpoint;
                if (((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Slot(device, Slots.DeviceActivate))(
                    device, &endpointIid, ClsCtxInprocServer, 0, &endpoint) >= 0)
                {
                    _endpoint = endpoint;
                    Call(_endpoint, Slots.EndpointRegisterNotify, Callbacks.EndpointVolume);
                }

                Note(0, null);
            }
            finally
            {
                ReleaseRaw(device);
            }
        }

        private void Unbind()
        {
            foreach (var tracked in _tracked.Values)
            {
                Forget(tracked);
            }

            _tracked.Clear();

            if (_manager != 0)
            {
                Call(_manager, Slots.ManagerUnregisterSessionNotification, Callbacks.SessionCreated);
                ReleaseRaw(_manager);
                _manager = 0;
            }

            if (_endpoint != 0)
            {
                Call(_endpoint, Slots.EndpointUnregisterNotify, Callbacks.EndpointVolume);
                ReleaseRaw(_endpoint);
                _endpoint = 0;
            }
        }

        private void Release(ref nint enumerator, bool unregisterClient)
        {
            if (enumerator == 0)
            {
                return;
            }

            if (unregisterClient && _clientRegistered)
            {
                Call(enumerator, Slots.EnumeratorUnregisterClient, Callbacks.DeviceClient);
                _clientRegistered = false;
            }

            ReleaseRaw(enumerator);
            enumerator = 0;
        }

        // ---- reading -------------------------------------------------------------------------

        private MixerSnapshot Read()
        {
            if (_manager == 0)
            {
                return MixerSnapshot.Unavailable;
            }

            nint list;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(_manager, Slots.ManagerGetSessionEnumerator))(_manager, &list) < 0)
            {
                return MixerSnapshot.Unavailable;
            }

            var sessions = new List<AudioSessionInfo>();

            try
            {
                int count;
                if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(list, Slots.ListGetCount))(list, &count) < 0)
                {
                    return MixerSnapshot.Unavailable;
                }

                foreach (var tracked in _tracked.Values)
                {
                    tracked.Seen = false;
                }

                for (var i = 0; i < count; i++)
                {
                    nint control;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(list, Slots.ListGetSession))(list, i, &control) < 0)
                    {
                        // [S-446] One app exiting mid-enumeration must not blank the whole mixer.
                        continue;
                    }

                    try
                    {
                        if (ReadSession(control, i) is { } info)
                        {
                            sessions.Add(info);
                        }
                    }
                    finally
                    {
                        ReleaseRaw(control);
                    }
                }

                // Sessions that expired or disconnected since the last read: stop listening.
                foreach (var key in _tracked.Where(pair => !pair.Value.Seen).Select(pair => pair.Key).ToList())
                {
                    Forget(_tracked[key]);
                    _tracked.Remove(key);
                }
            }
            finally
            {
                ReleaseRaw(list);
            }

            return new MixerSnapshot(true, sessions, ReadMaster());
        }

        private AudioSessionInfo? ReadSession(nint control, int index)
        {
            int state;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(control, Slots.ControlGetState))(control, &state) < 0
                || state == SessionStateExpired)
            {
                // Expired: the app is gone and Windows has not reaped the session yet.
                return null;
            }

            var control2 = Query(control, Iids.SessionControl2);
            if (control2 == 0)
            {
                return null;
            }

            try
            {
                // S_FALSE is a success code that means "no", so only an exact S_OK is a yes.
                var isSystem = ((delegate* unmanaged[Stdcall]<nint, int>)Slot(control2, Slots.Control2IsSystemSounds))(control2) == 0;

                uint pid;
                if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(control2, Slots.Control2GetProcessId))(control2, &pid) < 0)
                {
                    pid = 0;
                }

                var path = isSystem ? null : ProcessNames.ExePath(pid);
                var display = TakeString(control, Slots.ControlGetDisplayName);
                var name = isSystem ? string.Empty : MixerRules.SessionName(path is null ? null : ProcessNames.DisplayName(path), path, display);

                var groupKey = isSystem
                    ? MixerRules.SystemSoundsKey
                    : path?.ToLowerInvariant() ?? $"pid:{pid}";

                var instance = TakeString(control2, Slots.Control2GetInstanceIdentifier);
                var sessionKey = string.IsNullOrEmpty(instance) ? $"{groupKey}#{index}" : instance;

                if (!_tracked.TryGetValue(sessionKey, out var tracked))
                {
                    tracked = Track(control, groupKey);
                    if (tracked is null)
                    {
                        return null;
                    }

                    _tracked[sessionKey] = tracked;
                }

                tracked.Seen = true;

                float level;
                int muted;
                var volume = tracked.Volume;
                var gotLevel = ((delegate* unmanaged[Stdcall]<nint, float*, int>)Slot(volume, Slots.SimpleGetMasterVolume))(volume, &level) >= 0;
                var gotMute = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(volume, Slots.SimpleGetMute))(volume, &muted) >= 0;

                return new AudioSessionInfo(
                    sessionKey,
                    groupKey,
                    name,
                    path,
                    gotLevel ? level : 0,
                    gotMute && muted != 0,
                    state == SessionStateActive,
                    isSystem);
            }
            finally
            {
                ReleaseRaw(control2);
            }
        }

        /// <summary>Hold a session and listen to it. The control and its volume are kept until <see cref="Forget"/>.</summary>
        private static Tracked? Track(nint control, string groupKey)
        {
            var volume = Query(control, Iids.SimpleAudioVolume);
            if (volume == 0)
            {
                return null;
            }

            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(control, Slots.AddRef))(control);
            Call(control, Slots.ControlRegisterNotification, Callbacks.SessionEvents);

            return new Tracked { Control = control, Volume = volume, GroupKey = groupKey };
        }

        private static void Forget(Tracked tracked)
        {
            if (tracked.Control != 0)
            {
                Call(tracked.Control, Slots.ControlUnregisterNotification, Callbacks.SessionEvents);
                ReleaseRaw(tracked.Control);
                tracked.Control = 0;
            }

            if (tracked.Volume != 0)
            {
                ReleaseRaw(tracked.Volume);
                tracked.Volume = 0;
            }
        }

        private (double Level, bool Muted)? ReadMaster()
        {
            if (_endpoint == 0)
            {
                return null;
            }

            float level;
            int muted;
            var gotLevel = ((delegate* unmanaged[Stdcall]<nint, float*, int>)Slot(_endpoint, Slots.EndpointGetScalar))(_endpoint, &level) >= 0;
            var gotMute = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(_endpoint, Slots.EndpointGetMute))(_endpoint, &muted) >= 0;

            return gotLevel && gotMute ? (level, muted != 0) : null;
        }

        // ---- writing -------------------------------------------------------------------------

        /// <summary>
        /// [S-446] Send what is waiting: mutes first, then levels, newest value per key. A key that
        /// matches nothing is not an error — the app can exit between the snapshot that drew its row
        /// and the drag that moves it.
        /// </summary>
        private bool ApplyWrites()
        {
            var mutes = _mutes.Drain();
            var levels = _levels.Drain();

            foreach (var (key, muted) in mutes)
            {
                if (key == MixerRules.MasterKey)
                {
                    if (_endpoint != 0)
                    {
                        ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)Slot(_endpoint, Slots.EndpointSetMute))(_endpoint, muted ? 1 : 0, null);
                    }

                    continue;
                }

                foreach (var volume in Matching(key))
                {
                    ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)Slot(volume, Slots.SimpleSetMute))(volume, muted ? 1 : 0, null);
                }
            }

            foreach (var (key, level) in levels)
            {
                var value = (float)level;

                if (key == MixerRules.MasterKey)
                {
                    if (_endpoint != 0)
                    {
                        ((delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)Slot(_endpoint, Slots.EndpointSetScalar))(_endpoint, value, null);
                    }

                    continue;
                }

                foreach (var volume in Matching(key))
                {
                    ((delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)Slot(volume, Slots.SimpleSetMasterVolume))(volume, value, null);
                }
            }

            return mutes.Count > 0 || levels.Count > 0;
        }

        /// <summary>
        /// [S-446] The sessions a key names: an app's group key and a session's own key are disjoint,
        /// so one write serves the grouped row and the ungrouped one without being told which.
        /// </summary>
        private IEnumerable<nint> Matching(string key)
        {
            foreach (var (sessionKey, tracked) in _tracked)
            {
                if (tracked.Volume != 0 && (sessionKey == key || tracked.GroupKey == key))
                {
                    yield return tracked.Volume;
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // COM plumbing
    // ---------------------------------------------------------------------------------------------

    private const uint ClsCtxInprocServer = 0x1;
    private const uint CoInitMultithreaded = 0x0;
    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;
    private const int SessionStateActive = 1;
    private const int SessionStateExpired = 2;
    private const int NotFound = unchecked((int)0x80070490);
    private const int NoInterface = unchecked((int)0x80004002);

    /// <summary>Vtable slots. Named, because an off-by-one calls a different method with these arguments.</summary>
    private static class Slots
    {
        public const int QueryInterface = 0;
        public const int AddRef = 1;
        public const int Release = 2;

        public const int EnumeratorGetDefault = 4;
        public const int EnumeratorRegisterClient = 6;
        public const int EnumeratorUnregisterClient = 7;

        public const int DeviceActivate = 3;

        public const int ManagerGetSessionEnumerator = 5;
        public const int ManagerRegisterSessionNotification = 6;
        public const int ManagerUnregisterSessionNotification = 7;

        public const int ListGetCount = 3;
        public const int ListGetSession = 4;

        public const int ControlGetState = 3;
        public const int ControlGetDisplayName = 4;
        public const int ControlRegisterNotification = 10;
        public const int ControlUnregisterNotification = 11;

        public const int Control2GetInstanceIdentifier = 13;
        public const int Control2GetProcessId = 14;
        public const int Control2IsSystemSounds = 15;

        public const int SimpleSetMasterVolume = 3;
        public const int SimpleGetMasterVolume = 4;
        public const int SimpleSetMute = 5;
        public const int SimpleGetMute = 6;

        public const int EndpointRegisterNotify = 3;
        public const int EndpointUnregisterNotify = 4;
        public const int EndpointSetScalar = 7;
        public const int EndpointGetScalar = 9;
        public const int EndpointSetMute = 14;
        public const int EndpointGetMute = 15;
    }

    private static class Iids
    {
        public static readonly Guid DeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        public static readonly Guid DeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
        public static readonly Guid SessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        public static readonly Guid SessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
        public static readonly Guid SimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");
        public static readonly Guid EndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

        public static readonly Guid Unknown = new("00000000-0000-0000-C000-000000000046");
        public static readonly Guid NotificationClient = new("7991EEC9-7C89-4D85-8390-6C703CEC60C0");
        public static readonly Guid SessionNotification = new("641DD20B-4D41-49CC-ABA3-174B9477BB08");
        public static readonly Guid SessionEvents = new("24918ACC-64B3-37C1-8CA9-74A66E9957A8");
        public static readonly Guid EndpointVolumeCallback = new("657804FA-D6AD-4496-8A60-352752AF4F89");
    }

    private static nint Slot(nint com, int index) => (*(nint**)com)[index];

    /// <summary>A method that takes one interface pointer and returns an HRESULT — every (un)register call here.</summary>
    private static int Call(nint com, int slot, nint arg)
        => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(com, slot))(com, arg);

    private static void ReleaseRaw(nint com)
    {
        if (com != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, Slots.Release))(com);
        }
    }

    /// <summary>QueryInterface, or 0 when the object does not have it.</summary>
    private static nint Query(nint com, Guid iid)
    {
        nint result;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(com, Slots.QueryInterface))(com, &iid, &result) >= 0
            ? result
            : 0;
    }

    /// <summary>A string a COM getter allocated with the task allocator, read and freed.</summary>
    private static string TakeString(nint com, int slot)
    {
        char* text = null;

        try
        {
            return ((delegate* unmanaged[Stdcall]<nint, char**, int>)Slot(com, slot))(com, &text) >= 0 && text is not null
                ? new string(text)
                : string.Empty;
        }
        finally
        {
            if (text is not null)
            {
                CoTaskMemFree(text);
            }
        }
    }

    private static int _lastFailure;

    /// <summary>Log a failure the first time, and the recovery when it stops; never once per refresh.</summary>
    private static void Note(int result, string? message)
    {
        if (result == _lastFailure)
        {
            return;
        }

        _lastFailure = result;

        if (result >= 0)
        {
            Log.Info(Scope, "the output device is answering again");
        }
        else if (message is not null)
        {
            Log.Warn(Scope, $"{message}: 0x{result:X8}");
        }
    }

    // ---- the callback objects --------------------------------------------------------------

    /// <summary>
    /// The four objects Core Audio calls back. Each is a pointer to its vtable in native memory,
    /// allocated once for the life of the process.
    /// </summary>
    private static class Callbacks
    {
        public static readonly nint DeviceClient = Build(
        [
            (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryDeviceClient,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, char*, uint, int>)&OnDeviceStateChanged,
            (nint)(delegate* unmanaged<nint, char*, int>)&OnDeviceAdded,
            (nint)(delegate* unmanaged<nint, char*, int>)&OnDeviceRemoved,
            (nint)(delegate* unmanaged<nint, int, int, char*, int>)&OnDefaultDeviceChanged,
            (nint)(delegate* unmanaged<nint, char*, PropertyKey, int>)&OnPropertyValueChanged,
        ]);

        public static readonly nint SessionCreated = Build(
        [
            (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QuerySessionNotification,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, nint, int>)&OnSessionCreated,
        ]);

        public static readonly nint SessionEvents = Build(
        [
            (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QuerySessionEvents,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, char*, Guid*, int>)&OnDisplayNameChanged,
            (nint)(delegate* unmanaged<nint, char*, Guid*, int>)&OnIconPathChanged,
            (nint)(delegate* unmanaged<nint, float, int, Guid*, int>)&OnSimpleVolumeChanged,
            (nint)(delegate* unmanaged<nint, uint, float*, uint, Guid*, int>)&OnChannelVolumeChanged,
            (nint)(delegate* unmanaged<nint, Guid*, Guid*, int>)&OnGroupingParamChanged,
            (nint)(delegate* unmanaged<nint, int, int>)&OnStateChanged,
            (nint)(delegate* unmanaged<nint, int, int>)&OnSessionDisconnected,
        ]);

        public static readonly nint EndpointVolume = Build(
        [
            (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryEndpointVolume,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, uint>)&Immortal,
            (nint)(delegate* unmanaged<nint, nint, int>)&OnNotify,
        ]);

        /// <summary>An object is one pointer, to a vtable. Both live in native memory forever.</summary>
        private static nint Build(nint[] methods)
        {
            var table = (nint*)NativeMemory.Alloc((nuint)(methods.Length * sizeof(nint)));
            for (var i = 0; i < methods.Length; i++)
            {
                table[i] = methods[i];
            }

            var instance = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
            *instance = (nint)table;
            return (nint)instance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
        }

        private static int Answer(nint self, Guid* iid, nint* result, Guid own)
        {
            if (result is null)
            {
                return unchecked((int)0x80004003); // E_POINTER
            }

            if (*iid == Iids.Unknown || *iid == own)
            {
                *result = self;
                return 0;
            }

            *result = 0;
            return NoInterface;
        }

        [UnmanagedCallersOnly]
        private static int QueryDeviceClient(nint self, Guid* iid, nint* result) => Answer(self, iid, result, Iids.NotificationClient);

        [UnmanagedCallersOnly]
        private static int QuerySessionNotification(nint self, Guid* iid, nint* result) => Answer(self, iid, result, Iids.SessionNotification);

        [UnmanagedCallersOnly]
        private static int QuerySessionEvents(nint self, Guid* iid, nint* result) => Answer(self, iid, result, Iids.SessionEvents);

        [UnmanagedCallersOnly]
        private static int QueryEndpointVolume(nint self, Guid* iid, nint* result) => Answer(self, iid, result, Iids.EndpointVolumeCallback);

        /// <summary>The objects are never freed, so their count never matters.</summary>
        [UnmanagedCallersOnly]
        private static uint Immortal(nint self) => 1;

        /// <summary>
        /// Every notification ends here: set a flag, wake the worker, return. Nothing that can throw
        /// runs outside the try — an exception escaping into Core Audio's thread is a crash.
        /// </summary>
        private static int Wake(Change change)
        {
            try
            {
                Notify(change);
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "a Core Audio notification could not be passed on", ex);
            }

            return 0;
        }

        [UnmanagedCallersOnly]
        private static int OnDeviceStateChanged(nint self, char* id, uint state) => 0;

        [UnmanagedCallersOnly]
        private static int OnDeviceAdded(nint self, char* id) => 0;

        [UnmanagedCallersOnly]
        private static int OnDeviceRemoved(nint self, char* id) => 0;

        [UnmanagedCallersOnly]
        private static int OnDefaultDeviceChanged(nint self, int flow, int role, char* id)
            => flow == DataFlowRender && role == RoleConsole ? Wake(Change.Device) : 0;

        [UnmanagedCallersOnly]
        private static int OnPropertyValueChanged(nint self, char* id, PropertyKey key) => 0;

        [UnmanagedCallersOnly]
        private static int OnSessionCreated(nint self, nint session) => Wake(Change.Sessions);

        [UnmanagedCallersOnly]
        private static int OnDisplayNameChanged(nint self, char* name, Guid* context) => Wake(Change.Sessions);

        [UnmanagedCallersOnly]
        private static int OnIconPathChanged(nint self, char* path, Guid* context) => 0;

        [UnmanagedCallersOnly]
        private static int OnSimpleVolumeChanged(nint self, float level, int muted, Guid* context) => Wake(Change.Sessions);

        [UnmanagedCallersOnly]
        private static int OnChannelVolumeChanged(nint self, uint count, float* levels, uint changed, Guid* context) => 0;

        [UnmanagedCallersOnly]
        private static int OnGroupingParamChanged(nint self, Guid* grouping, Guid* context) => 0;

        [UnmanagedCallersOnly]
        private static int OnStateChanged(nint self, int state) => Wake(Change.Sessions);

        [UnmanagedCallersOnly]
        private static int OnSessionDisconnected(nint self, int reason) => Wake(Change.Sessions);

        [UnmanagedCallersOnly]
        private static int OnNotify(nint self, nint data) => Wake(Change.Sessions);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(void* memory);
}
