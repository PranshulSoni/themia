using System.Globalization;
using System.Text;

namespace Themia.Core.Office;

/// <summary>One content line: <c>NAME;PARAM=value:VALUE</c>, with the name and parameter names upper-cased.</summary>
public sealed record IcalProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value)
{
    public string? Parameter(string name) => Parameters.TryGetValue(name, out var value) ? value : null;
}

/// <summary>A <c>BEGIN:X</c> … <c>END:X</c> block and what is nested in it.</summary>
public sealed class IcalComponent(string name)
{
    public string Name { get; } = name;

    public List<IcalProperty> Properties { get; } = [];

    public List<IcalComponent> Children { get; } = [];

    /// <summary>The first property with this name, or null.</summary>
    public IcalProperty? First(string name) => Properties.FirstOrDefault(p => p.Name == name);

    /// <summary>Every property with this name, in order.</summary>
    public IEnumerable<IcalProperty> All(string name) => Properties.Where(p => p.Name == name);

    /// <summary>A text property's value, unescaped, or null when absent or blank.</summary>
    public string? Text(string name)
        => First(name) is { } p && Ical.UnescapeText(p.Value).Trim() is { Length: > 0 } text ? text : null;
}

/// <summary>
/// [S-576] Reading iCalendar (RFC 5545) and vCard (RFC 6350) text: unfolding, content lines with
/// their parameters, and the component tree.
/// <para>
/// Lenient by design — this reads whatever a CalDAV or CardDAV server sends, and a server that
/// writes one bad line must not cost the user the rest of the calendar. A line that is not a
/// content line is skipped; an <c>END</c> without its <c>BEGIN</c> is ignored; a component left
/// open at the end of the text is closed. Nothing here throws on input.
/// </para>
/// </summary>
public static class Ical
{
    /// <summary>
    /// Undo line folding: a line that starts with a space or a tab continues the one before it,
    /// minus that one character. Accepts CRLF, LF or CR line ends.
    /// </summary>
    public static IReadOnlyList<string> Unfold(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var current = new StringBuilder();
        var any = false;

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                current.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (any)
            {
                lines.Add(current.ToString());
            }

            current.Clear().Append(raw);
            any = true;
        }

        if (any)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>
    /// One content line, or null when the line is not one. Parameters are split on <c>;</c> and the
    /// value starts at the first <c>:</c> — both only outside double quotes, so a quoted
    /// <c>CN="Doe; Jane: PhD"</c> stays one parameter.
    /// </summary>
    public static IcalProperty? ParseLine(string line)
    {
        var inQuotes = false;
        var colon = -1;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ':' && !inQuotes)
            {
                colon = i;
                break;
            }
        }

        if (colon <= 0)
        {
            return null;
        }

        var head = line[..colon];
        var parts = SplitOutsideQuotes(head, ';');
        var name = parts[0].Trim().ToUpperInvariant();

        // A vCard group prefix ("item1.EMAIL") names the same property.
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }

        if (name.Length == 0)
        {
            return null;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts.Skip(1))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                // vCard 2.1's bare "TYPE" value ("TEL;CELL:…").
                parameters.TryAdd("TYPE", part.Trim().ToUpperInvariant());
                continue;
            }

            var key = part[..eq].Trim().ToUpperInvariant();
            var value = part[(eq + 1)..].Trim().Trim('"');
            parameters[key] = parameters.TryGetValue(key, out var existing) ? $"{existing},{value}" : value;
        }

        return new IcalProperty(name, parameters, line[(colon + 1)..]);
    }

    /// <summary>The component tree of a text: its top-level components, in order.</summary>
    public static IReadOnlyList<IcalComponent> Parse(string? text)
    {
        var roots = new List<IcalComponent>();
        var stack = new Stack<IcalComponent>();

        foreach (var line in Unfold(text))
        {
            if (ParseLine(line) is not { } property)
            {
                continue;
            }

            if (property.Name == "BEGIN")
            {
                var component = new IcalComponent(property.Value.Trim().ToUpperInvariant());
                if (stack.TryPeek(out var parent))
                {
                    parent.Children.Add(component);
                }
                else
                {
                    roots.Add(component);
                }

                stack.Push(component);
                continue;
            }

            if (property.Name == "END")
            {
                var name = property.Value.Trim().ToUpperInvariant();

                // Close up to the matching BEGIN; an END that matches nothing open is ignored.
                if (stack.Any(c => c.Name == name))
                {
                    while (stack.Pop().Name != name)
                    {
                    }
                }

                continue;
            }

            if (stack.TryPeek(out var owner))
            {
                owner.Properties.Add(property);
            }
        }

        return roots;
    }

    /// <summary>Every component with this name, at any depth, in document order.</summary>
    public static IEnumerable<IcalComponent> Descendants(IEnumerable<IcalComponent> roots, string name)
    {
        foreach (var root in roots)
        {
            if (root.Name == name)
            {
                yield return root;
            }

            foreach (var inner in Descendants(root.Children, name))
            {
                yield return inner;
            }
        }
    }

    /// <summary>A TEXT value with its escapes undone: <c>\n</c>, <c>\,</c>, <c>\;</c>, <c>\\</c>.</summary>
    public static string UnescapeText(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                result.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// A structured value split on <c>;</c> (an unescaped one), each part unescaped: <c>ORG</c>
    /// "Company;Department", <c>N</c> "Family;Given;…".
    /// </summary>
    public static IReadOnlyList<string> SplitStructured(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                current.Append(c).Append(value[++i]);
            }
            else if (c == ';')
            {
                parts.Add(UnescapeText(current.ToString()).Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(UnescapeText(current.ToString()).Trim());
        return parts;
    }

    /// <summary>
    /// [S-576] An iCalendar duration (<c>P1D</c>, <c>PT1H30M</c>, <c>P2W</c>, <c>-PT15M</c>), or null.
    /// </summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        var s = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        var sign = 1;
        if (s[0] is '+' or '-')
        {
            sign = s[0] == '-' ? -1 : 1;
            s = s[1..];
        }

        if (s.Length < 2 || s[0] != 'P')
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var number = 0;
        var digits = 0;
        var inTime = false;
        var parts = 0;

        foreach (var c in s[1..])
        {
            if (char.IsAsciiDigit(c))
            {
                // Nothing a calendar means runs past a few years; a longer run is garbage, and must
                // not overflow on the way to being refused.
                if (number > 100_000)
                {
                    return null;
                }

                number = (number * 10) + (c - '0');
                digits++;
                continue;
            }

            if (c == 'T')
            {
                inTime = true;
                continue;
            }

            if (digits == 0)
            {
                return null;
            }

            total += (c, inTime) switch
            {
                ('W', false) => TimeSpan.FromDays(7 * number),
                ('D', false) => TimeSpan.FromDays(number),
                ('H', true) => TimeSpan.FromHours(number),
                ('M', true) => TimeSpan.FromMinutes(number),
                ('S', true) => TimeSpan.FromSeconds(number),
                _ => TimeSpan.MinValue,
            };

            if (total < TimeSpan.Zero || total > TimeSpan.FromDays(36_500))
            {
                return null;
            }

            number = 0;
            parts++;
            digits = 0;
        }

        return digits == 0 && parts > 0 ? total * sign : null;
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (text[i] == separator && !inQuotes)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }
}

/// <summary>How an iCalendar date or date-time is anchored.</summary>
public enum IcalTimeKind
{
    /// <summary>A DATE: a square on the calendar, no time and no zone.</summary>
    Date,

    /// <summary>A DATE-TIME ending in <c>Z</c>.</summary>
    Utc,

    /// <summary>A DATE-TIME with neither <c>Z</c> nor <c>TZID</c>: the wall clock wherever it is read.</summary>
    Floating,

    /// <summary>A DATE-TIME on the wall clock of the zone named by <c>TZID</c>.</summary>
    Zoned,
}

/// <summary>An iCalendar time as written: the wall clock, how it is anchored, and the zone it names.</summary>
public readonly record struct IcalTime(DateTime Clock, IcalTimeKind Kind, string? Tzid)
{
    public bool IsDate => Kind == IcalTimeKind.Date;

    /// <summary>
    /// [S-576] Read a DATE or DATE-TIME value: <c>20260401</c>, <c>20260401T120000</c>,
    /// <c>20260401T120000Z</c>, with an optional <c>TZID</c>. Null for anything else — never a
    /// half-read date.
    /// </summary>
    public static IcalTime? Parse(string? value, string? tzid = null)
    {
        var s = value?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (s.Length == 8)
        {
            return DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? new IcalTime(day, IcalTimeKind.Date, null)
                : null;
        }

        var utc = s.EndsWith('Z') || s.EndsWith('z');
        var body = utc ? s[..^1] : s;

        if (!DateTime.TryParseExact(body, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            return null;
        }

        if (utc)
        {
            return new IcalTime(clock, IcalTimeKind.Utc, null);
        }

        return string.IsNullOrWhiteSpace(tzid)
            ? new IcalTime(clock, IcalTimeKind.Floating, null)
            : new IcalTime(clock, IcalTimeKind.Zoned, tzid.Trim());
    }

    /// <summary>The time a property carries, honouring its <c>TZID</c> and <c>VALUE=DATE</c>.</summary>
    public static IcalTime? From(IcalProperty? property)
    {
        if (property is null)
        {
            return null;
        }

        var value = property.Value.Trim();

        // VALUE=DATE with a stray time is still a date; take its first eight characters.
        if (string.Equals(property.Parameter("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase) && value.Length >= 8)
        {
            value = value[..8];
        }

        return Parse(value, property.Parameter("TZID"));
    }
}
