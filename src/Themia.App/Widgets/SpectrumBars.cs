using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1470] The spectrum's bars: <see cref="Spectrum.Bands"/> rounded rectangles that stretch to the
/// host's height and are scaled vertically from the bottom. A frame is 24 transform writes and no
/// layout pass, which is what lets it run at the capture rate on the UI thread for nothing.
/// </summary>
internal sealed partial class SpectrumBars : Grid
{
    /// <summary>The original's floor, so a quiet band is a stub rather than nothing.</summary>
    private const double Floor = 0.03;

    private readonly ScaleTransform[] _scales = new ScaleTransform[Spectrum.Bands];

    public SpectrumBars()
    {
        ColumnSpacing = 2;
        var fill = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        for (var i = 0; i < Spectrum.Bands; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var scale = new ScaleTransform { ScaleY = Floor };
            _scales[i] = scale;

            var bar = new Rectangle
            {
                Fill = fill,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Opacity = 0.85,
                RenderTransform = scale,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
            };

            SetColumn(bar, i);
            Children.Add(bar);
        }
    }

    /// <summary>Draw one frame; missing or extra levels are ignored.</summary>
    public void SetLevels(ReadOnlySpan<float> levels)
    {
        for (var i = 0; i < _scales.Length; i++)
        {
            var level = i < levels.Length ? levels[i] : 0;
            _scales[i].ScaleY = Math.Max(Floor, Math.Clamp(level, 0, 1));
        }
    }

    /// <summary>All bars down to the floor.</summary>
    public void Clear() => SetLevels([]);
}
