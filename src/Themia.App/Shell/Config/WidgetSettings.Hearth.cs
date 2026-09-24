using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// The rows the contacts, calendar and task widgets contribute to their settings pages.
/// <para>
/// None of them offer to sign anything in. Hearth owns the accounts, the sync and the consent, so
/// all there is to choose here is which of the collections hearth has already granted a widget
/// shows - and each collection row says where its list came from, so an empty box reads as "hearth
/// granted nothing" rather than as a broken setting.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Contacts(List<FrameworkElement> rows, ContactsConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Contacts"));

        rows.Add(CollectionRow(
            Glyphs.Contact,
            "Address book",
            "All address books",
            AppServices.Data?.ContactCollections ?? [],
            "address books",
            config,
            save));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show details",
            "The first email address or phone number under each name",
            FluentLayout.Toggle(config.ShowDetail, value => { config.ShowDetail = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.View, "Show initials",
            "A round monogram beside each name",
            FluentLayout.Toggle(config.ShowAvatar, value => { config.ShowAvatar = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "People",
            "How many the list holds; a short widget shows fewer",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

    }

    public static void Calendar(List<FrameworkElement> rows, CalendarConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Calendar"));

        rows.Add(CollectionRow(
            Glyphs.Calendar,
            "Calendar",
            "All calendars",
            AppServices.Data?.CalendarCollections ?? [],
            "calendars",
            config,
            save));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "Look ahead", "How far into the future to list",
            FluentLayout.Slider(config.EffectiveDaysAhead, 1, 90, 1, " days",
                value => { config.DaysAhead = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Show all-day events",
            "Events that occupy a whole day rather than a time",
            FluentLayout.Toggle(config.ShowAllDay, value => { config.ShowAllDay = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Place, "Show location",
            "Where the event is, when hearth has one for it",
            FluentLayout.Toggle(config.ShowLocation, value => { config.ShowLocation = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Events",
            "How many the list holds; a short widget shows fewer",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

    }

    public static void Tasks(List<FrameworkElement> rows, TasksConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Tasks"));

        rows.Add(CollectionRow(
            Glyphs.Task,
            "Task list",
            "All task lists",
            AppServices.Data?.TaskCollections ?? [],
            "task lists",
            config,
            save));

        rows.Add(FluentLayout.Row(Glyphs.Done, "Show completed",
            "List finished tasks after the open ones",
            FluentLayout.Toggle(config.ShowCompleted, value => { config.ShowCompleted = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Show due date",
            "Overdue dates are marked",
            FluentLayout.Toggle(config.ShowDueDate, value => { config.ShowDueDate = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Tasks",
            "How many the list holds; a short widget shows fewer",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

    }

    /// <summary>
    /// The collection picker the three hearth widgets share: everything hearth granted for that
    /// record type, plus an "all of them" entry first.
    /// </summary>
    /// <param name="allLabel">What the leading "every collection" entry is called.</param>
    /// <param name="plural">
    /// The record type in the user's words, for the line under the box. It says where the list came
    /// from rather than offering to sign anything in, because nothing here can.
    /// </param>
    private static FrameworkElement CollectionRow(
        string glyph,
        string title,
        string allLabel,
        IReadOnlyList<HearthCollection> collections,
        string plural,
        HearthConfig config,
        Action save)
    {
        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem { Content = allLabel, Tag = string.Empty });

        foreach (var collection in collections)
        {
            combo.Items.Add(new ComboBoxItem { Content = Name(collection), Tag = collection.Id });
        }

        // A collection the user picked and hearth has since stopped granting is not in the list, so
        // the box falls back to "all" rather than sitting blank on a setting that means something.
        var selectedIndex = 0;
        for (var i = 0; i < collections.Count; i++)
        {
            if (collections[i].Id == config.CollectionId)
            {
                selectedIndex = i + 1;
                break;
            }
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id })
            {
                config.CollectionId = id;
                save();
            }
        };

        var description = AppServices.Hub?.Status switch
        {
            HearthStatus.Ready when collections.Count > 0 => "Granted by hearth",
            HearthStatus.Ready => $"hearth has granted no {plural}",
            HearthStatus.NotRunning => "hearth is not running",
            HearthStatus.Denied => "hearth has not granted access",
            _ => "Accounts are managed in hearth",
        };

        return FluentLayout.Row(glyph, title, description, combo);
    }

    /// <summary>
    /// What a collection is called in the box. Hearth's name is preferred; the account is appended
    /// when there is one, because "Calendar" on its own is ambiguous the moment two accounts are
    /// granted and both call their default calendar that.
    /// </summary>
    private static string Name(HearthCollection collection)
    {
        var name = string.IsNullOrWhiteSpace(collection.Name) ? collection.Id : collection.Name;

        return string.IsNullOrWhiteSpace(collection.Account) || string.Equals(collection.Account, name, StringComparison.Ordinal)
            ? name
            : $"{name} — {collection.Account}";
    }
}
