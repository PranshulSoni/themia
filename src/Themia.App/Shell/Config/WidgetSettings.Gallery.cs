using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Gallery widget's settings rows.
/// <para>
/// The folder box is the one control here that is a trust boundary: whatever is typed goes on to a
/// filesystem walk, so it is checked by <see cref="FolderPath"/> before it is written and the reason
/// it was refused is shown under the box rather than swallowed. A refused value is not corrected
/// into a working one — the same rule the folder widget's box follows, and for the same reason.
/// </para>
/// <para>
/// An empty box is not an error and not a fallback either. It means "no folder chosen", which the
/// widget says out loud: a photo frame that starts showing pictures nobody pointed it at, on a
/// surface that sits on the desktop in front of whoever walks past, is a different kind of mistake
/// from a file list showing the wrong folder.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Gallery(List<FrameworkElement> rows, GalleryConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Gallery"));

        var verdict = FluentLayout.Verdict();

        var box = new TextBox
        {
            // Read once to seed the control, then never mirrored: the config stays the only copy of
            // the value, and the box writes to it rather than shadowing it.
            Text = config.Path,
            Width = 260,
            PlaceholderText = "No folder chosen",
        };

        var browse = new Button { Content = "Browse…" };

        void Commit()
        {
            var typed = box.Text;
            var blank = typed.Trim().Length == 0;
            var result = FolderPath.Validate(typed);

            if (blank || result.IsValid)
            {
                config.Path = blank ? string.Empty : result.Path;
                box.Text = config.Path;
                save();
            }

            ShowGalleryVerdict(verdict, config, blank, result);
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
        browse.Click += (_, _) => _ = BrowseAsync(box, Commit);

        ShowGalleryVerdict(verdict, config, config.Path.Trim().Length == 0, FolderPath.Validate(config.Path));

        rows.Add(FluentLayout.FieldRow(Glyphs.Gallery, "Folder", "Where the pictures come from",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { box, browse },
            },
            verdict));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Include subfolders",
            "Look in the folders below this one too",
            FluentLayout.Toggle(config.Recursive, value => { config.Recursive = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Grid, "Layout", "A slideshow of one, or a grid of many",
            FluentLayout.Choice(
                [(GalleryLayout.Slideshow, "Slideshow"), (GalleryLayout.Grid, "Grid")],
                config.Layout,
                value => { config.Layout = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Scale, "Picture size",
            "Fill crops to the card; fit shows the whole picture",
            FluentLayout.Choice(
                [(GalleryFit.Fill, "Fill"), (GalleryFit.Fit, "Fit")],
                config.Fit,
                value => { config.Fit = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Pictures", "How many are taken from the folder",
            FluentLayout.Slider(
                config.EffectiveMaxImages,
                1,
                GalleryConfig.MaxImageCount,
                1,
                string.Empty,
                value => { config.MaxImages = (int)Math.Round(value); save(); })));

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
        rows.Add(FluentLayout.Row(Glyphs.Open, "Files", "This widget only shows them; manage them in Explorer", open));

        rows.Add(FluentLayout.Header("Behaviour"));

        rows.Add(FluentLayout.Row(Glyphs.Shuffle, "Shuffle",
            "Take a different selection each time the folder is read",
            FluentLayout.Toggle(config.Shuffle, value => { config.Shuffle = value; save(); })));

        FluentLayout.RefreshSeconds(rows, "the slideshow moves on", config.EffectiveSlideshowSeconds,
            GalleryConfig.MinSlideSeconds, 120, 1,
            value => { config.SlideshowSeconds = value; save(); });
    }

    /// <summary>
    /// Say what is wrong, or confirm what will actually be shown. Both are worth saying: a box that
    /// looks accepted but silently reads somewhere else is the failure the whole check exists to
    /// prevent.
    /// </summary>
    private static void ShowGalleryVerdict(TextBlock verdict, GalleryConfig config, bool blank, FolderPathResult result)
    {
        if (blank)
        {
            FluentLayout.Confirm(verdict, "No folder chosen yet.");
            return;
        }

        if (result.IsValid)
        {
            FluentLayout.Confirm(verdict, $"Showing {FolderPath.DisplayName(config.Path)}");
            return;
        }

        // Refused, and the box keeps what was typed so it can be corrected rather than retyped.
        FluentLayout.Refuse(verdict, FolderFormatting.PathMessage(result.Problem));
    }
}
