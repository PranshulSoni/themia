using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Shell;
using Themia.Core.Widgets;

namespace Themia.App.Shell;

/// <summary>
/// [S-881] One notice as drawn: Windows 11's notification shape — a severity mark, a bold title, a
/// line of detail, an optional button, and a close button — on an opaque flyout surface.
/// <para>
/// Opaque rather than acrylic: the surface window is genuinely transparent, so an in-app acrylic
/// brush has nothing behind it to blur and reads as a hole. Every control refuses focus on
/// interaction: the surface never activates for a notice ([S-880]), and a click on its button must
/// not take the keyboard from the widget holding the lease.
/// </para>
/// </summary>
internal sealed partial class NoticeCard : Grid
{
    private const string Scope = "notice";

    /// <summary>Windows' own toast width.</summary>
    public const double CardWidth = 364;

    public NoticeCard(Notice notice, Action<NoticeCard> dismiss)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(dismiss);

        Notice = notice;

        var (glyph, brushKey, fallback) = notice.Kind switch
        {
            NoticeKind.Success => (Glyphs.Success, "SystemFillColorSuccessBrush", Colors.SeaGreen),
            NoticeKind.Warning => (Glyphs.Warning, "SystemFillColorCautionBrush", Colors.DarkGoldenrod),
            NoticeKind.Error => (Glyphs.ErrorBadge, "SystemFillColorCriticalBrush", Colors.Firebrick),
            _ => (Glyphs.Info, "SystemFillColorAttentionBrush", Colors.SteelBlue),
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = Fluent.IconSize,
            Foreground = FluentLayout.Themed(brushKey, fallback),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, Fluent.Space3, 0),
        };

        var title = Fluent.Text(FluentType.BodyStrong, "TextFillColorPrimaryBrush");
        title.Text = notice.Title;
        title.TextWrapping = TextWrapping.Wrap;

        var text = new StackPanel { Spacing = 2, Children = { title } };

        if (!string.IsNullOrWhiteSpace(notice.Message))
        {
            var message = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
            message.Text = notice.Message;
            message.TextWrapping = TextWrapping.Wrap;
            message.MaxLines = 4;
            message.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(message);
        }

        if (notice.Action is { } action)
        {
            var button = new Button
            {
                Content = action.Label,
                Margin = new Thickness(0, Fluent.Space2, 0, 0),
                AllowFocusOnInteraction = false,
                MinWidth = 96,
            };
            button.Click += (_, _) =>
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Error(Scope, $"the notice action '{action.Label}' threw", ex);
                }

                dismiss(this);
            };
            text.Children.Add(button);
        }

        var close = new Button
        {
            Content = new FontIcon { Glyph = Glyphs.Dismiss, FontSize = FluentType.Caption.FontSize },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(Fluent.Space2, -4, -6, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            AllowFocusOnInteraction = false,
        };
        ToolTipService.SetToolTip(close, "Dismiss");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Dismiss");
        close.Click += (_, _) => dismiss(this);

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(close, 2);
        row.Children.Add(icon);
        row.Children.Add(text);
        row.Children.Add(close);

        Children.Add(row);
        Width = CardWidth;
        Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space3, Fluent.Space4);
        CornerRadius = new CornerRadius(Fluent.OverlayRadius);
        BorderThickness = new Thickness(1);
        BorderBrush = FluentLayout.Themed("SurfaceStrokeColorFlyoutBrush", Colors.Gray);
        Background = FluentLayout.Themed("SolidBackgroundFillColorTertiaryBrush", Colors.DimGray);
        BackgroundSizing = BackgroundSizing.OuterBorderEdge;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, notice.Message is null ? notice.Title : $"{notice.Title}. {notice.Message}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(this, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
    }

    public Notice Notice { get; }
}
