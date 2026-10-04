using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.WidgetsC_VideoStream_Settings_Stream.Text()));

        rows.Add(AddressRow(
            Str.WidgetsC_WebElement_Settings_Address_Label.Text(),
            Str.WidgetsC_VideoStream_Settings_Address_Desc.Text(),
            "http://192.168.1.50:8080/?action=stream",
            () => config.Url,
            value => { config.Url = value; save(); },
            out _));

        rows.Add(FluentLayout.Header(Str.WidgetsC_VideoStream_Settings_Picture.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Fit, Str.WidgetsC_VideoStream_Settings_Fit_Label.Text(), Str.VideoStream_FitDesc.Text(),
            FluentLayout.Choice(
                [(StreamFit.Contain, Str.WidgetsC_VideoStream_Settings_Fit_Whole.Text()), (StreamFit.Cover, Str.WidgetsC_VideoStream_Settings_Fit_Crop.Text()), (StreamFit.Stretch, Str.VideoStream_FitStretch.Text())],
                config.EffectiveFit,
                value => { config.Fit = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Rotate, Str.VideoStream_RotationLabel.Text(), Str.VideoStream_RotationDesc.Text(),
            FluentLayout.Choice(
                [
                    (StreamRotation.None, Str.Common_None.Text()),
                    (StreamRotation.Quarter, "90°"),
                    (StreamRotation.Half, "180°"),
                    (StreamRotation.ThreeQuarter, "270°"),
                ],
                config.EffectiveRotation,
                value => { config.Rotation = value; save(); })));

        FluentLayout.RefreshSeconds(
            rows,
            Str.WidgetsC_VideoStream_Settings_Refresh_Desc.Text(),
            config.EffectiveRefreshSeconds,
            VideoStreamRules.LiveRefreshSeconds,
            VideoStreamRules.MaxRefreshSeconds,
            1,
            value => { config.RefreshSeconds = value; save(); });

        rows.Add(FluentLayout.Header(Str.WidgetsC_VideoStream_Settings_Overlay.Text()));

        // [S-1312] Both off the picture until there is room: never on the small sizes.
        rows.Add(FluentLayout.Row(Glyphs.Mouse, Str.WidgetsC_VideoStream_Settings_Hover_Label.Text(),
            Str.WidgetsC_VideoStream_Settings_Hover_Desc.Text(),
            FluentLayout.Toggle(config.HoverControls, value => { config.HoverControls = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Info, Str.WidgetsC_VideoStream_Settings_Chip_Label.Text(),
            Str.WidgetsC_VideoStream_Settings_Chip_Desc.Text(),
            FluentLayout.Toggle(config.ShowStatus, value => { config.ShowStatus = value; save(); })));

        rows.Add(FluentLayout.Note(Str.WidgetsC_VideoStream_Settings_Note.Text()));
    }

    public static void WebElement(List<FrameworkElement> rows, WebElementConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsC_WebElement_Settings_Page.Text()));

        var selectorBox = new TextBox
        {
            Text = config.Selector,
            Width = 300,
            MaxLength = WebElementRules.MaxSelectorLength,
            PlaceholderText = "#price",
        };

        var pickedLabel = new TextBlock();
        var address = AddressRow(
            Str.WidgetsC_WebElement_Settings_Address_Label.Text(),
            Str.WidgetsC_WebElement_Settings_Address_Desc.Text(),
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
        var clear = FluentLayout.IconCommand(Glyphs.Clear, Str.WidgetsC_WebElement_Settings_ClearPinned.Text());

        void Describe()
        {
            var selector = config.EffectiveSelector;
            pickedLabel.Text = selector.Length == 0 ? Str.WidgetsC_WebElement_Settings_NothingPinned.Text()
                : config.PickedLabel.Length > 0 ? QuoteLayout.Quoted(config.PickedLabel)
                : selector;

            // [S-1315] The button says what it will do: pick, or pick a different element.
            pickText.Text = (selector.Length == 0 ? Str.WidgetsC_WebElement_Settings_PickEllipsis : Str.WidgetsC_WebElement_Settings_PickAgain).Text();
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
                pickedLabel.Text = Str.WidgetsC_WebElement_Settings_AddressFirst.Text();
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

        var pickRow = FluentLayout.Row(Glyphs.Pick, Str.WidgetsC_WebElement_Settings_Element_Label.Text(),
            Str.WidgetsC_WebElement_Settings_Element_Desc.Text(), pick);
        rows.Add(pickRow);
        pickedLabel.VerticalAlignment = VerticalAlignment.Center;
        pickedLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        pickedLabel.MaxWidth = 260;
        rows.Add(FluentLayout.Row(Glyphs.Preview, Str.WidgetsC_WebElement_Settings_Pinned.Text(), null, new StackPanel
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
                FluentLayout.Refuse(verdict, Str.WidgetsC_WebElement_Settings_NotSelector.Text());
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

        rows.Add(FluentLayout.FieldRow(Glyphs.Code, Str.WidgetsC_WebElement_Settings_Selector_Label.Text(),
            Str.WidgetsC_WebElement_Settings_Selector_Desc.Text(), selectorBox, verdict));

        rows.Add(FluentLayout.Header(Str.WidgetsC_VideoStream_Settings_Picture.Text()));

        // [S-1316] How the element fills the widget.
        rows.Add(FluentLayout.Row(Glyphs.Fit, Str.WidgetsC_VideoStream_Settings_Fit_Label.Text(), Str.WidgetsC_WebElement_Settings_Fit_Desc.Text(),
            FluentLayout.Choice(
                [(ElementFit.Whole, Str.WidgetsC_WebElement_Settings_Fit_Whole.Text()), (ElementFit.FillWidth, Str.WidgetsC_WebElement_Settings_Fit_Width.Text())],
                config.EffectiveFit,
                value => { config.Fit = value; save(); })));

        FluentLayout.RefreshChoice(rows, WebElementRules.ClampRefreshMinutes(config.RefreshMinutes),
            value => { config.RefreshMinutes = value; save(); });

        rows.Add(FluentLayout.Note(
            Str.WidgetsC_WebElement_Settings_Note.Text()));
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
                    ? Str.WidgetsC_WebElement_Settings_OnlyHttp.Text()
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
