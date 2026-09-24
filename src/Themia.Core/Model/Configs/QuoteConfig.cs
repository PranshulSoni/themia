using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>One quote, as stored.</summary>
public sealed class QuoteItem
{
    public string Text { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public QuoteItem Clone() => new() { Text = Text, Author = Author };
}

/// <summary>
/// A line worth rereading, on a rotation.
/// <para>
/// Entirely offline. The quotes are the user's own list, seeded with a bundled set, and which one
/// shows is a pure function of the clock and the seed — see <see cref="QuoteRotation"/>. There is
/// no quote service, no fetch and no cache, which is also why this widget works on the first run
/// with no network and keeps working on a laptop in a tunnel.
/// </para>
/// </summary>
public sealed class QuoteConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Quote;

    public QuoteConfig()
    {
        foreach (var quote in QuoteRotation.Bundled)
        {
            Quotes.Add(new QuoteItem { Text = quote.Text, Author = quote.Author });
        }
    }

    /// <summary>The user's list, in order.</summary>
    public List<QuoteItem> Quotes { get; set; } = [];

    /// <summary>How often it turns over on its own.</summary>
    public QuoteCadence Cadence { get; set; } = QuoteCadence.Daily;

    /// <summary>Walk the list in order, or hash the period so consecutive turns are unrelated.</summary>
    public bool Shuffle { get; set; }

    /// <summary>Which shuffle. Changing it reshuffles the whole rotation, deterministically.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Show who said it.</summary>
    public bool ShowAuthor { get; set; } = true;

    /// <summary>
    /// The quotes that are actually showable: blank rows in the editor are not quotes, and a list
    /// of them is an empty list rather than a widget full of quotation marks round nothing.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<QuoteLine> EffectiveQuotes
    {
        get
        {
            var lines = new List<QuoteLine>(Quotes.Count);

            foreach (var quote in Quotes)
            {
                if (string.IsNullOrWhiteSpace(quote.Text))
                {
                    continue;
                }

                lines.Add(new QuoteLine(quote.Text.Trim(), quote.Author?.Trim() ?? string.Empty));
            }

            return lines;
        }
    }

    /// <summary>How often the widget needs to wake. Zero means it never does.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => QuoteRotation.TickInterval(Cadence);

    public override WidgetConfig Clone()
    {
        // Not the seeding constructor: see ColorSwatchConfig.Clone.
        var copy = new QuoteConfig
        {
            Cadence = Cadence,
            Shuffle = Shuffle,
            Seed = Seed,
            ShowAuthor = ShowAuthor,
        };

        copy.Quotes.Clear();

        foreach (var quote in Quotes)
        {
            copy.Quotes.Add(quote.Clone());
        }

        return copy;
    }
}
