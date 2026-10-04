using Themia.Core.Model;

namespace Themia.Core.Mail;

/// <summary>Which lines a list or compact row draws at the widget's current size.</summary>
public readonly record struct MailRowLayout(bool Subject, bool Preview, int PreviewLines, bool Account, bool Time);

/// <summary>One row-view column at the width it gets: a fixed width, or a share of what is left.</summary>
public readonly record struct MailColumnFit(MailColumn Column, double Width, double Star);

/// <summary>
/// [S-556] Shedding detail to fit the tile, decided in one place. A row never clips or overlaps:
/// the account tag goes first, then the preview, then extra preview lines; in the row view whole
/// columns go, the least essential first, and the first column the user chose always stays.
/// </summary>
public static class MailLayout
{
    /// <summary>Below this width the account tag has no room beside the subject.</summary>
    public const double AccountNeedsWidth = 260;

    /// <summary>Below this width a time beside the sender leaves the name a handful of letters.</summary>
    public const double TimeNeedsWidth = 110;

    /// <summary>Below this content height one more line per message costs a whole message.</summary>
    public const double PreviewNeedsHeight = 120;

    public static MailRowLayout Rows(EmailConfig config, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(config);

        var preview = config.Shows(MailListField.Preview) && height >= PreviewNeedsHeight && width >= 180;
        var lines = preview ? (height < 2 * PreviewNeedsHeight ? 1 : config.EffectivePreviewLines) : 0;

        return new MailRowLayout(
            config.Shows(MailListField.Subject),
            preview,
            lines,
            config.Shows(MailListField.Account) && width >= AccountNeedsWidth,
            width >= TimeNeedsWidth);
    }

    /// <summary>The narrowest each column can be and still say anything.</summary>
    public static double MinWidth(MailColumn column) => column switch
    {
        MailColumn.Date => 56,
        MailColumn.Account => 84,
        MailColumn.From => 80,
        _ => 96,
    };

    /// <summary>The order columns are given up in when the tile is too narrow for all of them.</summary>
    private static int Expendability(MailColumn column) => column switch
    {
        MailColumn.Preview => 0,
        MailColumn.Account => 1,
        MailColumn.Date => 2,
        MailColumn.Subject => 3,
        _ => 4,
    };

    /// <summary>
    /// The row view's columns at <paramref name="width"/> (the row's inner width): From, Date and
    /// Account have fixed widths, Subject and Preview share what is left. The most essential columns
    /// are kept first as long as their minimums fit — so a narrow tile keeps From and Date rather
    /// than From alone — and the user's order is kept. The most essential column always stays.
    /// </summary>
    public static IReadOnlyList<MailColumnFit> Columns(IReadOnlyList<MailColumn> chosen, double width, double gap)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        var distinct = chosen.Distinct().ToList();
        var keep = new HashSet<MailColumn>();
        var used = 0.0;

        foreach (var column in distinct.OrderByDescending(Expendability))
        {
            var cost = MinWidth(column) + (keep.Count > 0 ? gap : 0);
            if (keep.Count == 0 || used + cost <= width)
            {
                keep.Add(column);
                used += cost;
            }
        }

        var kept = distinct.Where(keep.Contains).ToList();

        var hasStar = kept.Any(c => c is MailColumn.Subject or MailColumn.Preview);

        return
        [
            .. kept.Select(c => c switch
            {
                MailColumn.Subject or MailColumn.Preview => new MailColumnFit(c, 0, 1),

                // With no flexible column the first one takes the slack, so the row still fills.
                _ when !hasStar && c == kept[0] => new MailColumnFit(c, 0, 1),
                MailColumn.From => new MailColumnFit(c, Math.Clamp(width * 0.28, MinWidth(c), 200), 0),
                _ => new MailColumnFit(c, MinWidth(c), 0),
            }),
        ];
    }
}
