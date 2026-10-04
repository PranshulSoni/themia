using System.Text.Json.Serialization;
using Themia.Core.Mail;

namespace Themia.Core.Model;

/// <summary>
/// [S-540] Recent mail from Themia's own accounts: which accounts, which folder, how many, and how
/// each message is drawn.
/// <para>
/// Every closed set is read leniently (<see cref="LenientWireConverter{T}"/>): an unknown or
/// mistyped value becomes the default and is logged, because a throw inside one widget's config
/// would refuse the whole state file and blank every desktop.
/// </para>
/// </summary>
public sealed class EmailConfig : WidgetConfig
{
    /// <summary>The fields the list and compact views show when the user has not chosen.</summary>
    public static IReadOnlyList<MailListField> DefaultListFields { get; } = [MailListField.Subject, MailListField.Preview, MailListField.Account];

    /// <summary>The row view's columns when the user has not chosen.</summary>
    public static IReadOnlyList<MailColumn> DefaultColumns { get; } = [MailColumn.From, MailColumn.Subject, MailColumn.Date];

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Email;

    /// <summary>
    /// [S-541] The accounts to merge into the list, by id. Empty means every mail account Themia is
    /// signed in to, so a widget keeps working when an account is added rather than needing a visit
    /// to its settings first.
    /// </summary>
    [JsonConverter(typeof(LenientStringListConverter))]
    public List<string>? AccountIds { get; set; } = [];

    public MailFolder Folder { get; set; } = MailFolder.Inbox;

    /// <summary>
    /// [S-1286] How many messages to list at least, 1 to 50. A tile with room for more whole rows
    /// lists that many instead (up to 50), so a big widget is never half empty.
    /// </summary>
    public int MaxCount { get; set; } = 10;

    public MailViewMode ViewMode { get; set; } = MailViewMode.List;

    /// <summary>Lines of preview per message in the list view, 1 to 5.</summary>
    public int PreviewLines { get; set; } = 2;

    /// <summary>The optional lines of the list and compact views; null is <see cref="DefaultListFields"/>.</summary>
    [JsonConverter(typeof(MailListFieldsConverter))]
    public List<MailListField>? ListFields { get; set; }

    /// <summary>The row view's columns in order; null is <see cref="DefaultColumns"/>, and never empty.</summary>
    [JsonConverter(typeof(MailColumnsConverter))]
    public List<MailColumn>? RowColumns { get; set; }

    public MailSortOrder SortOrder { get; set; } = MailSortOrder.NewestFirst;

    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 50);

    /// <summary>[S-1286] What to fetch for a tile that holds <paramref name="rowsThatFit"/> whole rows.</summary>
    public int FetchCount(int rowsThatFit) => Math.Clamp(Math.Max(MaxCount, rowsThatFit), 1, 50);

    [JsonIgnore]
    public int EffectivePreviewLines => Math.Clamp(PreviewLines, 1, 5);

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveAccountIds => AccountIds ?? [];

    [JsonIgnore]
    public IReadOnlyList<MailListField> EffectiveListFields => ListFields ?? DefaultListFields;

    [JsonIgnore]
    public IReadOnlyList<MailColumn> EffectiveColumns => RowColumns is { Count: > 0 } columns ? columns : DefaultColumns;

    /// <summary>
    /// [S-543] Whether a list-view line shows: subject and account whenever chosen, the preview only
    /// in the list view (the compact view is two lines by definition).
    /// </summary>
    public bool Shows(MailListField field)
        => EffectiveListFields.Contains(field) && (field != MailListField.Preview || ViewMode == MailViewMode.List);

    /// <summary>[S-540] Bring every value back inside its range.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        var ids = (AccountIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (AccountIds is null || ids.Count != AccountIds.Count)
        {
            repairs.Add("mail widget had blank or repeated account ids");
        }

        AccountIds = ids;

        if (MaxCount != EffectiveMaxCount)
        {
            repairs.Add($"mail widget count {MaxCount} was out of range");
            MaxCount = EffectiveMaxCount;
        }

        if (PreviewLines != EffectivePreviewLines)
        {
            repairs.Add($"mail widget preview lines {PreviewLines} were out of range");
            PreviewLines = EffectivePreviewLines;
        }

        if (!Enum.IsDefined(Folder) || Folder == MailFolder.Archive)
        {
            repairs.Add("mail widget had a folder it cannot list");
            Folder = MailFolder.Inbox;
        }

        if (!Enum.IsDefined(ViewMode))
        {
            repairs.Add("mail widget had an unknown view");
            ViewMode = MailViewMode.List;
        }

        if (!Enum.IsDefined(SortOrder))
        {
            repairs.Add("mail widget had an unknown sort order");
            SortOrder = MailSortOrder.NewestFirst;
        }

        if (ListFields is { } fields)
        {
            ListFields = [.. fields.Where(f => Enum.IsDefined(f)).Distinct()];
        }

        if (RowColumns is { } columns)
        {
            var clean = columns.Where(c => Enum.IsDefined(c)).Distinct().ToList();

            // At least one column always stays: a row view with none is a blank widget.
            if (clean.Count == 0)
            {
                repairs.Add("mail widget had no row columns");
                RowColumns = null;
            }
            else
            {
                RowColumns = clean;
            }
        }

        return repairs;
    }

    public override WidgetConfig Clone() => new EmailConfig
    {
        AccountIds = AccountIds is null ? null : [.. AccountIds],
        Folder = Folder,
        MaxCount = MaxCount,
        ViewMode = ViewMode,
        PreviewLines = PreviewLines,
        ListFields = ListFields is null ? null : [.. ListFields],
        RowColumns = RowColumns is null ? null : [.. RowColumns],
        SortOrder = SortOrder,
    };
}
