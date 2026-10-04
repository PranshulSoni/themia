using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Themia.App.Shell;

/// <summary>
/// [S-862] Holds the card's header upright, or turned a quarter to run up a side edge.
/// <para>
/// Turned, the header is laid out along the edge's length (its width is the card's height) and only
/// then rotated, so it reads bottom to top the way a side tab does, icon at the bottom — one header,
/// not a second sideways one. Unlike <c>RotatedBox</c> it sizes to its content across the edge, so it
/// can sit in an <c>Auto</c> column.
/// </para>
/// </summary>
internal sealed partial class SidewaysPanel : Panel
{
    private bool _sideways;

    public bool Sideways
    {
        get => _sideways;
        set
        {
            if (_sideways == value)
            {
                return;
            }

            _sideways = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var inner = _sideways ? new Size(availableSize.Height, availableSize.Width) : availableSize;

        foreach (var child in Children)
        {
            child.Measure(inner);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (!_sideways)
        {
            return new Size(width, height);
        }

        // Across the edge: the header's own height. Along it: the whole edge when it is known.
        return new Size(height, double.IsInfinity(availableSize.Height) ? width : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inner = _sideways ? new Size(finalSize.Height, finalSize.Width) : finalSize;
        var origin = new Point((finalSize.Width - inner.Width) / 2, (finalSize.Height - inner.Height) / 2);

        foreach (var child in Children)
        {
            if (_sideways)
            {
                // Turned about its own centre, which lands it back on the panel.
                child.RenderTransformOrigin = new Point(0.5, 0.5);
                if (child.RenderTransform is not RotateTransform { Angle: -90 })
                {
                    child.RenderTransform = new RotateTransform { Angle = -90 };
                }
            }
            else
            {
                child.RenderTransform = null;
            }

            child.Arrange(new Rect(origin, inner));
        }

        return finalSize;
    }
}
