using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services.Files;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Files;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>[S-1329]..[S-1331] Opening, the menus, the clipboard, delete, rename, new, search.</summary>
internal sealed partial class FolderView
{
    /// <summary>Opening more than this many at once asks nothing, it just stops: a stray Ctrl+A, Enter is not forty windows.</summary>
    private const int OpenAtMost = 15;

    private RenameSession? _renaming;

    /// <summary>A new folder or file to rename as soon as the reload shows it ([S-1331]).</summary>
    private string? _renameAfterLoad;

    private bool _closingSearch;

    private sealed record RenameSession(ItemVisual Visual, TextBox Box);

    /// <summary>Where the last menu opened, in screen pixels: "Show more options" opens the shell's menu there.</summary>
    private (int X, int Y) _menuPoint;

    private void RememberMenuPoint(UIElement element, Windows.Foundation.Point local)
    {
        var root = element.TransformToVisual(null).TransformPoint(local);
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        _menuPoint = ShellContextMenu.ToScreen(Window(), (int)(root.X * scale), (int)(root.Y * scale));
    }

    /// <summary>[S-1330] Windows' own menu for the selection, or for the folder's background.</summary>
    private void ShowShellMenu(IReadOnlyList<string>? items)
    {
        var (x, y) = _menuPoint;

        if (items is { Count: > 0 })
        {
            ShellContextMenu.ShowForItems(items, x, y, Window());
        }
        else if (CurrentPath.Length > 0)
        {
            ShellContextMenu.ShowForBackground(CurrentPath, x, y, Window());
        }
    }

    // ---- opening --------------------------------------------------------------------------------

    /// <summary>
    /// [S-1327] Open an item: a folder inside the widget (navigate mode) or in Explorer; a shortcut to a
    /// folder likewise, resolved first; anything else as Explorer would.
    /// </summary>
    private void OpenEntry(FolderEntry entry)
    {
        if (entry.IsDirectory)
        {
            if (Navigates)
            {
                NavigateTo(entry.FullPath);
            }
            else
            {
                ShellFileOps.Open(entry.FullPath);
            }

            return;
        }

        if (Navigates && entry.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && ShellFileOps.ResolveShortcut(entry.FullPath) is { } target && Directory.Exists(target))
        {
            NavigateTo(target);
            return;
        }

        ShellFileOps.Open(entry.FullPath);
    }

    private void OpenSelection()
    {
        var selected = SelectedEntries();

        if (selected.Count == 1)
        {
            OpenEntry(selected[0]);
            return;
        }

        foreach (var entry in selected.Take(OpenAtMost))
        {
            ShellFileOps.Open(entry.FullPath);
        }
    }

    private List<FolderEntry> SelectedEntries() => [.. _shown.Where(e => _selection.IsSelected(e.FullPath))];

    // ---- clipboard ------------------------------------------------------------------------------

    /// <summary>[S-1329] Ctrl+C / Ctrl+X: the selection onto the Windows clipboard, as Explorer puts it there.</summary>
    private void CopySelection(bool cut)
    {
        var paths = _selection.Selected;

        if (paths.Count > 0 && !FileClipboard.SetFiles(paths, cut))
        {
            Log.Warn(Scope, "the selection could not be put on the clipboard");
        }
    }

    /// <summary>
    /// [S-1329] Ctrl+V: files copied or cut anywhere (Explorer, another folder widget) into
    /// <paramref name="destination"/>; a cut moves and then empties the clipboard; virtual files (an
    /// Outlook attachment) are saved.
    /// </summary>
    private async Task PasteAsync(string destination)
    {
        try
        {
            if (destination.Length == 0 || FileClipboard.Read() is not { } content)
            {
                return;
            }

            if (content.Paths.Count > 0)
            {
                if (content.Effect == DropEffect.Move)
                {
                    if (await ShellFileOps.MoveAsync(content.Paths, destination).ConfigureAwait(true) == FileOpOutcome.Done)
                    {
                        FileClipboard.CompleteCut();
                    }
                }
                else
                {
                    await ShellFileOps.CopyAsync(content.Paths, destination).ConfigureAwait(true);
                }
            }
            else if (content.VirtualFiles > 0)
            {
                FileClipboard.SaveVirtualFiles(destination);
                Reload(force: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "paste failed", ex);
        }
    }

    /// <summary>Copy path: Explorer's "Copy as path", quoted, one per line.</summary>
    private static void CopyPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(string.Join("\r\n", paths.Select(p => $"\"{p}\"")));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not copy the path", ex);
        }
    }

    // ---- delete ---------------------------------------------------------------------------------

    /// <summary>[S-1330] Delete: asks first, then the Recycle Bin through the shell. Never permanent.</summary>
    private async Task DeleteSelectionAsync()
    {
        try
        {
            var paths = _selection.Selected;

            if (paths.Count == 0)
            {
                return;
            }

            var what = paths.Count == 1 ? $"\"{Path.GetFileName(paths[0])}\"" : $"these {paths.Count} items";

            if (!await ConfirmAsync($"Move {what} to the Recycle Bin?", "Delete").ConfigureAwait(true))
            {
                return;
            }

            var outcome = await ShellFileOps.RecycleAsync(paths).ConfigureAwait(true);

            if (outcome == FileOpOutcome.Failed)
            {
                Log.Warn(Scope, $"{paths.Count} item(s) could not be moved to the Recycle Bin");
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "delete failed", ex);
        }
    }

    /// <summary>
    /// A small confirmation at the widget itself — not a dialog over the whole desktop surface, which
    /// is the size of the monitor. Escape, a click elsewhere or Cancel are all "no".
    /// </summary>
    private Task<bool> ConfirmAsync(string question, string action)
    {
        var answer = new TaskCompletionSource<bool>();

        var text = Fluent.Text(FluentType.Body);
        text.Text = question;
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxWidth = 260;

        var yes = new Button { Content = action, Style = Application.Current.Resources["AccentButtonStyle"] as Style };
        var no = new Button { Content = "Cancel" };

        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = Fluent.Space3,
                Children =
                {
                    text,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } },
                },
            },
            Placement = FlyoutPlacementMode.Bottom,
        };

        yes.Click += (_, _) =>
        {
            answer.TrySetResult(true);
            flyout.Hide();
        };
        no.Click += (_, _) => flyout.Hide();
        flyout.Closed += (_, _) => answer.TrySetResult(false);

        try
        {
            flyout.ShowAt(this);
        }
        catch (Exception ex)
        {
            // No window to show it in: refusing beats deleting unasked.
            Log.Warn(Scope, "the confirmation could not be shown; nothing was deleted", ex);
            answer.TrySetResult(false);
        }

        return answer.Task;
    }

    // ---- rename ---------------------------------------------------------------------------------

    private void RenameSelection()
    {
        if (_selection.Count != 1 || _visuals.FirstOrDefault(v => _selection.IsSelected(v.Entry.FullPath)) is not { } visual)
        {
            return;
        }

        BeginRename(visual);
    }

    /// <summary>
    /// [S-1331] Rename in place: the name becomes a text box holding the name as shown (without the
    /// extension when extensions are hidden, as Explorer does); Enter or a click elsewhere commits,
    /// Escape cancels.
    /// </summary>
    private void BeginRename(ItemVisual visual)
    {
        CancelRename();

        var shown = FileNames.Display(visual.Entry.Name, visual.Entry.IsDirectory, _config.ShowExtensions);
        var box = new TextBox
        {
            Text = shown,
            Padding = new Thickness(4, 0, 4, 0),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

        _renaming = new RenameSession(visual, box);
        visual.Name.Visibility = Visibility.Collapsed;
        visual.NameCell.Children.Add(box);

        box.GotFocus += (_, _) => _surface?.RequestTextInput(true);
        box.LostFocus += (_, _) =>
        {
            _surface?.RequestTextInput(false);
            CommitRename();
        };
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                CommitRename();
            }
            else if (args.Key == Windows.System.VirtualKey.Escape)
            {
                args.Handled = true;
                CancelRename();
            }
        };

        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);

            // The stem selected, the extension left alone, as Explorer selects it.
            var dot = visual.Entry.IsDirectory ? -1 : shown.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : shown.Length);
        };
    }

    private void CommitRename()
    {
        if (_renaming is not { } session)
        {
            return;
        }

        _renaming = null;
        var entry = session.Visual.Entry;
        var name = FileNames.FromEdited(entry.Name, session.Box.Text, entry.IsDirectory, _config.ShowExtensions);
        EndRename(session);

        if (string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        var problem = FileNames.Validate(name);

        if (problem != FileNameProblem.None)
        {
            ShowNote(session.Visual.Root, FileNames.Message(problem));
            return;
        }

        _ = RenameAsync(entry.FullPath, name);
    }

    private async Task RenameAsync(string path, string name)
    {
        try
        {
            if (await ShellFileOps.RenameAsync(path, name).ConfigureAwait(true) == FileOpOutcome.Done)
            {
                // Keep the renamed item selected once the reload draws it under its new name.
                var renamed = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, name);
                _selection.SetOrder([.. _shown.Select(e => e.FullPath), renamed]);
                _selection.SelectOnly(renamed);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "rename failed", ex);
        }
    }

    private void CancelRename()
    {
        if (_renaming is { } session)
        {
            _renaming = null;
            EndRename(session);
        }
    }

    private static void EndRename(RenameSession session)
    {
        session.Visual.NameCell.Children.Remove(session.Box);
        session.Visual.Name.Visibility = Visibility.Visible;
    }

    private static void ShowNote(FrameworkElement at, string message)
    {
        var text = Fluent.Text(FluentType.Body);
        text.Text = message;
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxWidth = 240;

        try
        {
            new Flyout { Content = text }.ShowAt(at);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, $"could not show: {message}", ex);
        }
    }

    // ---- new ------------------------------------------------------------------------------------

    /// <summary>[S-1331] New folder: created, then renamed in place as soon as the reload shows it.</summary>
    private void NewFolder()
    {
        if (CurrentPath.Length > 0 && ShellFileOps.NewFolder(CurrentPath) is { } created)
        {
            _renameAfterLoad = created;
            Reload(force: false);
        }
    }

    private void NewFile(ShellNewTemplate template)
    {
        if (CurrentPath.Length > 0 && ShellFileOps.NewFile(CurrentPath, template) is { } created)
        {
            _renameAfterLoad = created;
            Reload(force: false);
        }
    }

    // ---- menus ----------------------------------------------------------------------------------

    private void OnBackgroundRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            CommitRename();

            RememberMenuPoint(_scrollContent, e.GetPosition(_scrollContent));

            if (VisualAt(e.OriginalSource) is { } visual)
            {
                _selection.Context(visual.Index);
                RefreshSelectionVisuals();
                ItemMenu.Show(visual.Root, e, ItemEntries());
            }
            else
            {
                ItemMenu.Show(_scrollContent, e, BackgroundEntries());
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the menu could not be opened", ex);
        }
    }

    private void ShowMenuFromKeyboard()
    {
        var focus = _selection.FocusIndex;
        var menu = focus >= 0 && focus < _visuals.Count && !_selection.IsEmpty
            ? ItemMenu.Build(ItemEntries())
            : ItemMenu.Build(BackgroundEntries());

        FrameworkElement anchor = focus >= 0 && focus < _visuals.Count ? _visuals[focus].Root : this;
        RememberMenuPoint(anchor, new Windows.Foundation.Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
        menu.ShowAt(anchor);
    }

    /// <summary>[S-1330] The item menu: Explorer's, in Explorer's order.</summary>
    private List<ItemMenuEntry> ItemEntries()
    {
        var selected = SelectedEntries();
        var paths = selected.Select(e => e.FullPath).ToList();
        var count = selected.Count;
        var several = count > 1 ? $" ({count})" : string.Empty;
        var single = count == 1 ? selected[0] : (FolderEntry?)null;
        var hasCloud = selected.Any(e => CloudBadges.State(e.Attributes) != CloudState.None);

        var entries = new List<ItemMenuEntry>
        {
            new(Glyphs.Open, "Open", OpenSelection),
        };

        if (single is { IsDirectory: false } file)
        {
            entries.Add(new(Glyphs.OpenWith, "Open with…", () => ShellFileOps.OpenWith(file.FullPath)));
        }

        entries.Add(new(Glyphs.Folder, "Show in Explorer", () => ShellFileOps.ShowInExplorer(paths)));
        entries.Add(ItemMenuEntry.Separator);
        entries.Add(new(Glyphs.Cut, "Cut" + several, () => CopySelection(cut: true)));
        entries.Add(new(Glyphs.Copy, "Copy" + several, () => CopySelection(cut: false)));

        // Paste into the folder that was right-clicked, as Explorer's "Paste" on a folder does.
        var pasteTarget = single is { IsDirectory: true } folder ? folder.FullPath : CurrentPath;
        entries.Add(new(Glyphs.Paste, "Paste", () => _ = PasteAsync(pasteTarget), Disabled: !FileClipboard.HasFiles || pasteTarget.Length == 0));
        entries.Add(new(Glyphs.Link, "Copy path", () => CopyPaths(paths)));
        entries.Add(ItemMenuEntry.Separator);

        if (single is { } one && _visuals.FirstOrDefault(v => v.Entry.FullPath == one.FullPath) is { } visual)
        {
            entries.Add(new(Glyphs.Rename, "Rename", () => BeginRename(visual)));
        }

        entries.Add(new(Glyphs.Delete, "Delete" + several, () => _ = DeleteSelectionAsync(), Danger: true));

        if (hasCloud)
        {
            entries.Add(ItemMenuEntry.Separator);
            entries.Add(new(Glyphs.Pin, "Always keep on this device", () => ShellFileOps.SetCloudPin(paths, CloudPin.Pin)));
            entries.Add(new(Glyphs.Unpin, "Free up space", () => ShellFileOps.SetCloudPin(paths, CloudPin.Unpin)));
        }

        entries.Add(ItemMenuEntry.Separator);
        entries.Add(new(Glyphs.Info, "Properties", () => ShellFileOps.Properties(paths)));
        entries.Add(new(Glyphs.More, "Show more options", () => ShowShellMenu(paths)));
        return entries;
    }

    /// <summary>[S-1331] The background menu: paste, select all, copy path, New, open, properties, refresh.</summary>
    private List<ItemMenuEntry> BackgroundEntries()
    {
        var here = CurrentPath;
        var available = here.Length > 0 && _snapshot.Status is FolderListingStatus.Ok or FolderListingStatus.Empty;

        var templates = ShellNewTemplates.GetAsync() is { IsCompletedSuccessfully: true } done ? done.Result : [];
        var newItems = new List<ItemMenuEntry> { new(Glyphs.NewFolder, "Folder", NewFolder) };

        if (templates.Count > 0)
        {
            newItems.Add(ItemMenuEntry.Separator);
            newItems.AddRange(templates.Select(t => new ItemMenuEntry(Glyphs.File, t.Label, () => NewFile(t))));
        }

        var entries = new List<ItemMenuEntry>
        {
            new(Glyphs.Paste, "Paste", () => _ = PasteAsync(here), Disabled: !available || !FileClipboard.HasFiles),
            new(Glyphs.Done, "Select all", () =>
            {
                _selection.SelectAll();
                RefreshSelectionVisuals();
            }, Disabled: _shown.Count == 0),
            new(Glyphs.Link, "Copy path", () => CopyPaths([here]), Disabled: here.Length == 0),
            ItemMenuEntry.Separator,
            new(Glyphs.Add, "New", null, Disabled: !available, Children: newItems),
            ItemMenuEntry.Separator,
        };

        if (Navigates && _history.CanGoUp)
        {
            entries.Add(new(Glyphs.MoveUp, "Up", GoUp));
        }

        entries.Add(new(Glyphs.OpenExternal, "Open in Explorer", () => ShellFileOps.Open(here), Disabled: here.Length == 0));
        entries.Add(new(Glyphs.Info, "Properties", () => ShellFileOps.Properties([here]), Disabled: here.Length == 0));
        entries.Add(new(Glyphs.Refresh, "Refresh", () => Reload(force: false)));
        entries.Add(new(Glyphs.More, "Show more options", () => ShowShellMenu(null), Disabled: here.Length == 0));
        return entries;
    }

    /// <summary>[S-1322] The column header's menu: every column, checked when it is on.</summary>
    private void OnColumnHeaderRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var entries = FolderColumns.All
            .Select(column => new ItemMenuEntry(null, FolderColumns.Title(column),
                () => Edit(c => c.Columns = FolderColumns.Toggle(c.Columns, column)),
                Checked: _config.Columns.Contains(column)))
            .ToList();

        ItemMenu.Show(_columnHeader, e, entries);
    }

    /// <summary>
    /// [S-1330] The card's own menu (right-click on the header): Explorer, refresh, the view, the size,
    /// hidden items and extensions, each a one-click switch whose current choice carries a check.
    /// </summary>
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            var actions = new List<WidgetAction>
            {
                new(Glyphs.OpenExternal, "Open in Explorer", () => ShellFileOps.Open(CurrentPath)),
                new(Glyphs.Refresh, "Refresh", () => Reload(force: false)),
            };

            foreach (var (mode, label, glyph) in new[]
            {
                (FolderViewMode.List, "List", Glyphs.ListLayout),
                (FolderViewMode.Details, "Details", Glyphs.ColumnsLayout),
                (FolderViewMode.Grid, "Grid", Glyphs.GridLayout),
                (FolderViewMode.Icons, "Icons", Glyphs.Apps),
            })
            {
                actions.Add(new(_config.View == mode ? Glyphs.Done : glyph, label + " view", () => Edit(c => c.View = mode)));
            }

            if (_config.View is FolderViewMode.Grid or FolderViewMode.Icons)
            {
                foreach (var (size, label) in new[] { (FolderTileSize.Small, "Small"), (FolderTileSize.Medium, "Medium"), (FolderTileSize.Large, "Large") })
                {
                    actions.Add(new(_config.TileSize == size ? Glyphs.Done : Glyphs.Scale, $"{label} tiles", () => Edit(c => c.TileSize = size)));
                }
            }
            else
            {
                foreach (var (size, label) in new[]
                {
                    (FolderItemSize.ExtraSmall, "Extra small"), (FolderItemSize.Small, "Small"),
                    (FolderItemSize.Medium, "Medium"), (FolderItemSize.Large, "Large"),
                })
                {
                    actions.Add(new(_config.ItemSize == size ? Glyphs.Done : Glyphs.TextSize, $"{label} rows", () => Edit(c => c.ItemSize = size)));
                }
            }

            actions.Add(new(_config.ShowHidden ? Glyphs.Done : Glyphs.Hidden, "Show hidden items", () => Edit(c => c.ShowHidden = !c.ShowHidden)));
            actions.Add(new(_config.ShowExtensions ? Glyphs.Done : Glyphs.File, "Show file extensions", () => Edit(c => c.ShowExtensions = !c.ShowExtensions)));
            return actions;
        }
    }

    // ---- search ---------------------------------------------------------------------------------

    /// <summary>[S-1326] The header's search: a box above the list that filters as you type.</summary>
    private void ToggleSearch()
    {
        if (_searchOpen)
        {
            CloseSearch();
            return;
        }

        _searchOpen = true;
        Lay();
        _searchBox.Visibility = Visibility.Visible;
        _searchBox.Focus(FocusState.Programmatic);
    }

    private void CloseSearch()
    {
        if (!_searchOpen && _search.Length == 0)
        {
            return;
        }

        _searchOpen = false;
        _closingSearch = true;
        _searchBox.Text = string.Empty;
        _closingSearch = false;
        _search = string.Empty;
        Lay();
        Render();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseSearch();
        }
        else if (e.Key == Windows.System.VirtualKey.Enter && _shown.Count > 0)
        {
            e.Handled = true;
            OpenEntry(_shown[0]);
        }
    }
}
