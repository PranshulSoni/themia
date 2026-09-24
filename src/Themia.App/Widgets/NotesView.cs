using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A plain-text note that fills its card.
/// <para>
/// The note lives in the widget's own config, so it travels with copy/paste, with the view it sits
/// in, and with a backup of the state file — there is no note store, no note list and no sync. The
/// original app's Notes widget is a markdown editor over a device-wide note pool; this is its
/// plain-text core and nothing else.
/// </para>
/// </summary>
internal sealed partial class NotesView : Grid, IWidgetView, IWidgetSurfaceAware
{
    private const string Scope = "notes";

    /// <summary>
    /// How long typing pauses before the note is written. Long enough that a sentence is one save,
    /// short enough that nothing is at risk for more than a moment.
    /// <para>
    /// The write is not a correctness path: <see cref="Flush"/> on blur and on
    /// <see cref="Detach"/> is what guarantees the last sentence is kept, and the timer only keeps
    /// the atomic single-writer state file from being rewritten per keystroke.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(600);

    private readonly TextBlock _title = new();

    /// <summary>The surface this note is drawn on: where it saves and where it asks for the keyboard.</summary>
    private IWidgetSurface? _surface;
    private readonly FontIcon _icon = new();
    private readonly Grid _header;
    private readonly TextBox _editor = new();
    private readonly DispatcherQueueTimer? _saveTimer;

    private NotesConfig _config = new();
    private bool _dirty;
    private bool _applying;
    private bool _detached;

    public NotesView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var muted = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        _icon.Glyph = WidgetCatalog.Definition(WidgetKind.Notes).Glyph;
        _icon.Foreground = muted;
        _icon.Margin = new Thickness(0, 0, 6, 0);

        _title.Text = WidgetCatalog.Definition(WidgetKind.Notes).Label;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = muted;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);

        _header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { _icon, _title },
        };

        BuildEditor();

        Grid.SetRow(_header, 0);
        Grid.SetRow(_editor, 1);

        Children.Add(_header);
        Children.Add(_editor);

        // The dispatcher timer is the debounce. Where there is no dispatcher queue (a design-time
        // or test host), the note is still written on blur and on detach — it is only the
        // save-as-you-type that is lost, which is why this is a warning rather than a failure.
        _saveTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_saveTimer is null)
        {
            Log.Warn(Scope, "no dispatcher queue on this thread; the note saves on blur and on close only");
        }
        else
        {
            _saveTimer.Interval = SaveDebounce;
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += OnSaveTick;
        }

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// A text box with its chrome taken off, so the note reads as writing on the card rather than as
    /// a form field on it.
    /// <para>
    /// The brushes are replaced through the control's own resource keys, not through its
    /// <c>Background</c> property: the stock template's visual states assign the pointer-over and
    /// focused backgrounds straight onto the border element, which outranks any local value set
    /// here, and the field would light up as a solid rectangle the moment the pointer crossed it.
    /// </para>
    /// </summary>
    private void BuildEditor()
    {
        var clear = new SolidColorBrush(Colors.Transparent);

        foreach (var key in new[]
        {
            "TextControlBackground",
            "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused",
            "TextControlBackgroundDisabled",
            "TextControlBorderBrush",
            "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused",
            "TextControlBorderBrushDisabled",
        })
        {
            _editor.Resources[key] = clear;
        }

        _editor.AcceptsReturn = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.PlaceholderText = "Type your notes here…";
        _editor.IsSpellCheckEnabled = false;
        _editor.IsTextPredictionEnabled = false;
        _editor.BorderThickness = new Thickness(0);
        _editor.Padding = new Thickness(0);
        _editor.Margin = new Thickness(-2, 0, -2, 0); // cancels the template's built-in text inset
        _editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        _editor.VerticalAlignment = VerticalAlignment.Stretch;
        _editor.VerticalContentAlignment = VerticalAlignment.Top;
        _editor.Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);

        ScrollViewer.SetVerticalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor, ScrollBarVisibility.Disabled);

        _editor.TextChanged += OnTextChanged;
        _editor.GotFocus += OnGotFocus;
        _editor.LostFocus += OnLostFocus;
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;

        // [S-02] The widget's own title when the user set one, so a desktop of notes is readable
        // without opening each of them.
        _title.Text = string.IsNullOrWhiteSpace(surface.Label)
            ? WidgetCatalog.Definition(WidgetKind.Notes).Label
            : surface.Label;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        // Nothing to tick: a note changes when it is typed in, not when time passes.
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as NotesConfig ?? new NotesConfig();

        _editor.FontSize = _config.EffectiveFontSizePx;
        _editor.TextWrapping = _config.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;

        ScrollViewer.SetHorizontalScrollBarVisibility(
            _editor,
            _config.WrapText ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);

        // A null can only come from a config file that spelled the note as JSON null; the TextBox
        // would throw on it, and "absent" here means an empty note, not a broken widget.
        var text = _config.Text ?? string.Empty;

        // An unflushed local edit wins over whatever is in the config. The config is only behind
        // because our own debounce has not landed yet, and taking it would throw away the words the
        // user is in the middle of typing. A change from elsewhere (the settings window's "Clear
        // notes") is adopted, because by then there is nothing pending.

        if (_dirty || string.Equals(_editor.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _applying = true;

        try
        {
            _editor.Text = text;
            _editor.SelectionStart = text.Length;
        }
        finally
        {
            _applying = false;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_applying || _detached)
        {
            return;
        }

        _dirty = true;
        _saveTimer?.Start(); // restarts the countdown, so a run of keystrokes is one write
    }

    /// <summary>
    /// Ask the surface for the keyboard. Outside edit mode it is unfocusable, so without this the
    /// caret blinks here while the letters go to whatever window actually has focus.
    /// </summary>
    private void OnGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnLostFocus(object sender, RoutedEventArgs args)
    {
        Flush();
        _surface?.RequestTextInput(false);
    }

    private void OnSaveTick(DispatcherQueueTimer sender, object args) => Flush();

    /// <summary>
    /// Write the editor's text back into the config and ask for it to be persisted. Idempotent: with
    /// nothing pending it does nothing at all, so calling it from three places costs nothing.
    /// </summary>
    private void Flush()
    {
        _saveTimer?.Stop();

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _config.Text = _editor.Text;
        _surface?.RequestSave();
    }

    /// <summary>
    /// The header tracks the tile; the note does not. [S-03] makes the note's size the user's
    /// setting, and a note that grew with its widget would change size every time it was resized.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _title.FontSize = TypeScale.Fit(ActualWidth, ActualHeight, 0.075, 0.10, 10, 18);
        _icon.FontSize = _title.FontSize;

        // Under about 90 DIPs the title is most of the widget, so the note gets the room instead.
        _header.Visibility = ActualHeight < 90 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        // Before anything is unhooked: this is the last chance the last sentence has.
        Flush();
        _detached = true;

        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTick;
        }

        _editor.TextChanged -= OnTextChanged;
        _editor.GotFocus -= OnGotFocus;
        _editor.LostFocus -= OnLostFocus;
        _surface?.RequestTextInput(false);
        SizeChanged -= OnSizeChanged;
    }

}
