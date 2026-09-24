using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// What has been copied recently, most recent first. Click a row to put it back on the clipboard.
/// <para>
/// This is the most sensitive widget in the app, and the design is mostly a list of things it does
/// not do. It reads <b>text only</b> — a copied file or image is never looked at. It keeps a
/// bounded history <b>in memory for the session</b> and writes nothing to disk, so closing the app
/// is the end of it; there is no persistence setting, because a clipboard history that survives a
/// restart is a credentials file nobody meant to create. Entries are <b>displayed, never
/// executed</b>: no row here is a launch target, whatever it contains.
/// </para>
/// <para>
/// The history is fed by <see cref="ClipboardWatcher"/>, which is a message-only window with
/// <c>AddClipboardFormatListener</c> rather than a poll — so the clipboard is read exactly when
/// Windows says it changed and at no other time.
/// </para>
/// </summary>
internal sealed partial class ClipboardView : Grid, IWidgetView, IWidgetHeaderContent
{
    private const string Scope = "clipboard-widget";

    /// <summary>Below this width the "when" column is dropped; the text is what the row is for.</summary>
    private const double TimestampMinimumWidth = 260;

    /// <summary>How many characters of an entry a standard row shows before the ellipsis.</summary>
    private const int PreviewLength = 120;

    /// <summary>What the "when" column reserves, so the times line up down the list.</summary>
    private const double TimestampWidth = 40;

    private readonly Button _clear;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();

    /// <summary>One per row, so the hover handlers come off with the rows they light.</summary>
    private readonly List<IDisposable> _hovers = [];

    private readonly ClipboardHistory _history = new();

    private ClipboardConfig _config = new();
    private IDisposable? _watch;
    private IDisposable? _subscription;
    private Ticker? _ticker;
    private bool _detached;
    private bool _compact;
    private bool _timestampVisible = true;

    public ClipboardView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _clear = new Button
        {
            Content = Fluent.Icon(Glyphs.Delete, "TextFillColorSecondaryBrush", 14),
            Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1),
            MinWidth = 0,
        };

        _clear.Click += OnClearClicked;
        ToolTipService.SetToolTip(_clear, "Clear history");

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        _empty = Fluent.EmptyState(Glyphs.Clipboard, _emptyText, out _emptyIcon);

        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The clear-history button, in the card's header. Built in the constructor because the card
    /// reads this once, when it builds that header.
    /// </summary>
    public FrameworkElement? HeaderTrailing => _clear;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;

        // The listener is shared by every Clipboard widget on every display: subscribers are
        // counted and the message window is created with the first and destroyed with the last.
        _watch ??= ClipboardWatcher.Subscribe(OnClipboardText);

        // The tick does not fetch anything. It only keeps the relative times ("4m", "2h") honest,
        // which is the one thing in this widget that goes stale on its own.
        _subscription ??= _ticker?.Subscribe(_config.TickInterval, Redraw);

        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ClipboardConfig ?? new ClipboardConfig();

        // Lowering the capacity drops the oldest entries immediately rather than at the next copy:
        // a shorter history is the one setting here that actually reduces exposure, and it should
        // take effect when it is asked for.
        _history.Capacity = _config.EffectiveCapacity;

        ApplyLayout();
        Render();
    }

    /// <summary>Something new was copied. Only a change to the list is worth a redraw.</summary>
    private void OnClipboardText(string text)
    {
        if (_detached)
        {
            return;
        }

        if (_history.Add(text, DateTimeOffset.Now))
        {
            Render();
        }
    }

    private void Redraw()
    {
        if (!_detached)
        {
            Render();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Height < Fluent.CompactBelow;
        var timestamp = args.NewSize.Width >= TimestampMinimumWidth;

        ApplyLayout();

        if (compact == _compact && timestamp == _timestampVisible)
        {
            return;
        }

        _compact = compact;
        _timestampVisible = timestamp;
        Render();
    }

    private void ApplyLayout()
    {
        var height = ActualHeight;
        var measured = height > 0;

        _emptyIcon.Visibility = measured && height < 180 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render()
    {
        ClearRows();

        _clear.IsEnabled = _history.Count > 0;

        if (_history.Count == 0)
        {
            // Each state says which it is. "Nothing copied yet" and "the listener never started"
            // need very different things from the reader.
            _emptyText.Text = ClipboardWatcher.IsRunning
                ? "Nothing copied yet. Copy something and it appears here."
                : "Windows would not let this app watch the clipboard.";

            _empty.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        // One clock reading for the whole list, so a list drawn across a minute boundary cannot
        // show two different ideas of "now".
        var now = DateTimeOffset.Now;
        var timestamp = _config.ShowTimestamp && _timestampVisible;

        for (var i = 0; i < _history.Count; i++)
        {
            _list.Children.Add(BuildRow(_history.Entries[i], i == 0, now, timestamp));
        }
    }

    private FrameworkElement BuildRow(ClipboardEntry entry, bool current, DateTimeOffset now, bool timestamp)
    {
        // The top entry is what is on the clipboard right now. Saying so with the stock tick in the
        // accent colour is the widget's only feedback that a click landed — and it is event-driven,
        // arriving with the clipboard change itself rather than on a timer.
        var icon = current
            ? Fluent.Icon(Glyphs.Done, "AccentTextFillColorPrimaryBrush")
            : Fluent.Icon(Glyphs.Copy);

        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var text = Fluent.Text(FluentType.Body);
        text.Text = ClipboardFormatting.Preview(entry.Text, PreviewLength);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(icon, 0);
        SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);

        if (timestamp)
        {
            var when = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            when.Text = Formatting.Ago(entry.CapturedAt, now);
            when.TextAlignment = TextAlignment.Right;
            when.MinWidth = TimestampWidth;
            when.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);

            SetColumn(when, 2);
            grid.Children.Add(when);
        }

        var row = Fluent.Row(grid, _compact);
        row.Tag = entry.Text;
        _hovers.Add(Fluent.Hover(row));

        // A single click, and deliberately so: unlike every other widget in this batch, this one
        // opens nothing. The worst a mis-click can do is put something back on the clipboard.
        row.Tapped += OnRowTapped;

        ToolTipService.SetToolTip(row, ClipboardFormatting.Describe(entry.Text));
        return row;
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;

        if (_detached || sender is not Border { Tag: string text })
        {
            return;
        }

        if (!ClipboardWatcher.SetText(text))
        {
            // Already logged with the Win32 reason. Nothing changed, and the list still shows what
            // is genuinely on the clipboard, so there is nothing to correct on screen.
            Log.Debug(Scope, "the entry could not be put back on the clipboard");
        }

        // No redraw here on purpose: the write raises a clipboard change, and the history redraws
        // from that. What is shown as current is then what Windows actually holds, not what this
        // widget hoped it wrote.
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();

        foreach (var child in _list.Children)
        {
            if (child is Border row)
            {
                row.Tapped -= OnRowTapped;

                // Drop the reference to the copied text with the row. A detached Border holding a
                // password in its Tag is exactly the thing this widget should not be doing.
                row.Tag = null;
            }
        }

        _list.Children.Clear();
    }

    // -----------------------------------------------------------------------------------------
    // Clearing
    // -----------------------------------------------------------------------------------------

    private void OnClearClicked(object sender, RoutedEventArgs args) => _ = ConfirmClearAsync();

    /// <summary>
    /// Ask, then clear. Awaited rather than fired and forgotten, so a dialog that could not be
    /// shown is never mistaken for the user saying yes.
    /// </summary>
    private async Task ConfirmClearAsync()
    {
        if (_detached || _history.Count == 0)
        {
            return;
        }

        var root = XamlRoot;

        if (root is null)
        {
            // No window to put a dialog in. Refusing beats clearing unasked.
            Log.Warn(Scope, "no XamlRoot for the clear-history confirmation; the history was left alone");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ActualTheme,
            Title = "Clear clipboard history?",
            Content = "Everything in this list is forgotten. This cannot be undone, and it does not "
                + "change what is on the clipboard now.",
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
            Log.Error(Scope, "could not show the clear-history confirmation", ex);
            return;
        }

        if (_detached)
        {
            return;
        }

        _history.Clear();
        Render();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        SizeChanged -= OnSizeChanged;
        _clear.Click -= OnClearClicked;

        _subscription?.Dispose();
        _subscription = null;

        // Unhooks this widget from the shared listener, and takes the message window down with it
        // when this was the last Clipboard widget on the desktop.
        _watch?.Dispose();
        _watch = null;

        // In memory only, and gone the moment the widget is: there is nothing to write out and
        // nothing left behind.
        _history.Clear();
        ClearRows();

        _ticker = null;
    }
}
