using Themia.Core.Diagnostics;
using Themia.Core.Widgets;
using System.Runtime.Versioning;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Themia.App.Services;

/// <summary>
/// [S-1611] The packaged build's location permission, which Windows requires before the Wi-Fi API
/// hands over the network name.
/// <para>
/// Lazy on purpose: nothing here runs at startup. The first question comes from a network tile that
/// is drawing a Wi-Fi line, and Windows' consent prompt is shown only when the user clicks that
/// line's hint, once per run (<see cref="WifiNameRules.Decide"/>). Windows' own <c>AccessChanged</c> event keeps the answer
/// current when the user flips the switch in Settings, and bumps <see cref="Generation"/> so the
/// Wi-Fi API is asked again instead of waiting out its cache — no polling.
/// </para>
/// <para>
/// The unpackaged build never touches this API: there Windows decides on its own (and may show its
/// own prompt for the Wi-Fi call), exactly as before ([S-1612]).
/// </para>
/// </summary>
internal static class LocationConsent
{
    private const string Scope = "location";
    private const string CapabilityName = "location";

    private static readonly Lock Gate = new();
    private static AppCapability? _capability;
    private static bool _created;
    private static int _permission = -1;
    private static int _generation;
    private static int _asked;

    /// <summary>Bumped whenever the permission changes, so a cached "refused" Wi-Fi answer is not reused.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>Whether this run already showed (or tried to show) the consent prompt.</summary>
    public static bool Asked => Volatile.Read(ref _asked) != 0;

    /// <summary>The permission now. <see cref="LocationPermission.NotRequired"/> when unpackaged or on a Windows without per-app consent.</summary>
    public static LocationPermission Permission
    {
        get
        {
            if (!PackageIdentity.IsPackaged)
            {
                return LocationPermission.NotRequired;
            }

            var known = Volatile.Read(ref _permission);
            if (known >= 0)
            {
                return (LocationPermission)known;
            }

            var permission = HasConsent() && Capability() is { } capability ? Check(capability) : LocationPermission.NotRequired;
            Volatile.Write(ref _permission, (int)permission);
            return permission;
        }
    }

    /// <summary>[S-1611] What a Wi-Fi line about to be drawn shows about its name. Never prompts.</summary>
    public static WifiNameDecision Decide(bool hasName, bool nameRefused)
    {
        var packaged = PackageIdentity.IsPackaged;
        var permission = packaged && !hasName ? Permission : LocationPermission.NotRequired;
        return WifiNameRules.Decide(packaged, permission, lineShown: true, Asked, hasName, nameRefused);
    }

    /// <summary>[S-1613] The hint was clicked: ask Windows, or open Settings › Privacy &amp; security › Location.</summary>
    public static void Follow(WifiHintAction action)
    {
        switch (action)
        {
            case WifiHintAction.AskWindows:
                Request();
                break;

            case WifiHintAction.OpenSettings:
                if (ShellLauncher.OpenWindowsSettings(WifiNameRules.PrivacyPage) != LaunchOutcome.Opened)
                {
                    Log.Warn(Scope, "could not open the location privacy settings");
                }

                break;
        }
    }

    /// <summary>Show Windows' consent prompt, once per run, from a click on the UI thread.</summary>
    private static void Request()
    {
        if (Interlocked.Exchange(ref _asked, 1) != 0 || !HasConsent() || Capability() is not { } capability)
        {
            return;
        }

        _ = RequestAsync(capability);

        [SupportedOSPlatform("windows10.0.18362.0")]
        static async Task RequestAsync(AppCapability capability)
        {
            try
            {
                Log.Info(Scope, "asking for location access to show the Wi-Fi network name");
                var status = await capability.RequestAccessAsync();
                Changed(Map(status));
            }
            catch (Exception ex)
            {
                // The prompt could not be shown: the line keeps its hint, and Settings is the way in.
                Log.Warn(Scope, "could not ask for location access", ex);
                Changed(null);
            }
        }
    }

    /// <summary>Per-app capability access (and so location consent for desktop apps) arrived in Windows 10 1903.</summary>
    [SupportedOSPlatformGuard("windows10.0.18362.0")]
    private static bool HasConsent() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362);

    /// <summary>The capability object, created once; null when it could not be made.</summary>
    [SupportedOSPlatform("windows10.0.18362.0")]
    private static AppCapability? Capability()
    {
        lock (Gate)
        {
            if (_created)
            {
                return _capability;
            }

            _created = true;

            try
            {
                _capability = AppCapability.Create(CapabilityName);
                _capability.AccessChanged += OnAccessChanged;
            }
            catch (Exception ex)
            {
                Log.Warn(Scope, "could not read the location permission", ex);
                _capability = null;
            }

            return _capability;
        }
    }

    [SupportedOSPlatform("windows10.0.18362.0")]
    private static LocationPermission Check(AppCapability capability)
    {
        try
        {
            return Map(capability.CheckAccess());
        }
        catch (Exception ex)
        {
            // Unknown is treated as refused: a hint the user can follow, never a prompt loop.
            Log.Warn(Scope, "could not check the location permission", ex);
            return LocationPermission.Denied;
        }
    }

    [SupportedOSPlatform("windows10.0.18362.0")]
    private static LocationPermission Map(AppCapabilityAccessStatus status) => status switch
    {
        AppCapabilityAccessStatus.Allowed => LocationPermission.Allowed,
        AppCapabilityAccessStatus.UserPromptRequired => LocationPermission.PromptRequired,
        _ => LocationPermission.Denied,
    };

    /// <summary>Windows says the answer moved (Settings, the prompt, policy): read it again on next use.</summary>
    [SupportedOSPlatform("windows10.0.18362.0")]
    private static void OnAccessChanged(AppCapability sender, AppCapabilityAccessChangedEventArgs args)
    {
        try
        {
            Changed(null);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not take a location permission change", ex);
        }
    }

    /// <param name="permission">The new answer, or null to read it again on next use.</param>
    private static void Changed(LocationPermission? permission)
    {
        Volatile.Write(ref _permission, permission is { } known ? (int)known : -1);
        Interlocked.Increment(ref _generation);
    }
}
