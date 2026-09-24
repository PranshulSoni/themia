namespace Themia.Core.Widgets;

/// <summary>One committed calculation: what was typed, and what it came to.</summary>
/// <param name="Expression">The expression exactly as the user wrote it.</param>
/// <param name="Result">The formatted result, which is also what clicking the row types back. [S-11]</param>
public readonly record struct Calculation(string Expression, string Result);

/// <summary>
/// The calculator's scratch history: newest first, at most <see cref="Capacity"/> entries. [S-09]
/// <para>
/// Deliberately not persisted and deliberately not in the config. A scratchpad is for the sum you
/// are in the middle of; keeping it on disk would turn a throwaway into a record the user has to
/// think about clearing, and put arbitrary typed text into the state file for no benefit.
/// </para>
/// </summary>
public sealed class CalculatorHistory
{
    /// <summary>How many rows are kept. Matches the original widget.</summary>
    public const int Capacity = 8;

    private readonly List<Calculation> _entries = new(Capacity);

    /// <summary>Newest first, so the row nearest the input is the last thing committed.</summary>
    public IReadOnlyList<Calculation> Entries => _entries;

    public int Count => _entries.Count;

    /// <summary>
    /// Push a calculation onto the front, dropping the oldest once full.
    /// <para>
    /// Whitespace-only expressions are refused here rather than at the call site, so the cap and the
    /// "nothing empty gets in" rule live in one place. [S-10]
    /// </para>
    /// </summary>
    /// <returns>Whether the entry was taken.</returns>
    public bool Add(string expression, string result)
    {
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        _entries.Insert(0, new Calculation(expression, result));

        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }

        return true;
    }

    public void Clear() => _entries.Clear();
}
