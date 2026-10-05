using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>The legend: one row per app, reused by position as the data changes.</summary>
internal sealed partial class ScreenTimeView
{
    /// <summary>
    /// [S-452] One legend row: the app's icon, its name with its colour dot right after it, and the
    /// figures on the right — average, share, then the time, which is always the last column.
    /// </summary>
    private sealed class LegendRow
    {
        private const double IconSize = Fluent.IconSize;
        private const double Dot = 6;

        private static readonly Brush TimeBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        private readonly ScreenTimeView _owner;
        private readonly Grid _icon = new() { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Center };
        private readonly Image _image = new() { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
        private readonly Border _chip = new() { Width = IconSize, Height = IconSize, CornerRadius = new CornerRadius(Fluent.ControlRadius) };
        private readonly TextBlock _initial = new()
        {
            FontSize = FluentType.Caption.FontSize,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly Microsoft.UI.Xaml.Shapes.Ellipse _dot = new() { Width = Dot, Height = Dot, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _average = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _percent = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _time = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly IDisposable _hover;

        private ScreenTimeSegment? _segment;
        private string? _iconPath;

        public LegendRow(ScreenTimeView owner)
        {
            _owner = owner;

            _initial.Foreground = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White);
            _chip.Child = _initial;
            _icon.Children.Add(_chip);
            _icon.Children.Add(_image);

            _average.VerticalAlignment = VerticalAlignment.Center;
            _percent.VerticalAlignment = VerticalAlignment.Center;
            _time.VerticalAlignment = VerticalAlignment.Center;
            _time.TextAlignment = TextAlignment.Right;
            Typography.SetNumeralAlignment(_time, FontNumeralAlignment.Tabular);
            Typography.SetNumeralAlignment(_average, FontNumeralAlignment.Tabular);
            Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);

            // [S-452] The name takes only what its text needs and ellipsises when the row runs out,
            // so the dot always sits right after it; the spare width goes after the dot and the
            // figures stay right-aligned.
            var name = new HugRow(_name, _dot, Fluent.Space2) { HorizontalAlignment = HorizontalAlignment.Left };
            _average.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            _percent.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            _time.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            name.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            SetColumn(_icon, 0);
            SetColumn(name, 1);
            SetColumn(_average, 2);
            SetColumn(_percent, 3);
            SetColumn(_time, 4);

            grid.Children.Add(_icon);
            grid.Children.Add(name);
            grid.Children.Add(_average);
            grid.Children.Add(_percent);
            grid.Children.Add(_time);

            Host = Fluent.Row(grid, compact: true);
            Host.PointerEntered += OnEntered;
            Host.PointerExited += OnLeft;
            Host.PointerCanceled += OnLeft;
            Host.PointerCaptureLost += OnLeft;
            Host.RightTapped += OnRightTapped;
            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        public string? Key => _segment?.Key;

        public void Show(ScreenTimeSegment segment, Brush colour, bool dot, string? average, string? percent, ScreenTimeGoal? goal)
        {
            _segment = segment;

            _name.Text = segment.Name;
            _dot.Fill = colour;
            _dot.Visibility = dot ? Visibility.Visible : Visibility.Collapsed;

            _average.Text = average ?? string.Empty;
            _average.Visibility = average is null ? Visibility.Collapsed : Visibility.Visible;
            _percent.Text = percent ?? string.Empty;
            _percent.Visibility = percent is null ? Visibility.Collapsed : Visibility.Visible;

            _time.Text = ScreenTimeRules.FormatDuration(segment.Seconds);

            // [S-457] Over its own daily limit, the app's time says so in the critical colour.
            _time.Foreground = goal is { Over: true } ? CriticalBrush : TimeBrush;

            var tip = $"{segment.Name} — {_time.Text}";
            if (average is not null)
            {
                tip += $" · {average}";
            }

            if (goal is { } g)
            {
                tip += $" · {(g.Over ? Str.WidgetsC_ScreenTime_LimitOverTip : Str.WidgetsC_ScreenTime_LimitTip).Format(("limit", ScreenTimeRules.FormatLimit(g.Limit)))}";
            }

            ToolTipService.SetToolTip(Host, tip);

            ShowIcon(segment, colour);
        }

        public void Dim(bool dimmed) => Host.Opacity = dimmed ? 0.45 : 1;

        /// <summary>The app's icon; while it loads, or when it has none, a chip of its colour with its initial.</summary>
        private void ShowIcon(ScreenTimeSegment segment, Brush colour)
        {
            _chip.Background = colour;
            _initial.Text = segment.IsOther ? string.Empty : ScreenTimeRules.Initial(segment.Name);

            if (segment.Path is not { } path)
            {
                _iconPath = null;
                _image.Source = null;
                _chip.Visibility = Visibility.Visible;
                return;
            }

            if (string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase) && _image.Source is not null)
            {
                return;
            }

            _iconPath = path;
            _image.Source = null;
            _chip.Visibility = Visibility.Visible;
            _ = LoadIconAsync(path);
        }

        private async Task LoadIconAsync(string path)
        {
            var image = await AppIcons.GetAsync(path).ConfigureAwait(true);

            if (image is null || !string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _image.Source = image;
            _chip.Visibility = Visibility.Collapsed;
        }

        private void OnEntered(object sender, PointerRoutedEventArgs args)
        {
            if (_segment is { } segment)
            {
                _owner.SetFocus(segment.Key);
            }
        }

        private void OnLeft(object sender, PointerRoutedEventArgs args) => _owner.SetFocus(null);

        private void OnRightTapped(object sender, RightTappedRoutedEventArgs args)
        {
            if (_segment is not { IsOther: false } segment)
            {
                return;
            }

            ItemMenu.Show(Host, args, [new ItemMenuEntry(Glyphs.Hide, Str.Screentime_HideApp.Text(), () => _owner.Hide(segment.Key))]);
        }

        public void Detach()
        {
            _hover.Dispose();
            Host.PointerEntered -= OnEntered;
            Host.PointerExited -= OnLeft;
            Host.PointerCanceled -= OnLeft;
            Host.PointerCaptureLost -= OnLeft;
            Host.RightTapped -= OnRightTapped;
        }
    }
}
