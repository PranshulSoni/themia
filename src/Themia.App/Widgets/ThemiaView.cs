using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The Themia mark, the name and the version ([S-484]), and on a large tile the things around them
/// ([S-1533]): a tagline and the maker (L), "Edit widgets" and "Settings" (L), and links to the
/// release notes, feedback and the source (XL).
/// <para>
/// Nothing ticks and nothing is fetched: the version is the running build's own, read once by
/// <see cref="AppInfo"/>, so it is known before the first frame. A strip lays the mark beside the
/// words rather than shrinking it into a centred stack; everywhere else the stack is centred.
/// </para>
/// </summary>
internal sealed partial class ThemiaView : Grid, IWidgetView, IWidgetSizeAware
{
    /// <summary>The mark is decoded at its drawn size rounded up to this, so a resize drag does not decode it per pixel.</summary>
    private const int DecodeStep = 32;

    /// <summary>The least the mark keeps before any words are given room: the logo is the widget.</summary>
    private const double MarkFloor = 56;

    /// <summary>A button row's line: the stock button height.</summary>
    private const double ButtonLine = 32;

    private static readonly Uri LogoUri = new(Path.Combine(AppContext.BaseDirectory, "Assets", "themia-logo.png"));

    private readonly Image _logo = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _name = Fluent.Text(FluentType.BodyStrong);
    private readonly TextBlock _version = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _tagline = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly StackPanel _maker;
    private readonly StackPanel _actions;
    private readonly StackPanel _links;
    private readonly StackPanel _words;
    private readonly StackPanel _stack;

    private WidgetArea _area;
    private int _decodedAt;
    private bool _detached;

    public ThemiaView()
    {
        _name.Text = "Themia";
        _version.Text = $"v{AppInfo.Version}";
        _tagline.TextWrapping = TextWrapping.Wrap;

        _maker = new StackPanel { Orientation = Orientation.Horizontal };
        _actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2 };
        _links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };
        Fill();

        _words = new StackPanel
        {
            Spacing = Fluent.Space1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _name, _version, _tagline, _maker, _actions, _links },
        };

        _stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _logo, _words },
        };

        Children.Add(_stack);
    }

    /// <summary>
    /// The words, set again on a language change: the tagline, the maker line, the buttons and the
    /// links are rebuilt (their captions are the buttons' content); the name and version are not words.
    /// </summary>
    private void Fill()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_logo, Str.WidgetsC_Themia_Logo.Text());
        _tagline.Text = Str.WidgetsC_Themia_Tagline.Text();

        var by = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        by.Text = Str.WidgetsC_Themia_MadeBy.Text();
        by.VerticalAlignment = VerticalAlignment.Center;

        Replace(_maker, by, AppActions.Link("Lometics", AppActions.MakerUrl));
        Replace(_actions, AppActions.EditLayout(), AppActions.Settings());
        Replace(_links,
            AppActions.Link(Str.WidgetsC_Themia_ReleaseNotes.Text(), AppActions.ReleaseNotesUrl),
            AppActions.Link(Str.WidgetsC_Themia_Feedback.Text(), AppActions.FeedbackUrl),
            AppActions.Link(Str.WidgetsC_Themia_Source.Text(), AppActions.SourceUrl));
    }

    private static void Replace(StackPanel panel, params UIElement[] children)
    {
        panel.Children.Clear();

        foreach (var child in children)
        {
            panel.Children.Add(child);
        }
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        Strings.Changed -= OnLanguage;
        Strings.Changed += OnLanguage;
    }

    private void OnLanguage(Locale locale)
    {
        if (_detached)
        {
            return;
        }

        Fill();
        Lay();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        // ignore: the Themia widget has no settings
    }

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
    }

    /// <summary>
    /// [S-1533] The mark keeps its floor first; then the name, the version, and — on a large tile —
    /// the tagline, the maker, the buttons and the links take their lines in that order; the mark
    /// takes what is left, up to <see cref="ThemiaLayout.MaxMark"/>. The words give way before the
    /// mark does: on a tile too small for all of it, a recognisable logo says more than a clipped
    /// version number.
    /// </summary>
    private void Lay()
    {
        if (_detached || !_area.IsMeasured)
        {
            return;
        }

        var box = _area.Deflate(Fluent.Space4, Fluent.Space4);
        var beside = ThemiaLayout.Beside(_area);

        var inner = Math.Min(box.Width, box.Height);
        var nameStep = ThemiaLayout.Links(_area) ? FluentType.TitleLarge
            : inner >= 240 || beside ? FluentType.Title
            : inner >= 140 ? FluentType.Subtitle
            : FluentType.BodyStrong;
        var smallStep = inner >= 240 || beside ? FluentType.Body : FluentType.Caption;
        _name.Apply(nameStep);
        _version.Apply(smallStep);
        _tagline.Apply(smallStep);

        double logo;

        if (beside)
        {
            // The words get the column right of the mark; the mark is as tall as the strip allows.
            logo = Math.Min(Math.Min(box.Height, ThemiaLayout.Mark(_area)), box.Width * 0.4);
            var words = new Disclosure(box.Width - logo - Fluent.Space4, box.Height, gap: Fluent.Space1);
            Show(_name, words.Take(nameStep.LineHeight));
            Show(_version, words.Take(smallStep.LineHeight));
            Show(_tagline, words.Take(smallStep.LineHeight));
            Show(_maker, ThemiaLayout.About(_area) && words.Take(ButtonLine));
            Show(_actions, ThemiaLayout.Actions(_area) && words.Take(ButtonLine));
            Show(_links, ThemiaLayout.Links(_area) && words.Take(ButtonLine));

            _stack.Orientation = Orientation.Horizontal;
            _stack.Spacing = Fluent.Space4;
            _words.HorizontalAlignment = HorizontalAlignment.Left;
            Align(TextAlignment.Left, HorizontalAlignment.Left);
        }
        else
        {
            var room = new Disclosure(box, gap: Fluent.Space2);
            room.Reserve(Math.Min(MarkFloor, inner));

            var name = room.Take(nameStep.LineHeight);
            var version = name && room.Take(smallStep.LineHeight);
            var about = version && ThemiaLayout.About(_area);
            var tagline = about && room.Take(smallStep.LineHeight);
            var maker = tagline && room.Take(ButtonLine);
            var actions = maker && ThemiaLayout.Actions(_area) && room.Take(ButtonLine);
            var links = actions && ThemiaLayout.Links(_area) && room.Take(ButtonLine);

            Show(_name, name);
            Show(_version, version);
            Show(_tagline, tagline);
            Show(_maker, maker);
            Show(_actions, actions);
            Show(_links, links);

            logo = Math.Min(Math.Min(MarkFloor, inner) + room.Remaining, box.Width);

            _stack.Orientation = Orientation.Vertical;
            _stack.Spacing = name ? Fluent.Space3 : 0;
            _words.HorizontalAlignment = HorizontalAlignment.Center;
            Align(TextAlignment.Center, HorizontalAlignment.Center);
        }

        logo = Math.Floor(Math.Clamp(logo, 16, ThemiaLayout.Mark(_area)));
        _logo.Width = logo;
        _logo.Height = logo;
        _logo.HorizontalAlignment = HorizontalAlignment.Center;
        _logo.VerticalAlignment = VerticalAlignment.Center;

        Decode(logo);
    }

    private void Align(TextAlignment text, HorizontalAlignment horizontal)
    {
        foreach (var block in (TextBlock[])[_name, _version, _tagline])
        {
            block.TextAlignment = text;
            block.HorizontalAlignment = horizontal;
        }

        _maker.HorizontalAlignment = horizontal;
        _actions.HorizontalAlignment = horizontal;
        _links.HorizontalAlignment = horizontal;
    }

    private static void Show(FrameworkElement element, bool shown)
        => element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Decode the mark at the size it is drawn, in DIPs: logical decoding lets WIC scale for the
    /// display, so the same widget is sharp at 200% without holding a 256px bitmap at 100%.
    /// </summary>
    private void Decode(double logoDip)
    {
        var target = (int)Math.Ceiling(logoDip / DecodeStep) * DecodeStep;
        if (target == _decodedAt)
        {
            return;
        }

        _decodedAt = target;
        _logo.Source = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = target,
            UriSource = LogoUri,
        };
    }

    public void Detach()
    {
        _detached = true;
        Strings.Changed -= OnLanguage;
    }
}
