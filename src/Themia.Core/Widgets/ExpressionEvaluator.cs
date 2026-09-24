using System.Globalization;
using System.Text;

namespace Themia.Core.Widgets;

/// <summary>What an expression came to. A closed set, so no caller has to guess at a magic string.</summary>
public enum ExpressionStatus
{
    /// <summary>Nothing to evaluate: the box is empty, or held only spaces and separators.</summary>
    Empty,

    /// <summary>A finite number.</summary>
    Value,

    /// <summary>Malformed, or arithmetic that left the finite numbers (1/0, an overflow).</summary>
    Error,
}

/// <summary>
/// The outcome of evaluating one expression, plus how it is written for the user.
/// <para>
/// A struct with a status rather than a <c>double?</c>, because "nothing typed yet" and "that is not
/// arithmetic" are different things to the widget: one hides the preview, the other shows
/// <see cref="ErrorText"/> in an error style.
/// </para>
/// </summary>
public readonly record struct ExpressionResult
{
    /// <summary>What a failed evaluation reads as. [S-08]</summary>
    public const string ErrorText = "Error";

    /// <summary>Digits are clamped to this, both because more is noise and because <see cref="Math.Round(double, int)"/> rejects past 15.</summary>
    public const int MaxDecimalPlaces = 8;

    private static readonly string[] Formats = BuildFormats();

    private ExpressionResult(ExpressionStatus status, double value)
    {
        Status = status;
        Value = value;
    }

    public ExpressionStatus Status { get; }

    /// <summary>The number, only meaningful when <see cref="Status"/> is <see cref="ExpressionStatus.Value"/>.</summary>
    public double Value { get; }

    public bool IsNumber => Status == ExpressionStatus.Value;

    public static ExpressionResult Empty { get; } = new(ExpressionStatus.Empty, 0);

    public static ExpressionResult Error { get; } = new(ExpressionStatus.Error, double.NaN);

    /// <summary>
    /// A finite result, or <see cref="Error"/>. The only constructor of a value, so an infinity or a
    /// NaN can never be handed out as a number. [S-08]
    /// </summary>
    public static ExpressionResult Number(double value)
        => double.IsFinite(value) ? new ExpressionResult(ExpressionStatus.Value, value) : Error;

    /// <summary>
    /// How this reads in the widget: empty for nothing typed, <c>Error</c> for a failure, otherwise
    /// the number rounded to <paramref name="decimalPlaces"/> with trailing zeros dropped.
    /// </summary>
    public string Format(int decimalPlaces) => Status switch
    {
        ExpressionStatus.Value => FormatNumber(Value, decimalPlaces),
        ExpressionStatus.Error => ErrorText,
        _ => string.Empty,
    };

    private static string FormatNumber(double value, int decimalPlaces)
    {
        var digits = Math.Clamp(decimalPlaces, 0, MaxDecimalPlaces);
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);

        if (!double.IsFinite(rounded))
        {
            return ErrorText;
        }

        if (rounded == 0)
        {
            // -0.001 rounded to two places is negative zero, and "-0" reads as a bug.
            rounded = 0;
        }

        // Invariant throughout: the app builds with InvariantGlobalization, and a result written
        // with a decimal comma could not be typed back into the box it came from.
        return rounded.ToString(Formats[digits], CultureInfo.InvariantCulture);
    }

    private static string[] BuildFormats()
    {
        var formats = new string[MaxDecimalPlaces + 1];
        formats[0] = "0";

        for (var digits = 1; digits <= MaxDecimalPlaces; digits++)
        {
            formats[digits] = "0." + new string('#', digits);
        }

        return formats;
    }
}

/// <summary>
/// The calculator's arithmetic, evaluated by a hand-written recursive-descent parser. [S-07]
/// <para>
/// Hand-written because there is nothing to hand it to: the original could not use <c>eval</c>
/// because the webview CSP blocked it, and a Native AOT binary has no scripting engine at all — and
/// must not grow one to add four operators.
/// </para>
/// <para>
/// Precedence, low to high: <c>+ -</c>, then <c>* /</c>, then unary <c>+ -</c>, then <c>^</c>, which
/// is right-associative. Parentheses group. Nothing here throws to its caller: every failure,
/// including a nesting depth that would otherwise exhaust the stack, comes back as
/// <see cref="ExpressionResult.Error"/>, because this runs on a render path. [S-08]
/// </para>
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>
    /// How deep the grammar may nest before the expression is called an error.
    /// <para>
    /// A cap rather than a bigger stack: parentheses and unary signs both recurse, so
    /// <c>((((…1…))))</c> pasted from anywhere would otherwise take the whole process down with a
    /// stack overflow, which .NET does not let anyone catch.
    /// </para>
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>Evaluate an expression as typed. Total: never throws, whatever the input.</summary>
    public static ExpressionResult Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ExpressionResult.Empty;
        }

        var normalised = Normalise(input);

        if (normalised.Length == 0)
        {
            return ExpressionResult.Empty;
        }

        try
        {
            var tokens = Tokenize(normalised);

            if (tokens.Count == 0)
            {
                return ExpressionResult.Empty;
            }

            return ExpressionResult.Number(new Parser(tokens).Parse());
        }
        catch (ExpressionFormatException)
        {
            // The only exception this file raises, and it never leaves it: a malformed expression is
            // a normal thing for a half-typed box to contain, not an incident.
            return ExpressionResult.Error;
        }
    }

    /// <summary>
    /// Keep only the characters the grammar knows, drop thousands separators, and turn a percent
    /// into the division it means. [S-07]
    /// <para>
    /// Note what dropping every comma implies: <c>1,5</c> is one thousand five hundred written
    /// short, never one-point-five. The app is invariant-culture throughout, so a decimal comma must
    /// not quietly become a decimal point.
    /// </para>
    /// </summary>
    private static string Normalise(string input)
    {
        var builder = new StringBuilder(input.Length);
        var meaningful = false;

        foreach (var c in input)
        {
            switch (c)
            {
                case ',':
                    // A thousands separator: it carries no value of its own.
                    break;

                case '%':
                    builder.Append("/100");
                    meaningful = true;
                    break;

                case ' ':
                    builder.Append(c);
                    break;

                case '+' or '-' or '*' or '/' or '(' or ')' or '.' or '^':
                case >= '0' and <= '9':
                    builder.Append(c);
                    meaningful = true;
                    break;

                default:
                    // Anything else is not arithmetic. Dropped rather than rejected, so pasting
                    // "€12 + 3" still adds up. [S-07]
                    break;
            }
        }

        return meaningful ? builder.ToString() : string.Empty;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            switch (c)
            {
                case ' ':
                    i++;
                    continue;

                case '(':
                    tokens.Add(Token.LeftParen);
                    i++;
                    continue;

                case ')':
                    tokens.Add(Token.RightParen);
                    i++;
                    continue;

                case '+' or '-' or '*' or '/' or '^':
                    tokens.Add(Token.Operator(c));
                    i++;
                    continue;

                default:
                    if (!IsNumberStart(c))
                    {
                        throw new ExpressionFormatException();
                    }

                    tokens.Add(Token.Number(ScanNumber(text, ref i)));
                    continue;
            }
        }

        return tokens;
    }

    private static bool IsNumberStart(char c) => c is '.' or (>= '0' and <= '9');

    /// <summary>
    /// Read one number. A second decimal point ends the token rather than being swallowed, so
    /// <c>1.2.3</c> is two numbers in a row and fails as "2 3" does, instead of silently meaning 1.2.
    /// </summary>
    private static double ScanNumber(string text, ref int i)
    {
        var start = i;
        var seenDot = false;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '.')
            {
                if (seenDot)
                {
                    break;
                }

                seenDot = true;
            }
            else if (c is < '0' or > '9')
            {
                break;
            }

            i++;
        }

        var span = text.AsSpan(start, i - start);

        // NumberStyles without AllowThousands or AllowExponent: the separators are already gone, and
        // "1e9" cannot reach here because 'e' is not a character the grammar keeps.
        if (!double.TryParse(span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            // A literal too big for a double parses to infinity; that is an error, not a number.
            throw new ExpressionFormatException();
        }

        return value;
    }

    private enum TokenKind
    {
        Number,
        Operator,
        LeftParen,
        RightParen,
    }

    private readonly record struct Token(TokenKind Kind, double Value, char Op)
    {
        public static Token LeftParen { get; } = new(TokenKind.LeftParen, 0, '\0');

        public static Token RightParen { get; } = new(TokenKind.RightParen, 0, '\0');

        public static Token Number(double value) => new(TokenKind.Number, value, '\0');

        public static Token Operator(char op) => new(TokenKind.Operator, 0, op);

        public bool Is(char op) => Kind == TokenKind.Operator && Op == op;
    }

    /// <summary>Evaluates as it descends: the grammar is small enough that a tree would only be a tree.</summary>
    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public double Parse()
        {
            var value = Expression(0);

            if (_position != tokens.Count)
            {
                // Trailing tokens: "2 3" is not two expressions, it is a typo.
                throw new ExpressionFormatException();
            }

            return value;
        }

        private Token? Peek() => _position < tokens.Count ? tokens[_position] : null;

        private Token Next()
            => _position < tokens.Count ? tokens[_position++] : throw new ExpressionFormatException();

        private double Expression(int depth)
        {
            Guard(depth);
            var value = Term(depth + 1);

            while (Peek() is { } token && (token.Is('+') || token.Is('-')))
            {
                _position++;
                var right = Term(depth + 1);
                value = token.Is('+') ? value + right : value - right;
            }

            return value;
        }

        private double Term(int depth)
        {
            Guard(depth);
            var value = Unary(depth + 1);

            while (Peek() is { } token && (token.Is('*') || token.Is('/')))
            {
                _position++;
                var right = Unary(depth + 1);
                value = token.Is('*') ? value * right : value / right;
            }

            return value;
        }

        /// <summary>Unary sign binds tighter than <c>*</c> and <c>/</c>, looser than <c>^</c>.</summary>
        private double Unary(int depth)
        {
            Guard(depth);

            if (Peek() is { } token && (token.Is('+') || token.Is('-')))
            {
                _position++;
                var value = Unary(depth + 1);
                return token.Is('-') ? -value : value;
            }

            return Power(depth + 1);
        }

        /// <summary>
        /// Right-associative: <c>2^3^2</c> is <c>2^(3^2)</c>. The exponent goes through
        /// <see cref="Unary"/>, which is what makes <c>2^-3</c> mean what it looks like.
        /// </summary>
        private double Power(int depth)
        {
            Guard(depth);
            var b = Primary(depth + 1);

            if (Peek() is { } token && token.Is('^'))
            {
                _position++;
                return Math.Pow(b, Unary(depth + 1));
            }

            return b;
        }

        private double Primary(int depth)
        {
            Guard(depth);
            var token = Next();

            switch (token.Kind)
            {
                case TokenKind.Number:
                    return token.Value;

                case TokenKind.LeftParen:
                    var value = Expression(depth + 1);

                    if (Next().Kind != TokenKind.RightParen)
                    {
                        throw new ExpressionFormatException();
                    }

                    return value;

                default:
                    throw new ExpressionFormatException();
            }
        }

        private static void Guard(int depth)
        {
            if (depth > MaxDepth)
            {
                throw new ExpressionFormatException();
            }
        }
    }

    /// <summary>
    /// Raised and caught inside this file only. Deliberately not public: a malformed expression is
    /// an <see cref="ExpressionResult"/>, never something a caller has to catch.
    /// </summary>
    private sealed class ExpressionFormatException : Exception;
}
