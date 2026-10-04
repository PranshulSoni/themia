using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// One audio widget's connection to <see cref="CoreAudio"/>: what it wants, the snapshots and meters
/// that come back, and the writes it sends — the part the mixer and the volume tile share.
/// <para>
/// [S-1233] A preview card (the widget gallery) never writes: its sliders and buttons are inert, so
/// rendering the gallery can never move the machine's real volume. With a gallery fixture installed
/// a preview does not touch Core Audio at all and shows the fixture's devices and apps instead.
/// </para>
/// </summary>
internal sealed class AudioFeed(Action<AudioSnapshot> onSnapshot, Action<AudioPeaks> onPeaks) : IDisposable
{
    private IAudioSubscription? _subscription;
    private AudioWant? _want;

    /// <summary>Set from the surface before the first <see cref="Want"/>: a preview never writes.</summary>
    public bool Preview { get; set; }

    /// <summary>Start, or change what is asked for. Cheap when nothing changed.</summary>
    public void Want(AudioWant want)
    {
        if (_want == want)
        {
            return;
        }

        _want = want;

#if DEBUG
        if (Preview && Shell.Gallery.AudioFixture.Snapshot(want) is { } fixture)
        {
            onSnapshot(fixture);
            onPeaks(want.Peaks ? Shell.Gallery.AudioFixture.Peaks : AudioPeaks.Silent);
            return;
        }
#endif

        if (_subscription is null)
        {
            _subscription = CoreAudio.Subscribe(want, onSnapshot, onPeaks);
        }
        else
        {
            _subscription.Want(want);
        }
    }

    /// <summary>[S-446] Queue a level for a row of this widget's device; nothing in a preview.</summary>
    public void SetLevel(string key, double level)
    {
        if (!Preview && _want is { } want)
        {
            CoreAudio.SetLevel(want.DeviceId, key, level);
        }
    }

    /// <summary>[S-447] Queue a mute for a row of this widget's device; nothing in a preview.</summary>
    public void SetMute(string key, bool muted)
    {
        if (!Preview && _want is { } want)
        {
            CoreAudio.SetMute(want.DeviceId, key, muted);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _want = null;
    }
}

/// <summary>
/// [S-1222] The output-device menu both audio widgets open from their device line: "Default output"
/// (which follows Windows as it changes) and every connected device, the one the widget is bound to
/// checked. Picking one binds the widget to it; Windows' own default output is left alone.
/// </summary>
internal static class AudioDeviceMenu
{
    /// <param name="chosen">The widget's configured device id, or null when it follows the default.</param>
    /// <param name="pick">Called with the picked device, or null for "follow the default".</param>
    public static MenuFlyout Build(AudioSnapshot snapshot, string? chosen, Action<AudioDevice?> pick)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pick);

        var menu = new MenuFlyout();
        var defaultName = snapshot.DefaultId is { } id ? AudioRules.DeviceName(id, snapshot.Devices, null) : "none";

        var follow = new RadioMenuFlyoutItem
        {
            Text = $"Default output ({defaultName})",
            GroupName = "device",
            IsChecked = string.IsNullOrEmpty(chosen),
        };
        follow.Click += (_, _) => pick(null);
        menu.Items.Add(follow);
        menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var device in snapshot.Devices)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = AudioRules.DeviceName(device.Id, snapshot.Devices, null),
                GroupName = "device",
                IsChecked = string.Equals(device.Id, chosen, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => pick(device);
            menu.Items.Add(item);
        }

        return menu;
    }
}
