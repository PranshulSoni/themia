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

        rows.Add(FluentLayout.Header("Overlay"));

        // [S-1312] Both off the picture until there is room: never on the small sizes.
        rows.Add(FluentLayout.Row(Glyphs.Mouse, "Controls on hover",
            "With the pointer on the picture: live state and frame age from medium size; pause, rotate, refresh, resolution and frame rate from large",
            FluentLayout.Toggle(config.HoverControls, value => { config.HoverControls = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Info, "Status chip",
            "Always shows LIVE, reconnecting or the interval, and the stream type, at the largest size",
            FluentLayout.Toggle(config.ShowStatus, value => { config.ShowStatus = value; save(); })));

        rows.Add(FluentLayout.Note("Pause, rotate and reconnect from the widget's right-click menu too. A paused camera stays paused across restarts and uses no network. Cameras that need a user name and password are not supported."));
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

        var pickText = new TextBlock();
        var pick = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { new FontIcon { Glyph = Glyphs.Pick, FontSize = 14 }, pickText },
            },
        };

        // [S-1315] Unpin without hunting for the selector box.
        var clear = FluentLayout.IconCommand(Glyphs.Clear, "Clear the pinned element");

        void Describe()
        {
            var selector = config.EffectiveSelector;
            pickedLabel.Text = selector.Length == 0 ? "Nothing pinned yet"
                : config.PickedLabel.Length > 0 ? $"“{config.PickedLabel}”"
                : selector;

            // [S-1315] The button says what it will do: pick, or pick a different element.
            pickText.Text = selector.Length == 0 ? "Pick element…" : "Pick again…";
            clear.Visibility = selector.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        clear.Click += (_, _) =>
        {
            config.Selector = string.Empty;
            config.PickedLabel = string.Empty;
            save();
            selectorBox.Text = string.Empty;
            Describe();
        };

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
        pickedLabel.VerticalAlignment = VerticalAlignment.Center;
        pickedLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        pickedLabel.MaxWidth = 260;
        rows.Add(FluentLayout.Row(Glyphs.Preview, "Pinned", null, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { pickedLabel, clear },
        }));

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

        rows.Add(FluentLayout.Header("Picture"));

        // [S-1316] How the element fills the widget.
        rows.Add(FluentLayout.Row(Glyphs.Fit, "Fit", "Whole element shows all of it, larger as the widget grows; fill width uses the full width and cuts off the bottom",
            FluentLayout.Choice(
                [(ElementFit.Whole, "Whole element"), (ElementFit.FillWidth, "Fill width")],
                config.EffectiveFit,
                value => { config.Fit = value; save(); })));

        FluentLayout.RefreshChoice(rows, WebElementRules.ClampRefreshMinutes(config.RefreshMinutes),
            value => { config.RefreshMinutes = value; save(); });

        rows.Add(FluentLayout.Note(
            "The page is drawn by a browser that runs only while the widget refreshes, then closes. Sign-ins and cookie choices made in the picker are kept for it. The last picture is kept, so it shows at once after a restart. Pages on your local network are refused unless local network access is on in Settings › General."));
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
