using System.Text.Json.Serialization;
using Themia.Core.Tasks;

namespace Themia.Core.Model;

/// <summary>
/// [S-590] A task list: from a Microsoft or Google account through Themia's own sign-in, or a list
/// kept on this PC.
/// <para>
/// Empty account and list ids are not "nothing chosen": they mean the first account that has tasks
/// and its default list, so a widget dropped on the desktop of someone already signed in shows their
/// tasks without a trip to settings. A named account that has since been signed out is <i>not</i>
/// quietly swapped for another — that widget says so instead.
/// </para>
/// </summary>
public sealed class TasksConfig : WidgetConfig
{
    private List<string> _taskOrder = [];

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Tasks;

    public TaskSource Source { get; set; } = TaskSource.Account;

    /// <summary>The account, or empty for the first account with tasks.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>[S-592] The list on that account, <see cref="TaskRules.TodayListId"/>, or empty for its default list.</summary>
    public string ListId { get; set; } = string.Empty;

    /// <summary>The list's name when it was picked: the header's title while the lists load.</summary>
    public string ListName { get; set; } = string.Empty;

    /// <summary>[S-603] Which list on this PC, when <see cref="Source"/> is <see cref="TaskSource.Local"/>.</summary>
    public string LocalListId { get; set; } = string.Empty;

    /// <summary>[S-595] List finished tasks alongside the open ones.</summary>
    public bool ShowCompleted { get; set; }

    /// <summary>Show each task's due date.</summary>
    public bool ShowDueDate { get; set; } = true;

    /// <summary>[S-602] Due date under the title, or beside it.</summary>
    public TaskViewMode ViewMode { get; set; } = TaskViewMode.List;

    /// <summary>[S-601] Lines a title wraps to before it is cut. Clamped on read.</summary>
    public int TitleLines { get; set; } = TaskRules.TitleLinesDefault;

    /// <summary>[S-591] Seconds between refreshes of an account list, 30 to 600.</summary>
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>
    /// [S-598] The user's own row order for an account list, as task ids. Neither provider stores a
    /// hand-sorted order, so it lives here. A list on this PC keeps its order in the list itself.
    /// </summary>
    public List<string> TaskOrder
    {
        get => _taskOrder;

        // A hand-edited "taskOrder": null must not leave the widget holding a null list.
        set => _taskOrder = value ?? [];
    }

    /// <summary>
    /// [S-605] What the hearth-backed widget stored as its collection. Read so an old state file can
    /// be recognised and upgraded; never written (null is omitted), and cleared on load.
    /// </summary>
    [JsonPropertyName("collectionId")]
    public string? LegacyCollectionId { get; set; }

    /// <summary>[S-605] The hearth widget's row cap; read for recognition only, as above.</summary>
    [JsonPropertyName("maxCount")]
    public int? LegacyMaxCount { get; set; }

    [JsonIgnore]
    public int EffectiveTitleLines => TaskRules.ClampTitleLines(TitleLines);

    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(Math.Clamp(RefreshSeconds, 30, 600));

    /// <summary>
    /// [S-605] Upgrade a config written by the hearth-backed widget. A hearth collection id means
    /// nothing to Graph or Google, so it cannot be carried over: the widget falls back to the first
    /// account with tasks and its default list, which is what the hearth widget's own "all task
    /// lists" default came closest to. What the two had in common — show completed, show due date —
    /// is kept. Returns a sentence for the load report, or null when there was nothing to upgrade.
    /// </summary>
    public string? UpgradeFromHearth()
    {
        if (LegacyCollectionId is null && LegacyMaxCount is null)
        {
            return null;
        }

        var hadCollection = !string.IsNullOrEmpty(LegacyCollectionId);
        LegacyCollectionId = null;
        LegacyMaxCount = null;

        Source = TaskSource.Account;
        AccountId = string.Empty;
        ListId = string.Empty;
        ListName = string.Empty;

        return hadCollection
            ? "a Tasks widget named a hearth task list, which Themia's own accounts cannot open; it now shows the first account's default list"
            : "a Tasks widget was set up for the hearth hub; it now shows the first account's default list";
    }

    /// <summary>
    /// [S-605] Fold an older or hand-edited shape back inside the invariants: the hearth upgrade,
    /// and nulls a hand edit can put where the model promises a value.
    /// </summary>
    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (UpgradeFromHearth() is { } upgraded)
        {
            repairs.Add(upgraded);
        }

        // System.Text.Json writes a JSON null straight through a non-nullable string property.
        AccountId ??= string.Empty;
        ListId ??= string.Empty;
        ListName ??= string.Empty;
        LocalListId ??= string.Empty;

        if (!Enum.IsDefined(Source))
        {
            Source = TaskSource.Account;
            repairs.Add("a Tasks widget named an unknown source; it reads an account again");
        }

        if (!Enum.IsDefined(ViewMode))
        {
            ViewMode = TaskViewMode.List;
        }

        var order = TaskRules.CleanOrder(TaskOrder);
        if (order.Count != TaskOrder.Count)
        {
            TaskOrder = order;
            repairs.Add("a Tasks widget's row order held blank or repeated entries");
        }

        return repairs;
    }

    public override WidgetConfig Clone() => new TasksConfig
    {
        Source = Source,
        AccountId = AccountId,
        ListId = ListId,
        ListName = ListName,
        LocalListId = LocalListId,
        ShowCompleted = ShowCompleted,
        ShowDueDate = ShowDueDate,
        ViewMode = ViewMode,
        TitleLines = TitleLines,
        RefreshSeconds = RefreshSeconds,
        TaskOrder = [.. TaskOrder],
    };
}
