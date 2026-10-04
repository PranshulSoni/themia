using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Storage.Streams;

namespace Themia.App.Widgets;

/// <summary>
/// Whatever Windows is playing, from Windows' own media session.
/// <para>
/// No account, no token and no per-player integration:
/// <c>GlobalSystemMediaTransportControlsSessionManager</c> is the same session the media keys and
/// the volume flyout's transport drive, so anything on the machine that plays sound has already
/// told the OS what it is playing. That is the whole design, and it is why the widget works for a
/// browser tab and a desktop player alike.
/// </para>
/// <para>
/// <b>Event-driven.</b> The session raises when its metadata, its playback state or its timeline
/// changes, and the tile redraws on that. The one thing on a clock is the elapsed-time readout,
/// which is arithmetic against the anchor the session gave — not a question asked of it.
/// </para>
/// <para>
/// The title, the artist and the album come from whichever app is playing. They are somebody
/// else's strings: normalised to one line by the display rule, drawn, and nothing else.
/// </para>
/// </summary>
internal sealed partial class MusicView : Grid, IWidgetView
{
    private const string Scope = "music";

    /// <summary>Below this there is no room for art beside the title, so the art goes.</summary>
    private const double ArtMinimumWidth = 240;

    /// <summary>Below this the elapsed/total labels go and the bar carries the position alone.</summary>
    private const double TimeLabelsMinimumWidth = 220;

    /// <summary>Below this there is no room for a transport row under the title.</summary>
    private const double TransportMinimumHeight = 140;

    /// <summary>The art's smallest useful size, and what it grows from.</summary>
    private const double ArtMinimum = 48;

    /// <summary>And its largest: past this it stops being a thumbnail and starts being the widget.</summary>
    private const double ArtMaximum = 160;

    /// <summary>The share of the tile's height the art takes between those two bounds.</summary>
    private const double ArtShare = 0.5;

    /// <summary>From this height a tile too narrow for art beside the title stacks instead. [S-650]</summary>
    private const double PortraitMinimumHeight = 300;

    /// <summary>The share of a portrait tile's height the art on top may take.</summary>
    private const double PortraitArtShare = 0.4;

    /// <summary>Narrower than this, the title and artist cannot be read beside the transport.</summary>
    private const double LinesMinimumWidth = 120;

    /// <summary>A transport row under the lines: a button's height and the gap above it.</summary>
    private const double TransportRowHeight = Fluent.RowHeightCompact + Fluent.Space2;

    /// <summary>What the art leaves of the tile's height when the transport sits under it.</summary>
    private const double TransportBelowReserve = 120;

    /// <summary>
    /// Three icon buttons and the gaps between them, until the transport has been measured once.
    /// Its real width is used from then on.
    /// </summary>
    private const double TransportEstimate = 112;

    // Segoe Fluent Icons code points this view needs beyond the shared table.
    private static readonly string PreviousGlyph = From(0xE892);
    private static readonly string NextGlyph = From(0xE893);

    private readonly Border _artHost;
    private readonly Image _art = new();
    private readonly FontIcon _artPlaceholder;

    private readonly TextBlock _track = Fluent.Text(FluentType.BodyStrong);
    private readonly TextBlock _subtitle = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly TextBlock _elapsed = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _total = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly ProgressBar _progress = new();
    private readonly Grid _times = new();

    private readonly Button _previous = new();
    private readonly Button _playPause = new();
    private readonly Button _next = new();
    private readonly FontIcon _playPauseIcon;
    private readonly StackPanel _transport;

    private readonly Grid _body = new();
    private readonly StackPanel _lines = new();
    private readonly StackPanel _empty;

    /// <summary>The arrangement the last layout pass chose, so a pass that changes nothing moves nothing.</summary>
    private MusicArrangement _arrangement = MusicArrangement.Row;
    private readonly TextBlock _emptyMessage = new();

    private Ticker? _ticker;
    private MusicConfig _config = new();
    private MediaSession? _session;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private NowPlaying _now = NowPlaying.Of(MusicState.Loading);
    private CancellationTokenSource? _reader;
    private CancellationTokenSource? _artwork;
    private string _artKey = string.Empty;

    /// <summary>
    /// Which read is current. A snapshot carrying an older number belongs to a session the widget
    /// has moved on from, and drawing it would put the previous track back on screen.
    /// </summary>
    private int _generation;

    private TypeStep _trackStep = FluentType.BodyStrong;
    private TypeStep _subtitleStep = FluentType.Body;
    private bool _detached;

    public MusicView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var definition = WidgetCatalog.Definition(WidgetKind.Music);

        _artPlaceholder = BuildArtPlaceholder(definition.Glyph);
        _artHost = BuildArt(_artPlaceholder, _art);
        BuildTransport(out _transport, out _playPauseIcon);

        _track.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;

        var lines = _lines;
        lines.Spacing = Fluent.Space1;
        lines.VerticalAlignment = VerticalAlignment.Center;
        lines.Children.Add(_track);
        lines.Children.Add(_subtitle);

        // Rows for the two arrangements that stack ([S-650]); in the side-by-side one everything
        // sits in the first and the others are empty, so they take no height.
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Art on the left, the two lines taking the slack, the transport on the right at its
        // natural width. A Grid rather than a horizontal StackPanel: a StackPanel measures with
        // unbounded width, so a long title never learns its room and runs off the card instead of
        // ellipsising.
        _body.ColumnSpacing = Fluent.Space3;
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _body.Margin = WidgetLayout.SideInset;
        _body.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_artHost, 0);
        SetColumn(lines, 1);
        SetColumn(_transport, 2);
        _body.Children.Add(_artHost);
        _body.Children.Add(lines);
        _body.Children.Add(_transport);

        BuildFooter();

        _emptyMessage.Text = MusicRules.StatusMessage(MusicState.Loading);
        _empty = Fluent.EmptyState(definition.Glyph, _emptyMessage, out _);
        _empty.Visibility = Visibility.Collapsed;

        Children.Add(_body);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The album art: a square with Fluent's control radius, a themed fill behind it, and the
    /// widget's own glyph in the middle while there is no picture — so the tile has the same shape
    /// whether or not the player offered one.
    /// </summary>
    private static FontIcon BuildArtPlaceholder(string glyph)
    {
        var placeholder = Fluent.Icon(glyph, "TextFillColorTertiaryBrush", Fluent.Space6);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        return placeholder;
    }

    private static Border BuildArt(FontIcon placeholder, Image art)
    {
        art.Stretch = Stretch.UniformToFill;
        art.Visibility = Visibility.Collapsed;

        // The picture over the placeholder rather than instead of it: swapping the Border's child
        // per track would rebuild the visual on every skip, and the two never show at once anyway.
        var stack = new Grid { Children = { placeholder, art } };

        return new Border
        {
            Child = stack,
            Background = FluentLayout.Themed("ControlFillColorSecondaryBrush", Colors.DimGray),

            // A Border clips its child to its corner radius, which is what keeps a cover that is
            // not square from painting over the rounded corners while it fills the tile.
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            VerticalAlignment = VerticalAlignment.Center,
            Width = ArtMinimum,
            Height = ArtMinimum,
        };
    }

    /// <summary>
    /// Previous, play/pause and next as stock WinUI buttons with Segoe glyphs — the same controls
    /// the media flyout uses, with the same focus, hover and pressed states the rest of Windows
    /// has, rather than three hand-drawn shapes that only look like buttons.
    /// </summary>
    private void BuildTransport(out StackPanel panel, out FontIcon playPauseIcon)
    {
        playPauseIcon = Fluent.Icon(Glyphs.Play, "TextFillColorPrimaryBrush");

        Style(_previous, Fluent.Icon(PreviousGlyph, "TextFillColorPrimaryBrush"), "Previous");
        Style(_playPause, playPauseIcon, "Play or pause");
        Style(_next, Fluent.Icon(NextGlyph, "TextFillColorPrimaryBrush"), "Next");

        _previous.Click += OnPrevious;
        _playPause.Click += OnPlayPause;
        _next.Click += OnNext;

        panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _previous, _playPause, _next },
        };

        static void Style(Button button, FontIcon icon, string tooltip)
        {
            button.Content = icon;
            button.Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1);
            button.CornerRadius = new CornerRadius(Fluent.ControlRadius);
            button.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(button, tooltip);
        }
    }

    /// <summary>
    /// The progress bar and its labels.
    /// <para>
    /// The bar is full-bleed: no side inset, flush to the bottom of the card, which is where every
    /// chart in this app sits. A progress bar floating inside a margin reads as a picture of one.
    /// </para>
    /// </summary>
    private void BuildFooter()
    {
        _progress.Minimum = 0;
        _progress.Maximum = 1;
        _progress.HorizontalAlignment = HorizontalAlignment.Stretch;
        _progress.VerticalAlignment = VerticalAlignment.Bottom;
        _progress.IsHitTestVisible = false;

        _total.TextAlignment = TextAlignment.Right;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_elapsed, FontNumeralAlignment.Tabular);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_total, FontNumeralAlignment.Tabular);

        _times.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _times.Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2);

        SetColumn(_elapsed, 0);
        SetColumn(_total, 1);
        _times.Children.Add(_elapsed);
        _times.Children.Add(_total);

        var footer = new StackPanel { Spacing = 0, Children = { _times, _progress } };
        SetRow(footer, 1);
        Children.Add(footer);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;

        _session ??= new MediaSession(OnSessionChanged);

        // Opening the manager is an async WinRT call; the first snapshot lands through the same
        // change notification as every later one, so there is one path into Present and not two.
        _ = StartSessionAsync();

        ApplyConfig(config);
    }

    private async Task StartSessionAsync()
    {
        var session = _session;

        if (session is null)
        {
            return;
        }

        try
        {
            // No token: the session's own Dispose is what stops it, and it is called from Detach.
            // A token here would be a second way to say the same thing.
            await session.StartAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not start listening to the media session", ex);
        }
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as MusicConfig ?? new MusicConfig();

        // The art can be switched on after a track is already loaded, and the key is what decides
        // whether the picture in hand is still the right one. Clearing it forces a fetch.
        if (!_config.ShowAlbumArt)
        {
            ClearArtwork();
        }

        // A config touch can change what has to be fetched — the art, most obviously — so the
        // snapshot is re-read rather than merely redrawn. Session reads are cheap and rare.
        Reload();
        Present();
    }

    /// <summary>
    /// The position ticker: one second while a track is playing, nothing otherwise.
    /// <para>
    /// Rebuilt only when the cadence actually changed. Dropping and remaking the subscription on
    /// every redraw would tear down the shared timer for every widget on the desktop once a second,
    /// which is exactly the stutter the guard exists to stop.
    /// </para>
    /// </summary>
    private bool Resubscribe()
    {
        // Nothing to advance unless a track is running and somebody can see the number.
        var wanted = _now.State == MusicState.Playing && _config.ShowProgress
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.Zero;

        if (wanted == _subscribedInterval && (_subscription is not null || wanted == TimeSpan.Zero))
        {
            return false;
        }

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = wanted;

        if (wanted == TimeSpan.Zero)
        {
            return false;
        }

        _subscription = _ticker?.Subscribe(wanted, Advance);
        return _subscription is not null;
    }

    /// <summary>Move the elapsed readout on, without asking the session anything.</summary>
    private void Advance()
    {
        if (_detached || !_now.HasTrack)
        {
            return;
        }

        UpdatePosition();
    }

    /// <summary>
    /// The session said something changed. Raised on a WinRT thread, so it hops to the dispatcher
    /// before touching a single control.
    /// </summary>
    private void OnSessionChanged()
    {
        if (_detached)
        {
            return;
        }

        var queue = DispatcherQueue;

        if (queue is null || !queue.TryEnqueue(Reload))
        {
            Log.Debug(Scope, "a session change arrived with no dispatcher to draw it on");
        }
    }

    private void Reload()
    {
        if (_detached || _session is null)
        {
            return;
        }

        _reader?.Cancel();

        var cts = new CancellationTokenSource();
        _reader = cts;
        var generation = ++_generation;

        _ = ReadAsync(generation, cts);
    }

    private async Task ReadAsync(int generation, CancellationTokenSource cts)
    {
        try
        {
            var session = _session;

            if (session is null)
            {
                return;
            }

            var now = await session.ReadAsync(cts.Token).ConfigureAwait(true);

            // A late snapshot of a session the widget has moved on from would put the previous
            // track back on screen.
            if (_detached || generation != _generation)
            {
                return;
            }

            _now = now;
            Present();

            if (_config.ShowAlbumArt && now.HasTrack && now.TrackKey != _artKey)
            {
                await LoadArtworkAsync(now.TrackKey, generation, cts.Token).ConfigureAwait(true);
            }
            else if (!now.HasTrack)
            {
                ClearArtwork();
            }
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer read, which will draw in this one's place.
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not read the media session", ex);
        }
        finally
        {
            if (ReferenceEquals(_reader, cts))
            {
                _reader = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Fetch the album art and put it on screen.
    /// <para>
    /// The bytes come off the session's stream on a background continuation, and the decode is a
    /// <c>BitmapImage</c> over an in-memory stream — which decodes on XAML's own worker rather than
    /// on the dispatcher. Both streams are disposed on every path: this runs once per track, for as
    /// long as the desktop is up.
    /// </para>
    /// </summary>
    private async Task LoadArtworkAsync(string key, int generation, CancellationToken token)
    {
        var session = _session;

        if (session is null)
        {
            return;
        }

        var bytes = await session.ReadArtworkAsync(token).ConfigureAwait(true);

        if (_detached || generation != _generation)
        {
            return;
        }

        if (bytes is null || bytes.Length == 0)
        {
            ClearArtwork();

            // Remembered even when there was none, so a track with no cover is not re-fetched on
            // every timeline push.
            _artKey = key;
            return;
        }

        _artwork?.Cancel();
        var cts = new CancellationTokenSource();
        _artwork = cts;

        InMemoryRandomAccessStream? stream = null;

        try
        {
            stream = new InMemoryRandomAccessStream();

            var writer = new DataWriter(stream.GetOutputStreamAt(0));

            try
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask(cts.Token).ConfigureAwait(true);
                await writer.FlushAsync().AsTask(cts.Token).ConfigureAwait(true);

                // Detached before disposal: a DataWriter closes the stream it was built on, and
                // the decoder is about to read from exactly that stream.
                writer.DetachStream();
            }
            finally
            {
                writer.Dispose();
            }

            stream.Seek(0);

            var image = new BitmapImage();
            await image.SetSourceAsync(stream).AsTask(cts.Token).ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            _art.Source = image;
            _art.Visibility = Visibility.Visible;
            _artPlaceholder.Visibility = Visibility.Collapsed;
            _artKey = key;
        }
        catch (OperationCanceledException)
        {
            // ignore: a newer track's art is already on its way.
        }
        catch (Exception ex)
        {
            // Art that will not decode is a tile with a placeholder, not a broken widget.
            Log.Debug(Scope, $"could not decode the album art: {ex.GetType().Name}");
            ClearArtwork();
        }
        finally
        {
            if (ReferenceEquals(_artwork, cts))
            {
                _artwork = null;
            }

            cts.Dispose();
            stream?.Dispose();
        }
    }

    private void ClearArtwork()
    {
        _art.Source = null;
        _art.Visibility = Visibility.Collapsed;
        _artPlaceholder.Visibility = Visibility.Visible;
        _artKey = string.Empty;
    }

    /// <summary>Put the snapshot in hand on screen, and settle the layout around it.</summary>
    private void Present()
    {
        var track = _now.HasTrack;

        _body.Visibility = track ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = track ? Visibility.Collapsed : Visibility.Visible;

        if (!track)
        {
            _emptyMessage.Text = MusicRules.StatusMessage(_now.State);
            ApplyLayout();
            Resubscribe();
            return;
        }

        _track.Text = _now.Title;
        _subtitle.Text = _now.Subtitle;
        _subtitle.Visibility = _now.Subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        _playPauseIcon.Glyph = _now.State == MusicState.Playing ? Glyphs.Pause : Glyphs.Play;
        ToolTipService.SetToolTip(_playPause, _now.State == MusicState.Playing ? "Pause" : "Play");

        // Disabled rather than hidden: a transport whose buttons come and go moves the two that
        // remain, and the one under the pointer is then a different button than it was.
        _previous.IsEnabled = _now.CanPrevious;
        _playPause.IsEnabled = _now.CanPlayPause;
        _next.IsEnabled = _now.CanNext;

        UpdatePosition();
        ApplyLayout();
        Resubscribe();
    }

    /// <summary>
    /// Advance the bar and the labels from the anchor. Display only: the next snapshot from the
    /// session overwrites it, so the two can never drift apart permanently.
    /// </summary>
    private void UpdatePosition()
    {
        var position = MusicRules.Interpolate(
            _now.Timeline,
            _now.AnchoredAt,
            DateTimeOffset.UtcNow,
            _now.State == MusicState.Playing);

        _progress.Value = MusicRules.Progress(position, _now.Timeline.Duration);
        _elapsed.Text = MusicRules.Clock(position);

        // A stream with no end has no total to show, and "0:00" in that slot reads as a track that
        // will not start.
        _total.Text = _now.Timeline.Duration > TimeSpan.Zero ? MusicRules.Clock(_now.Timeline.Duration) : string.Empty;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout();

    /// <summary>
    /// What fits, and at what step.
    /// <para>
    /// The parts drop in the order they stop being worth their space: first the art, then the time
    /// labels, then the transport. The title never drops — it is what the tile is for — and it
    /// steps the ramp rather than being scaled to the tile.
    /// </para>
    /// </summary>
    private void ApplyLayout()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var showTransport = _config.ShowTransport
            && _now.HasTrack
            && (height <= 0 || height >= TransportMinimumHeight);

        // [S-650] A tall, narrow tile stacks: the art on top, the lines under it, the transport
        // under those — the shape every media flyout has. Side by side, a 200px column gave the
        // title the 26px left between the transport and the edge ("LA…").
        var portrait = _config.ShowAlbumArt && height >= PortraitMinimumHeight && width > 0 && width < ArtMinimumWidth;

        var showArt = _config.ShowAlbumArt && (portrait || width <= 0 || width >= ArtMinimumWidth);
        _artHost.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;

        var side = ArtMinimum;
        if (showArt && height > 0)
        {
            // A square between a readable floor and a ceiling, rather than whatever is left over.
            // Left over is how a thumbnail ends up taller than the title it belongs to.
            side = portrait
                ? Math.Clamp(Math.Min(width - (Fluent.Space4 * 2), height * PortraitArtShare), ArtMinimum, ArtMaximum)
                : Math.Clamp(height * ArtShare, ArtMinimum, ArtMaximum);
        }

        // [S-650] And when the transport beside the lines would leave them too little to read, it
        // moves under them; the art stays beside, where it is the same height as the two lines.
        var arrangement = MusicArrangement.Row;
        if (portrait)
        {
            arrangement = MusicArrangement.Portrait;
        }
        else if (showTransport && width > 0)
        {
            var lines = width - (Fluent.Space4 * 2)
                - (showArt ? side + Fluent.Space3 : 0)
                - TransportWidth() - Fluent.Space3;

            if (lines < LinesMinimumWidth)
            {
                arrangement = MusicArrangement.TransportBelow;

                // The transport row now shares the height with the art, so the art gives some back.
                side = Math.Clamp(Math.Min(side, height - TransportBelowReserve), ArtMinimum, ArtMaximum);
            }
        }

        if (showArt)
        {
            _artHost.Width = side;
            _artHost.Height = side;
        }

        PlaceParts(arrangement);

        _transport.Visibility = showTransport ? Visibility.Visible : Visibility.Collapsed;

        var showProgress = _config.ShowProgress && _now.HasTrack;
        _progress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;

        _times.Visibility = showProgress && (width <= 0 || width >= TimeLabelsMinimumWidth)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyTypeScale();
    }

    /// <summary>
    /// Step the title and the line under it. Two steps apart at most: a subtitle the same size as
    /// the title reads as two headlines, and one three steps down reads as a footnote.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth - (Fluent.Space4 * 2);
        var art = _artHost.Visibility == Visibility.Visible;
        var transport = _transport.Visibility == Visibility.Visible;

        // Only what shares the lines' row takes width from them.
        if (art && _arrangement != MusicArrangement.Portrait)
        {
            width -= _artHost.Width + Fluent.Space3;
        }

        if (transport && _arrangement == MusicArrangement.Row)
        {
            width -= TransportWidth() + Fluent.Space3;
        }

        // A title is long and is allowed to ellipsise, so the step is chosen against the height it
        // has rather than against the string in it: fitting to the live title would change the
        // type size on every track.
        var room = ActualHeight
            - Fluent.Space4
            - (_progress.Visibility == Visibility.Visible ? Fluent.Space4 : 0)
            - (_times.Visibility == Visibility.Visible ? FluentType.Caption.LineHeight + Fluent.Space2 : 0);

        // Stacked parts take their height out of the lines' share.
        if (_arrangement == MusicArrangement.Portrait)
        {
            room -= (art ? _artHost.Height + Fluent.Space3 : 0) + (transport ? TransportRowHeight : 0);
        }
        else if (_arrangement == MusicArrangement.TransportBelow && transport)
        {
            room -= TransportRowHeight;
        }

        var step = FluentType.Fit(
            Math.Max(1, width),
            Math.Max(1, room / 2),
            characters: 16,
            FluentType.Body,
            FluentType.Title);

        var track = FluentType.Strong(step);

        // One ramp step below the title, and never above Body: the subtitle is metadata.
        var subtitleStep = step.FontSize >= FluentType.Subtitle.FontSize
            ? FluentType.BodyLarge
            : FluentType.Body;

        if (track != _trackStep)
        {
            _trackStep = track;
            _track.Apply(track);
        }

        if (subtitleStep != _subtitleStep)
        {
            _subtitleStep = subtitleStep;
            _subtitle.Apply(subtitleStep);
        }
    }

    private double TransportWidth() => _transport.ActualWidth > 0 ? _transport.ActualWidth : TransportEstimate;

    /// <summary>Put the art, the lines and the transport where an arrangement wants them. [S-650]</summary>
    private void PlaceParts(MusicArrangement arrangement)
    {
        _arrangement = arrangement;

        var portrait = arrangement == MusicArrangement.Portrait;
        var below = arrangement != MusicArrangement.Row;

        SetRow(_artHost, 0);
        SetColumn(_artHost, 0);
        SetColumnSpan(_artHost, portrait ? 3 : 1);

        SetRow(_lines, portrait ? 1 : 0);
        SetColumn(_lines, portrait ? 0 : 1);
        SetColumnSpan(_lines, portrait ? 3 : 1);
        _lines.Margin = portrait ? new Thickness(0, Fluent.Space3, 0, 0) : new Thickness(0);

        SetRow(_transport, portrait ? 2 : below ? 1 : 0);
        SetColumn(_transport, below ? 0 : 2);
        SetColumnSpan(_transport, below ? 3 : 1);
        _transport.HorizontalAlignment = below ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _transport.Margin = below ? new Thickness(0, Fluent.Space2, 0, 0) : new Thickness(0);

        // Stacked lines are centred under the art, the way a media flyout sets them.
        var alignment = portrait ? TextAlignment.Center : TextAlignment.Left;
        _track.TextAlignment = alignment;
        _subtitle.TextAlignment = alignment;
    }

    private void OnPlayPause(object sender, RoutedEventArgs args) => Press(MediaTransport.PlayPause);

    private void OnNext(object sender, RoutedEventArgs args) => Press(MediaTransport.Next);

    private void OnPrevious(object sender, RoutedEventArgs args) => Press(MediaTransport.Previous);

    /// <summary>
    /// Ask the session to do something, and let its own change notification draw the result.
    /// <para>
    /// Deliberately not optimistic: the tile does not flip the glyph and hope. If the player
    /// refuses — a stream that cannot be paused, an app that has just exited — the button goes back
    /// to saying what the machine is actually doing, because the session never reports a change.
    /// </para>
    /// </summary>
    private void Press(MediaTransport command)
    {
        if (_detached || _session is null)
        {
            return;
        }

        _ = PressAsync(command);
    }

    private async Task PressAsync(MediaTransport command)
    {
        var session = _session;

        if (session is null)
        {
            return;
        }

        try
        {
            await session.InvokeAsync(command, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, $"the media session refused {command}", ex);
        }
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        _previous.Click -= OnPrevious;
        _playPause.Click -= OnPlayPause;
        _next.Click -= OnNext;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        // Cancel but do not dispose: each in-flight task owns its source and frees it on the way
        // out, so there is exactly one owner however many times this is called.
        _reader?.Cancel();
        _reader = null;
        _artwork?.Cancel();
        _artwork = null;

        // Unhooks the manager and the current session. Safe to call twice.
        _session?.Dispose();
        _session = null;

        _art.Source = null;
        _ticker = null;
    }

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);
}

/// <summary>How the now-playing tile sets out its parts at a given size. [S-650]</summary>
internal enum MusicArrangement
{
    /// <summary>Art, lines and transport side by side.</summary>
    Row,

    /// <summary>Art and lines side by side, the transport centred under them.</summary>
    TransportBelow,

    /// <summary>Art on top, the lines under it, the transport under those.</summary>
    Portrait,
}
