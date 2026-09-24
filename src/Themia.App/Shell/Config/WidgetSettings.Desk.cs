using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The settings rows for the two widgets that are typed into rather than watched: Notes and the
/// Calculator.
/// <para>
/// Each row writes straight to the live config and saves, so the widget on the desktop behind the
/// window changes as the control moves. Nothing is mirrored into a local field first: the config is
/// the single copy, and a second one would be a second thing to keep in step.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string DeskScope = "widget-settings";

    public static void Notes(List<FrameworkElement> rows, NotesConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Notes"));

        // [S-05] The note's own size, not a scale factor: it is the one piece of formatting a
        // plain-text note has.
        rows.Add(FluentLayout.Row(Glyphs.TextSize, "Text size", "How big the note is written",
            // The full range the config clamps to, so the slider can never sit at 32 while the
            // stored size is 40 and the note is drawn at neither of the two.
            FluentLayout.Slider(config.EffectiveFontSizePx, 8, 48, 1, "px",
                value => { config.FontSizePx = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Wrap, "Wrap lines",
            "Off scrolls sideways instead of breaking long lines",
            FluentLayout.Toggle(config.WrapText, value => { config.WrapText = value; save(); })));

        var clear = new Button { Content = "Clear notes" };

        // [S-06] The text is gone for good afterwards — there is no undo, no bin and no second copy
        // — so this asks first. A single click that silently empties a note the user has been
        // keeping for months is not a button, it is a trap.
        clear.Click += (_, _) => _ = ConfirmClearAsync(clear, config, save);

        rows.Add(FluentLayout.Row(Glyphs.Clear, "Clear notes", "Empties this note. This cannot be undone", clear));
    }

    public static void Calculator(List<FrameworkElement> rows, CalculatorConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Calculator"));

        // [S-05] Applied to the live result and to every row already in the history.
        rows.Add(FluentLayout.Row(Glyphs.Calculator, "Decimal places", "Digits shown after the point",
            FluentLayout.Slider(config.EffectiveDecimalPlaces, 0, ExpressionResult.MaxDecimalPlaces, 1, string.Empty,
                value => { config.DecimalPlaces = (int)Math.Round(value); save(); })));
    }

    /// <summary>
    /// Ask, then clear. Awaited rather than fired and forgotten so a failure to show the dialog can
    /// never be mistaken for the user saying yes.
    /// </summary>
    private static async Task ConfirmClearAsync(Button source, NotesConfig config, Action save)
    {
        var root = source.XamlRoot;

        if (root is null)
        {
            // No window to put a dialog in. Refusing beats clearing unasked.
            Log.Warn(DeskScope, "no XamlRoot for the clear-notes confirmation; the note was left alone");
            return;
        }

        if (string.IsNullOrEmpty(config.Text))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = "Clear this note?",
            Content = "Everything written in it is deleted. This cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // ShowAsync throws when another dialog already owns this window. Say so and change
            // nothing.
            Log.Error(DeskScope, "could not show the clear-notes confirmation", ex);
            return;
        }

        config.Text = string.Empty;
        save();
    }
}
