using System.Text.Json;
using System.Text.Json.Serialization;

namespace Themia.Core.Widgets;

/// <summary>[S-762] Bing's <c>HPImageArchive</c> reply, as far as the gallery reads it.</summary>
public sealed class BingArchiveWire
{
    [JsonPropertyName("images")]
    public List<BingImageWire>? Images { get; set; }
}

public sealed class BingImageWire
{
    /// <summary>A path on www.bing.com, e.g. <c>/th?id=OHR.Example_EN-US123_1920x1080.jpg&amp;pid=hp</c>.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; set; }
}

/// <summary>Source-generated contract for the photo of the day. Native AOT has no reflection serializer.</summary>
[JsonSerializable(typeof(BingArchiveWire))]
public sealed partial class BingWireContext : JsonSerializerContext;

/// <summary>[S-762] Today's photo: where the picture is, and the words that go with it.</summary>
public sealed record BingPhoto(Uri Image, GalleryCaption Caption)
{
    /// <summary>
    /// The archive endpoint the original widget used: today's photo (<c>idx=0</c>), one of them,
    /// the US market's metadata.
    /// </summary>
    public static Uri Endpoint { get; } = new("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-US");

    private const string Host = "www.bing.com";

    /// <summary>Parse a reply body. Null for anything that is not a usable photo.</summary>
    public static BingPhoto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return From(JsonSerializer.Deserialize(json, BingWireContext.Default.BingArchiveWire));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The trust boundary: the reply names a path, and only a path on Bing's own host is accepted.
    /// A protocol-relative <c>//elsewhere</c>, an absolute URL or anything that resolves off
    /// www.bing.com is refused rather than handed to the image decoder, which would fetch it.
    /// </summary>
    public static BingPhoto? From(BingArchiveWire? wire)
    {
        var first = wire?.Images is { Count: > 0 } images ? images[0] : null;
        var path = first?.Url?.Trim();

        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\'))
        {
            return null;
        }

        if (!Uri.TryCreate("https://" + Host + path, UriKind.Absolute, out var image)
            || image.Scheme != Uri.UriSchemeHttps
            || !string.Equals(image.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new BingPhoto(image, new GalleryCaption(first?.Title?.Trim() ?? string.Empty, first?.Copyright?.Trim() ?? string.Empty));
    }
}
