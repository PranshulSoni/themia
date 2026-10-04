using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Shell;

/// <summary>
/// A hand-rolled equivalent of the Community Toolkit's settings card, built from stock Fluent theme
/// resources.
/// <para>
/// Rolled by hand rather than taken from the toolkit purely to keep the dependency surface at one
/// package: every extra assembly is more for the AOT compiler to root, and a settings row is a
/// border with two text blocks.
/// </para>
/// <para>
/// This is the whole vocabulary a settings page gets: a <see cref="Header"/>, a <see cref="Row"/>
/// and the handful of controls that go in one. Everything a widget's settings page wants to draw is
/// here rather than in the page, because thirty pages each inventing their own list editor, their
/// own refusal message and their own confirmation dialog is how they drifted apart in the first
/// place. A page that needs a shape that is not here adds it here.
/// </para>
/// </summary>
internal static class FluentLayout
{
    private const string Scope = "layout";

    /// <summary>
    /// The marker that sends a row to the bottom of the page.
    /// <para>
    /// A row built with <see cref="DangerRow"/> destroys something the user cannot get back, and the
    /// page hoists every one of them into its own section under the last heading — so a destructive
    /// action is never sitting between two switches where a stray click finds it.
    /// </para>
    /// </summary>
    private const string DangerMark = "themia.danger";

    /// <summary>A settings row: icon, title, optional description, and a control on the right.</summary>
    public static Border Row(string glyph, string title, string? description, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });

        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(text, 1);

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = Fluent.IconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space4, 0),
        };

        Grid.SetColumn(icon, 0);

        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(Fluent.Space4, 0, 0, 0);
        Grid.SetColumn(control, 2);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { icon, text, control },
        };

        return Card(grid);
    }

    /// <summary>
    /// The card a settings row sits in: the padding, radius, fill and stroke every surface on a
    /// settings page shares.
    /// <para>
    /// Public because the list editor's item cards are the same card. Five widgets had each written
    /// this border out by hand and two of them had already drifted on the thickness.
    /// </para>
    /// </summary>
    public static Border Card(FrameworkElement child) => new()
    {
        Child = child,
        Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
        CornerRadius = new CornerRadius(Fluent.ControlRadius),
        Background = Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
        BorderBrush = Themed("CardStrokeColorDefaultBrush", Colors.Gray),
        BorderThickness = new Thickness(Fluent.Hairline),
    };

    /// <summary>A group heading above a run of rows.</summary>
    public static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, Fluent.Space3, 0, 0),
    };

    /// <summary>A plain line of explanation between rows, in the tertiary text colour.</summary>
    public static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Themed("TextFillColorTertiaryBrush", Colors.Gray),
        Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space2),
    };

    public static ToggleSwitch Toggle(bool value, Action<bool> onChanged)
    {
        // MinWidth 0 matters: the default ToggleSwitch style reserves about 150px for on/off
        // content. With that content cleared the knob stayed at the left of the reserved block, so
        // every switch sat visibly short of the right edge that the combo boxes and sliders lined
        // up against.
        var toggle = new ToggleSwitch
        {
            IsOn = value,
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        toggle.Toggled += (_, _) => onChanged(toggle.IsOn);
        return toggle;
    }

    public static ComboBox Choice<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> onChanged)
        where T : struct
    {
        var combo = new ComboBox { MinWidth = 150 };

        foreach (var (value, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, selected))
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value })
            {
                onChanged(value);
            }
        };

        return combo;
    }

    /// <summary>A slider with a live numeric readout, used for the gap and corner sizes.</summary>
    public static StackPanel Slider(double value, double min, double max, double step, string unit, Action<double> onChanged)
    {
        var readout = new TextBlock
        {
            Text = $"{value:0}{unit}",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 44,
            Opacity = 0.7,
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = value,
            Width = 160,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, args) =>
        {
            readout.Text = $"{args.NewValue:0}{unit}";
            onChanged(args.NewValue);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { slider, readout },
        };
    }

    // ---------------------------------------------------------------------------------------
    // Fields that can be refused
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A row whose control is a field with its verdict on the line underneath it.
    /// <para>
    /// Every text field on a settings page that can be wrong — a host, a feed address, a shortcut
    /// target, a folder, a coordinate, a search template — reports through this one shape: the value
    /// stays exactly as it was typed, and a single line under the field says why it was refused or
    /// confirms what it actually resolved to. Nothing is ever silently corrected into something that
    /// works, because a field that looks accepted and does something else is the failure all of this
    /// checking exists to prevent.
    /// </para>
    /// </summary>
    public static Border FieldRow(
        string glyph,
        string title,
        string? description,
        FrameworkElement field,
        TextBlock verdict)
        => Row(glyph, title, description, new StackPanel
        {
            Spacing = Fluent.Space1,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { field, verdict },
        });

    /// <summary>The line under a field, before anything has been said on it.</summary>
    public static TextBlock Verdict() => new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextAlignment = TextAlignment.Right,
        MaxWidth = 300,
    };

    /// <summary>Say why the value was refused. The field keeps what was typed.</summary>
    public static void Refuse(TextBlock verdict, string message)
        => Say(verdict, message, Themed("SystemFillColorCriticalBrush", Colors.Red));

    /// <summary>
    /// Confirm what the value resolved to, or say what an empty field falls back to. Worth as much
    /// as a refusal: a field that looks accepted but points somewhere else is the same failure.
    /// </summary>
    public static void Confirm(TextBlock verdict, string message)
        => Say(verdict, message, Themed("TextFillColorTertiaryBrush", Colors.Gray));

    private static void Say(TextBlock verdict, string message, Brush colour)
    {
        verdict.Text = message;
        verdict.Foreground = colour;
        verdict.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------------------------------
    // How often a widget updates
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The refresh row for a widget that reads this machine: seconds, because a local read costs a
    /// wake-up and nothing else.
    /// </summary>
    /// <param name="what">
    /// Completes "How often …", in the user's words — "the devices are enumerated", "the host is
    /// probed".
    /// </param>
    public static void RefreshSeconds(
        List<FrameworkElement> rows,
        string what,
        double current,
        double min,
        double max,
        double step,
        Action<int> onChanged)
    {
        rows.Add(Header("Updating"));

        rows.Add(Row(Glyphs.Timer, "Refresh", $"How often {what}",
            Slider(current, min, max, step, "s", value => onChanged((int)Math.Round(value)))));
    }

    /// <summary>
    /// The refresh row for a widget that asks a service: a fixed set of cadences in minutes, and
    /// nothing under a minute anywhere.
    /// <para>
    /// The other refresh row is a slider in seconds, and the difference is deliberate rather than
    /// drift. Every service behind these widgets is free, shared and unauthenticated, and a slider
    /// that goes to "every second" is an invitation to get the whole app blocked. The row reads the
    /// same either way — same glyph, same title, same place on the page — so only the granularity
    /// differs, which is the part that is genuinely not the same.
    /// </para>
    /// </summary>
    public static void RefreshChoice(
        List<FrameworkElement> rows,
        int current,
        Action<int> onChanged,
        bool fast = false)
    {
        (int Value, string Label)[] options = fast
            ? [(1, "Every minute"), (5, "Every 5 minutes"), (15, "Every 15 minutes"), (30, "Every 30 minutes")]
            : [(5, "Every 5 minutes"), (15, "Every 15 minutes"), (30, "Every 30 minutes"), (60, "Every hour")];

        // A persisted value outside the set — hand-edited, or left over from an older build —
        // would otherwise leave the box blank and the setting pointing at nothing visible.
        var selected = current;
        var known = false;

        foreach (var (value, _) in options)
        {
            known |= value == selected;
        }

        if (!known)
        {
            // The second entry either way: five minutes on a fast widget, fifteen on a slow one.
            selected = options[1].Value;
            onChanged(selected);
        }

        rows.Add(Header("Updating"));

        rows.Add(Row(Glyphs.Timer, "Refresh", "How often the service is asked",
            Choice(options, selected, onChanged)));
    }

    // ---------------------------------------------------------------------------------------
    // Destroying something
    // ---------------------------------------------------------------------------------------

    /// <summary>A row whose action cannot be undone. The page puts every one of these last.</summary>
    public static Border DangerRow(string glyph, string title, string? description, FrameworkElement control)
    {
        var row = Row(glyph, title, description, control);
        row.Tag = DangerMark;
        return row;
    }

    /// <summary>Whether a row belongs in the page's danger section.</summary>
    public static bool IsDanger(FrameworkElement element)
        => element is Border { Tag: string mark } && mark == DangerMark;

    /// <summary>
    /// A destructive row: a button that asks first, the same way everywhere.
    /// </summary>
    /// <param name="describe">
    /// What the dialog says, or <see langword="null"/> when there is nothing to destroy. A button
    /// whose only possible outcome is to report that it had nothing to do is a button that should
    /// not have been offered, so it is disabled instead.
    /// </param>
    public static Border ConfirmRow(
        string glyph,
        string title,
        string description,
        string action,
        string question,
        Func<string?> describe,
        Action confirmed)
    {
        var button = new Button { Content = action, IsEnabled = describe() is not null };
        button.Click += (_, _) => _ = ConfirmClickAsync(button, action, question, describe, confirmed);

        return DangerRow(glyph, title, description, button);
    }

    /// <summary>
    /// Ask, then do it. Awaited rather than fired and forgotten, so a dialog that could not be shown
    /// is never mistaken for the user saying yes.
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        FrameworkElement source,
        string question,
        string detail,
        string action)
    {
        var root = source.XamlRoot;

        if (root is null)
        {
            // No window to put a dialog in. Refusing beats destroying something unasked.
            Log.Warn(Scope, $"no XamlRoot for the '{question}' confirmation; nothing was changed");
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = question,
            Content = detail,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            // ShowAsync throws when another dialog already owns this window. Say so and change
            // nothing.
            Log.Error(Scope, $"could not show the '{question}' confirmation", ex);
            return false;
        }
    }

    /// <summary>
    /// The body of a <see cref="ConfirmRow"/> click. Its own method rather than an async lambda on
    /// the event: an exception out of an async void handler takes the process with it, and this one
    /// runs the caller's code.
    /// </summary>
    private static async Task ConfirmClickAsync(
        Button button,
        string action,
        string question,
        Func<string?> describe,
        Action confirmed)
    {
        if (describe() is not { } detail)
        {
            return;
        }

        if (!await ConfirmAsync(button, question, detail, action).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            confirmed();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"'{question}' could not be carried out", ex);
            return;
        }

        button.IsEnabled = describe() is not null;
    }

    // ---------------------------------------------------------------------------------------
    // Editable lists
    // ---------------------------------------------------------------------------------------

    /// <summary>A compact icon command on a list card: move up, move down, remove.</summary>
    public static Button IconCommand(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = Fluent.IconSize },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// The one editable list on a settings page: a card per item with the item's own editor on the
    /// left and move-up, move-down and remove on the right, a line saying so when there is nothing
    /// in it, and an add row underneath.
    /// <para>
    /// Five widgets — timers, shortcuts, swatches, quotes and habits — had each grown their own
    /// version of this, and no two agreed on where the commands sat, what the empty list said, or
    /// whether the ends of the list could move past themselves. The differences were all accidents
    /// of who wrote which page. What genuinely varies is the body of one item, which is the one
    /// thing the caller supplies.
    /// </para>
    /// </summary>
    /// <returns>
    /// A handle for the pages that can add an item from somewhere other than the add button — the
    /// shortcuts page adds from a file picker and from the Windows locations list, and both must go
    /// through the same append so there is one way an item comes into being.
    /// </returns>
    public static ListEditorHandle<T> ListEditor<T>(List<FrameworkElement> rows, ListEditorOptions<T> options)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);

        // One host whose children are rebuilt in place, because adding, removing and reordering all
        // change the shape of this section while the page around it is built once.
        var host = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(host);

        var add = new Button { Content = options.AddAction };

        void Rebuild()
        {
            host.Children.Clear();

            if (options.Items.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = options.EmptyText,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Themed("TextFillColorTertiaryBrush", Colors.Gray),
                    Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
                });
            }

            for (var index = 0; index < options.Items.Count; index++)
            {
                host.Children.Add(ItemCard(options, index, Rebuild));
            }

            add.IsEnabled = options.Items.Count < options.Capacity;
        }

        void Append(T item)
        {
            if (options.Items.Count >= options.Capacity)
            {
                return; // ignore: the add button is already disabled at the cap.
            }

            options.Items.Add(item);
            options.Save();
            Rebuild();
        }

        add.Click += (_, _) => Append(options.Create());

        Rebuild();

        rows.Add(Row(Glyphs.Add, options.AddTitle, options.AddDescription, add));

        return new ListEditorHandle<T>(Append, Rebuild);
    }

    private static FrameworkElement ItemCard<T>(ListEditorOptions<T> options, int index, Action rebuild)
        where T : class
    {
        var item = options.Items[index];

        var body = options.Body(item);
        body.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(body, 0);

        var up = IconCommand(Glyphs.MoveUp, "Move up");
        var down = IconCommand(Glyphs.MoveDown, "Move down");
        var remove = IconCommand(Glyphs.Delete, options.RemoveTooltip);

        // One swap per click, and the ends cannot move past themselves.
        up.IsEnabled = index > 0;
        down.IsEnabled = index < options.Items.Count - 1;

        up.Click += (_, _) => Reorder(options, index, -1, rebuild);
        down.Click += (_, _) => Reorder(options, index, 1, rebuild);
        remove.Click += (_, _) => _ = RemoveAsync(options, item, remove, rebuild);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space1,
            Margin = new Thickness(Fluent.Space3, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { up, down, remove },
        };

        Grid.SetColumn(commands, 1);

        return Card(new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { body, commands },
        });
    }

    private static void Reorder<T>(ListEditorOptions<T> options, int index, int delta, Action rebuild)
        where T : class
    {
        if (!DeskListOrder.Move(options.Items, index, delta))
        {
            return; // ignore: the arrow at the end of the list is pressable and does nothing.
        }

        options.Save();
        rebuild();
    }

    private static async Task RemoveAsync<T>(
        ListEditorOptions<T> options,
        T item,
        FrameworkElement source,
        Action rebuild)
        where T : class
    {
        if (options.ConfirmRemove is { } ask && !await ask(item, source).ConfigureAwait(true))
        {
            return;
        }

        // Removed by identity rather than by the index the card was built with: the list may have
        // been reordered while a confirmation was up.
        if (!options.Items.Remove(item))
        {
            return; // ignore: something else already took it out.
        }

        options.Save();
        rebuild();
    }

    /// <summary>
    /// A stock Fluent brush by resource key, with a fallback colour for the case where the key is
    /// missing.
    /// <para>
    /// Public because every settings file and every widget view wants it, and four private copies of
    /// the same four lines is four places for the fallback to drift. Looking the brush up by key is
    /// what makes a surface follow the theme; a literal colour is a surface that does not.
    /// </para>
    /// </summary>
    public static Brush Themed(string key, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }

        // A missing key is a typo or a renamed system resource, and the literal that replaces it is
        // a colour that will not follow the theme. Worth saying out loud rather than quietly wrong.
        Log.Warn(Scope, $"theme resource '{key}' missing; using a literal colour");
        return new SolidColorBrush(fallback);
    }
}

/// <summary>What one <see cref="FluentLayout.ListEditor{T}"/> needs to know about its list.</summary>
internal sealed class ListEditorOptions<T>
    where T : class
{
    /// <summary>The live list out of the widget's config. Edited in place, never copied.</summary>
    public required IList<T> Items { get; init; }

    /// <summary>
    /// One item's own editor — the only part of a list that genuinely differs between widgets. The
    /// move and remove commands are added beside whatever this returns.
    /// </summary>
    public required Func<T, FrameworkElement> Body { get; init; }

    public required Action Save { get; init; }

    /// <summary>What the section says when the list is empty, in the widget's own words.</summary>
    public required string EmptyText { get; init; }

    /// <summary>The title of the add row, e.g. "Add a shortcut".</summary>
    public required string AddTitle { get; init; }

    /// <summary>The label on the add button, e.g. "Add".</summary>
    public required string AddAction { get; init; }

    /// <summary>A new, empty item. Nothing about it is filled in from anywhere else.</summary>
    public required Func<T> Create { get; init; }

    public string? AddDescription { get; init; }

    /// <summary>The most the list may hold. The add button is disabled at the cap.</summary>
    public int Capacity { get; init; } = int.MaxValue;

    public string RemoveTooltip { get; init; } = "Remove";

    /// <summary>
    /// Ask before removing, for a list whose items carry something the user cannot get back. Left
    /// unset, removing is immediate — which is right for a row that holds only what is on screen.
    /// </summary>
    public Func<T, FrameworkElement, Task<bool>>? ConfirmRemove { get; init; }
}

/// <summary>A way back into a built list editor, for a page that can add an item from elsewhere.</summary>
internal sealed class ListEditorHandle<T>
    where T : class
{
    private readonly Action<T> _append;
    private readonly Action _rebuild;

    internal ListEditorHandle(Action<T> append, Action rebuild)
    {
        _append = append;
        _rebuild = rebuild;
    }

    /// <summary>Append an item through the same path the add button uses.</summary>
    public void Add(T item) => _append(item);

    /// <summary>Redraw the cards from the list as it stands.</summary>
    public void Refresh() => _rebuild();
}
