using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The rows in their three modes, what a click does ([S-1291]), the item menu, and keyboard
/// navigation ([S-1288]): Up/Down, Page Up/Down, Home/End move, Enter opens (or walks in), Escape
/// lets go.
/// </summary>
internal sealed partial class OneDriveView
{
    /// <summary>The items the rows show, in the same order, for the keyboard.</summary>
    private IReadOnlyList<DriveItem> _shown = [];

    /// <summary>The focused item, by id, so a redraw (a refresh, a resize) keeps it.</summary>
    private string? _focusId;

    private int _focusIndex = -1;

    /// <summary>
    /// [S-606]/[S-1290] One item: its kind's icon and its name; then, by the tile's size, either a
    /// second line with <c>size · when</c>, or the details in columns on the same line.
    /// </summary>
    private Border BuildRow(DriveItem item, DateTimeOffset now)
    {
        var fit = _shape.Fit;
        var icon = Fluent.Icon(Glyph(DriveRules.Kind(item)), item.IsFolder ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = item.Name;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;

        var body = RowGrid(fit);
        body.Children.Add(icon);

        var metaText = DriveRules.Meta(item, now);

        if (fit.Mode == DriveRowMode.TwoLine && metaText.Length > 0)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(name);

            var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            line.Text = metaText;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(line);

            Grid.SetColumn(text, 1);
            body.Children.Add(text);
        }
        else
        {
            Grid.SetColumn(name, 1);
            body.Children.Add(name);
        }

        for (var i = 0; i < fit.Columns.Count; i++)
        {
            var column = fit.Columns[i].Column;
            var cell = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.Text = Cell(item, column, now);
            cell.TextTrimming = TextTrimming.CharacterEllipsis;
            cell.Margin = new Thickness(DriveLayout.Gap, 0, 0, 0);

            if (column == DriveColumn.Size)
            {
                // Sizes line up on their units, like Explorer's.
                cell.TextAlignment = TextAlignment.Right;
            }

            Grid.SetColumn(cell, i + 2);
            body.Children.Add(cell);
        }

        var row = Fluent.Row(body, fit.Mode == DriveRowMode.Columns || _shape.Compact);
        RowFocus.Prepare(row);
        row.Tag = item;
        _hovers.Add(Fluent.Hover(row));
        _rows.Add(row);

        row.Tapped += OnRowTapped;
        row.DoubleTapped += OnRowDoubleTapped;
        row.RightTapped += OnRowRightTapped;

        var how = (item.IsFolder
            ? Str.WidgetsC_OneDrive_Tip_OpenFolder
            : _config.ClickMode == ItemClickMode.Single ? Str.WidgetsC_OneDrive_Tip_ClickBrowser : Str.WidgetsC_OneDrive_Tip_DoubleClickBrowser).Text();
        var where = item.Location is { } location ? $"{Environment.NewLine}{Str.WidgetsC_OneDrive_Tip_In.Format(("location", location))}" : string.Empty;
        var who = item.ModifiedBy is { } by ? $"{Environment.NewLine}{Str.WidgetsC_OneDrive_Tip_ModifiedBy.Format(("name", by))}" : string.Empty;
        ToolTipService.SetToolTip(row, $"{item.Name}{(metaText.Length > 0 ? $"{Environment.NewLine}{metaText}" : string.Empty)}{who}{where}{Environment.NewLine}{how}");
        return row;
    }

    /// <summary>[S-1293] A detail column's text: the age, the size (a folder's item count), who, where.</summary>
    private static string Cell(DriveItem item, DriveColumn column, DateTimeOffset now) => column switch
    {
        DriveColumn.Modified => item.Modified is { } modified ? DriveRules.Ago(modified, now) : string.Empty,
        DriveColumn.Size => item.IsFolder ? DriveRules.ChildCountText(item.ChildCount) : Formatting.Bytes(item.Size),
        DriveColumn.ModifiedBy => item.ModifiedBy ?? string.Empty,
        _ => item.Location ?? string.Empty,
    };

    /// <summary>The icon, the name (taking what is left), then each detail column at its width.</summary>
    private static Grid RowGrid(DriveFit fit)
    {
        var grid = new Grid { ColumnSpacing = 0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        foreach (var column in fit.Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width + DriveLayout.Gap) });
        }

        return grid;
    }

    /// <summary>[S-1290] The column titles over the details, on the same grid as the rows.</summary>
    private void BuildColumnHeader()
    {
        _columnHeader.Children.Clear();
        _columnHeader.ColumnDefinitions.Clear();

        var fit = _shape.Fit;
        if (fit.Mode != DriveRowMode.Columns || !fit.ColumnHeader)
        {
            _columnHeader.Visibility = Visibility.Collapsed;
            return;
        }

        var template = RowGrid(fit);
        foreach (var definition in template.ColumnDefinitions.ToList())
        {
            template.ColumnDefinitions.Remove(definition);
            _columnHeader.ColumnDefinitions.Add(definition);
        }

        _columnHeader.Padding = new Thickness(Fluent.Space4 + RowFocus.Ring, 0, Fluent.Space4 + RowFocus.Ring, Fluent.Space1);

        // The icon's lane, so "Name" sits over the names.
        var lane = new Border { Width = Fluent.IconSize + Fluent.Space3 };
        _columnHeader.Children.Add(lane);

        AddTitle(Str.WidgetsC_OneDrive_Col_Name.Text(), 1, TextAlignment.Left);
        for (var i = 0; i < fit.Columns.Count; i++)
        {
            AddTitle(DriveLayout.Label(fit.Columns[i].Column), i + 2, fit.Columns[i].Column == DriveColumn.Size ? TextAlignment.Right : TextAlignment.Left);
        }

        _columnHeader.Visibility = Visibility.Visible;

        void AddTitle(string text, int column, TextAlignment alignment)
        {
            var title = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            title.Text = text;
            title.TextAlignment = alignment;
            title.Margin = new Thickness(column >= 2 ? DriveLayout.Gap : 0, 0, 0, 0);
            Grid.SetColumn(title, column);
            _columnHeader.Children.Add(title);
        }
    }

    private static string Glyph(DriveItemKind kind) => kind switch
    {
        DriveItemKind.Folder => Glyphs.Folder,
        DriveItemKind.Image => Glyphs.Gallery,
        DriveItemKind.Video => Glyphs.Video,
        DriveItemKind.Audio => Glyphs.Audio,
        DriveItemKind.Spreadsheet => Glyphs.Spreadsheet,
        DriveItemKind.Presentation => Glyphs.Presentation,
        DriveItemKind.Document => Glyphs.Document,
        _ => Glyphs.File,
    };

    // ---- clicks ------------------------------------------------------------------------------------

    /// <summary>
    /// [S-609]/[S-1291] A click walks into a folder; on a file it opens it when the widget is set to
    /// single click, and otherwise focuses the row for the keyboard.
    /// </summary>
    private void OnRowTapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is not Border { Tag: DriveItem item } row)
        {
            return;
        }

        args.Handled = true;

        if (item.IsFolder)
        {
            EnterFolder(item);
        }
        else if (_config.ClickMode == ItemClickMode.Single)
        {
            OpenOnWeb(item);
        }
        else
        {
            FocusRow(_rows.IndexOf(row));
        }
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is Border { Tag: DriveItem { IsFolder: false } file } && _config.ClickMode == ItemClickMode.Double)
        {
            args.Handled = true;
            OpenOnWeb(file);
        }
    }

    /// <summary>The item's own menu: open (or walk in), open on the web, copy its link.</summary>
    private void OnRowRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not Border { Tag: DriveItem item } row)
        {
            return;
        }

        ItemMenu.Show(row, args, ItemEntries(item));
    }

    private ItemMenuEntry[] ItemEntries(DriveItem item) =>
        [
            item.IsFolder
                ? new ItemMenuEntry(Glyphs.Folder, Str.Common_Open.Text(), () => EnterFolder(item))
                : new ItemMenuEntry(Glyphs.Open, Str.Github_OpenInBrowser.Text(), () => OpenOnWeb(item)),
            item.IsFolder ? new ItemMenuEntry(Glyphs.Open, Str.Github_OpenInBrowser.Text(), () => OpenOnWeb(item)) : ItemMenuEntry.Separator,
            new ItemMenuEntry(Glyphs.Link, Str.WidgetsC_OneDrive_CopyLink.Text(), () =>
            {
                if (!ClipboardWatcher.SetText(item.WebUrl))
                {
                    Themia.Core.Diagnostics.Log.Warn(Scope, "could not copy a link to the clipboard");
                }
            }, Disabled: item.WebUrl.Length == 0),
        ];

    // ---- keyboard ----------------------------------------------------------------------------------

    /// <summary>
    /// [S-1560] A key from the shell's keyboard lease, decided by <see cref="ListKeys"/>; true when it
    /// meant something here. A drive is read-only: no Delete, no F2. The Menu key opens the item's
    /// menu; Escape — also sent whenever the lease ends — drops the row focus.
    /// </summary>
    public bool HandleKey(WidgetKey key)
    {
        if (_detached)
        {
            return false;
        }

        var decision = ListKeys.Decide(key, _focusId is null ? -1 : _focusIndex, _shown.Count);

        switch (decision.Command)
        {
            case ListCommand.Focus:
                FocusRow(decision.Index);
                return true;

            case ListCommand.Open:
                var item = _shown[decision.Index];
                if (item.IsFolder)
                {
                    EnterFolder(item);
                }
                else
                {
                    OpenOnWeb(item);
                }

                return true;

            case ListCommand.Menu when decision.Index < _rows.Count:
                ItemMenu.Build(ItemEntries(_shown[decision.Index])).ShowAt(_rows[decision.Index]);
                return true;

            case ListCommand.Release:
                var had = _focusId is not null;
                ReleaseFocus();
                return had;

            default:
                return false;
        }
    }

    private void FocusRow(int index)
    {
        if (index < 0 || index >= _rows.Count || index >= _shown.Count)
        {
            return;
        }

        if (_focusId is not null && _focusIndex >= 0 && _focusIndex < _rows.Count)
        {
            RowFocus.Mark(_rows[_focusIndex], false);
        }

        _focusIndex = index;
        _focusId = _shown[index].Id;
        RowFocus.Mark(_rows[index], true);
    }

    private void ReleaseFocus()
    {
        if (_focusId is not null && _focusIndex >= 0 && _focusIndex < _rows.Count)
        {
            RowFocus.Mark(_rows[_focusIndex], false);
        }

        _focusId = null;
        _focusIndex = -1;
    }

    /// <summary>After a redraw: the focus back on its item, or on the row now where it was.</summary>
    private void RestoreFocus()
    {
        if (_focusId is null)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < _shown.Count; i++)
        {
            if (_shown[i].Id == _focusId)
            {
                index = i;
                break;
            }
        }

        index = index >= 0 ? index : ListKeys.AfterRemoval(_focusIndex, _shown.Count);
        if (index < 0)
        {
            _focusId = null;
            _focusIndex = -1;
            return;
        }

        FocusRow(index);
    }
}
