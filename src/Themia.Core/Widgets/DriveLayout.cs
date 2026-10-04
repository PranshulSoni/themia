namespace Themia.Core.Widgets;

/// <summary>[S-1290] How a OneDrive row is drawn at the tile's size.</summary>
public enum DriveRowMode
{
    /// <summary>Icon and name only: a tile too narrow or too short for anything else.</summary>
    NameOnly,

    /// <summary>The name with <c>size · when</c> under it: a narrow tile with height to spare.</summary>
    TwoLine,

    /// <summary>One line per item, the details in columns beside the name: anything wide enough.</summary>
    Columns,
}

/// <summary>[S-1290] The detail columns beside an item's name, in the order they are given room.</summary>
public enum DriveColumn
{
    Modified,
    Size,
    ModifiedBy,
    Location,
}

/// <summary>One detail column and its fixed width; the name takes what is left.</summary>
public readonly record struct DriveColumnFit(DriveColumn Column, double Width);

/// <summary>The row mode, the detail columns that fit, and whether a column header has room.</summary>
public sealed record DriveFit(DriveRowMode Mode, IReadOnlyList<DriveColumnFit> Columns, bool ColumnHeader);

/// <summary>
/// [S-1290] The OneDrive widget's disclosure ladder, decided in one place. The name always shows.
/// As the tile widens, details join it on the same line — when it changed, then its size, then who
/// changed it, then (for recent files) the folder it is in — so a wide strip lists more items rather
/// than fewer two-line ones, and a big tile has no empty right half. A narrow tile with height keeps
/// the details as a second line; a narrow, short one keeps just the names.
/// </summary>
public static class DriveLayout
{
    /// <summary>The narrowest the name column may get before a detail column gives way to it.</summary>
    public const double NameMinWidth = 120;

    /// <summary>Under this text width (a 180px tile) the second line has no room to say anything useful.</summary>
    public const double TwoLineNeedsWidth = 120;

    /// <summary>A column header costs a row; under this content height that row is worth more as an item.</summary>
    public const double HeaderNeedsHeight = 280;

    /// <summary>Space between two columns.</summary>
    public const double Gap = 8;

    /// <summary>The width each detail column needs to say its longest usual value ("12 Sep 2025", "182 MB").</summary>
    public static double Width(DriveColumn column) => column switch
    {
        DriveColumn.Modified => 88,
        DriveColumn.Size => 72,
        DriveColumn.ModifiedBy => 128,
        _ => 168,
    };

    /// <summary>The column header's words.</summary>
    public static string Label(DriveColumn column) => column switch
    {
        DriveColumn.Modified => "Modified",
        DriveColumn.Size => "Size",
        DriveColumn.ModifiedBy => "Modified by",
        _ => "Location",
    };

    /// <param name="textWidth">The width the row's text shares: the row less its padding and icon.</param>
    /// <param name="height">The list's height.</param>
    /// <param name="compact">Whether rows are compact (one line tall).</param>
    /// <param name="showLocation">Whether items come from many folders (recent files), so the folder says something.</param>
    public static DriveFit Fit(double textWidth, double height, bool compact, bool showLocation)
    {
        var columns = new List<DriveColumnFit>();
        var used = NameMinWidth;

        foreach (var column in Enum.GetValues<DriveColumn>())
        {
            if (column == DriveColumn.Location && !showLocation)
            {
                continue;
            }

            var cost = Width(column) + Gap;
            if (used + cost > textWidth + Disclosure.Tolerance)
            {
                // The ladder: a column that misses closes it, so a narrower one never jumps the queue.
                break;
            }

            columns.Add(new DriveColumnFit(column, Width(column)));
            used += cost;
        }

        if (columns.Count > 0)
        {
            return new DriveFit(DriveRowMode.Columns, columns, height >= HeaderNeedsHeight);
        }

        return new DriveFit(!compact && textWidth >= TwoLineNeedsWidth ? DriveRowMode.TwoLine : DriveRowMode.NameOnly, [], false);
    }
}
