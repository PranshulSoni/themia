using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Pictures from a folder on this PC. [S-300]
/// <para>
/// Built on the same shape as the folder widget, because it has the same hazards: the path is
/// validated in Core before anything opens a handle, the walk happens on the thread pool because a
/// disconnected share can sit for the better part of a minute, a reply that arrives after the folder
/// changed is discarded rather than drawn, and every outcome with no picture in it says <i>which</i>
/// outcome it is.
/// </para>
/// <para>
/// It also has one the folder widget does not: decoding. A folder of phone photographs is tens of
/// megapixels each, and drawing one into a 300px tile at full resolution costs a second of UI thread
/// and ninety megabytes of surface. So nothing is ever decoded at more than the size it is drawn
/// at, and <see cref="ImageLoader"/> bounds both how many are held and how many are decoding at
/// once. [S-302]
/// </para>
/// </summary>
internal sealed partial class GalleryView : Grid, IWidgetView
{
    private const string Scope = "gallery-view";

    /// <summary>
    /// Roughly how wide a grid tile wants to be. The column count is this divided into the card, so
    /// a wider widget shows more pictures at the same size rather than the same pictures bigger —
    /// the list rule from <see cref="Fluent.RowHeight"/>, applied to tiles.
    /// </summary>
    private const double TargetTile = 160;

    /// <summary>
    /// Past this no more tiles are added however large the card is. Twenty-four postage stamps is
    /// already past the point where a picture is a picture.
    /// </summary>
    private const int MaxTiles = 24;

    private readonly TextBlock _title = new() { Text = "Gallery" };
    private readonly TextBlock _counter = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _header;
    private readonly Grid _stage = new();
    private readonly Image _slide = new();
    private readonly Grid _tiles = new();
    private readonly Border _messageHost;
    private readonly TextBlock _message;
    private readonly FontIcon _messageIcon;
    private readonly ImageLoader _loader = new();
    private readonly DispatcherQueueTimer? _slideTimer;

    private Ticker? _ticker;
    private GalleryConfig _config = new();
    private FolderPathResult _resolved;
    private GalleryScan _scan = GalleryScan.Of(GalleryStatus.Loading);
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;

    /// <summary>Cancels the decodes belonging to the layout currently on screen.</summary>
    private CancellationTokenSource? _drawing;

    /// <summary>
    /// Which scan is current. A reply carrying an older number is a folder the widget has stopped
    /// pointing at, and a share that took thirty seconds to answer must not overwrite the folder the
    /// user has since chosen.
    /// </summary>
    private int _generation;

    private int _columns;
    private int _rows;
    private int _index;

    /// <summary>What the single picture currently shows, and at what width it was decoded.</summary>
    private string? _slidePath;

    private double _slideWidth;
    private bool _everScanned;
    private bool _detached;

    public GalleryView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _counter.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_counter, FontNumeralAlignment.Tabular);

        _header = Fluent.Header(Glyphs.Gallery, _title, _counter);
        _header.Margin = Fluent.HeaderMargin;
        SetRow(_header, 0);

        _slide.HorizontalAlignment = HorizontalAlignment.Stretch;
        _slide.VerticalAlignment = VerticalAlignment.Stretch;

        _tiles.RowSpacing = Fluent.Space1;
        _tiles.ColumnSpacing = Fluent.Space1;

        _stage.Children.Add(_slide);
        _stage.Children.Add(_tiles);
        SetRow(_stage, 1);

        // [S-301] Every outcome with no picture in it names itself. A photo frame showing a blank
        // card is indistinguishable from a broken one, and "no folder chosen", "that folder is
        // gone" and "Windows will not let me read it" each need a different thing from the reader.
        _message = Fluent.Text(FluentType.Body);
        var empty = Fluent.EmptyState(Glyphs.Gallery, _message, out _messageIcon);

        _messageHost = new Border
        {
            Child = empty,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        SetRow(_messageHost, 1);

        Children.Add(_header);
        Children.Add(_stage);
        Children.Add(_messageHost);

        // The slideshow owns its own timer rather than counting shared one-second ticks. The shared
        // ticker only knows seconds and minutes, and waking the whole desktop once a second to find
        // out whether ten of them have passed is the cost of a clock for the benefit of a picture.
        _slideTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_slideTimer is null)
        {
            // A design-time or test host. The pictures still show; only the advance is lost.
            Log.Warn(Scope, "no dispatcher queue on this thread; the slideshow will not advance");
        }
        else
        {
            _slideTimer.IsRepeating = true;
            _slideTimer.Tick += OnSlideTick;
        }

        SizeChanged += OnSizeChanged;

        // The stage's own size, as well as this view's. The star row's height is only settled once
        // the children have been arranged, so reading it during the parent's size-changed can still
        // see the previous pass — and a grid laid out for the old card would stay there until
        // something else moved.
        _stage.SizeChanged += OnStageResized;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as GalleryConfig ?? new GalleryConfig();
        var previous = _config;
        var previouslyResolved = _resolved;
        _config = next;

        // [S-300] The trust boundary, before anything opens a handle. An empty path is not an error
        // here and not a fallback either: it is "no folder chosen", which is its own state.
        _resolved = next.Path.Trim().Length == 0
            ? new FolderPathResult(FolderPathProblem.Empty, string.Empty, false)
            : FolderPath.Validate(next.Path);

        _title.Text = _resolved.IsValid ? FolderPath.DisplayName(_resolved.Path) : "Gallery";

        // Only the fields that change what comes back off the disk are worth a rescan. Dragging the
        // count slider must not fire a directory walk per frame.
        var pathChanged = _resolved.Path != previouslyResolved.Path
            || _resolved.Problem != previouslyResolved.Problem;

        var needsScan = !_everScanned
            || pathChanged
            || previous.Recursive != next.Recursive
            || previous.Shuffle != next.Shuffle
            || previous.EffectiveMaxImages != next.EffectiveMaxImages;

        if (pathChanged)
        {
            // Pictures from the folder the widget has left are dead weight, and holding them would
            // only evict the ones it is about to want.
            _loader.Clear();
            _index = 0;
        }

        ApplySlideCadence();

        // Subscribing fires a tick of its own, and that tick rescans with the config already in
        // place above; rescanning here as well would walk the folder twice.
        if (Resubscribe())
        {
            return;
        }

        if (needsScan)
        {
            Rescan();
        }
        else
        {
            Render();
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed, and say whether it
    /// did — a new subscription ticks immediately, so the caller has nothing left to do.
    /// </summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Rescan);
        return _subscription is not null;
    }

    private void ApplySlideCadence()
    {
        if (_slideTimer is null)
        {
            return;
        }

        var running = _config.Layout == GalleryLayout.Slideshow && _scan.Paths.Count > 1;

        if (!running)
        {
            // Nothing to advance to means nothing to wake for. A one-picture folder is a picture
            // frame, not a slideshow.
            _slideTimer.Stop();
            return;
        }

        if (_slideTimer.Interval != _config.SlideInterval)
        {
            _slideTimer.Interval = _config.SlideInterval;
        }

        _slideTimer.Start();
    }

    private void OnSlideTick(DispatcherQueueTimer sender, object args)
    {
        if (_detached || _scan.Paths.Count == 0)
        {
            return;
        }

        _index = (_index + 1) % _scan.Paths.Count;
        Render();
    }

    /// <summary>
    /// Start a scan. The walk itself happens on the thread pool: a mapped drive whose server has
    /// gone away can leave a single directory call sitting for most of a minute, and on the UI
    /// thread that is every widget on the desktop frozen with it.
    /// </summary>
    private void Rescan()
    {
        if (_detached)
        {
            return;
        }

        _everScanned = true;

        if (!_resolved.IsValid)
        {
            _scan = GalleryScan.Of(_resolved.Problem == FolderPathProblem.Empty
                ? GalleryStatus.NoFolder
                : GalleryStatus.InvalidPath);

            Render();
            return;
        }

        var request = new GalleryRequest(
            _resolved.Path,
            _config.Recursive,
            _config.Shuffle,

            // Re-rolled per scan, so a folder larger than the cap eventually shows all of it rather
            // than the same two dozen pictures for ever.
            Environment.TickCount,
            _config.EffectiveMaxImages);

        _inFlight?.Cancel();

        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        // Say something immediately, but only when there is nothing on screen to keep: a backstop
        // rescan of the same folder should not blank the picture it is about to redraw.
        if (_scan.Paths.Count == 0)
        {
            _scan = GalleryScan.Of(GalleryStatus.Loading);
            Render();
        }

        _ = ScanAsync(request, generation, cts);
    }

    private async Task ScanAsync(GalleryRequest request, int generation, CancellationTokenSource cts)
    {
        try
        {
            var scan = await Task.Run(() => ImageGallery.Read(request, cts.Token), cts.Token)
                .ConfigureAwait(true);

            // A late reply from a folder the widget has since left. Discarded rather than drawn.
            if (_detached || generation != _generation)
            {
                return;
            }

            var sameSet = scan.Status == _scan.Status && scan.Paths.SequenceEqual(_scan.Paths, StringComparer.Ordinal);

            _scan = scan;

            if (_index >= scan.Paths.Count)
            {
                _index = 0;
            }

            ApplySlideCadence();

            // A backstop rescan that found exactly what the last one found must not redraw: it
            // would restart the slideshow from the picture currently shown and re-decode the grid.
            if (!sameSet)
            {
                Render();
            }
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer scan, which will draw in this one's place.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
            }

            cts.Dispose();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Under Fluent's floor there is no room for a header above the picture, so it sheds and the
        // picture gets the card.
        _header.Visibility = e.NewSize.Height >= Fluent.HeaderHidesBelow
            ? Visibility.Visible
            : Visibility.Collapsed;

        Render();
    }

    private void OnStageResized(object sender, SizeChangedEventArgs e) => Render();

    /// <summary>
    /// Draw what is in hand. Cheap when nothing about the shape changed: the grid is only rebuilt
    /// when the number of tiles it holds actually differs, because rebuilding it throws away every
    /// decoded bitmap on screen and asks for them all again.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        if (_scan.Status != GalleryStatus.Ok || _scan.Paths.Count == 0)
        {
            ShowMessage(GalleryFormatting.StatusMessage(_scan.Status, _resolved.Problem));
            return;
        }

        _messageHost.Visibility = Visibility.Collapsed;
        _stage.Visibility = Visibility.Visible;

        var stretch = _config.Fit == GalleryFit.Fill ? Stretch.UniformToFill : Stretch.Uniform;

        if (_config.Layout == GalleryLayout.Slideshow)
        {
            DrawSlide(stretch);
        }
        else
        {
            DrawTiles(stretch);
        }
    }

    private void ShowMessage(string text)
    {
        StopDrawing();

        _tiles.Children.Clear();
        _tiles.RowDefinitions.Clear();
        _tiles.ColumnDefinitions.Clear();
        _columns = 0;
        _rows = 0;
        _slide.Source = null;

        _message.Text = text;
        _counter.Text = string.Empty;
        _messageHost.Visibility = Visibility.Visible;
        _stage.Visibility = Visibility.Collapsed;

        // The glyph is dropped on a card too short to carry it: a tile showing only an icon says
        // less than one showing only the sentence.
        _messageIcon.Visibility = ActualHeight >= Fluent.HeaderHidesBelow
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void DrawSlide(Stretch stretch)
    {
        _tiles.Visibility = Visibility.Collapsed;
        _slide.Visibility = Visibility.Visible;
        _slide.Stretch = stretch;

        var path = _scan.Paths[Math.Clamp(_index, 0, _scan.Paths.Count - 1)];
        var width = _stage.ActualWidth;

        _counter.Text = _scan.Paths.Count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{_index + 1}/{_scan.Paths.Count}")
            : string.Empty;

        AutomationProperties.SetName(_slide, System.IO.Path.GetFileName(path));
        ToolTipService.SetToolTip(_slide, path);

        // Nothing to do when the same picture is already up at the same decode size. Without this,
        // a drag-resize cancels and restarts the decode on every frame and the card stays empty for
        // as long as the pointer is held — the fit, the counter and the tooltip above are the whole
        // of what a resize can change.
        if (_slide.Source is not null
            && string.Equals(_slidePath, path, StringComparison.Ordinal)
            && Math.Abs(width - _slideWidth) < TargetTile)
        {
            return;
        }

        StopDrawing();
        var cts = new CancellationTokenSource();
        _drawing = cts;
        _slidePath = path;
        _slideWidth = width;

        _ = ShowAsync(_slide, path, width, cts);
    }

    private void DrawTiles(Stretch stretch)
    {
        _slide.Visibility = Visibility.Collapsed;
        _slide.Source = null;
        _tiles.Visibility = Visibility.Visible;
        _counter.Text = string.Empty;

        var width = _stage.ActualWidth;
        var height = _stage.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return; // Not measured yet. The size-changed handler will bring us straight back.
        }

        var columns = Math.Clamp((int)Math.Round(width / TargetTile), 1, MaxTiles);
        var rows = Math.Clamp((int)Math.Round(height / TargetTile), 1, MaxTiles);
        var wanted = Math.Min(_scan.Paths.Count, Math.Min(columns * rows, MaxTiles));

        // Keep the last row from being mostly empty: with seven pictures in a four-wide card, three
        // rows of a grid built for twelve leaves five holes.
        rows = (int)Math.Ceiling((double)wanted / columns);

        if (columns == _columns && rows == _rows && _tiles.Children.Count == wanted)
        {
            // Same shape, same count: only the fit can have changed, so re-letter rather than
            // rebuild. Rebuilding would throw away every decoded bitmap on screen.
            foreach (var child in _tiles.Children)
            {
                if (child is Border { Child: Image image })
                {
                    image.Stretch = stretch;
                }
            }

            return;
        }

        StopDrawing();
        var cts = new CancellationTokenSource();
        _drawing = cts;

        _tiles.Children.Clear();
        _tiles.RowDefinitions.Clear();
        _tiles.ColumnDefinitions.Clear();
        _columns = columns;
        _rows = rows;

        for (var i = 0; i < columns; i++)
        {
            _tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < rows; i++)
        {
            _tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        var tileWidth = width / columns;

        for (var i = 0; i < wanted; i++)
        {
            var path = _scan.Paths[i];
            var image = new Image { Stretch = stretch };

            var frame = new Border
            {
                Child = image,
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                Background = FluentLayout.Themed("ControlAltFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            };

            AutomationProperties.SetName(frame, System.IO.Path.GetFileName(path));
            ToolTipService.SetToolTip(frame, path);

            SetColumn(frame, i % columns);
            SetRow(frame, i / columns);
            _tiles.Children.Add(frame);

            _ = ShowAsync(image, path, tileWidth, cts);
        }
    }

    /// <summary>
    /// Decode one picture and put it in its frame, unless the layout it belonged to has been
    /// replaced in the meantime.
    /// </summary>
    private async Task ShowAsync(Image target, string path, double logicalWidth, CancellationTokenSource cts)
    {
        try
        {
            // The decode is in physical pixels, so the tile's logical width is multiplied by the
            // monitor's scale. Getting this wrong is a picture that is crisp on one display and
            // soft on the one beside it.
            var scale = XamlRoot?.RasterizationScale ?? 1;
            var bitmap = await _loader.LoadAsync(path, logicalWidth * scale, cts.Token).ConfigureAwait(true);

            // A picture that finished decoding after the folder changed, the layout was rebuilt or
            // the widget was torn down. Discarded rather than drawn into a frame that has moved on.
            if (bitmap is null || _detached || cts.IsCancellationRequested || !ReferenceEquals(_drawing, cts))
            {
                return;
            }

            target.Source = bitmap;
        }
        catch (OperationCanceledException)
        {
            // ignore: the layout this decode belonged to has been replaced.
        }
    }

    /// <summary>
    /// Cancel the decodes belonging to the layout being replaced.
    /// <para>
    /// Cancelled but <b>not</b> disposed. One source covers every tile of a layout, so there is no
    /// single load that owns it, and a disposed source's <c>Token</c> throws the moment the next
    /// waiter touches it — which is a decode already queued behind the semaphore. It holds no timer
    /// and no unmanaged handle, and every registration on it comes off as its awaits unwind, so
    /// letting it go is the correct end of its life.
    /// </para>
    /// </summary>
    private void StopDrawing()
    {
        var drawing = _drawing;
        _drawing = null;
        drawing?.Cancel();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        SizeChanged -= OnSizeChanged;
        _stage.SizeChanged -= OnStageResized;

        if (_slideTimer is not null)
        {
            _slideTimer.Stop();
            _slideTimer.Tick -= OnSlideTick;
        }

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        // Cancel but do not dispose: the in-flight scan owns its source and frees it on the way out.
        _inFlight?.Cancel();
        _inFlight = null;

        StopDrawing();

        _slide.Source = null;
        _tiles.Children.Clear();
        _loader.Clear();
        _ticker = null;
    }
}
