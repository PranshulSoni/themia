using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

internal sealed partial class MixerView
{
    /// <summary>
    /// [S-1229] The extra-large mixer's side panel: the device's own level (which leaves the list
    /// there), the default microphone ([S-1224]) and every output device as a picker ([S-1222]) — the
    /// width a list of apps cannot use, spent on the things around them.
    /// </summary>
    private sealed class SidePanel
    {
        private readonly MixerView _owner;
        private readonly StackPanel _stack = new() { Spacing = Fluent.Space1 };
        private readonly TextBlock _deviceName = Fluent.Text(FluentType.BodyStrong);
        private readonly LevelRow _master;
        private readonly TextBlock _micCaption = Caption("Input");
        private readonly TextBlock _micName = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly LevelRow _mic;
        private readonly TextBlock _devicesCaption = Caption("Output devices");
        private readonly StackPanel _devices = new();
        private readonly List<(Button Button, RoutedEventHandler Click)> _deviceButtons = [];
        private AudioSnapshot? _snapshot;

        public SidePanel(MixerView owner)
        {
            _owner = owner;

            _master = new LevelRow(LevelRow.OutputGlyph, level => owner.OnLevel(Master(), level), () => owner.OnMute(Master()));
            _mic = new LevelRow(LevelRow.MicrophoneGlyph, level => owner.OnLevel(Microphone(), level), () => owner.OnMute(Microphone()));

            _deviceName.TextTrimming = TextTrimming.CharacterEllipsis;
            _micName.TextTrimming = TextTrimming.CharacterEllipsis;
            _micName.Margin = new Thickness(0, Fluent.Space1, 0, 0);

            _stack.Children.Add(Caption("Output"));
            _stack.Children.Add(_deviceName);
            _stack.Children.Add(_master.Root);
            _stack.Children.Add(_micCaption);
            _stack.Children.Add(_micName);
            _stack.Children.Add(_mic.Root);
            _stack.Children.Add(_devicesCaption);
            _stack.Children.Add(_devices);

            _micCaption.Margin = new Thickness(0, Fluent.Space3, 0, 0);
            _devicesCaption.Margin = new Thickness(0, Fluent.Space3, 0, 0);

            Root = new Border
            {
                Child = _stack,
                Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space2),
                BorderThickness = new Thickness(Fluent.Hairline, 0, 0, 0),
                BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            };
        }

        public Border Root { get; }

        public void Show(AudioSnapshot snapshot, MixerConfig config, IReadOnlyDictionary<string, double> overrides, double height)
        {
            _snapshot = snapshot;

            var master = Master();
            _deviceName.Text = snapshot.Output?.Name is { Length: > 0 } name ? name : "Output device";
            ToolTipService.SetToolTip(_deviceName, _deviceName.Text);
            _master.Show(overrides.GetValueOrDefault(master.Key, master.Level), master.Muted, master.Muted ? "Unmute" : "Mute");

            var mic = snapshot.Microphone is not null && config.ShowMicrophone;
            _micCaption.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
            _micName.Visibility = _micCaption.Visibility;
            _mic.Root.Visibility = _micCaption.Visibility;

            if (snapshot.Microphone is { } input && mic)
            {
                _micName.Text = input.Name.Length > 0 ? input.Name : "Microphone";
                _mic.Show(overrides.GetValueOrDefault(MixerRules.MicrophoneKey, input.Level), input.Muted, input.Muted ? "Unmute the microphone" : "Mute the microphone");
            }

            ShowDevices(snapshot, config.DeviceId, height);
            ShowPeaks(_owner._peaks);
        }

        /// <summary>
        /// [S-1222] Every output device as a row; the bound one checked. As many as fit under the
        /// level controls, which a desktop with more than six outputs will rarely test.
        /// </summary>
        private void ShowDevices(AudioSnapshot snapshot, string? chosen, double height)
        {
            var used = Fluent.Space1 + (Fluent.Space1 * 7) + (FluentType.Caption.LineHeight * 3) + FluentType.BodyStrong.LineHeight
                + FluentType.Body.LineHeight + (Fluent.RowHeight * 2) + (Fluent.Space3 * 2) + Fluent.Space2;
            var fit = Disclosure.WholeRows(height - used, Fluent.RowHeightCompact);
            var devices = snapshot.Devices.Take(Math.Max(0, fit)).ToList();

            _devicesCaption.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            while (_deviceButtons.Count < devices.Count)
            {
                var button = DeviceButton();
                var index = _deviceButtons.Count;
                RoutedEventHandler click = (_, _) => OnDevicePicked(index);
                button.Click += click;
                _deviceButtons.Add((button, click));
                _devices.Children.Add(button);
            }

            for (var i = 0; i < _deviceButtons.Count; i++)
            {
                var button = _deviceButtons[i].Button;

                if (i >= devices.Count)
                {
                    button.Visibility = Visibility.Collapsed;
                    continue;
                }

                var device = devices[i];
                var bound = string.IsNullOrEmpty(chosen)
                    ? string.Equals(device.Id, snapshot.DefaultId, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(device.Id, chosen, StringComparison.OrdinalIgnoreCase);

                var line = (Grid)button.Content;
                ((FontIcon)line.Children[0]).Visibility = bound ? Visibility.Visible : Visibility.Collapsed;
                var label = (TextBlock)line.Children[1];
                label.Text = AudioRules.DeviceName(device.Id, snapshot.Devices, null);
                label.Foreground = FluentLayout.Themed(bound ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush", Colors.White);
                ToolTipService.SetToolTip(button, string.Equals(device.Id, snapshot.DefaultId, StringComparison.OrdinalIgnoreCase)
                    ? $"{label.Text} (Windows default)"
                    : label.Text);
                button.Visibility = Visibility.Visible;
            }
        }

        private void OnDevicePicked(int index)
        {
            if (_snapshot is not { } snapshot || index >= snapshot.Devices.Count)
            {
                return;
            }

            var device = snapshot.Devices[index];

            // The Windows default picked here means "follow the default", which keeps following it.
            _owner.PickDevice(string.Equals(device.Id, snapshot.DefaultId, StringComparison.OrdinalIgnoreCase) ? null : device);
        }

        public void ShowPeaks(AudioPeaks peaks)
        {
            var on = _owner._peaksOn;
            _master.ShowPeak(peaks.Output, on);
            _mic.ShowPeak(peaks.Microphone, on);
        }

        private MixerRow Master()
        {
            var output = _snapshot?.Output;
            return new MixerRow(MixerRules.MasterKey, string.Empty, null, output?.Level ?? 0, output?.Muted ?? false, true, false, 0);
        }

        private MixerRow Microphone()
        {
            var input = _snapshot?.Microphone;
            return new MixerRow(MixerRules.MicrophoneKey, input?.Name ?? string.Empty, null, input?.Level ?? 0, input?.Muted ?? false, true, false, 0);
        }

        private static Button DeviceButton()
        {
            var line = new Grid { ColumnSpacing = Fluent.Space2 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Fluent.IconSize) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var check = Fluent.Icon(Glyphs.Done, "AccentTextFillColorPrimaryBrush", Fluent.IconSize - 4);
            var label = Fluent.Text(FluentType.Body);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(label, 1);
            line.Children.Add(check);
            line.Children.Add(label);

            return new Button
            {
                Content = line,
                Height = Fluent.RowHeightCompact,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, 0),
                Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
            };
        }

        private static TextBlock Caption(string text)
        {
            var caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            caption.Text = text;
            return caption;
        }

        public void Detach()
        {
            _master.Detach();
            _mic.Detach();

            foreach (var (button, click) in _deviceButtons)
            {
                button.Click -= click;
            }

            _deviceButtons.Clear();
        }
    }
}
