using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The settings rows for the two web widgets: the camera stream and the pinned page element.
/// <para>
/// Both address boxes are the trust boundary on their page — what is typed is what the widget
/// connects to — so each is checked by <see cref="WebInput"/> on commit and a refused address is
/// explained under the box rather than stored and silently refused later.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void VideoStream(List<FrameworkElement> rows, VideoStreamConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Stream"));

        rows.Add(AddressRow(
            "Address",
            "An MJPEG stream, a snapshot, MP4 or HLS, over http or https",
            "http://192.168.1.50:8080/?action=stream",
            () => config.Url,
            value => { config.Url = value; save(); },
            out _));

        rows.Add(FluentLayout.Header("Picture"));

        rows.Add(FluentLayout.Row(Glyphs.Fit, "Fit", "How the picture fills the widget",
            FluentLayout.Choice(
                [(StreamFit.Contain, "Whole picture"), (StreamFit.Cover, "Fill and crop"), (StreamFit.Stretch, "Stretch")],
                config.EffectiveFit,
                value => { config.Fit = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Rotate, "Rotation", "For a camera mounted sideways or upside down",
            FluentLayout.Choice(
                [
                    (StreamRotation.None, "None"),
                    (StreamRotation.Quarter, "90°"),
                    (StreamRotation.Half, "180°"),
                    (StreamRotation.ThreeQuarter, "270°"),
                ],
                config.EffectiveRotation,
                value => { config.Rotation = value; save(); })));

        FluentLayout.RefreshSeconds(
            rows,
            "a frame is grabbed. 0 keeps the stream open at the camera's own rate; anything else grabs one frame and hangs up until the next",
            config.EffectiveRefreshSeconds,
            VideoStreamRules.LiveRefreshSeconds,
            VideoStreamRules.MaxRefreshSeconds,
            1,
            value => { config.RefreshSeconds = value; save(); });

        rows.Add(FluentLayout.Note("Pause, rotate and reconnect from the widget's right-click menu. A paused camera stays paused across restarts and uses no network."));
    }

    public static void WebElement(List<FrameworkElement> rows, WebElementConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Page"));

        var selectorBox = new TextBox
        {
            Text = config.Selector,
            Width = 300,
            MaxLength = WebElementRules.MaxSelectorLength,
            PlaceholderText = "#price",
        };

        var pickedLabel = new TextBlock();
        var address = AddressRow(
            "Address",
            "The page the element is on, http or https",
            "https://example.com/",
            () => config.Url,
            value => { config.Url = value; save(); },
            out var addressBox);

        rows.Add(address);

        var pick = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { new FontIcon { Glyph = Glyphs.Pick, FontSize = 14 }, new TextBlock { Text = "Pick element…" } },
            },
        };

        void Describe()
        {
            var selector = config.EffectiveSelector;
            pickedLabel.Text = selector.Length == 0 ? "Nothing pinned yet"
                : config.PickedLabel.Length > 0 ? $"“{config.PickedLabel}”"
                : selector;
        }

        pick.Click += (_, _) =>
        {
            var start = config.ResolvedUrl;

            if (!start.IsValid || !Uri.TryCreate(start.Url, UriKind.Absolute, out var uri))
            {
                pickedLabel.Text = "Enter the page's address first";
                return;
            }

            ElementPickerWindow.Open(uri, message =>
            {
                // The page the user picked on, which may not be the one they started from: they
                // were free to navigate first.
                config.Url = message.Url;
                config.Selector = message.Selector;
                config.PickedLabel = message.Label;
                save();

                addressBox.Text = message.Url;
                selectorBox.Text = message.Selector;
                Describe();
            });
        };

        Describe();

        var pickRow = FluentLayout.Row(Glyphs.Pick, "Element",
            "Opens the page: browse to it, then click Pick element and click what to pin", pick);
        rows.Add(pickRow);
        rows.Add(FluentLayout.Row(Glyphs.Preview, "Pinned", null, pickedLabel));

        var verdict = FluentLayout.Verdict();

        void CommitSelector()
        {
            var typed = selectorBox.Text.Trim();

            if (typed == config.Selector)
            {
                return;
            }

            if (typed.Length > 0 && !WebElementRules.IsPlausibleSelector(typed))
            {
                FluentLayout.Refuse(verdict, "That is not a CSS selector");
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            config.Selector = typed;

            // A hand-written selector is not what the picker described any more.
            config.PickedLabel = string.Empty;
            save();
            Describe();
        }

        selectorBox.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitSelector();
            }
        };

        selectorBox.LostFocus += (_, _) => CommitSelector();

        rows.Add(FluentLayout.FieldRow(Glyphs.Code, "Selector",
            "The CSS selector the picker wrote. Edit it by hand if a page changed", selectorBox, verdict));

        FluentLayout.RefreshChoice(rows, WebElementRules.ClampRefreshMinutes(config.RefreshMinutes),
            value => { config.RefreshMinutes = value; save(); });

        rows.Add(FluentLayout.Note(
            "The page is drawn by a browser that runs only while the widget refreshes, then closes. Sign-ins and cookie choices made in the picker are kept for it."));
    }

    /// <summary>
    /// An address box that commits on Enter or on leaving it, checks with <see cref="WebInput"/>,
    /// and says why a refused address was refused. Never repaired into one that works.
    /// </summary>
    private static Border AddressRow(
        string title,
        string description,
        string placeholder,
        Func<string> current,
        Action<string> commit,
        out TextBox box)
    {
        box = new TextBox
        {
            Text = current(),
            Width = 300,
            MaxLength = WebInput.MaxUrlLength,
            PlaceholderText = placeholder,
        };

        var verdict = FluentLayout.Verdict();

        var input = box;

        void Commit()
        {
            var typed = input.Text.Trim();

            if (typed == current())
            {
                return;
            }

            var checkedUrl = WebInput.Url(typed);

            if (typed.Length > 0 && !checkedUrl.IsValid)
            {
                FluentLayout.Refuse(verdict, checkedUrl.Problem == WebUrlProblem.UnsupportedScheme
                    ? "Only http and https addresses"
                    : WebInput.Explain(checkedUrl.Problem));
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            commit(typed);
        }

        input.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        input.LostFocus += (_, _) => Commit();

        return FluentLayout.FieldRow(Glyphs.Link, title, description, input, verdict);
    }
}
