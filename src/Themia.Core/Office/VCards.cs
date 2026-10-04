namespace Themia.Core.Office;

/// <summary>
/// [S-579] The contacts in CardDAV address data (vCard 3.0 / 4.0, and 2.1's bare type parameters).
/// <para>
/// Ported from the classic app's <c>parse_vcards</c>: the name is <c>FN</c>, a card without one
/// falls back to its structured <c>N</c> and is skipped only when it has neither; the email is the
/// preferred <c>EMAIL</c> (or the first); the phone the preferred <c>TEL</c>; the company the first
/// part of <c>ORG</c>; plus <c>TITLE</c>, <c>UID</c> and <c>BDAY</c>. Values are unescaped, and a
/// parameter carrying a colon (<c>TYPE="a:b"</c>) can no longer swallow the address.
/// </para>
/// </summary>
public static class VCards
{
    /// <summary>One person read from a card, with their birthday when the card holds one.</summary>
    public sealed record Card(Contact Contact, Birthday? Birthday);

    public static IReadOnlyList<Card> Read(string? text)
    {
        var cards = new List<Card>();

        foreach (var vcard in Ical.Descendants(Ical.Parse(text), "VCARD"))
        {
            var name = vcard.Text("FN") ?? NameFromN(vcard.First("N")?.Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var company = vcard.First("ORG") is { } org ? Ical.SplitStructured(org.Value)[0] : null;

            var contact = new Contact
            {
                Id = vcard.First("UID")?.Value.Trim() is { Length: > 0 } uid ? uid : $"carddav:{cards.Count}",
                Name = name,
                Email = Preferred(vcard, "EMAIL"),
                Phone = Preferred(vcard, "TEL"),
                JobTitle = vcard.Text("TITLE"),
                Company = string.IsNullOrWhiteSpace(company) ? null : company,
            };

            cards.Add(new Card(contact, BirthdayRules.Parse(name, vcard.First("BDAY")?.Value)));
        }

        return cards;
    }

    /// <summary>"Doe;Jane;;Dr.;" as "Jane Doe".</summary>
    private static string? NameFromN(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = Ical.SplitStructured(value);
        var given = parts.Count > 1 ? parts[1] : string.Empty;
        var family = parts[0];
        var joined = $"{given} {family}".Trim();
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>
    /// The value marked preferred (<c>PREF=1</c> in 4.0, <c>TYPE=pref</c> in 3.0), else the first.
    /// A <c>mailto:</c> / <c>tel:</c> URI value is reduced to its address.
    /// </summary>
    private static string? Preferred(IcalComponent vcard, string name)
    {
        IcalProperty? chosen = null;

        foreach (var p in vcard.All(name))
        {
            if (string.IsNullOrWhiteSpace(p.Value))
            {
                continue;
            }

            var preferred = p.Parameter("PREF") is "1"
                || (p.Parameter("TYPE")?.Split(',').Any(t => t.Trim().Equals("PREF", StringComparison.OrdinalIgnoreCase)) ?? false);

            if (chosen is null || preferred)
            {
                chosen = p;
                if (preferred)
                {
                    break;
                }
            }
        }

        if (chosen is null)
        {
            return null;
        }

        var value = Ical.UnescapeText(chosen.Value).Trim();
        foreach (var scheme in new[] { "mailto:", "tel:" })
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value[scheme.Length..].Trim();
            }
        }

        return value.Length > 0 ? value : null;
    }
}
