using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>The numeric columns a process row can carry, as flags.</summary>
[Flags]
public enum ProcessColumn
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Threads = 4,
    Pid = 8,
}

/// <summary>Which columns the user allows; the width decides which of those fit.</summary>
public readonly record struct ProcessColumnOptions(ProcessSort Sort, bool Memory, bool Threads, bool Pid);

/// <summary>
/// [S-1152] The process list's columns: the ranked column always, then the other figure, then
/// threads, then the pid, each only while the name keeps <see cref="MinNameWidth"/> to itself. The
/// original [S-648] rule (both figures from 232 px, else only the ranked one) is the first two steps.
/// </summary>
public static class ProcessTable
{
    /// <summary>The narrowest a name may get before a column gives way to it: a dozen characters.</summary>
    public const double MinNameWidth = 96;

    /// <summary>The gap between columns.</summary>
    public const double Spacing = 12;

    /// <summary>"100%".</summary>
    public const double CpuWidth = 48;

    /// <summary>"1023.9 MB".</summary>
    public const double MemoryWidth = 64;

    /// <summary>"1,024".</summary>
    public const double ThreadsWidth = 48;

    /// <summary>"123456".</summary>
    public const double PidWidth = 48;

    public static double WidthOf(ProcessColumn column) => column switch
    {
        ProcessColumn.Cpu => CpuWidth,
        ProcessColumn.Memory => MemoryWidth,
        ProcessColumn.Threads => ThreadsWidth,
        ProcessColumn.Pid => PidWidth,
        _ => 0,
    };

    /// <summary>The columns that fit a row whose text has <paramref name="width"/> DIPs.</summary>
    public static ProcessColumn Columns(double width, ProcessColumnOptions options)
    {
        var ranked = options.Sort == ProcessSort.Memory ? ProcessColumn.Memory : ProcessColumn.Cpu;
        var other = ranked == ProcessColumn.Cpu ? ProcessColumn.Memory : ProcessColumn.Cpu;

        // The ranked figure is the list's reason to exist, so it stays even when the name has to
        // shrink below its minimum for it.
        var shown = ranked;
        var used = MinNameWidth + Spacing + WidthOf(ranked);

        ReadOnlySpan<(ProcessColumn Column, bool Allowed)> ladder =
        [
            (other, other != ProcessColumn.Memory || options.Memory),
            (ProcessColumn.Threads, options.Threads),
            (ProcessColumn.Pid, options.Pid),
        ];

        foreach (var (column, allowed) in ladder)
        {
            if (!allowed)
            {
                continue;
            }

            var need = Spacing + WidthOf(column);

            // A step that does not fit ends the ladder: a pid column without the thread column
            // before it would be a gap in the order the eye expects.
            if (used + need > width + Disclosure.Tolerance)
            {
                break;
            }

            shown |= column;
            used += need;
        }

        return shown;
    }
}
