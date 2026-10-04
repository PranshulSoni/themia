using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Themia.Localization.Generator;

/// <summary>
/// [S-1101] Compiles <c>Localization/Locales/&lt;code&gt;.json</c> (passed as AdditionalFiles) into
/// the <c>Str</c> enum and one static table per language.
/// <para>
/// en.json is the master: its keys, in file order, are the enum. Every other file may only hold
/// keys en.json has. Anything malformed is a build error with a squiggle on the offending line, so
/// a broken locale never reaches a user. Completeness (every key in every language) is deliberately
/// <b>not</b> a build error — a new English string must be able to land before its translations —
/// and is pinned by the completeness test instead ([S-1105]).
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StringTableGenerator : IIncrementalGenerator
{
    /// <summary>File stem → <c>Locale</c> member, in enum order.</summary>
    private static readonly string[][] Locales =
    {
        new[] { "en", "En" }, new[] { "de", "De" }, new[] { "es", "Es" }, new[] { "fr", "Fr" }, new[] { "it", "It" },
        new[] { "ja", "Ja" }, new[] { "pl", "Pl" }, new[] { "pt", "Pt" }, new[] { "zh", "Zh" },
    };

    /// <summary>CLDR plural categories, in the index order <c>PluralCategory</c> uses.</summary>
    private static readonly string[] Categories = { "zero", "one", "two", "few", "many", "other" };

    private static readonly Regex KeyPattern = new("^[a-z][A-Za-z0-9]*(\\.[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor BadJson = Rule("THEMIALOC001", "Locale file is not valid JSON", "{0}: {1}");
    private static readonly DiagnosticDescriptor NoEnglish = Rule("THEMIALOC002", "en.json is missing", "Localization/Locales/en.json is missing; it defines every string key");
    private static readonly DiagnosticDescriptor UnknownLocale = Rule("THEMIALOC003", "Unknown locale file", "'{0}' is not one of the app's languages (en, de, es, fr, it, ja, pl, pt, zh)");
    private static readonly DiagnosticDescriptor BadKey = Rule("THEMIALOC004", "Bad string key", "Key '{0}' must be dotted camelCase segments, e.g. settings.general.language");
    private static readonly DiagnosticDescriptor BadValue = Rule("THEMIALOC005", "Bad string value", "{0}: {1}");
    private static readonly DiagnosticDescriptor NotInEnglish = Rule("THEMIALOC006", "Key not in en.json", "{0}.json has '{1}', which en.json does not define (typo or stale key)");
    private static readonly DiagnosticDescriptor NameClash = Rule("THEMIALOC007", "Key names clash", "Keys '{0}' and '{1}' both become Str.{2}");

    private static DiagnosticDescriptor Rule(string id, string title, string format)
        => new(id, title, format, "Localization", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(f => IsLocaleFile(f.Path))
            .Select((f, ct) => new SourceFile(f.Path, f.GetText(ct)?.ToString() ?? string.Empty))
            .Collect();

        context.RegisterSourceOutput(files, Emit);
    }

    private static bool IsLocaleFile(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(path);
        return dir is not null
            && string.Equals(Path.GetFileName(dir), "Locales", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(dir)), "Localization", StringComparison.OrdinalIgnoreCase);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<SourceFile> files)
    {
        if (files.IsDefaultOrEmpty)
        {
            // No locale files in this compilation (e.g. a project that only references Core).
            return;
        }

        var parsed = new Dictionary<string, LocaleFile>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(file.Path).ToLowerInvariant();
            if (!Locales.Any(l => l[0] == code))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnknownLocale, At(file, 0), Path.GetFileName(file.Path)));
                continue;
            }

            if (Read(context, file, code) is { } locale)
            {
                parsed[code] = locale;
            }
        }

        if (!parsed.TryGetValue("en", out var en))
        {
            if (!files.Any(f => Path.GetFileNameWithoutExtension(f.Path).Equals("en", StringComparison.OrdinalIgnoreCase)))
            {
                context.ReportDiagnostic(Diagnostic.Create(NoEnglish, Location.None));
            }

            return;
        }

        // The enum: en.json's keys in file order.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in en.Entries)
        {
            var member = MemberName(entry.Key);
            if (members.TryGetValue(member, out var other))
            {
                context.ReportDiagnostic(Diagnostic.Create(NameClash, At(en.Source, entry.Offset), other, entry.Key, member));
                return;
            }

            members[member] = entry.Key;
            ids[entry.Key] = ids.Count;
        }

        var englishOnly = new List<int>();
        foreach (var (key, offset) in en.EnglishOnly)
        {
            if (ids.TryGetValue(key, out var id))
            {
                englishOnly.Add(id);
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(BadValue, At(en.Source, offset), "$englishOnly", $"'{key}' is not a key of en.json"));
            }
        }

        foreach (var locale in parsed.Values)
        {
            foreach (var entry in locale.Entries.Where(e => !ids.ContainsKey(e.Key)))
            {
                context.ReportDiagnostic(Diagnostic.Create(NotInEnglish, At(locale.Source, entry.Offset), locale.Code, entry.Key));
            }
        }

        context.AddSource("Str.g.cs", SourceText.From(EnumSource(en), Encoding.UTF8));
        context.AddSource("StringTables.g.cs", SourceText.From(TableSource(en, parsed, ids, englishOnly), Encoding.UTF8));
    }

    // -----------------------------------------------------------------------------------------
    // Reading one file
    // -----------------------------------------------------------------------------------------

    private static LocaleFile? Read(SourceProductionContext context, SourceFile file, string code)
    {
        JsonNode root;
        try
        {
            root = LocaleJson.Parse(file.Text);
        }
        catch (JsonError error)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadJson, At(file, error.Offset), Path.GetFileName(file.Path), error.Message));
            return null;
        }

        if (root is not JsonObject obj)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadJson, At(file, root.Offset), Path.GetFileName(file.Path), "the file must be one object of key: text"));
            return null;
        }

        var result = new LocaleFile(code, file);
        var ok = true;

        foreach (var member in obj.Members)
        {
            var key = member.Key.Value;

            if (key.StartsWith("$", StringComparison.Ordinal))
            {
                ok &= ReadMeta(context, file, code, member.Key, member.Value, result);
                continue;
            }

            if (!KeyPattern.IsMatch(key))
            {
                context.ReportDiagnostic(Diagnostic.Create(BadKey, At(file, member.Key.Offset), key));
                ok = false;
                continue;
            }

            switch (member.Value)
            {
                case JsonString text:
                    result.Entries.Add(new Entry(key, member.Key.Offset, text.Value, null));
                    break;

                case JsonObject plural when ReadPlural(context, file, key, plural) is { } forms:
                    result.Entries.Add(new Entry(key, member.Key.Offset, null, forms));
                    break;

                case JsonObject:
                    ok = false;
                    break;

                default:
                    context.ReportDiagnostic(Diagnostic.Create(BadValue, At(file, member.Value.Offset), key,
                        "a value is a string, or a plural object such as { \"one\": \"…\", \"other\": \"…\" }"));
                    ok = false;
                    break;
            }
        }

        return ok ? result : null;
    }

    private static bool ReadMeta(SourceProductionContext context, SourceFile file, string code, JsonString name, JsonNode value, LocaleFile result)
    {
        switch (name.Value)
        {
            case "$comment":
                return true;

            case "$englishOnly" when code == "en" && value is JsonArray array:
                foreach (var item in array.Items)
                {
                    if (item is JsonString key)
                    {
                        result.EnglishOnly.Add((key.Value, key.Offset));
                    }
                    else
                    {
                        context.ReportDiagnostic(Diagnostic.Create(BadValue, At(file, item.Offset), "$englishOnly", "list keys as strings"));
                        return false;
                    }
                }

                return true;

            default:
                context.ReportDiagnostic(Diagnostic.Create(BadValue, At(file, name.Offset), name.Value,
                    "the only meta entries are \"$comment\" and, in en.json, \"$englishOnly\": [keys]"));
                return false;
        }
    }

    private static string?[]? ReadPlural(SourceProductionContext context, SourceFile file, string key, JsonObject plural)
    {
        var forms = new string?[Categories.Length];
        foreach (var form in plural.Members)
        {
            var index = Array.IndexOf(Categories, form.Key.Value);
            if (index < 0 || form.Value is not JsonString text)
            {
                context.ReportDiagnostic(Diagnostic.Create(BadValue, At(file, form.Key.Offset), key,
                    "plural forms are zero, one, two, few, many and other, each a string"));
                return null;
            }

            forms[index] = text.Value;
        }

        if (forms[Categories.Length - 1] is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadValue, At(file, plural.Offset), key, "a plural needs an \"other\" form"));
            return null;
        }

        return forms;
    }

    // -----------------------------------------------------------------------------------------
    // Writing the C#
    // -----------------------------------------------------------------------------------------

    /// <summary><c>settings.general.language</c> → <c>Settings_General_Language</c>.</summary>
    internal static string MemberName(string key)
        => string.Join("_", key.Split('.').Select(s => char.IsDigit(s[0]) ? "_" + s : char.ToUpperInvariant(s[0]) + s.Substring(1)));

    private static string EnumSource(LocaleFile en)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Themia.Localization.Generator, from Localization/Locales/en.json.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Themia.Core.Localization;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// [S-1100] Every UI string, by key. Generated from en.json (one member per key, in file order);");
        sb.AppendLine("/// each member's summary is its English text. Read with <c>Str.X.Text()</c>, <c>.Format(...)</c>, <c>.Plural(n)</c>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public enum Str");
        sb.AppendLine("{");
        var id = 0;
        foreach (var entry in en.Entries)
        {
            var english = entry.Text ?? entry.Forms![Categories.Length - 1]!;
            sb.Append("    /// <summary>").Append(XmlDoc(english)).AppendLine("</summary>");
            sb.Append("    /// <remarks><c>").Append(entry.Key).AppendLine(entry.Forms is null ? "</c></remarks>" : "</c> (plural)</remarks>");
            sb.Append("    ").Append(MemberName(entry.Key)).Append(" = ").Append(id++).AppendLine(",");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string TableSource(LocaleFile en, Dictionary<string, LocaleFile> parsed, Dictionary<string, int> ids, List<int> englishOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Themia.Localization.Generator, from Localization/Locales/*.json.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Themia.Core.Localization;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>[S-1101] The compiled locale files. Each language's table is built on first use only.</summary>");
        sb.AppendLine("internal static class StringTables");
        sb.AppendLine("{");
        sb.Append("    internal const int Count = ").Append(ids.Count).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The key of each <see cref=\"Str\"/>, by id.</summary>");
        sb.AppendLine("    internal static readonly string[] Keys =");
        sb.AppendLine("    {");
        foreach (var entry in en.Entries)
        {
            sb.Append("        ").Append(Literal(entry.Key)).AppendLine(",");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Ids en.json lists under $englishOnly: not translated yet, on purpose.</summary>");
        sb.Append("    internal static readonly int[] EnglishOnly = { ").Append(string.Join(", ", englishOnly)).AppendLine(" };");
        sb.AppendLine();
        sb.AppendLine("    internal static LocaleTable For(Locale locale) => locale switch");
        sb.AppendLine("    {");
        foreach (var l in Locales.Skip(1))
        {
            sb.Append("        Locale.").Append(l[1]).Append(" => ").Append(l[1]).AppendLine(".Table,");
        }

        sb.AppendLine("        _ => En.Table,");
        sb.AppendLine("    };");

        foreach (var l in Locales)
        {
            sb.AppendLine();
            sb.Append("    private static class ").AppendLine(l[1]);
            sb.AppendLine("    {");
            sb.Append("        internal static readonly LocaleTable Table = new(Locale.").Append(l[1]).AppendLine(",");

            parsed.TryGetValue(l[0], out var locale);
            var byId = new Entry?[ids.Count];
            if (locale is not null)
            {
                foreach (var entry in locale.Entries)
                {
                    if (ids.TryGetValue(entry.Key, out var id))
                    {
                        byId[id] = entry;
                    }
                }
            }

            sb.AppendLine("            new string?[]");
            sb.AppendLine("            {");
            foreach (var entry in byId)
            {
                var text = entry is null ? null : entry.Text ?? entry.Forms![Categories.Length - 1];
                sb.Append("                ").Append(text is null ? "null" : Literal(text)).AppendLine(",");
            }

            sb.AppendLine("            },");
            sb.AppendLine("            new (int Id, string?[] Forms)[]");
            sb.AppendLine("            {");
            for (var id = 0; id < byId.Length; id++)
            {
                if (byId[id]?.Forms is { } forms)
                {
                    sb.Append("                (").Append(id).Append(", new string?[] { ")
                        .Append(string.Join(", ", forms.Select(f => f is null ? "null" : Literal(f))))
                        .AppendLine(" }),");
                }
            }

            sb.AppendLine("            });");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Literal(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || char.IsSurrogate(c) || c == (char)0x2028 || c == (char)0x2029 || c == (char)0x85)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static string XmlDoc(string text)
    {
        var flat = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", " ").Replace("\n", " ");
        return flat.Length > 160 ? flat.Substring(0, 159) + "…" : flat;
    }

    private static Location At(SourceFile file, int offset)
    {
        var text = SourceText.From(file.Text);
        var safe = Math.Max(0, Math.Min(offset, Math.Max(0, file.Text.Length - 1)));
        var position = text.Lines.GetLinePosition(safe);
        return Location.Create(file.Path, new TextSpan(safe, file.Text.Length == 0 ? 0 : 1), new LinePositionSpan(position, position));
    }

    // -----------------------------------------------------------------------------------------
    // Model
    // -----------------------------------------------------------------------------------------

    /// <summary>A locale file's path and text: equatable, so the generator re-runs only when one changes.</summary>
    private sealed class SourceFile : IEquatable<SourceFile>
    {
        public SourceFile(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }

        public string Text { get; }

        public bool Equals(SourceFile? other) => other is not null && other.Path == Path && other.Text == Text;

        public override bool Equals(object? obj) => Equals(obj as SourceFile);

        public override int GetHashCode() => (Path.GetHashCode() * 397) ^ Text.GetHashCode();
    }

    private sealed class Entry
    {
        public Entry(string key, int offset, string? text, string?[]? forms)
        {
            Key = key;
            Offset = offset;
            Text = text;
            Forms = forms;
        }

        public string Key { get; }

        public int Offset { get; }

        public string? Text { get; }

        public string?[]? Forms { get; }
    }

    private sealed class LocaleFile
    {
        public LocaleFile(string code, SourceFile source)
        {
            Code = code;
            Source = source;
        }

        public string Code { get; }

        public SourceFile Source { get; }

        public List<Entry> Entries { get; } = new();

        public List<(string Key, int Offset)> EnglishOnly { get; } = new();
    }
}
