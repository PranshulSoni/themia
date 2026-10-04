using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
/// written and the reason it was refused is shown under the box rather than swallowed. Nothing is
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

        var list = FluentLayout.ListEditor(rows, new ListEditorOptions<ShortcutItem>
        {
            Items = config.Items,
            Save = save,
            EmptyText = "No shortcuts yet.",
            AddTitle = "Add a shortcut",
            AddDescription = "An empty row you can point at a web address, a file or a folder",
            AddAction = "Add",
            RemoveTooltip = "Remove shortcut",
            Create = () => ShortcutItem.Create(string.Empty, string.Empty),
            Body = item => ShortcutBody(item, save),
        });

        var file = new Button { Content = "File…" };
        var folder = new Button { Content = "Folder…" };

        file.Click += (_, _) => _ = PickFileAsync(list);
        folder.Click += (_, _) => _ = PickFolderAsync(list);

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
            ShellPicker(list)));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show the target",
            "Hidden automatically when the widget is too narrow",
            FluentLayout.Toggle(config.ShowTarget, value => { config.ShowTarget = value; save(); })));

        rows.Add(FluentLayout.Header("Behaviour"));

        rows.Add(FluentLayout.Row(Glyphs.Open, "Open on one click",
            "Off, a shortcut opens on a double-click. A single click is easy to do by accident on the desktop",
            FluentLayout.Toggle(config.OpenOnSingleClick, value => { config.OpenOnSingleClick = value; save(); })));
    }

    /// <summary>
    /// One shortcut's editor: what it is called, where it points, and whether that is allowed. The
    /// ordering and the removal belong to the shared list editor.
    /// </summary>
    private static FrameworkElement ShortcutBody(ShortcutItem item, Action save)
    {
        var label = new TextBox
        {
            Text = item.Label,
            PlaceholderText = NameHint(item.Target),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var target = new TextBox
        {
            Text = item.Target,
            PlaceholderText = @"https://example.com  ·  C:\Users\…  ·  shell:Downloads",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
        };

        // The verdict sits under the field it judges, the same way it does in every other settings
        // field on every other page. Left-aligned here only because the field it belongs to is.
        var verdict = FluentLayout.Verdict();
        verdict.HorizontalAlignment = HorizontalAlignment.Left;
        verdict.TextAlignment = TextAlignment.Left;
        verdict.Margin = new Thickness(0, Fluent.Space2, 0, 0);

        void ShowVerdict()
        {
            if (item.Target.Trim().Length == 0)
            {
                FluentLayout.Confirm(verdict, "Not pointing anywhere yet.");
                return;
            }

            var classified = LaunchTargets.Classify(item.Target);

            if (!classified.IsValid)
            {
                FluentLayout.Refuse(verdict, LaunchTargets.RefusalMessage(classified.Refusal));
                return;
            }

            // Confirming what will actually open is worth as much as refusing: a row that looks
            // accepted but opens something else is the failure this check exists to prevent.
            FluentLayout.Confirm(verdict, classified.Kind switch
            {
                LaunchKind.Web => $"Opens {classified.Value} in your browser.",
                LaunchKind.ShellNamespace => $"Opens {LaunchTargets.ShellDisplayName(classified.Value)}.",
                _ => $"Opens {classified.Value}.",
            });
        }

        // On commit, not per keystroke: a path or a URL is only ever valid once it has been fully
        // typed, so validating each character would spend the whole time saying it is wrong.
        void CommitTarget()
        {
            var typed = target.Text.Trim();

            if (typed == item.Target)
            {
                ShowVerdict();
                return;
            }

            // Stored whether or not it passes, and the box keeps what was typed: a wrong target is
            // corrected by editing it, not by having it wiped. The widget refuses to open it, which
            // is where refusing belongs.
            item.Target = typed;
            label.PlaceholderText = NameHint(item.Target);

            save();
            ShowVerdict();
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

        ShowVerdict();

        return new StackPanel { Children = { label, target, verdict } };
    }

    /// <summary>What the name box suggests when it is empty: whatever the target is called.</summary>
    private static string NameHint(string target)
        => LaunchTargets.DisplayName(null, target) is { Length: > 0 } derived ? derived : "Name";

    /// <summary>
    /// The Windows locations, as a dropdown of names rather than of <c>shell:</c> URIs. The URI is
    /// what gets stored; nobody should have to read one to add their Downloads folder.
    /// </summary>
    private static ComboBox ShellPicker(ListEditorHandle<ShortcutItem> list)
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

            list.Add(ShortcutItem.Create(uri, string.Empty));

            // Back to the placeholder, so the control reads as an action rather than as a setting
            // that is now stuck on "Downloads".
            combo.SelectedIndex = -1;
        };

        return combo;
    }

    /// <summary>
    /// The shell's own file picker, so nobody has to type a path they could point at. It writes
    /// through the same add as everything else, so there is one way a shortcut comes into being.
    /// </summary>
    private static async Task PickFileAsync(ListEditorHandle<ShortcutItem> list)
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

            list.Add(ShortcutItem.Create(file.Path, string.Empty));
        }
        catch (Exception ex)
        {
            // The dialog is a convenience over a box that still works. Losing it must not take the
            // settings window down with it.
            Log.Error(LauncherScope, "the file picker could not be shown", ex);
        }
    }

    private static async Task PickFolderAsync(ListEditorHandle<ShortcutItem> list)
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

            list.Add(ShortcutItem.Create(folder.Path, string.Empty));
        }
        catch (Exception ex)
        {
            Log.Error(LauncherScope, "the folder picker could not be shown", ex);
        }
    }
}
