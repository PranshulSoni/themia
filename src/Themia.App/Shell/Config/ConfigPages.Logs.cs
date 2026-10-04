using System.Globalization;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-989] The Logs page, in the menu only while developer mode is on ([S-990]): the verbose
/// switch, the log folder, and this session's records with level and source filters, copy and
/// clear.
/// <para>
/// The page links to the files, it does not re-export them ([S-989]): the files carry every
/// session; the list carries this one. Copy is for pasting a filtered excerpt into an issue, which
/// is a different job from attaching the files.
/// </para>
/// </summary>
internal static partial class ConfigPages
{
    /// <summary>The most rows drawn at once. The buffer holds more; a filter narrows to them.</summary>
    private const int LogRowsShown = 200;

    private static FrameworkElement BuildLogs(AppHost host)
    {
        var rows = new List<FrameworkElement> { FluentLayout.Header("Logging") };

        rows.Add(FluentLayout.Row(Glyphs.Document, "Verbose logging",
            "Record everything Themia does, not only warnings and errors. Turn it off when you are done: it writes far more",
            FluentLayout.Toggle(host.State.Preferences.VerboseLogging,
                value => host.Mutate(s => s.Preferences.VerboseLogging = value, relayout: false)),
            "debug", "logging", "diagnostics"));

        var (files, bytes) = LogFiles.Usage();
        var openFolder = new Button { Content = "Open folder" };
        var folderVerdict = FluentLayout.Verdict();
        openFolder.Click += (_, _) =>
        {
            if (LogFiles.OpenFolder() != LaunchOutcome.Opened)
            {
                FluentLayout.Refuse(folderVerdict, "The log folder could not be opened.");
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Log files",
            files == 0
                ? $"Nothing written yet. Files go to {LogFiles.Folder}"
                : $"{files} {(files == 1 ? "file" : "files")}, {Formatting.Bytes(bytes)}, in {LogFiles.Folder}. Kept to the newest {LogFileRules.MaxArchives + 1} files of {Formatting.Bytes(LogFileRules.MaxFileBytes)}; files older than {LogFileRules.MaxAge.TotalDays:F0} days are deleted",
            openFolder,
            "folder", "files", "open"));
        rows.Add(folderVerdict);

        rows.Add(FluentLayout.Note(
            "Errors are also written to the Windows event log (Event Viewer › Windows Logs › Application, source Themia). Passwords, tokens and email addresses are removed from every record before it is written."));

        rows.Add(FluentLayout.Header("This session"));
        rows.Add(new LogList(host).Root);

        return Page("Logs", rows);
    }

    /// <summary>
    /// The session's records under a filter bar. Live while on screen: it listens to the buffer only
    /// between Loaded and Unloaded, since the window rebuilds the page on every visit and search
    /// builds it off-screen.
    /// </summary>
    private sealed class LogList
    {
        private readonly AppHost _host;
        private readonly StackPanel _rows = new() { Spacing = 0 };
        private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _scope = new() { MinWidth = 160 };
        private readonly DispatcherQueue _dispatcher;
        private LogLevel _minimum = LogLevel.Debug;
        private string? _scopeFilter;
        private int _pending;
        private bool _rendering;
        private IReadOnlyList<LogRecord> _shown = [];

        public LogList(AppHost host)
        {
            _host = host;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            FluentLayout.ApplyStyle(_count, "SettingsCaptionTextBlockStyle");

            var level = FluentLayout.Choice<LogLevel>(
                [
                    (LogLevel.Debug, "All levels"),
                    (LogLevel.Info, "Info and above"),
                    (LogLevel.Warn, "Warnings and errors"),
                    (LogLevel.Error, "Errors only"),
                ],
                _minimum,
                value =>
                {
                    _minimum = value;
                    Render();
                });
            level.MinWidth = 180;

            _scope.SelectionChanged += (_, _) =>
            {
                if (_rendering)
                {
                    return;
                }

                _scopeFilter = _scope.SelectedItem is ComboBoxItem { Tag: string tag } && tag.Length > 0 ? tag : null;
                Render();
            };

            var copy = FluentLayout.IconCommand(Glyphs.Copy, "Copy the records shown");
            copy.Click += (_, _) => Copy();

            var clear = FluentLayout.IconCommand(Glyphs.Delete, "Clear this list (the files are kept)");
            clear.Click += (_, _) => LogFiles.Recent.Clear();

            var bar = new Grid
            {
                ColumnSpacing = Fluent.Space2,
                Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space3, Fluent.Space3),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            Place(bar, level, 0);
            Place(bar, _scope, 1);
            Place(bar, _count, 2);
            Place(bar, copy, 3);
            Place(bar, clear, 4);
            _count.HorizontalAlignment = HorizontalAlignment.Right;
            _count.Margin = new Thickness(0, 0, Fluent.Space2, 0);

            var list = new ScrollViewer
            {
                Content = _rows,
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            var panel = new StackPanel
            {
                Children =
                {
                    bar,
                    new Border { Height = Fluent.Hairline, Background = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray) },
                    list,
                },
            };

            var card = FluentLayout.Card(panel);
            card.Padding = new Thickness(0);
            card.MinHeight = 0;
            Root = card;

            card.Loaded += (_, _) => LogFiles.Recent.Changed += OnChanged;
            card.Unloaded += (_, _) => LogFiles.Recent.Changed -= OnChanged;
            Render();
        }

        public FrameworkElement Root { get; }

        private static void Place(Grid grid, FrameworkElement element, int column)
        {
            Grid.SetColumn(element, column);
            element.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(element);
        }

        /// <summary>
        /// From any thread. A burst of records posts one render, not one per record; a record logged
        /// by this page's own render (a missing style, say) does not schedule another, or the page
        /// would feed itself.
        /// </summary>
        private void OnChanged()
        {
            if (_rendering && _dispatcher.HasThreadAccess)
            {
                return;
            }

            if (Interlocked.Exchange(ref _pending, 1) == 0 && !_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Volatile.Write(ref _pending, 0);
                Render();
            }))
            {
                // ignore: the dispatcher is shutting down with the window; nothing left to draw on.
                Volatile.Write(ref _pending, 0);
            }
        }

        private void Render()
        {
            _rendering = true;
            try
            {
                var all = LogFiles.Recent.Snapshot();
                RenderScopes(all);
                _shown = LogBuffer.Filter(all, _minimum, _scopeFilter, LogRowsShown);

                _rows.Children.Clear();
                if (_shown.Count == 0)
                {
                    _rows.Children.Add(Empty());
                }
                else
                {
                    foreach (var record in _shown)
                    {
                        _rows.Children.Add(Row(record));
                    }
                }

                var matching = LogBuffer.Filter(all, _minimum, _scopeFilter, int.MaxValue).Count;
                _count.Text = matching > _shown.Count
                    ? $"Newest {_shown.Count} of {matching}"
                    : matching == 1 ? "1 record" : $"{matching.ToString(CultureInfo.CurrentCulture)} records";
            }
            finally
            {
                _rendering = false;
            }
        }

        /// <summary>"All sources" plus every scope in the buffer, keeping the chosen one even after it scrolls off.</summary>
        private void RenderScopes(IReadOnlyList<LogRecord> all)
        {
            var scopes = all.Select(r => r.Scope).Append(_scopeFilter ?? string.Empty)
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            _scope.Items.Clear();
            _scope.Items.Add(new ComboBoxItem { Content = "All sources", Tag = string.Empty });
            foreach (var scope in scopes)
            {
                _scope.Items.Add(new ComboBoxItem { Content = scope, Tag = scope });
            }

            _scope.SelectedIndex = _scopeFilter is null ? 0 : scopes.IndexOf(_scopeFilter) + 1;
        }

        private FrameworkElement Empty()
        {
            var verbose = LogPolicy.Verbose(_host.State.Preferences);
            var text = new TextBlock
            {
                Text = verbose || Log.Floor == LogLevel.Debug
                    ? "Nothing recorded under this filter in this session."
                    : "No warnings or errors in this session. Only those are recorded until verbose logging is on.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Fluent.Space4, Fluent.Space4, Fluent.Space4, Fluent.Space4),
            };
            FluentLayout.ApplyStyle(text, "SettingsCaptionTextBlockStyle");
            return text;
        }

        private static FrameworkElement Row(LogRecord record)
        {
            var time = new TextBlock { Text = record.At.ToString("HH:mm:ss", CultureInfo.CurrentCulture) };
            FluentLayout.ApplyStyle(time, "CaptionTextBlockStyle");
            time.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

            var level = new TextBlock
            {
                Text = LogFormat.Level(record.Level).Trim(),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            FluentLayout.ApplyStyle(level, "CaptionTextBlockStyle");
            level.Foreground = LevelBrush(record.Level);

            var scope = new TextBlock { Text = record.Scope, TextTrimming = TextTrimming.CharacterEllipsis };
            FluentLayout.ApplyStyle(scope, "CaptionTextBlockStyle");
            scope.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

            var message = new TextBlock
            {
                Text = record.Message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            FluentLayout.ApplyStyle(message, "BodyTextBlockStyle");

            var body = new StackPanel { Spacing = Fluent.Space1, Children = { message } };
            if (!string.IsNullOrEmpty(record.Detail))
            {
                var detail = new TextBlock
                {
                    Text = record.Detail,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 8,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsTextSelectionEnabled = true,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                };
                FluentLayout.ApplyStyle(detail, "CaptionTextBlockStyle");
                detail.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
                body.Children.Add(detail);
            }

            var grid = new Grid
            {
                ColumnSpacing = Fluent.Space3,
                Padding = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space2),
                BorderThickness = new Thickness(0, 0, 0, Fluent.Hairline),
                BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(64) },
                    new ColumnDefinition { Width = new GridLength(48) },
                    new ColumnDefinition { Width = new GridLength(110) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
            };

            Grid.SetColumn(level, 1);
            Grid.SetColumn(scope, 2);
            Grid.SetColumn(body, 3);
            grid.Children.Add(time);
            grid.Children.Add(level);
            grid.Children.Add(scope);
            grid.Children.Add(body);
            ToolTipService.SetToolTip(scope, record.Scope);
            return grid;
        }

        private static Brush LevelBrush(LogLevel level) => level switch
        {
            LogLevel.Error => FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed),
            LogLevel.Warn => FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod),
            LogLevel.Info => FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
            _ => FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray),
        };

        /// <summary>The rows shown, newest first, as the file writes them.</summary>
        private void Copy()
        {
            if (_shown.Count == 0)
            {
                return;
            }

            var text = new StringBuilder();
            foreach (var record in _shown)
            {
                text.Append(LogFormat.Record(record)).Append(Environment.NewLine);
            }

            try
            {
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetText(text.ToString());
                Clipboard.SetContent(package);
                _count.Text = _shown.Count == 1 ? "Copied 1 record" : $"Copied {_shown.Count} records";
            }
            catch (Exception ex)
            {
                // Another process can hold the clipboard open; say so rather than claim it copied.
                Log.Warn(Scope, "could not copy the log records", ex);
                _count.Text = "The clipboard is busy. Try again";
            }
        }
    }
}
