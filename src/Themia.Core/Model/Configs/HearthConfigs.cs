using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Settings shared by the widgets that read the hearth hub.
/// <para>
/// None of them have account settings of their own: hearth owns the accounts, the sync and the
/// consent, so all a widget chooses is which of the collections hearth has granted it shows.
/// </para>
/// </summary>
public abstract class HearthConfig : WidgetConfig
{
    /// <summary>Hearth collection id, or empty for every collection the app may read.</summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>How many rows to list, 1 to 50.</summary>
    public int MaxCount { get; set; } = 10;

    /// <summary>Clamped on read; a persisted zero would draw an empty list.</summary>
    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 50);

    /// <summary>
    /// The list is refreshed by hearth's change events, not by polling, so the timer is only a
    /// backstop that keeps relative timestamps honest.
    /// </summary>
    [JsonIgnore]
    public virtual TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    protected void CopyTo(HearthConfig other)
    {
        other.CollectionId = CollectionId;
        other.MaxCount = MaxCount;
    }
}

/// <summary>People from the hearth hub.</summary>
public sealed class ContactsConfig : HearthConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Contacts;

    /// <summary>Show the primary email or phone under each name.</summary>
    public bool ShowDetail { get; set; } = true;

    /// <summary>Show a round monogram beside each name.</summary>
    public bool ShowAvatar { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new ContactsConfig { ShowDetail = ShowDetail, ShowAvatar = ShowAvatar };
        CopyTo(copy);
        return copy;
    }
}

/// <summary>Upcoming events from the hearth hub.</summary>
public sealed class CalendarConfig : HearthConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Calendar;

    /// <summary>How far ahead to look, 1 to 90 days.</summary>
    public int DaysAhead { get; set; } = 7;

    /// <summary>Include events that occupy a whole day.</summary>
    public bool ShowAllDay { get; set; } = true;

    /// <summary>Show where the event is, when hearth has a location for it.</summary>
    public bool ShowLocation { get; set; } = true;

    [JsonIgnore]
    public int EffectiveDaysAhead => Math.Clamp(DaysAhead, 1, 90);

    public override WidgetConfig Clone()
    {
        var copy = new CalendarConfig
        {
            DaysAhead = DaysAhead,
            ShowAllDay = ShowAllDay,
            ShowLocation = ShowLocation,
        };

        CopyTo(copy);
        return copy;
    }
}

/// <summary>Open tasks from the hearth hub.</summary>
public sealed class TasksConfig : HearthConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Tasks;

    /// <summary>List tasks that have already been completed alongside the open ones.</summary>
    public bool ShowCompleted { get; set; }

    /// <summary>Show each task's due date.</summary>
    public bool ShowDueDate { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new TasksConfig { ShowCompleted = ShowCompleted, ShowDueDate = ShowDueDate };
        CopyTo(copy);
        return copy;
    }
}
