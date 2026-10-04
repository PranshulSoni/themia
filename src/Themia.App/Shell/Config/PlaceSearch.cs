using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services.Web;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// What a settings page hands <see cref="PlaceSearch"/>: what is chosen now, and what to do when the
/// user picks a place or asks to change it. The search itself owns nothing in the config.
/// </summary>
/// <param name="Query">The text last searched for, to put back in the box.</param>
/// <param name="Chosen">The chosen place's name, or null when none is chosen (the search is then shown).</param>
/// <param name="QueryChanged">Remember what was typed (display only; never re-geocoded).</param>
/// <param name="Picked">A result was picked: store its coordinates and name, and save.</param>
/// <param name="Changed">"Change" was clicked: forget the choice if the widget can have none, and save.</param>
internal sealed record PlaceSearchBinding(
    string Query,
    string? Chosen,
    Action<string> QueryChanged,
    Action<GeocodedPlace> Picked,
    Action Changed);

/// <summary>
/// [S-732] The city search, as one reusable piece: both weather widgets use it, and any other widget
/// that needs a place by name (Sun &amp; Moon) can, with a <see cref="PlaceSearchBinding"/> of its own.
/// <para>
/// The original's behaviour: a search 400 ms after the last keystroke once two characters are typed,
/// Enter searches at once, the results open as a drop-down over the rows below rather than pushing
/// them down, and three outcomes said three ways — "Searching…", "No locations found." (not an
/// error) and "Search failed." (an error, logged). Once a place is chosen the search gives way to
/// one row naming it, with a Change button that brings the search back ([S-23] of the original).
/// </para>
/// <para>
/// The place resolves to coordinates <b>once</b>, at the pick, never on a refresh: re-geocoding
/// would double the requests and let the widget move on its own the day the geocoder changes its
/// mind about which Springfield is the popular one.
/// </para>
/// </summary>
internal static class PlaceSearch
{
    private const string Scope = "weather";

    /// <summary>How long typing has to pause before a search goes out: six requests to type "Zurich" is five too many.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    public static void Add(List<FrameworkElement> rows, PlaceSearchBinding binding)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(binding);

        var box = new TextBox
        {
            PlaceholderText = Str.Shell_Place_CityName.Text(),
            Text = binding.Query,
            Width = 260,
            MaxLength = WebInput.MaxPlaceLength,
        };

        // A drop-down rather than a list in the page: the results float over the rows below
        // instead of pushing them down, and close on a pick, Escape or a click elsewhere.
        var results = new ComboBox
        {
            Width = 260,
            PlaceholderText = Str.Shell_Place_PickPlace.Text(),
            Visibility = Visibility.Collapsed,
        };

        var verdict = FluentLayout.Verdict();

        var search = FluentLayout.FieldRow(Glyphs.Search, Str.Common_Search.Text(), Str.Shell_Place_SearchDesc.Text(),
            new StackPanel
            {
                Spacing = Fluent.Space1,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { box, results },
            },
            verdict);

        var chosen = FluentLayout.Note(binding.Chosen ?? string.Empty);
        chosen.Margin = new Thickness(0);
        chosen.VerticalAlignment = VerticalAlignment.Center;
        chosen.TextTrimming = TextTrimming.CharacterEllipsis;
        chosen.MaxWidth = 260;

        var change = new Button { Content = Str.Common_Change.Text() };

        var location = FluentLayout.Row(Glyphs.Place, Str.Shell_Place_Location.Text(), null, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { chosen, change },
        });

        void ShowChosen(string? name)
        {
            var has = !string.IsNullOrWhiteSpace(name);
            chosen.Text = name ?? string.Empty;
            location.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            search.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        }

        ShowChosen(binding.Chosen);

        // One search at a time. A new one cancels the last, so a slow answer for "par" cannot land
        // on top of the results for "paris".
        CancellationTokenSource? pending = null;
        var lastQuery = string.Empty;

        void Collapse()
        {
            results.Items.Clear();
            results.Visibility = Visibility.Collapsed;
        }

        async Task RunAsync(string query)
        {
            pending?.Cancel();
            var cts = new CancellationTokenSource();
            pending = cts;
            lastQuery = query;

            Collapse();
            FluentLayout.Confirm(verdict, Str.Shell_Place_Searching.Text());

            try
            {
                var (status, places, detail) = await WeatherService.FindAsync(query, cts.Token).ConfigureAwait(true);

                if (!ReferenceEquals(pending, cts))
                {
                    return;
                }

                if (status == FetchStatus.Empty)
                {
                    FluentLayout.Confirm(verdict, Str.Shell_Place_NoneFound.Text());
                    return;
                }

                if (status != FetchStatus.Ok)
                {
                    Log.Warn(Scope, $"place search failed: {FetchState.Message(status)} {detail}".TrimEnd());
                    FluentLayout.Refuse(verdict, Str.Shell_Place_SearchFailed.Text());
                    return;
                }

                foreach (var place in places)
                {
                    results.Items.Add(new ComboBoxItem { Content = place.Name, Tag = place });
                }

                FluentLayout.Confirm(verdict, string.Empty);
                results.Visibility = Visibility.Visible;
                results.IsDropDownOpen = true;
            }
            catch (OperationCanceledException)
            {
                // ignore: superseded by a newer search, which reports in this one's place.
            }
            finally
            {
                if (ReferenceEquals(pending, cts))
                {
                    pending = null;
                }

                cts.Dispose();
            }
        }

        void Start()
        {
            var query = box.Text.Trim();
            binding.QueryChanged(query);

            if (query.Length == 0)
            {
                pending?.Cancel();
                lastQuery = string.Empty;
                Collapse();
                FluentLayout.Confirm(verdict, string.Empty);
                return;
            }

            if (!WebInput.IsPlausiblePlace(query))
            {
                // Under two characters is not yet a question; anything else implausible is said.
                Collapse();
                FluentLayout.Confirm(verdict, query.Length < 2 ? string.Empty : Str.Shell_Place_TypeName.Text());
                return;
            }

            if (query == lastQuery && results.Items.Count > 0)
            {
                results.IsDropDownOpen = true;
                return;
            }

            _ = RunAsync(query);
        }

        // The timer belongs to this page's thread and dies with the page: stopped on unload, so a
        // keystroke made just before the settings window closed does not search into nowhere.
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (timer is not null)
        {
            timer.Interval = Debounce;
            timer.IsRepeating = false;
            timer.Tick += (_, _) => Start();
        }

        box.TextChanged += (_, _) =>
        {
            if (timer is null)
            {
                return;
            }

            timer.Stop();
            timer.Start();
        };

        box.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                timer?.Stop();
                Start();
            }
        };

        box.Unloaded += (_, _) =>
        {
            timer?.Stop();
            pending?.Cancel();
        };

        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not ComboBoxItem { Tag: GeocodedPlace place })
            {
                return;
            }

            binding.Picked(place);
            Collapse();
            FluentLayout.Confirm(verdict, string.Empty);
            ShowChosen(place.Name);
        };

        change.Click += (_, _) =>
        {
            binding.Changed();
            ShowChosen(null);
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        rows.Add(search);
        rows.Add(location);
    }
}
