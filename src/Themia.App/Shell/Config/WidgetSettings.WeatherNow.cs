using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services.Web;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    private const string WeatherScope = "weather";

    public static void WeatherNow(List<FrameworkElement> rows, WeatherNowConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Place"));
        AddPlacePicker(rows, config, save);

        rows.Add(FluentLayout.Header("Weather"));

        rows.Add(FluentLayout.Row(Glyphs.Weather, "Units", "Celsius or Fahrenheit",
            FluentLayout.Choice(
                [(TemperatureUnit.Celsius, "Celsius"), (TemperatureUnit.Fahrenheit, "Fahrenheit")],
                config.Unit,
                value => { config.Unit = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Info, "Show detail",
            "Feels-like, humidity and wind under the temperature",
            FluentLayout.Toggle(config.ShowDetails, value => { config.ShowDetails = value; save(); })));

        AddRefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    /// <summary>
    /// The place row, shared by both weather widgets.
    /// <para>
    /// A search box that resolves a name to coordinates <b>once</b>, at the moment the user picks a
    /// result, rather than a free-text place that is re-geocoded on every refresh. Re-geocoding
    /// would double the requests and, worse, let the widget's location move on its own the day the
    /// geocoder changes its mind about which Springfield is the popular one.
    /// </para>
    /// </summary>
    private static void AddPlacePicker(List<FrameworkElement> rows, WeatherConfig config, Action save)
    {
        var box = new TextBox
        {
            PlaceholderText = "City name",
            Text = config.Place,
            Width = 260,
            MaxLength = WebInput.MaxPlaceLength,
        };

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
        };

        var results = new ComboBox
        {
            Width = 260,
            PlaceholderText = "Pick a place",
            Visibility = Visibility.Collapsed,
        };

        var panel = new StackPanel { Spacing = 4, Children = { box, results, status } };

        // One search at a time. Typing a new query while the last is still out cancels it, so a
        // slow answer for "par" cannot land on top of the results for "paris".
        CancellationTokenSource? search = null;

        void Report(string message, bool bad)
        {
            status.Text = message;
            status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            status.Foreground = bad
                ? FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed)
                : FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        }

        async Task RunAsync(string query)
        {
            search?.Cancel();
            search?.Dispose();
            var cts = new CancellationTokenSource();
            search = cts;

            results.Items.Clear();
            results.Visibility = Visibility.Collapsed;
            Report("Searching…", bad: false);

            try
            {
                var (fetchStatus, places, detail) = await WeatherService
                    .FindAsync(query, cts.Token)
                    .ConfigureAwait(true);

                if (!ReferenceEquals(search, cts))
                {
                    return;
                }

                // Three outcomes, said three different ways. "No match" is not a failure and must
                // not be reported as one: a mistyped city and a dead network need very different
                // things from the person who just typed.
                if (fetchStatus == FetchStatus.Empty)
                {
                    Report($"No place called \"{query}\"", bad: false);
                    return;
                }

                if (fetchStatus != FetchStatus.Ok)
                {
                    Log.Warn(WeatherScope, $"place search failed: {FetchState.Message(fetchStatus)} {detail}".TrimEnd());
                    Report(FetchState.Message(fetchStatus), bad: true);
                    return;
                }

                foreach (var place in places)
                {
                    results.Items.Add(new ComboBoxItem { Content = place.Name, Tag = place });
                }

                results.Visibility = Visibility.Visible;
                Report($"{places.Count} matches — pick one", bad: false);
            }
            catch (OperationCanceledException)
            {
                // ignore: superseded by a newer search, which reports in this one's place.
            }
            finally
            {
                if (ReferenceEquals(search, cts))
                {
                    search = null;
                }

                cts.Dispose();
            }
        }

        void Start()
        {
            var query = box.Text.Trim();

            if (query == config.Place && results.Items.Count > 0)
            {
                return;
            }

            config.Place = query;

            if (!WebInput.IsPlausiblePlace(query))
            {
                results.Items.Clear();
                results.Visibility = Visibility.Collapsed;
                Report(query.Length == 0 ? string.Empty : "Type a place name", bad: query.Length > 0);
                return;
            }

            _ = RunAsync(query);
        }

        // On commit, not on every keystroke: a search per character is six requests to type
        // "Zurich", at a free service, to answer a question the user has not finished asking.
        box.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Start();
            }
        };

        box.LostFocus += (_, _) => Start();

        var chosen = new TextBlock
        {
            Text = config.HasLocation ? config.LocationName : "No place chosen",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
        };

        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not ComboBoxItem { Tag: GeocodedPlace place })
            {
                return;
            }

            config.Latitude = place.Latitude;
            config.Longitude = place.Longitude;
            config.LocationName = place.Name;
            chosen.Text = place.Name;
            Report(string.Empty, bad: false);
            save();
        };

        var clear = new Button { Content = "Clear" };

        clear.Click += (_, _) =>
        {
            // Null, not zero. Zero is the Gulf of Guinea, and a widget cleared to a real
            // coordinate would go on fetching the weather at sea.
            config.Latitude = null;
            config.Longitude = null;
            config.LocationName = string.Empty;
            config.Place = string.Empty;
            box.Text = string.Empty;
            chosen.Text = "No place chosen";
            results.Items.Clear();
            results.Visibility = Visibility.Collapsed;
            Report(string.Empty, bad: false);
            save();
        };

        rows.Add(FluentLayout.Row(Glyphs.Search, "Search", "Type a city and press Enter", panel));
        rows.Add(FluentLayout.Row(Glyphs.Place, "Location", null, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { chosen, clear },
        }));
    }

    /// <summary>
    /// The cadence row, shared by all four network widgets.
    /// <para>
    /// A fixed set rather than a free number, and nothing under a minute anywhere. Every service
    /// behind these widgets is free, shared and unauthenticated, and a slider that goes to "every
    /// second" is an invitation to get the whole app blocked.
    /// </para>
    /// </summary>
    private static void AddRefreshChoice(
        List<FrameworkElement> rows,
        int current,
        Action<int> onChanged,
        bool fast = false)
    {
        (int Value, string Label)[] options = fast
            ? [(1, "Every minute"), (5, "Every 5 minutes"), (15, "Every 15 minutes"), (30, "Every 30 minutes")]
            : [(5, "Every 5 minutes"), (15, "Every 15 minutes"), (30, "Every 30 minutes"), (60, "Every hour")];

        // A persisted value outside the set — hand-edited, or left over from an older build —
        // would otherwise leave the box blank and the setting pointing at nothing visible.
        var selected = current;
        var known = false;

        foreach (var (value, _) in options)
        {
            known |= value == selected;
        }

        if (!known)
        {
            // The second entry either way: five minutes on a fast widget, fifteen on a slow one.
            selected = options[1].Value;
            onChanged(selected);
        }

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Refresh", "How often to ask the service",
            FluentLayout.Choice(options, selected, onChanged)));
    }
}
