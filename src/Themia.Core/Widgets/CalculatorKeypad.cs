namespace Themia.Core.Widgets;

/// <summary>A key on the calculator's keypad.</summary>
public enum KeypadKey
{
    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
    Point,
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Percent,
    Square,
    OpenParen,
    CloseParen,
    Answer,
    Backspace,
    Clear,
    Equals,
}

/// <summary>What a key press made of the line: its text, where the caret is, and whether to file it.</summary>
/// <param name="Text">The line after the key.</param>
/// <param name="Caret">The caret after the key (a selection the key replaced is gone).</param>
/// <param name="Commit">Equals: file the line as Enter does ([S-10]).</param>
public readonly record struct KeypadEdit(string Text, int Caret, bool Commit);

/// <summary>
/// [S-1510] The calculator's keypad: which keys there are, in which rows, what each one is labelled,
/// and what pressing it does to the line being typed.
/// <para>
/// The keypad is how the calculator is used without a keyboard — on a desktop whose widgets never
/// take focus until asked, it is the only way to work a sum with the mouse alone. The keys edit the
/// same text the keyboard does and file it through the same commit, so there is one calculator,
/// not two. Pure, so the caret and selection arithmetic (easy to get one-off wrong) is tested here.
/// </para>
/// </summary>
public static class CalculatorKeypad
{
    /// <summary>Keys per row.</summary>
    public const int Columns = 4;

    /// <summary>The standard keypad: Windows Calculator's shape, with the brackets the grammar has.</summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Standard { get; } =
    [
        [KeypadKey.Clear, KeypadKey.OpenParen, KeypadKey.CloseParen, KeypadKey.Divide],
        [KeypadKey.D7, KeypadKey.D8, KeypadKey.D9, KeypadKey.Multiply],
        [KeypadKey.D4, KeypadKey.D5, KeypadKey.D6, KeypadKey.Subtract],
        [KeypadKey.D1, KeypadKey.D2, KeypadKey.D3, KeypadKey.Add],
        [KeypadKey.Backspace, KeypadKey.D0, KeypadKey.Point, KeypadKey.Equals],
    ];

    /// <summary>The extra row a tall keypad adds on top: the last answer and the rest of the grammar.</summary>
    public static IReadOnlyList<KeypadKey> ExtraRow { get; } =
        [KeypadKey.Answer, KeypadKey.Percent, KeypadKey.Power, KeypadKey.Square];

    /// <summary>The rows to draw: <see cref="Standard"/>, with <see cref="ExtraRow"/> on top when extended.</summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Rows(bool extended)
        => extended ? [ExtraRow, .. Standard] : Standard;

    /// <summary>What the key shows. Operators use the typographic signs; what they type is ASCII.</summary>
    public static string Label(KeypadKey key) => key switch
    {
        >= KeypadKey.D0 and <= KeypadKey.D9 => ((char)('0' + (key - KeypadKey.D0))).ToString(),
        KeypadKey.Point => ".",
        KeypadKey.Add => "+",
        KeypadKey.Subtract => "−",
        KeypadKey.Multiply => "×",
        KeypadKey.Divide => "÷",
        KeypadKey.Power => "xʸ",
        KeypadKey.Percent => "%",
        KeypadKey.Square => "x²",
        KeypadKey.OpenParen => "(",
        KeypadKey.CloseParen => ")",
        KeypadKey.Answer => "Ans",
        KeypadKey.Backspace => "⌫",
        KeypadKey.Clear => "C",
        KeypadKey.Equals => "=",
        _ => string.Empty,
    };

    /// <summary>What a screen reader and the tooltip call the key.</summary>
    public static string Name(KeypadKey key) => key switch
    {
        >= KeypadKey.D0 and <= KeypadKey.D9 => Label(key),
        KeypadKey.Point => "Decimal point",
        KeypadKey.Add => "Plus",
        KeypadKey.Subtract => "Minus",
        KeypadKey.Multiply => "Multiply by",
        KeypadKey.Divide => "Divide by",
        KeypadKey.Power => "To the power of",
        KeypadKey.Percent => "Percent",
        KeypadKey.Square => "Square",
        KeypadKey.OpenParen => "Open bracket",
        KeypadKey.CloseParen => "Close bracket",
        KeypadKey.Answer => "Last answer",
        KeypadKey.Backspace => "Backspace",
        KeypadKey.Clear => "Clear",
        KeypadKey.Equals => "Equals",
        _ => string.Empty,
    };

    /// <summary>Whether the key is an operator (drawn quieter than the digits, as Windows Calculator does).</summary>
    public static bool IsOperator(KeypadKey key) => key is KeypadKey.Add or KeypadKey.Subtract or KeypadKey.Multiply
        or KeypadKey.Divide or KeypadKey.Power or KeypadKey.Percent or KeypadKey.Square;

    /// <summary>
    /// Press <paramref name="key"/> on <paramref name="text"/> with the caret at
    /// <paramref name="selectionStart"/> and <paramref name="selectionLength"/> characters selected.
    /// </summary>
    /// <param name="lastResult">
    /// The newest history result, or null. [S-1511] A binary operator pressed on an empty line
    /// continues from it, as a desk calculator does (<c>× 2</c> after <c>= 21</c> is <c>21*2</c>);
    /// <see cref="KeypadKey.Answer"/> types it.
    /// </param>
    public static KeypadEdit Apply(KeypadKey key, string? text, int selectionStart, int selectionLength, string? lastResult)
    {
        text ??= string.Empty;
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);

        switch (key)
        {
            case KeypadKey.Clear:
                return new KeypadEdit(string.Empty, 0, false);

            case KeypadKey.Equals:
                return new KeypadEdit(text, start + length, true);

            case KeypadKey.Backspace:
                if (length > 0)
                {
                    return new KeypadEdit(text.Remove(start, length), start, false);
                }

                return start == 0
                    ? new KeypadEdit(text, 0, false)
                    : new KeypadEdit(text.Remove(start - 1, 1), start - 1, false);
        }

        var insert = Typed(key, text, start, length, lastResult);
        var after = text.Remove(start, length).Insert(start, insert);
        return new KeypadEdit(after, start + insert.Length, false);
    }

    /// <summary>The characters a key types at the caret (empty: the key does nothing here).</summary>
    private static string Typed(KeypadKey key, string text, int start, int length, string? lastResult)
    {
        var answer = string.IsNullOrWhiteSpace(lastResult) ? string.Empty : lastResult.Trim();

        // A negative answer is bracketed: unary minus binds looser than ^, so -5^2 is -25 while the
        // square of the answer -5 is 25.
        if (answer.StartsWith('-'))
        {
            answer = "(" + answer + ")";
        }

        // The line with the selection taken out, as it will be around the inserted text.
        var before = text[..start];
        var after = text[(start + length)..];
        var lineEmpty = before.Trim().Length == 0 && after.Trim().Length == 0;

        switch (key)
        {
            case >= KeypadKey.D0 and <= KeypadKey.D9:
                return ((char)('0' + (key - KeypadKey.D0))).ToString();

            case KeypadKey.Point:
                // [S-1512] One point per number: a second would only make the line an error. A point
                // that starts a number gets its zero, as Windows Calculator writes it.
                if (NumberAround(before, after).Contains('.', StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                return before.Length > 0 && char.IsAsciiDigit(before[^1]) ? "." : "0.";

            case KeypadKey.Add or KeypadKey.Multiply or KeypadKey.Divide or KeypadKey.Power:
                var op = key switch
                {
                    KeypadKey.Add => "+",
                    KeypadKey.Multiply => "*",
                    KeypadKey.Divide => "/",
                    _ => "^",
                };

                return lineEmpty && answer.Length > 0 ? answer + op : op;

            case KeypadKey.Subtract:
                // Not continued from the answer: on an empty line a minus starts a negative number.
                return "-";

            case KeypadKey.Percent:
                return "%";

            case KeypadKey.Square:
                return lineEmpty && answer.Length > 0 ? answer + "^2" : "^2";

            case KeypadKey.OpenParen:
                return "(";

            case KeypadKey.CloseParen:
                return ")";

            case KeypadKey.Answer:
                return answer;

            default:
                return string.Empty;
        }
    }

    /// <summary>The run of digits and points the caret is inside of (both sides of it).</summary>
    private static string NumberAround(string before, string after)
    {
        var left = before.Length;

        while (left > 0 && IsNumberChar(before[left - 1]))
        {
            left--;
        }

        var right = 0;

        while (right < after.Length && IsNumberChar(after[right]))
        {
            right++;
        }

        return before[left..] + after[..right];
    }

    private static bool IsNumberChar(char c) => char.IsAsciiDigit(c) || c == '.';
}

/// <summary>
/// [S-1513] Up and Down in the calculator's line step through what was worked out, newest first, the
/// way a shell recalls commands; stepping back past the newest puts back what was being typed.
/// </summary>
public static class CalculatorRecall
{
    /// <summary>
    /// The history row to show after a step, or -1 for "the line being typed".
    /// </summary>
    /// <param name="index">The row recalled now, or -1.</param>
    /// <param name="older">Up (towards older rows) rather than Down.</param>
    /// <param name="count">How many rows the history holds.</param>
    public static int Step(int index, bool older, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        var at = Math.Clamp(index, -1, count - 1);
        return older ? Math.Min(count - 1, at + 1) : Math.Max(-1, at - 1);
    }
}
