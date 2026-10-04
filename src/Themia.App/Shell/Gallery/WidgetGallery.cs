#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// The debug-only widget gallery: one widget, drawn at every size in <see cref="Sizes"/> side by
/// side, so a layout that breaks at one size is seen next to the ones where it holds.
/// <para>
/// The sizes are the same for every widget on purpose. A widget that has to be judged at the sizes
/// its author happened to pick is a widget whose failures are always somewhere else.
/// </para>
/// <para>
/// Two ways in, one drawing. A person opens it from the edit toolbar's overflow menu and switches
/// widgets with the picker at the top. A script runs <c>Themia.exe --gallery-png &lt;Kind|all&gt; &lt;dir&gt;</c>,
/// which renders the same page off-screen to a PNG per widget and exits — no input, no state file,
/// no single-instance hand-off, so several can run side by side without touching the user's desktop.
/// </para>
/// </summary>
internal sealed class WidgetGallery : IDisposable
{
    private const string Scope = "gallery";

    /// <summary>Space between frames and around the page, in DIPs.</summary>
    private const double Gap = 24;

    /// <summary>Height reserved above each frame for its size label.</summary>
    private const double LabelHeight = 22;

    /// <summary>
    /// The sizes every widget is drawn at, in DIPs of the painted card. Chosen to cover what people
    /// actually make: a tile barely bigger than an icon, the default-ish small and medium, a wide
    /// strip, a tall column and a large panel.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, double Width, double Height)> Sizes =
    [
        ("XS", 120, 120),
        ("S", 200, 150),
        ("M", 320, 240),
        ("Wide", 480, 160),
        ("Tall", 200, 400),
        ("L", 560, 420),
    ];

    /// <summary>Which row each size sits on: four across the top, the two big ones underneath.</summary>
    private static readonly int[] RowOf = [0, 0, 0, 0, 1, 1];

    private readonly Window _window;
    private readonly Ticker _ticker;
    private readonly Canvas _page = new();

    /// <summary>
    /// What is rendered to PNG: the page on its own backdrop. Rendering the bare canvas gave a
    /// transparent image with the white size labels invisible on it, cropped to the cards' bounds.
    /// </summary>
    private readonly Border _frame;
    private readonly ComboBox _picker = new() { MinWidth = 220 };
    private readonly GalleryCardHost _host;
    private readonly List<WidgetCard> _cards = [];
    private bool _disposed;

    private WidgetGallery(DispatcherQueue dispatcher, bool interactive)
    {
        _ticker = new Ticker(dispatcher);
        _host = new GalleryCardHost(_ticker);

        foreach (var definition in WidgetCatalog.All)
        {
            _picker.Items.Add(new ComboBoxItem { Content = definition.Label, Tag = definition.Kind });
        }

        _picker.SelectionChanged += (_, _) =>
        {
            if (_picker.SelectedItem is ComboBoxItem { Tag: WidgetKind kind })
            {
                Show(kind);
            }
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space3,
            Padding = new Thickness(Gap, Fluent.Space3, Gap, Fluent.Space3),
            Visibility = interactive ? Visibility.Visible : Visibility.Collapsed,
            Children = { new TextBlock { Text = "Widget", VerticalAlignment = VerticalAlignment.Center }, _picker },
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },

            RequestedTheme = ElementTheme.Dark,
        };

        _frame = new Border
        {
            Child = _page,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,

            // A desktop-ish backdrop rather than the window's own surface, so a translucent card
            // reads the way it will over a wallpaper rather than over a flat panel.
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x10, 0x16, 0x24), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x24, 0x1a, 0x30), Offset = 1 },
                },
            },
        };

        var scroll = new ScrollViewer
        {
            Content = _frame,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Grid.SetRow(bar, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(bar);
        root.Children.Add(scroll);

        _window = new Window { Title = "Themia widget gallery", Content = root };
        _window.Closed += (_, _) => Dispose();

        var (width, height) = PageSize();
        var scale = 1.5; // sized for the common 150% desk; the window is resizable anyway
        _window.AppWindow.Resize(new SizeInt32((int)((width + 40) * scale), (int)((height + 110) * scale)));
    }

    /// <summary>Open the gallery for a person to look at, starting on one widget.</summary>
    public static void Open(WidgetKind start)
    {
        if (DispatcherQueue.GetForCurrentThread() is not { } dispatcher)
        {
            Log.Error(Scope, "no dispatcher on this thread; cannot open the gallery");
            return;
        }

        var gallery = new WidgetGallery(dispatcher, interactive: true);
        gallery._window.Activate();
        gallery.Select(start);
    }

    /// <summary>
    /// Handle <c>--gallery-png &lt;Kind|all&gt; &lt;dir&gt; [waitMs]</c>. Returns false when the arguments are
    /// not for the gallery, so the caller starts the app normally.
    /// </summary>
    public static bool TryRunFromCommandLine(Application app, string[] args)
    {
        var at = Array.IndexOf(args, "--gallery-png");
        if (at < 0)
        {
            var open = Array.IndexOf(args, "--gallery");
            if (open < 0)
            {
                return false;
            }

            Open(open + 1 < args.Length && Enum.TryParse<WidgetKind>(args[open + 1], true, out var first) ? first : WidgetKind.Clock);
            return true;
        }

        if (at + 2 >= args.Length)
        {
            Log.Error(Scope, "usage: --gallery-png <Kind|all> <outDir> [waitMs]");
            app.Exit();
            return true;
        }

        var which = args[at + 1];
        var outDir = args[at + 2];
        var wait = at + 3 < args.Length && int.TryParse(args[at + 3], out var ms) ? ms : 3500;

        List<WidgetKind> kinds;
        if (string.Equals(which, "all", StringComparison.OrdinalIgnoreCase))
        {
            kinds = [.. WidgetCatalog.All.Select(d => d.Kind)];
        }
        else
        {
            kinds = [];
            foreach (var part in which.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<WidgetKind>(part, true, out var kind))
                {
                    kinds.Add(kind);
                }
                else
                {
                    Log.Error(Scope, $"unknown widget kind '{part}'");
                }
            }
        }

        if (DispatcherQueue.GetForCurrentThread() is not { } dispatcher)
        {
            Log.Error(Scope, "no dispatcher on this thread; cannot render");
            app.Exit();
            return true;
        }

        var gallery = new WidgetGallery(dispatcher, interactive: false);

        // Off-screen and never activated: rendering needs a live window, but the user's focus and
        // their desktop must not notice it exists.
        gallery._window.AppWindow.Move(new PointInt32(-32000, -32000));
        gallery._window.AppWindow.Show(activateWindow: false);

        _ = gallery.RenderAllAsync(app, kinds, outDir, wait);
        return true;
    }

    private async Task RenderAllAsync(Application app, IReadOnlyList<WidgetKind> kinds, string outDir, int waitMs)
    {
        try
        {
            Directory.CreateDirectory(outDir);

            foreach (var kind in kinds)
            {
                Show(kind);

                // Long enough for a meter's first sample and a network fetch to land; a widget
                // still loading after this is itself something worth seeing.
                await Task.Delay(waitMs).ConfigureAwait(true);

                var path = System.IO.Path.Combine(outDir, $"{kind}.png");
                await SavePngAsync(_frame, path).ConfigureAwait(true);
                Console.WriteLine($"gallery: wrote {path}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "rendering the gallery failed", ex);
        }
        finally
        {
            Dispose();
            app.Exit();
        }
    }

    private void Select(WidgetKind kind)
    {
        foreach (var item in _picker.Items)
        {
            if (item is ComboBoxItem { Tag: WidgetKind tagged } box && tagged == kind)
            {
                _picker.SelectedItem = box;
                return;
            }
        }
    }

    /// <summary>Replace the page with one widget at every size. The old cards are detached first.</summary>
    private void Show(WidgetKind kind)
    {
        ClearCards();
        _window.Title = $"Themia widget gallery - {WidgetCatalog.Definition(kind).Label}";

        var x = new[] { Gap, Gap };
        var rowTop = new[] { Gap, Gap + LabelHeight + RowHeight(0) + Gap };

        for (var i = 0; i < Sizes.Count; i++)
        {
            var (name, width, height) = Sizes[i];
            var row = RowOf[i];

            var label = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            label.Text = $"{name}  {width:0} x {height:0}";
            Canvas.SetLeft(label, x[row]);
            Canvas.SetTop(label, rowTop[row]);
            _page.Children.Add(label);

            var model = WidgetInstance.Create(kind, new FracRect(0, 0, 0.1, 0.1));
            var card = new WidgetCard(_host, model)
            {
                Width = width,
                Height = height,
            };

            // The size is the painted card: the gap is the space between widgets on a desktop and
            // there is nothing here for it to separate.
            card.ApplyGap(0);
            Canvas.SetLeft(card, x[row]);
            Canvas.SetTop(card, rowTop[row] + LabelHeight);
            _page.Children.Add(card);
            _cards.Add(card);

            x[row] += width + Gap;
        }

        var (pageWidth, pageHeight) = PageSize();
        _page.Width = pageWidth;
        _page.Height = pageHeight;
    }

    private static double RowHeight(int row)
    {
        var tallest = 0.0;
        for (var i = 0; i < Sizes.Count; i++)
        {
            if (RowOf[i] == row)
            {
                tallest = Math.Max(tallest, Sizes[i].Height);
            }
        }

        return tallest;
    }

    private static (double Width, double Height) PageSize()
    {
        var widths = new double[2];
        for (var i = 0; i < Sizes.Count; i++)
        {
            widths[RowOf[i]] += Sizes[i].Width + Gap;
        }

        var width = Gap + Math.Max(widths[0], widths[1]);
        var height = Gap + LabelHeight + RowHeight(0) + Gap + LabelHeight + RowHeight(1) + Gap;
        return (width, height);
    }

    private static async Task SavePngAsync(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            96,
            96,
            pixels.ToArray());
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
    }

    private void ClearCards()
    {
        foreach (var card in _cards)
        {
            card.Detach();
        }

        _cards.Clear();
        _page.Children.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearCards();
        _ticker.Dispose();
    }

    /// <summary>
    /// A card host with nowhere to go: the gallery shows widgets, it does not lay them out, save them
    /// or edit them, so every request is accepted and ignored.
    /// </summary>
    private sealed class GalleryCardHost(Ticker ticker) : IWidgetCardHost
    {
        public ScreenMetrics Metrics { get; } = new(1920, 1080);

        public DesignState Design { get; } = new();

        public Ticker Ticker => ticker;

        public bool EditMode => false;

        public ViewState? ViewForCards => null;

        public bool CanPaste => false;

        public void OnGeometryPreview(WidgetCard card)
        {
            // ignore: gallery cards are not draggable
        }

        public void OnGeometryCommitted(WidgetCard card)
        {
            // ignore: as above
        }

        public void RequestConfigure(WidgetCard card)
        {
            // ignore: the gallery has no settings window
        }

        public void RequestDuplicate(WidgetCard card)
        {
            // ignore: the gallery has a fixed set of cards
        }

        public void RequestDelete(WidgetCard card)
        {
            // ignore: as above
        }

        public void RequestEditMode(bool enabled)
        {
            // ignore: the gallery is never in edit mode
        }

        public bool IsSelected(WidgetCard card) => false;

        public void RequestSelect(WidgetCard card)
        {
            // ignore: nothing to select into
        }

        public void RequestCopy(WidgetCard card)
        {
            // ignore: no clipboard here
        }

        public void RequestCut(WidgetCard card)
        {
            // ignore: as above
        }

        public void RequestPaste()
        {
            // ignore: as above
        }

        public void RequestReparent(WidgetCard card, string? containerId)
        {
            // ignore: no containers to move into
        }

        public void RequestSaveConfig()
        {
            // ignore: nothing here is persisted
        }

        public void RequestTextInput(WidgetCard card, bool wanted)
        {
            // ignore: the gallery window is an ordinary window and takes the keyboard by itself
        }
    }
}
#endif
