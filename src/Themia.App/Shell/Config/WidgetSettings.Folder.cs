using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Services.Files;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Folder widget's settings rows.
/// <para>
/// The path box is the one control here that is a trust boundary: whatever is typed goes on to a
/// filesystem call, so it is checked by <see cref="FolderPath"/> before it is written, and the
/// reason it was refused is shown under the box rather than swallowed. A refused value is not
/// corrected into a working one — a relative path becomes a request to type a full one, never a
/// silent jump to whatever the process's working directory happens to be.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string FolderScope = "folder-settings";

    public static void Folder(List<FrameworkElement> rows, FolderConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Folder"));

        var verdict = FluentLayout.Verdict();

        var box = new TextBox
        {
            // Read once to seed the control, then never mirrored: the config stays the only copy of
            // the value, and the box writes to it rather than shadowing it.
            Text = config.Path,
            Width = 260,
            PlaceholderText = ProfilePlaceholder(),
        };

        var browse = new Button { Content = "Browse…" };

        void Commit()
        {
            var typed = box.Text;

            // An emptied box is a deliberate "use my profile folder", which is what the config
            // documents a blank path to mean. It is not an error.
            var result = FolderPath.Resolve(typed, string.Empty);

            if (typed.Trim().Length == 0 || result.IsValid)
            {
                config.Path = result.IsValid ? result.Path : string.Empty;
                box.Text = config.Path;
                save();
            }

            ShowFolderVerdict(verdict, config, typed, result);
        }

        // On commit, not per keystroke: a path is only ever valid once it has been fully typed, so
        // validating each character would spend the whole time telling the user they are wrong.
        box.LostFocus += (_, _) => Commit();

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        browse.Click += (_, _) => _ = BrowseAsync(box, Commit);

        ShowFolderVerdict(verdict, config, config.Path, FolderPath.Resolve(config.Path, string.Empty));

        rows.Add(FluentLayout.FieldRow(Glyphs.Folder, "Folder",
            "Which folder to list. Leave it empty for your user folder",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { box, browse },
            },
            verdict));

        rows.Add(FluentLayout.Header("View"));

        rows.Add(FluentLayout.Row(Glyphs.View, "Layout", "Rows, a table with columns, labelled tiles, or icons alone",
            FluentLayout.Choice(
                [(FolderViewMode.List, "List"), (FolderViewMode.Details, "Details"), (FolderViewMode.Grid, "Grid"), (FolderViewMode.Icons, "Icons")],
                config.View,
                value => { config.View = value; save(); }),
            "view mode", "grid", "details", "icons"));

        rows.Add(FluentLayout.Row(Glyphs.TextSize, "Row size", "How tall list and details rows are",
            FluentLayout.Choice(
                [(FolderItemSize.ExtraSmall, "Extra small"), (FolderItemSize.Small, "Small"), (FolderItemSize.Medium, "Medium"), (FolderItemSize.Large, "Large")],
                config.ItemSize,
                value => { config.ItemSize = value; save(); }),
            "density", "item size"));

        rows.Add(FluentLayout.Expander(Glyphs.GridLayout, "Tile size", "How big grid and icon tiles are",
            FluentLayout.Choice(
                [(FolderTileSize.Small, "Small"), (FolderTileSize.Medium, "Medium"), (FolderTileSize.Large, "Large")],
                config.TileSize,
                value => { config.TileSize = value; save(); }),
            [
                FluentLayout.Row(Glyphs.Fit, "Stretch tiles to fill",
                    "When every tile fits, rows share the widget's height",
                    FluentLayout.Toggle(config.StretchTiles, value => { config.StretchTiles = value; save(); })),
            ]));

        rows.Add(FluentLayout.Expander(Glyphs.ColumnsLayout, "Details columns",
            "Shown after the name, in this order, as far as the widget is wide",
            null,
            [.. FolderColumns.All.Select(column => FluentLayout.Row(Glyphs.ColumnsLayout, FolderColumns.Title(column), ColumnHint(column),
                FluentLayout.Toggle(config.Columns.Contains(column), value =>
                {
                    if (value != config.Columns.Contains(column))
                    {
                        config.Columns = FolderColumns.Toggle(config.Columns, column);
                        save();
                    }
                })))]));

        rows.Add(FluentLayout.Header("Contents"));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Sort by", "Which column the list is ordered on",
            FluentLayout.Choice(
                [(FolderSort.Name, "Name"), (FolderSort.Modified, "Date modified"), (FolderSort.Size, "Size")],
                config.Sort,
                value => { config.Sort = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.MoveDown, "Reverse order",
            "Z to A, newest or largest first, depending on the sort",
            FluentLayout.Toggle(config.Descending, value => { config.Descending = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Folders first", "Group folders above files, as Explorer does",
            FluentLayout.Toggle(config.FoldersFirst, value => { config.FoldersFirst = value; save(); })));

        var filter = new TextBox { Text = config.Filter, Width = 260, PlaceholderText = "e.g. report, .pdf" };
        filter.LostFocus += (_, _) =>
        {
            if (filter.Text != config.Filter)
            {
                config.Filter = filter.Text;
                save();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Search, "Filter",
            "Only names containing one of these words; separate them with commas",
            filter, "keywords", "match"));

        rows.Add(FluentLayout.Row(Glyphs.Hidden, "Show hidden items",
            "Include what Windows marks hidden or system",
            FluentLayout.Toggle(config.ShowHidden, value => { config.ShowHidden = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.File, "Show file extensions",
            "\"report.docx\" rather than \"report\"",
            FluentLayout.Toggle(config.ShowExtensions, value => { config.ShowExtensions = value; save(); })));

        rows.Add(FluentLayout.Header("Opening"));

        rows.Add(FluentLayout.Row(Glyphs.Open, "Open items with",
            "A double click, as on the desktop, or a single click, as on a web page",
            FluentLayout.Choice(
                [(ItemClickMode.Double, "Double click"), (ItemClickMode.Single, "Single click")],
                config.ClickMode,
                value => { config.ClickMode = value; save(); }),
            "click mode"));

        rows.Add(FluentLayout.Row(Glyphs.Back, "Open folders",
            "In Explorer, or inside the widget with Back, Forward and Up",
            FluentLayout.Choice(
                [(FolderNavigation.Explorer, "In Explorer"), (FolderNavigation.Navigate, "Inside the widget")],
                config.Navigation,
                value => { config.Navigation = value; save(); }),
            "navigation", "browse"));

        var open = new Button { Content = "Open in Explorer" };
        open.Click += (_, _) =>
        {
            var result = FolderPath.Resolve(config.Path, ProfilePlaceholder());

            if (result.IsValid)
            {
                ShellFileOps.Open(result.Path);
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.OpenExternal, "Explorer",
            "Delete always goes to the Recycle Bin, and asks first", open));
    }

    private static string ColumnHint(FolderColumn column) => column switch
    {
        FolderColumn.Size => "A file's size",
        FolderColumn.Modified => "When it last changed",
        FolderColumn.Type => "\"Text Document\", \"File folder\"",
        FolderColumn.Cloud => "OneDrive and other sync status",
        FolderColumn.Git => "The item's git status, inside a repository",
        FolderColumn.ItemCount => "How many items a folder holds",
        _ => string.Empty,
    };

    private static void ShowFolderVerdict(TextBlock verdict, FolderConfig config, string typed, FolderPathResult result)
    {
        if (typed.Trim().Length == 0)
        {
            FluentLayout.Confirm(verdict, $"Empty: listing {ProfilePlaceholder()}");
            return;
        }

        if (result.IsValid)
        {
            FluentLayout.Confirm(verdict, $"Listing {FolderPath.DisplayName(config.Path)}");
            return;
        }

        // Refused, and the box keeps what was typed so it can be corrected rather than retyped.
        FluentLayout.Refuse(verdict, FolderFormatting.PathMessage(result.Problem));
    }

    /// <summary>
    /// The folder an empty setting means. Shown rather than written into the config, so the setting
    /// keeps meaning "my user folder" if the profile ever moves.
    /// </summary>
    private static string ProfilePlaceholder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The shell's own folder picker, so nobody has to type a path they could point at.
    /// <para>
    /// The picker needs an owner window, and the click that opened it came from the settings
    /// window, so the foreground window <i>is</i> that window at this instant. It writes into the
    /// same box the user types into and goes through the same commit, so there is one path to the
    /// setting rather than two.
    /// </para>
    /// </summary>
    private static async Task BrowseAsync(TextBox box, Action commit)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();

            // Required: PickSingleFolderAsync throws without at least one filter.
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32.GetForegroundWindow());

            var folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return; // ignore: the user cancelled the dialog.
            }

            box.Text = folder.Path;
            commit();
        }
        catch (Exception ex)
        {
            // The dialog is a convenience over a box that still works. Losing it must not take the
            // settings window down with it.
            Log.Error(FolderScope, "the folder picker could not be shown", ex);
        }
    }
}
