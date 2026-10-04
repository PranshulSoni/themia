using System.Text.Json.Serialization;
using Themia.Core.Office;

namespace Themia.Core.Model;

/// <summary>[S-561] The four ways the calendar widget lays out time.</summary>
[JsonConverter(typeof(LenientEnumConverter<CalendarViewMode>))]
public enum CalendarViewMode
{
    /// <summary>A six-week grid, the original's default.</summary>
    Month = 0,

    /// <summary>Seven day columns.</summary>
    Week = 1,

    /// <summary>One day on an hour timeline.</summary>
    Day = 2,

    /// <summary>A list of what is coming up, grouped by day.</summary>
    Agenda = 3,
}

/// <summary>[S-564] Which day a week starts on: a closed set, never a stringly day name.</summary>
[JsonConverter(typeof(LenientEnumConverter<WeekStartDay>))]
public enum WeekStartDay
{
    Monday = 0,
    Sunday = 1,
}

/// <summary>[S-581] The four ways the contacts widget lays out people.</summary>
[JsonConverter(typeof(LenientEnumConverter<ContactsLayout>))]
public enum ContactsLayout
{
    /// <summary>Avatar, name, job · company and the address or number.</summary>
    List = 0,

    /// <summary>A small avatar, the name and one secondary field.</summary>
    Compact = 1,

    /// <summary>A table of the chosen columns.</summary>
    Details = 2,

    /// <summary>Avatar cards.</summary>
    Grid = 3,
}

/// <summary>[S-582] A column of the contacts widget's details table.</summary>
[JsonConverter(typeof(LenientEnumConverter<ContactColumn>))]
public enum ContactColumn
{
    Name = 0,
    Email = 1,
    Phone = 2,
    Company = 3,
    Title = 4,
}

/// <summary>
/// [S-561] The calendar widget's settings: which accounts and calendars, how time is laid out, and
/// whether birthdays are shown. Data comes from Themia's own accounts ([S-529]). Every value is
/// read leniently ([S-563]): a wrong one becomes its default rather than refusing the state file.
/// <para>
/// <b>Legacy shape.</b> Before this the widget read the hearth hub, and a saved state may still
/// hold that config (<c>collectionId</c>, <c>maxCount</c>, <c>daysAhead</c>, <c>showAllDay</c>,
/// <c>showLocation</c>). Those names are read here and nowhere else ([S-563]): a hearth collection
/// id means nothing to an account, so <see cref="Repair"/> starts the widget on every calendar
/// account in the agenda layout that widget was, keeps the user's row count, reports the repair
/// (the store logs it), and the old fields are never written back.
/// </para>
/// </summary>
public sealed class CalendarConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Calendar;

    /// <summary>[S-562] The accounts to show; empty means every calendar account.</summary>
    [JsonConverter(typeof(LenientIdListConverter))]
    public List<string> AccountIds { get; set; } = [];

    /// <summary>
    /// [S-567] Per account, the calendars selected. An account absent here (or with an empty list)
    /// shows its default calendar, so a fresh widget starts primary-only.
    /// </summary>
    [JsonConverter(typeof(LenientCalendarMapConverter))]
    public Dictionary<string, List<string>> Calendars { get; set; } = [];

    /// <summary>[S-566] Colour overrides as <c>#rrggbb</c>, keyed by calendar id, or account id for a default calendar.</summary>
    [JsonConverter(typeof(LenientStringMapConverter))]
    public Dictionary<string, string> CalendarColors { get; set; } = [];

    public CalendarViewMode View { get; set; } = CalendarViewMode.Month;

    public WeekStartDay WeekStart { get; set; } = WeekStartDay.Monday;

    /// <summary>[S-575] Show contacts' birthdays beside the events.</summary>
    public bool ShowBirthdays { get; set; } = true;

    /// <summary>How many entries the agenda lists, 1 to 100.</summary>
    public int AgendaCount { get; set; } = 20;

    [JsonIgnore]
    public int EffectiveAgendaCount => Math.Clamp(AgendaCount, 1, 100);

    /// <summary>The view, narrowed to the closed set.</summary>
    [JsonIgnore]
    public CalendarViewMode EffectiveView => Enum.IsDefined(View) ? View : CalendarViewMode.Month;

    [JsonIgnore]
    public WeekStartDay EffectiveWeekStart => Enum.IsDefined(WeekStart) ? WeekStart : WeekStartDay.Monday;

    // ---- the hearth shape, read once and never written ([S-563]) ----------------------------

    [JsonPropertyName("collectionId")]
    public string? LegacyCollectionId { get; set; }

    [JsonPropertyName("maxCount")]
    public int? LegacyMaxCount { get; set; }

    [JsonPropertyName("daysAhead")]
    public int? LegacyDaysAhead { get; set; }

    [JsonPropertyName("showAllDay")]
    public bool? LegacyShowAllDay { get; set; }

    [JsonPropertyName("showLocation")]
    public bool? LegacyShowLocation { get; set; }

    /// <summary>[S-563] Fold the hearth shape in and bring every value back inside its range.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (LegacyCollectionId is not null || LegacyMaxCount is not null || LegacyDaysAhead is not null
            || LegacyShowAllDay is not null || LegacyShowLocation is not null)
        {
            // The hearth widget was an agenda list with a row count; keep both, drop the rest.
            View = CalendarViewMode.Agenda;
            if (LegacyMaxCount is { } count)
            {
                AgendaCount = Math.Clamp(count, 1, 100);
            }

            repairs.Add("calendar widget moved from the hearth hub to Themia's accounts; it now shows every calendar account in the agenda layout");

            LegacyCollectionId = null;
            LegacyMaxCount = null;
            LegacyDaysAhead = null;
            LegacyShowAllDay = null;
            LegacyShowLocation = null;
        }

        var ids = (AccountIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (AccountIds is null || ids.Count != AccountIds.Count)
        {
            repairs.Add("calendar widget had blank or repeated account ids");
        }

        AccountIds = ids;
        Calendars ??= [];
        CalendarColors ??= [];

        if (AgendaCount != EffectiveAgendaCount)
        {
            repairs.Add($"calendar widget agenda count {AgendaCount} was out of range");
            AgendaCount = EffectiveAgendaCount;
        }

        if (!Enum.IsDefined(View) || !Enum.IsDefined(WeekStart))
        {
            repairs.Add("calendar widget had an unknown view or week start");
            View = EffectiveView;
            WeekStart = EffectiveWeekStart;
        }

        return repairs;
    }

    public override WidgetConfig Clone() => new CalendarConfig
    {
        AccountIds = [.. AccountIds],
        Calendars = Calendars.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal),
        CalendarColors = new Dictionary<string, string>(CalendarColors, StringComparer.Ordinal),
        View = View,
        WeekStart = WeekStart,
        ShowBirthdays = ShowBirthdays,
        AgendaCount = AgendaCount,
    };
}

/// <summary>
/// [S-580] The contacts widget's settings: which accounts, the layout, the columns of the details
/// table, how many to list and a search to start with. Data comes from Themia's own accounts.
/// <para>
/// <b>Legacy shape.</b> A saved hearth contacts config (<c>collectionId</c>, <c>maxCount</c>,
/// <c>showDetail</c>, <c>showAvatar</c>) is folded in by <see cref="Repair"/> ([S-563]): the widget
/// starts on every contacts account, keeps the row count (at least five), takes the compact layout
/// when the avatar was off, and never writes the old fields back.
/// </para>
/// </summary>
public sealed class ContactsConfig : WidgetConfig
{
    /// <summary>The columns of a fresh details table, in canonical order.</summary>
    public static IReadOnlyList<ContactColumn> AllColumns { get; } =
        [ContactColumn.Name, ContactColumn.Email, ContactColumn.Phone, ContactColumn.Company, ContactColumn.Title];

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Contacts;

    /// <summary>The accounts to show; empty means every contacts account.</summary>
    [JsonConverter(typeof(LenientIdListConverter))]
    public List<string> AccountIds { get; set; } = [];

    public ContactsLayout Layout { get; set; } = ContactsLayout.List;

    /// <summary>[S-582] The details table's columns; kept in canonical order with at least one.</summary>
    [JsonConverter(typeof(LenientEnumListConverter<ContactColumn>))]
    public List<ContactColumn> Columns { get; set; } = [.. AllColumns];

    /// <summary>[S-583] How many people to list: at least five, in steps of five, no upper bound.</summary>
    public int Count { get; set; } = 20;

    /// <summary>[S-584] A search the widget starts with.</summary>
    public string DefaultSearch { get; set; } = string.Empty;

    [JsonIgnore]
    public int EffectiveCount => Math.Max(5, Count);

    [JsonIgnore]
    public ContactsLayout EffectiveLayout => Enum.IsDefined(Layout) ? Layout : ContactsLayout.List;

    /// <summary>[S-582] The columns as shown: canonical order, unknown values dropped, never none.</summary>
    [JsonIgnore]
    public IReadOnlyList<ContactColumn> EffectiveColumns
    {
        get
        {
            var chosen = AllColumns.Where(c => Columns?.Contains(c) == true).ToList();
            return chosen.Count > 0 ? chosen : [ContactColumn.Name, ContactColumn.Email, ContactColumn.Phone];
        }
    }

    // ---- the hearth shape, read once and never written ([S-563]) ----------------------------

    [JsonPropertyName("collectionId")]
    public string? LegacyCollectionId { get; set; }

    [JsonPropertyName("maxCount")]
    public int? LegacyMaxCount { get; set; }

    [JsonPropertyName("showDetail")]
    public bool? LegacyShowDetail { get; set; }

    [JsonPropertyName("showAvatar")]
    public bool? LegacyShowAvatar { get; set; }

    /// <summary>[S-563] Fold the hearth shape in and bring every value back inside its range.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (LegacyCollectionId is not null || LegacyMaxCount is not null || LegacyShowDetail is not null || LegacyShowAvatar is not null)
        {
            if (LegacyMaxCount is { } count)
            {
                Count = Math.Max(5, count);
            }

            Layout = LegacyShowAvatar == false ? ContactsLayout.Compact : ContactsLayout.List;
            repairs.Add("contacts widget moved from the hearth hub to Themia's accounts; it now lists every contacts account");

            LegacyCollectionId = null;
            LegacyMaxCount = null;
            LegacyShowDetail = null;
            LegacyShowAvatar = null;
        }

        var ids = (AccountIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (AccountIds is null || ids.Count != AccountIds.Count)
        {
            repairs.Add("contacts widget had blank or repeated account ids");
        }

        AccountIds = ids;
        Columns ??= [.. AllColumns];
        DefaultSearch ??= string.Empty;

        if (Count != EffectiveCount)
        {
            repairs.Add($"contacts widget count {Count} was below five");
            Count = EffectiveCount;
        }

        if (!Enum.IsDefined(Layout))
        {
            repairs.Add("contacts widget had an unknown layout");
            Layout = ContactsLayout.List;
        }

        return repairs;
    }

    public override WidgetConfig Clone() => new ContactsConfig
    {
        AccountIds = [.. AccountIds],
        Layout = Layout,
        Columns = [.. Columns],
        Count = Count,
        DefaultSearch = DefaultSearch,
    };
}
