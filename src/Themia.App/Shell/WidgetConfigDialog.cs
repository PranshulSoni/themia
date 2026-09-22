using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;

namespace Themia.App.Shell;

/// <summary>
/// Per-widget settings.
/// <para>
/// Edits a <b>clone</b> and only writes it back when the user confirms, so Cancel really cancels.
/// The clone is the dialog's own model rather than a mirror of the live one: mirroring a config
/// into local state and syncing it back is what makes a settings panel fight the thing it edits.
/// </para>
/// </summary>
internal static class WidgetConfigDialog
{
    /// <summary>Returns true when the widget was changed and the caller should persist.</summary>
    public static async Task<bool> EditAsync(XamlRoot xamlRoot, WidgetInstance widget)
    {
        var draft = (ClockConfig)widget.Config.Clone();
        var label = widget.Label ?? string.Empty;
        var surface = widget.Surface;

        var labelBox = new TextBox { Text = label, MinWidth = 180, PlaceholderText = "Clock" };
        labelBox.TextChanged += (_, _) => label = labelBox.Text;

        var body = new StackPanel { Spacing = 4, MinWidth = 420 };

        body.Children.Add(FluentLayout.Header("Clock"));

        body.Children.Add(FluentLayout.Row("", "Face", "Digital readout or a sweeping analog dial",
            FluentLayout.Choice(
                [(ClockFace.Digital, "Digital"), (ClockFace.Analog, "Analog")],
                draft.Face,
                value => draft.Face = value)));

        body.Children.Add(FluentLayout.Row("", "24-hour time", null,
            FluentLayout.Toggle(draft.Use24Hour, value => draft.Use24Hour = value)));

        body.Children.Add(FluentLayout.Row("", "Show seconds",
            "Wakes the app once a second instead of once a minute",
            FluentLayout.Toggle(draft.ShowSeconds, value => draft.ShowSeconds = value)));

        body.Children.Add(FluentLayout.Row("", "Show date", null,
            FluentLayout.Toggle(draft.ShowDate, value => draft.ShowDate = value)));

        body.Children.Add(FluentLayout.Header("Appearance"));

        body.Children.Add(FluentLayout.Row("", "Background", "How this widget's card is filled",
            FluentLayout.Choice(
                [
                    (WidgetSurface.Card, "Card"),
                    (WidgetSurface.Translucent, "Translucent"),
                    (WidgetSurface.Transparent, "None"),
                ],
                surface,
                value => surface = value)));

        body.Children.Add(FluentLayout.Row("", "Title", "Shown in menus; leave blank for the default", labelBox));

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Widget settings",
            Content = new ScrollViewer { Content = body, MaxHeight = 460 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        widget.Config = draft;
        widget.Surface = surface;
        widget.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        return true;
    }
}
