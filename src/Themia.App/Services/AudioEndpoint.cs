using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>One reading of the default output endpoint.</summary>
/// <param name="Available">False when there is no output device, or Core Audio would not answer.</param>
/// <param name="Scalar">Master level 0..1. Meaningless unless <paramref name="Available"/>.</param>
/// <param name="Muted">Whether the endpoint is muted.</param>
public readonly record struct VolumeReading(bool Available, double Scalar, bool Muted)
{
    /// <summary>The state before the first read, and after one that failed.</summary>
    public static VolumeReading Unavailable { get; } = new(false, 0, false);
}

/// <summary>
/// The default output endpoint's master level and mute, straight from Core Audio.
/// <para>
/// Hand-rolled vtable calls rather than a COM interop assembly, for the same reason the rest of the
/// app uses <see cref="LibraryImportAttribute"/>: under Native AOT there is no runtime IL emission,
/// so a built-in COM interop type would need a marshalling stub that cannot be generated. Three
/// interfaces, eight methods between them, each one a single indirect call through the object's
/// vtable — which is all a COM call has ever been.
/// </para>
/// <para>
/// <b>The endpoint is acquired per call and released on every path.</b> Caching it would be faster
/// and wrong: the default endpoint changes the moment a headset is plugged in, and a cached
/// interface then reports the level of a device nobody is listening to. A read is four local calls,
/// so the cost of asking again is smaller than the cost of being stale.
/// </para>
/// </summary>
internal static unsafe partial class AudioEndpoint
{
    private const string Scope = "volume";

    /// <summary>CLSID_MMDeviceEnumerator.</summary>
    private static readonly Guid DeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>IID_IMMDeviceEnumerator.</summary>
    private static readonly Guid DeviceEnumeratorInterface = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    /// <summary>IID_IAudioEndpointVolume.</summary>
    private static readonly Guid EndpointVolumeInterface = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private const uint ClsCtxInprocServer = 0x1;

    /// <summary>eRender: the direction sound goes out.</summary>
    private const int DataFlowRender = 0;

    /// <summary>eConsole: the endpoint the volume keys and the flyout act on.</summary>
    private const int RoleConsole = 0;

    /// <summary>E_NOTFOUND. A machine with no output device at all, which is not a fault.</summary>
    private const int NotFound = unchecked((int)0x80070490);

    /// <summary>CO_E_NOTINITIALIZED: called from a thread that never entered an apartment.</summary>
    private const int NotInitialized = unchecked((int)0x800401F0);

    // ---- vtable slots ----------------------------------------------------------------------
    //
    // Named rather than written inline, because an off-by-one here does not fail to compile and
    // does not throw: it calls a different method of the same interface with the arguments of this
    // one, which is how an interop bug becomes a crash somewhere else entirely.

    private const int IUnknownRelease = 2;
    private const int EnumeratorGetDefaultEndpoint = 4;
    private const int DeviceActivate = 3;
    private const int VolumeSetMasterScalar = 7;
    private const int VolumeGetMasterScalar = 9;
    private const int VolumeSetMute = 14;
    private const int VolumeGetMute = 15;

    /// <summary>Whether the last failure has already been logged, and with which code.</summary>
    private static int _lastFailure;

    /// <summary>
    /// The level and mute of the endpoint the volume keys act on.
    /// <para>
    /// Never throws: it runs on the shared ticker, and a tick that throws takes the widget's clock
    /// down with it.
    /// </para>
    /// </summary>
    public static VolumeReading Read()
    {
        using var endpoint = Acquire();

        if (!endpoint.Ok)
        {
            return VolumeReading.Unavailable;
        }

        float level;
        int muted;

        var levelResult = ((delegate* unmanaged[Stdcall]<nint, float*, int>)
            Slot(endpoint.Pointer, VolumeGetMasterScalar))(endpoint.Pointer, &level);

        var muteResult = ((delegate* unmanaged[Stdcall]<nint, int*, int>)
            Slot(endpoint.Pointer, VolumeGetMute))(endpoint.Pointer, &muted);

        if (levelResult < 0 || muteResult < 0)
        {
            Note(levelResult < 0 ? levelResult : muteResult, "could not read the output level");
            return VolumeReading.Unavailable;
        }

        Note(0, null);
        return new VolumeReading(true, level, muted != 0);
    }

    /// <summary>
    /// Set the master level. Returns false when the endpoint would not take it, so the caller can
    /// leave the control where the machine actually is rather than where the drag left it.
    /// </summary>
    public static bool SetScalar(double scalar)
    {
        using var endpoint = Acquire();

        if (!endpoint.Ok)
        {
            return false;
        }

        var level = (float)Math.Clamp(scalar, 0, 1);

        // A null event context, which is what "this change came from outside the app" looks like to
        // every other volume client. Passing a context of our own would be how we tell our own
        // callback apart from somebody else's, and we have no callback.
        var result = ((delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)
            Slot(endpoint.Pointer, VolumeSetMasterScalar))(endpoint.Pointer, level, null);

        if (result < 0)
        {
            Note(result, "could not set the output level");
            return false;
        }

        return true;
    }

    /// <summary>Mute or unmute the endpoint.</summary>
    public static bool SetMute(bool muted)
    {
        using var endpoint = Acquire();

        if (!endpoint.Ok)
        {
            return false;
        }

        var result = ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)
            Slot(endpoint.Pointer, VolumeSetMute))(endpoint.Pointer, muted ? 1 : 0, null);

        if (result < 0)
        {
            Note(result, "could not set mute");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get at the default render endpoint's volume interface, or an empty handle.
    /// <para>
    /// The enumerator and the device are released before this returns whether or not it succeeded —
    /// they are scaffolding, and only the volume interface outlives the call, owned by the
    /// <see cref="ComHandle"/> the caller disposes.
    /// </para>
    /// </summary>
    private static ComHandle Acquire()
    {
        nint enumerator = 0;
        nint device = 0;
        nint volume = 0;

        try
        {
            var clsid = DeviceEnumeratorClass;
            var iid = DeviceEnumeratorInterface;

            var created = CoCreateInstance(&clsid, 0, ClsCtxInprocServer, &iid, &enumerator);

            if (created < 0)
            {
                Note(created, "could not open the audio device enumerator");
                return default;
            }

            var gotDevice = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)
                Slot(enumerator, EnumeratorGetDefaultEndpoint))(enumerator, DataFlowRender, RoleConsole, &device);

            if (gotDevice < 0)
            {
                // E_NOTFOUND is a machine with nothing to play sound out of. Not a fault, and not
                // worth a log line every two seconds on a headless box.
                Note(gotDevice, gotDevice == NotFound ? null : "no default output device");
                return default;
            }

            var volumeIid = EndpointVolumeInterface;

            var activated = ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)
                Slot(device, DeviceActivate))(device, &volumeIid, ClsCtxInprocServer, 0, &volume);

            if (activated < 0)
            {
                Note(activated, "could not open the endpoint volume");
                return default;
            }

            var handle = new ComHandle(volume);

            // Ownership has moved into the handle; the finally below must not release it as well.
            volume = 0;
            return handle;
        }
        finally
        {
            ReleaseIfSet(ref volume);
            ReleaseIfSet(ref device);
            ReleaseIfSet(ref enumerator);
        }
    }

    private static void ReleaseIfSet(ref nint com)
    {
        if (com == 0)
        {
            return;
        }

        ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, IUnknownRelease))(com);
        com = 0;
    }

    /// <summary>The function pointer in a COM object's vtable at a slot.</summary>
    private static nint Slot(nint com, int index) => (*(nint**)com)[index];

    /// <summary>
    /// Log a failure the first time it happens, and the recovery when it stops.
    /// <para>
    /// A missing output device answers identically on every tick for as long as it stays missing.
    /// Logging each one would bury everything else in the file within the hour and say nothing the
    /// first line did not.
    /// </para>
    /// </summary>
    private static void Note(int result, string? message)
    {
        if (result == _lastFailure)
        {
            return;
        }

        var recovered = result >= 0;
        _lastFailure = result;

        if (recovered)
        {
            Log.Info(Scope, "the output endpoint is answering again");
            return;
        }

        if (message is null)
        {
            return;
        }

        var detail = result == NotInitialized
            ? $"{message}: the calling thread is not in a COM apartment"
            : $"{message}: 0x{result:X8}";

        Log.Warn(Scope, detail);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        Guid* rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        Guid* riid,
        nint* ppv);

    /// <summary>
    /// A COM interface pointer that releases itself. The whole of the RAII here: every path out of
    /// <see cref="Acquire"/> either releases what it took or hands it to one of these.
    /// </summary>
    private readonly ref struct ComHandle(nint pointer)
    {
        public nint Pointer { get; } = pointer;

        public bool Ok => Pointer != 0;

        public void Dispose()
        {
            if (Pointer == 0)
            {
                return;
            }

            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(Pointer, IUnknownRelease))(Pointer);
        }
    }
}
