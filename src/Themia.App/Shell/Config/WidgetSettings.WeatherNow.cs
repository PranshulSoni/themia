using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services.Web;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The current-conditions widget's settings rows, and the place picker both weather widgets share.
/// </summary>
internal static partial class WidgetSettings
{
    private const string WeatherScope = "weather";

    public static void WeatherNow(List<FrameworkElement> rows, WeatherNowConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

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

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    /// <summary>
    /// The place rows, shared by both weather widgets.
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

        var results = new ComboBox
        {
            Width = 260,
            PlaceholderText = "Pick a place",
            Visibility = Visibility.Collapsed,
        };

        var verdict = FluentLayout.Verdict();

        // One search at a time. Typing a new query while the last is still out cancels it, so a
        // slow answer for "par" cannot land on top of the results for "paris".
        CancellationTokenSource? search = null;

        async Task RunAsync(string query)
        {
            search?.Cancel();
            search?.Dispose();
            var cts = new CancellationTokenSource();
            search = cts;

            results.Items.Clear();
            results.Visibility = Visibility.Collapsed;
            FluentLayout.Confirm(verdict, "Searching…");

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
                    FluentLayout.Confirm(verdict, $"No place called \"{query}\"");
                    return;
                }

                if (fetchStatus != FetchStatus.Ok)
                {
                    Log.Warn(WeatherScope, $"place search failed: {FetchState.Message(fetchStatus)} {detail}".TrimEnd());
                    FluentLayout.Refuse(verdict, FetchState.Message(fetchStatus));
                    return;
                }

                foreach (var place in places)
                {
                    results.Items.Add(new ComboBoxItem { Content = place.Name, Tag = place });
                }

                results.Visibility = Visibility.Visible;
                FluentLayout.Confirm(verdict, $"{places.Count} matches — pick one");
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

                if (query.Length == 0)
                {
                    FluentLayout.Confirm(verdict, string.Empty);
                }
                else
                {
                    FluentLayout.Refuse(verdict, "Type a place name");
                }

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

        var chosen = FluentLayout.Note(config.HasLocation ? config.LocationName : "No place chosen");
        chosen.Margin = new Thickness(0);
        chosen.VerticalAlignment = VerticalAlignment.Center;

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
            FluentLayout.Confirm(verdict, string.Empty);
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
            FluentLayout.Confirm(verdict, string.Empty);
            save();
        };

        rows.Add(FluentLayout.FieldRow(Glyphs.Search, "Search", "Type a city and press Enter",
            new StackPanel
            {
                Spacing = Fluent.Space1,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { box, results },
            },
            verdict));

        rows.Add(FluentLayout.Row(Glyphs.Place, "Location", null, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { chosen, clear },
        }));
    }
}
