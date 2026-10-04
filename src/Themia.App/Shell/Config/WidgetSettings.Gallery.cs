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
/// The Gallery widget's settings rows.
/// <para>
/// The folder and picture boxes are the controls here that are trust boundaries: whatever is typed
/// goes on to the filesystem, so it is checked by <see cref="FolderPath"/> before it is written and
/// the reason it was refused is shown under the box rather than swallowed. A refused value is not
/// corrected into a working one — the same rule the folder widget's box follows, and for the same
/// reason.
/// </para>
/// <para>
/// An empty box is not an error and not a fallback either. It means "nothing chosen", which the
/// widget says out loud: a photo frame that starts showing pictures nobody pointed it at, on a
/// surface that sits on the desktop in front of whoever walks past, is a different kind of mistake
/// from a file list showing the wrong folder.
/// </para>
/// <para>
/// Rows that do not apply to the chosen source or layout are hidden rather than disabled
/// ([S-36]): a "Pictures" slider on a photo-of-the-day widget is a setting that does nothing.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string GalleryScope = "gallery-settings";

    public static void Gallery(List<FrameworkElement> rows, GalleryConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        var folderOnly = new List<FrameworkElement>();
        var slideshowOnly = new List<FrameworkElement>();
        FrameworkElement? fileRow = null;
        FrameworkElement? captionRow = null;
        FrameworkElement? fitRow = null;

        void Update()
        {
            var folder = config.Source == GallerySource.Folder;

            foreach (var row in folderOnly)
            {
                row.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
            }

            foreach (var row in slideshowOnly)
            {
                row.Visibility = folder && config.Layout == GalleryLayout.Slideshow ? Visibility.Visible : Visibility.Collapsed;
            }

            fileRow!.Visibility = config.Source == GallerySource.File ? Visibility.Visible : Visibility.Collapsed;
            captionRow!.Visibility = config.Source == GallerySource.Bing ? Visibility.Visible : Visibility.Collapsed;

            // [S-15] A mosaic always crops to fill, so the choice would do nothing there.
            fitRow!.Visibility = folder && config.Layout == GalleryLayout.Mosaic ? Visibility.Collapsed : Visibility.Visible;
        }

        rows.Add(FluentLayout.Header("Gallery"));

        // [S-760] The source first: every other row depends on it.
        rows.Add(FluentLayout.Row(Glyphs.Gallery, "Source", "Where the pictures come from",
            FluentLayout.Choice(
                [(GallerySource.Folder, "A folder"), (GallerySource.File, "One picture"), (GallerySource.Bing, "Bing photo of the day")],
                config.Source,
                value => { config.Source = value; save(); Update(); })));

        var folderRow = GalleryPathRow(config, save, pickFile: false);
        rows.Add(folderRow);
        folderOnly.Add(folderRow);

        fileRow = GalleryPathRow(config, save, pickFile: true);
        rows.Add(fileRow);

        captionRow = FluentLayout.Row(Glyphs.Info, "Caption", "Show the photo's title and credit under it",
            FluentLayout.Toggle(config.ShowCaption, value => { config.ShowCaption = value; save(); }));
        rows.Add(captionRow);

        var recursive = FluentLayout.Row(Glyphs.Folder, "Include subfolders",
            "Look in the folders below this one too",
            FluentLayout.Toggle(config.Recursive, value => { config.Recursive = value; save(); }));
        rows.Add(recursive);
        folderOnly.Add(recursive);

        var layout = FluentLayout.Row(Glyphs.Grid, "Layout", "One at a time, tiles, prints on a table, or a mosaic",
            FluentLayout.Choice(
                [(GalleryLayout.Slideshow, "Slideshow"), (GalleryLayout.Grid, "Grid"), (GalleryLayout.Scatter, "Scatter"), (GalleryLayout.Mosaic, "Mosaic")],
                config.Layout,
                value => { config.Layout = value; save(); Update(); }));
        rows.Add(layout);
        folderOnly.Add(layout);

        fitRow = FluentLayout.Row(Glyphs.Scale, "Picture size",
            "Fill crops to the frame; fit shows the whole picture",
            FluentLayout.Choice(
                [(GalleryFit.Fill, "Fill"), (GalleryFit.Fit, "Fit")],
                config.Fit,
                value => { config.Fit = value; save(); }));
        rows.Add(fitRow);

        var count = FluentLayout.Row(Glyphs.Count, "Pictures", "How many are taken from the folder",
            FluentLayout.Slider(
                config.EffectiveMaxImages,
                1,
                GalleryConfig.MaxImageCount,
                1,
                string.Empty,
                value => { config.MaxImages = (int)Math.Round(value); save(); }));
        rows.Add(count);
        folderOnly.Add(count);

        var shuffle = FluentLayout.Row(Glyphs.Shuffle, "Shuffle",
            "Take a different selection each time the folder is read",
            FluentLayout.Toggle(config.Shuffle, value => { config.Shuffle = value; save(); }));
        rows.Add(shuffle);
        folderOnly.Add(shuffle);

        var open = new Button { Content = "Open in Explorer" };

        open.Click += (_, _) =>
        {
            var result = FolderPath.Validate(config.Path);

            if (result.IsValid)
            {
                Services.FolderListing.Open(result.Path);
            }
        };

        // The widget will not move, rename or delete anything, so this is the way out to the place
        // that will.
        var files = FluentLayout.Row(Glyphs.Open, "Files", "This widget only shows them; manage them in Explorer", open);
        rows.Add(files);
        folderOnly.Add(files);

        rows.Add(FluentLayout.Header("Updating"));

        var slide = FluentLayout.Row(Glyphs.Timer, "Slide time", "How long each picture is shown",
            FluentLayout.Slider(config.EffectiveSlideshowSeconds, GalleryConfig.MinSlideSeconds, 120, 1, "s",
                value => { config.SlideshowSeconds = (int)Math.Round(value); save(); }));
        rows.Add(slide);
        slideshowOnly.Add(slide);

        // [S-772] The original's cadences. A folder changes when something writes to it, which no
        // timer can know; this is the backstop, and "Refresh images" on the widget is the way now.
        var refreshOptions = GalleryConfig.RefreshChoices
            .Select(minutes => (minutes, minutes == 60 ? "Every hour" : $"Every {minutes} minutes"))
            .ToArray();

        rows.Add(FluentLayout.Row(Glyphs.Refresh, "Refresh", "How often the pictures are read again",
            FluentLayout.Choice(refreshOptions, config.EffectiveRefreshMinutes,
                value => { config.RefreshMinutes = value; save(); })));

        Update();
    }

    /// <summary>
    /// The folder box or the picture box: typed or browsed, validated on commit, with the verdict
    /// under it. One shape for both, so the two cannot drift on what a valid path is.
    /// </summary>
    private static Border GalleryPathRow(GalleryConfig config, Action save, bool pickFile)
    {
        var verdict = FluentLayout.Verdict();
        string Current() => pickFile ? config.FilePath : config.Path;

        var box = new TextBox
        {
            // Read once to seed the control, then never mirrored: the config stays the only copy of
            // the value, and the box writes to it rather than shadowing it.
            Text = Current(),
            Width = 260,
            PlaceholderText = pickFile ? "No picture chosen" : "No folder chosen",
        };

        var browse = new Button { Content = "Browse…" };

        void Commit()
        {
            var typed = box.Text;
            var blank = typed.Trim().Length == 0;
            var result = FolderPath.Validate(typed);

            // [S-761] A picture box also refuses a name that is not a picture: it would only come
            // back from the widget as "not a picture this app can show".
            var refusedType = pickFile && !blank && result.IsValid && !ImageFiles.IsImage(result.Path);

            if ((blank || result.IsValid) && !refusedType)
            {
                var value = blank ? string.Empty : result.Path;

                if (pickFile)
                {
                    config.FilePath = value;
                }
                else
                {
                    config.Path = value;
                }

                box.Text = value;
                save();
            }

            ShowGalleryVerdict(verdict, Current(), blank, result, pickFile, refusedType);
        }

        // On commit, not per keystroke: a path is only ever valid once it has been fully typed, so
        // validating each character would spend the whole time saying it is wrong.
        box.LostFocus += (_, _) => Commit();

        box.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        // The same picker and the same commit the folder widget uses, so there is one way into this
        // setting rather than two that can disagree about what a valid path is.
        browse.Click += (_, _) => _ = pickFile ? BrowsePictureAsync(box, Commit) : BrowseAsync(box, Commit);

        var current = Current();
        ShowGalleryVerdict(verdict, current, current.Trim().Length == 0, FolderPath.Validate(current), pickFile, refusedType: false);

        return FluentLayout.FieldRow(pickFile ? Glyphs.File : Glyphs.Folder, pickFile ? "Picture" : "Folder",
            pickFile ? "The one picture to show" : "Where the pictures are",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { box, browse },
            },
            verdict);
    }

    /// <summary>[S-761] Pick one picture, offering only the types the widget can draw.</summary>
    private static async Task BrowsePictureAsync(TextBox box, Action commit)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
            };

            foreach (var extension in ImageFiles.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            // The picker needs an owner window, and the click that opened it came from the settings
            // window, so the foreground window is that window at this instant.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32.GetForegroundWindow());

            var file = await picker.PickSingleFileAsync();

            if (file is null)
            {
                return; // ignore: the user cancelled the dialog.
            }

            box.Text = file.Path;
            commit();
        }
        catch (Exception ex)
        {
            // The dialog is a convenience over a box that still works. Losing it must not take the
            // settings window down with it.
            Log.Error(GalleryScope, "the picture picker could not be shown", ex);
        }
    }

    /// <summary>
    /// Say what is wrong, or confirm what will actually be shown. Both are worth saying: a box that
    /// looks accepted but silently reads somewhere else is the failure the whole check exists to
    /// prevent.
    /// </summary>
    private static void ShowGalleryVerdict(TextBlock verdict, string current, bool blank, FolderPathResult result, bool pickFile, bool refusedType)
    {
        if (refusedType)
        {
            FluentLayout.Refuse(verdict, "That is not a picture this app can show.");
            return;
        }

        if (blank)
        {
            FluentLayout.Confirm(verdict, pickFile ? "No picture chosen yet." : "No folder chosen yet.");
            return;
        }

        if (result.IsValid)
        {
            FluentLayout.Confirm(verdict, $"Showing {FolderPath.DisplayName(current)}");
            return;
        }

        // Refused, and the box keeps what was typed so it can be corrected rather than retyped.
        FluentLayout.Refuse(verdict, FolderFormatting.PathMessage(result.Problem));
    }
}
