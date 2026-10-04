using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Themia.Localization.Generator;

/// <summary>A JSON value from a locale file, with where it started (for diagnostics).</summary>
internal abstract class JsonNode
{
    protected JsonNode(int offset) => Offset = offset;

    public int Offset { get; }
}

internal sealed class JsonString : JsonNode
{
    public JsonString(int offset, string value)
        : base(offset) => Value = value;

    public string Value { get; }
}

internal sealed class JsonArray : JsonNode
{
    public JsonArray(int offset, List<JsonNode> items)
        : base(offset) => Items = items;

    public List<JsonNode> Items { get; }
}

/// <summary>An object, members in file order (the order of en.json is the order of the enum).</summary>
internal sealed class JsonObject : JsonNode
{
    public JsonObject(int offset, List<KeyValuePair<JsonString, JsonNode>> members)
        : base(offset) => Members = members;

    public List<KeyValuePair<JsonString, JsonNode>> Members { get; }
}

/// <summary>Any literal a locale file has no use for (numbers, true, false, null).</summary>
internal sealed class JsonOther : JsonNode
{
    public JsonOther(int offset)
        : base(offset)
    {
    }
}

internal sealed class JsonError : Exception
{
    public JsonError(int offset, string message)
        : base(message) => Offset = offset;

    public int Offset { get; }
}

/// <summary>
/// A small strict JSON reader. The generator cannot rely on System.Text.Json being loadable inside
/// every compiler host (Visual Studio's runs on .NET Framework), and locale files only need
/// objects, arrays and strings — so this is the whole grammar, with offsets for error squiggles.
/// Duplicate member names are an error: in a locale file the second one silently winning is a bug.
/// </summary>
internal sealed class LocaleJson
{
    private readonly string _text;
    private int _pos;

    private LocaleJson(string text) => _text = text;

    public static JsonNode Parse(string text)
    {
        var reader = new LocaleJson(text);

        // A UTF-8 BOM read as text.
        if (reader._text.Length > 0 && reader._text[0] == '\uFEFF')
        {
            reader._pos = 1;
        }

        var value = reader.Value();
        reader.SkipWhitespace();
        if (reader._pos != reader._text.Length)
        {
            throw new JsonError(reader._pos, "unexpected text after the top-level value");
        }

        return value;
    }

    private JsonNode Value()
    {
        SkipWhitespace();
        if (_pos >= _text.Length)
        {
            throw new JsonError(_pos, "unexpected end of file");
        }

        var c = _text[_pos];
        switch (c)
        {
            case '{':
                return Object();
            case '[':
                return Array();
            case '"':
                return String();
            default:
                return Literal();
        }
    }

    private JsonObject Object()
    {
        var start = _pos++;
        var members = new List<KeyValuePair<JsonString, JsonNode>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        SkipWhitespace();
        if (Peek() == '}')
        {
            _pos++;
            return new JsonObject(start, members);
        }

        while (true)
        {
            SkipWhitespace();
            if (Peek() != '"')
            {
                throw new JsonError(_pos, "expected a member name in double quotes");
            }

            var name = String();
            if (!seen.Add(name.Value))
            {
                throw new JsonError(name.Offset, $"duplicate key \"{name.Value}\"");
            }

            SkipWhitespace();
            Expect(':');
            var value = Value();
            members.Add(new KeyValuePair<JsonString, JsonNode>(name, value));

            SkipWhitespace();
            if (Peek() == ',')
            {
                _pos++;
                continue;
            }

            Expect('}');
            return new JsonObject(start, members);
        }
    }

    private JsonArray Array()
    {
        var start = _pos++;
        var items = new List<JsonNode>();

        SkipWhitespace();
        if (Peek() == ']')
        {
            _pos++;
            return new JsonArray(start, items);
        }

        while (true)
        {
            items.Add(Value());
            SkipWhitespace();
            if (Peek() == ',')
            {
                _pos++;
                continue;
            }

            Expect(']');
            return new JsonArray(start, items);
        }
    }

    private JsonString String()
    {
        var start = _pos++;
        var sb = new StringBuilder();

        while (true)
        {
            if (_pos >= _text.Length)
            {
                throw new JsonError(start, "unterminated string");
            }

            var c = _text[_pos++];
            if (c == '"')
            {
                return new JsonString(start, sb.ToString());
            }

            if (c < ' ')
            {
                throw new JsonError(_pos - 1, "control character in a string (write \\n, \\t)");
            }

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (_pos >= _text.Length)
            {
                throw new JsonError(start, "unterminated string");
            }

            var e = _text[_pos++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (_pos + 4 > _text.Length
                        || !int.TryParse(_text.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    {
                        throw new JsonError(_pos - 2, "bad \\u escape");
                    }

                    sb.Append((char)code);
                    _pos += 4;
                    break;
                default:
                    throw new JsonError(_pos - 2, $"unknown escape \\{e}");
            }
        }
    }

    private JsonOther Literal()
    {
        var start = _pos;
        while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] is '-' or '+' or '.'))
        {
            _pos++;
        }

        if (_pos == start)
        {
            throw new JsonError(start, $"unexpected '{_text[start]}'");
        }

        return new JsonOther(start);
    }

    private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    private void Expect(char c)
    {
        if (Peek() != c)
        {
            throw new JsonError(_pos, $"expected '{c}'");
        }

        _pos++;
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && _text[_pos] is ' ' or '\t' or '\r' or '\n')
        {
            _pos++;
        }
    }
}
