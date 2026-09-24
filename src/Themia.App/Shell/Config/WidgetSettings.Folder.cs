using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Interop;
using Themia.App.Services;
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
        rows.Add(FluentLayout.Header("Folder"));

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, 0),
        };

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

            ShowStatus(status, config, typed, result);
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

        ShowStatus(status, config, config.Path, FolderPath.Resolve(config.Path, string.Empty));

        var picker = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { box, browse },
        };

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Folder",
            "Which folder to list. Leave it empty for your user folder.", picker));

        rows.Add(status);

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Sort by", null,
            FluentLayout.Choice(
                [(FolderSort.Name, "Name"), (FolderSort.Modified, "Date modified"), (FolderSort.Size, "Size")],
                config.Sort,
                value => { config.Sort = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.MoveDown, "Reverse order",
            "Newest or largest first, depending on the sort",
            FluentLayout.Toggle(config.Descending, value => { config.Descending = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Folders first", "Group folders above files, as Explorer does",
            FluentLayout.Toggle(config.FoldersFirst, value => { config.FoldersFirst = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Hidden, "Show hidden items",
            "Include what Windows marks hidden or system",
            FluentLayout.Toggle(config.ShowHidden, value => { config.ShowHidden = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show size and date",
            "Hidden automatically when the widget is too narrow for it",
            FluentLayout.Toggle(config.ShowDetail, value => { config.ShowDetail = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Items", "How many entries the list shows",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 100, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

        var open = new Button { Content = "Open in Explorer" };
        open.Click += (_, _) =>
        {
            var result = FolderPath.Resolve(config.Path, ProfilePlaceholder());

            if (result.IsValid)
            {
                FolderListing.Open(result.Path);
            }
        };

        // The widget itself will not delete, rename or move anything, so this is the way out to the
        // place that will.
        rows.Add(FluentLayout.Row(Glyphs.Open, "Files", "This widget only lists and opens; edit them in Explorer", open));
    }

    /// <summary>
    /// Say what is wrong, or confirm what will actually be listed. Both are worth showing: a box
    /// that looks accepted but silently lists somewhere else is the failure this whole check exists
    /// to prevent.
    /// </summary>
    private static void ShowStatus(TextBlock status, FolderConfig config, string typed, FolderPathResult result)
    {
        if (typed.Trim().Length == 0)
        {
            status.Text = $"Empty: listing {ProfilePlaceholder()}";
            status.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
            return;
        }

        if (result.IsValid)
        {
            status.Text = $"Listing {FolderPath.DisplayName(config.Path)}";
            status.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
            return;
        }

        // Refused, and the box keeps what was typed so it can be corrected rather than retyped.
        status.Text = FolderFormatting.PathMessage(result.Problem);
        status.Foreground = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);
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
