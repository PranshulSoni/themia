using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// [S-468] A panel that turns its one child a quarter turn at a time and still fits it.
/// <para>
/// A render transform alone rotates the child <i>after</i> layout, so a picture laid out for a wide
/// frame and then turned sideways overflows top and bottom and is cut off. Here a sideways child is
/// laid out against the swapped axes — a 320x240 frame lays its child out at 240x320 — and only
/// then turned, so a rotated camera fits the frame exactly as an upright one does.
/// </para>
/// <para>
/// It also clips to its own bounds: the "cover" fit deliberately overflows, and the overflow must
/// stop at the card rather than paint over its neighbours.
/// </para>
/// </summary>
internal sealed partial class RotatedBox : Panel
{
    private readonly RectangleGeometry _clip = new();
    private StreamRotation _rotation = StreamRotation.None;

    public RotatedBox()
    {
        Clip = _clip;
    }

    public StreamRotation Turn
    {
        get => _rotation;
        set
        {
            if (_rotation == value)
            {
                return;
            }

            _rotation = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var sideways = VideoStreamRules.IsSideways(_rotation);
        var inner = sideways ? new Size(availableSize.Height, availableSize.Width) : availableSize;

        foreach (var child in Children)
        {
            child.Measure(inner);
        }

        // Take the whole frame: the picture inside decides how much of it to paint.
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var sideways = VideoStreamRules.IsSideways(_rotation);
        var inner = sideways ? new Size(finalSize.Height, finalSize.Width) : finalSize;

        // Centred on the frame, so the turn about the child's own centre lands it back in place.
        var origin = new Point((finalSize.Width - inner.Width) / 2, (finalSize.Height - inner.Height) / 2);

        foreach (var child in Children)
        {
            child.RenderTransformOrigin = new Point(0.5, 0.5);

            if (_rotation == StreamRotation.None)
            {
                child.RenderTransform = null;
            }
            else
            {
                // One transform per child: a transform is not shared between elements.
                if (child.RenderTransform is not RotateTransform turn)
                {
                    turn = new RotateTransform();
                    child.RenderTransform = turn;
                }

                turn.Angle = (int)_rotation;
            }

            child.Arrange(new Rect(origin, inner));
        }

        _clip.Rect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        return finalSize;
    }
}
