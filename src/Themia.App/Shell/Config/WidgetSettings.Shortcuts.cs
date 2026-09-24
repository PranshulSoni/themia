using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Interop;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Shortcuts widget's settings rows: the list itself, and how it opens.
/// <para>
/// The target box is the trust boundary on this page. Whatever is typed becomes something the
/// desktop will open on a click, so it is checked by <see cref="LaunchTargets"/> before it is
/// written and the reason it was refused is shown under the row rather than swallowed. Nothing is
/// corrected: a bare word is refused rather than turned into <c>https://word</c>, and a relative
/// path is refused rather than resolved against wherever the app happens to have been started.
/// </para>
/// <para>
/// The original widget could also hold an "app layout" — a tree of panes that opens several
/// applications and snaps each onto a region of a display. That is a window manager rather than a
/// shortcut, and it is deliberately not ported.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string LauncherScope = "shortcut-settings";

    public static void Shortcuts(List<FrameworkElement> rows, ShortcutsConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Shortcuts"));

        // One host whose children are rebuilt in place, because adding, removing and reordering all
        // change the shape of this section while the page around it is built once.
        var host = new StackPanel { Spacing = 4 };
        rows.Add(host);

        void Rebuild()
        {
            host.Children.Clear();

            if (config.Items.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = "No shortcuts yet.",
                    Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray),
                    Margin = new Thickness(0, 4, 0, 4),
                });

                return;
            }

            for (var index = 0; index < config.Items.Count; index++)
            {
                host.Children.Add(ShortcutCard(config, index, save, Rebuild));
            }
        }

        Rebuild();

        void Append(string target, string label)
        {
            config.Items.Add(ShortcutItem.Create(target, label));
            save();
            Rebuild();
        }

        rows.Add(FluentLayout.Row(Glyphs.Add, "Add a shortcut",
            "An empty row you can point at a web address, a file or a folder",
            NewButton("Add", () => Append(string.Empty, string.Empty))));

        var file = new Button { Content = "File…" };
        var folder = new Button { Content = "Folder…" };

        file.Click += (_, _) => _ = PickFileAsync(Append);
        folder.Click += (_, _) => _ = PickFolderAsync(Append);

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Pick from this PC",
            "Browse for a file or a folder instead of typing its path",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { file, folder },
            }));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Windows locations",
            "Add one of the places Windows has a name for",
            ShellPicker(Append)));

        rows.Add(FluentLayout.Header("Opening"));

        rows.Add(FluentLayout.Row(Glyphs.Open, "Open on one click",
            "Off, a shortcut opens on a double-click. A single click is easy to do by accident on the desktop",
            FluentLayout.Toggle(config.OpenOnSingleClick, value => { config.OpenOnSingleClick = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show the target",
            "Hidden automatically when the widget is too narrow",
            FluentLayout.Toggle(config.ShowTarget, value => { config.ShowTarget = value; save(); })));
    }

    /// <summary>
    /// One shortcut's editor: what it is called, where it points, whether that is allowed, and
    /// where it sits in the list.
    /// </summary>
    private static FrameworkElement ShortcutCard(ShortcutsConfig config, int index, Action save, Action rebuild)
    {
        var item = config.Items[index];

        var label = new TextBox
        {
            Text = item.Label,
            PlaceholderText = LaunchTargets.DisplayName(null, item.Target) is { Length: > 0 } derived
                ? derived
                : "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var target = new TextBox
        {
            Text = item.Target,
            PlaceholderText = @"https://example.com  ·  C:\Users\…  ·  shell:Downloads",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
        };

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        void ShowStatus()
        {
            if (item.Target.Trim().Length == 0)
            {
                status.Text = "Not pointing anywhere yet.";
                status.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
                return;
            }

            var classified = LaunchTargets.Classify(item.Target);

            if (classified.IsValid)
            {
                // Confirming what will actually open is worth as much as refusing: a row that looks
                // accepted but opens something else is the failure this check exists to prevent.
                status.Text = classified.Kind switch
                {
                    LaunchKind.Web => $"Opens {classified.Value} in your browser.",
                    LaunchKind.ShellNamespace => $"Opens {LaunchTargets.ShellDisplayName(classified.Value)}.",
                    _ => $"Opens {classified.Value}.",
                };

                status.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
                return;
            }

            status.Text = LaunchTargets.RefusalMessage(classified.Refusal);
            status.Foreground = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);
        }

        // On commit, not per keystroke: a path or a URL is only ever valid once it has been fully
        // typed, so validating each character would spend the whole time saying it is wrong.
        void CommitTarget()
        {
            var typed = target.Text.Trim();

            if (typed == item.Target)
            {
                ShowStatus();
                return;
            }

            // Stored whether or not it passes, and the box keeps what was typed: a wrong target is
            // corrected by editing it, not by having it wiped. The widget refuses to open it, which
            // is where refusing belongs.
            item.Target = typed;
            label.PlaceholderText = LaunchTargets.DisplayName(null, item.Target) is { Length: > 0 } derived
                ? derived
                : "Name";

            save();
            ShowStatus();
        }

        void CommitLabel()
        {
            var typed = label.Text.Trim();

            if (typed == item.Label)
            {
                return;
            }

            item.Label = typed;
            save();
        }

        target.LostFocus += (_, _) => CommitTarget();
        target.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitTarget();
            }
        };

        label.LostFocus += (_, _) => CommitLabel();
        label.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitLabel();
            }
        };

        var up = LauncherIconButton(Glyphs.MoveUp, "Move up");
        var down = LauncherIconButton(Glyphs.MoveDown, "Move down");
        var remove = LauncherIconButton(Glyphs.Delete, "Remove shortcut");

        // One swap per click, and the ends cannot move past themselves.
        up.IsEnabled = index > 0;
        down.IsEnabled = index < config.Items.Count - 1;

        up.Click += (_, _) => MoveShortcut(config, index, index - 1, save, rebuild);
        down.Click += (_, _) => MoveShortcut(config, index, index + 1, save, rebuild);

        remove.Click += (_, _) =>
        {
            config.Items.RemoveAt(index);
            save();
            rebuild();
        };

        var top = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 2);
        Grid.SetColumn(remove, 3);

        top.Children.Add(label);
        top.Children.Add(up);
        top.Children.Add(down);
        top.Children.Add(remove);

        target.Margin = new Thickness(0, 8, 0, 0);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(target);
        body.Children.Add(status);

        ShowStatus();

        // The same card the stock settings rows use, so a list of shortcuts reads as part of the
        // page rather than as a panel bolted onto it.
        return new Border
        {
            Child = body,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(4),
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
        };
    }

    private static void MoveShortcut(ShortcutsConfig config, int from, int to, Action save, Action rebuild)
    {
        if (to < 0 || to >= config.Items.Count || from == to)
        {
            return;
        }

        (config.Items[from], config.Items[to]) = (config.Items[to], config.Items[from]);
        save();
        rebuild();
    }

    /// <summary>
    /// The Windows locations, as a dropdown of names rather than of <c>shell:</c> URIs. The URI is
    /// what gets stored; nobody should have to read one to add their Downloads folder.
    /// </summary>
    private static ComboBox ShellPicker(Action<string, string> append)
    {
        var combo = new ComboBox { MinWidth = 180, PlaceholderText = "Choose…" };

        foreach (var name in LaunchTargets.WellKnownShellNames)
        {
            var uri = LaunchTargets.ShellTarget(name);

            combo.Items.Add(new ComboBoxItem
            {
                Content = LaunchTargets.ShellDisplayName(uri),
                Tag = uri,
            });
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string uri })
            {
                return;
            }

            append(uri, string.Empty);

            // Back to the placeholder, so the control reads as an action rather than as a setting
            // that is now stuck on "Downloads".
            combo.SelectedIndex = -1;
        };

        return combo;
    }

    private static Button NewButton(string content, Action onClick)
    {
        var button = new Button { Content = content };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button LauncherIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// The shell's own file picker, so nobody has to type a path they could point at. It writes
    /// through the same add as everything else, so there is one way a shortcut comes into being.
    /// </summary>
    private static async Task PickFileAsync(Action<string, string> append)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // Required: PickSingleFileAsync throws without at least one filter.
            picker.FileTypeFilter.Add("*");

            // The picker needs an owner window, and the click that opened it came from the settings
            // window, so the foreground window is that window at this instant.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32.GetForegroundWindow());

            var file = await picker.PickSingleFileAsync();

            if (file is null)
            {
                return; // ignore: the user cancelled the dialog.
            }

            append(file.Path, string.Empty);
        }
        catch (Exception ex)
        {
            // The dialog is a convenience over a box that still works. Losing it must not take the
            // settings window down with it.
            Log.Error(LauncherScope, "the file picker could not be shown", ex);
        }
    }

    private static async Task PickFolderAsync(Action<string, string> append)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32.GetForegroundWindow());

            var folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return; // ignore: the user cancelled the dialog.
            }

            append(folder.Path, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(LauncherScope, "the folder picker could not be shown", ex);
        }
    }
}
