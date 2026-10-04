using System.Text.Json;

namespace Themia.Core.Widgets;

/// <summary>Where the pinned element sits on the rendered page, in CSS pixels of the whole document.</summary>
public readonly record struct ElementBox(double X, double Y, double Width, double Height);

/// <summary>What the page said about the selector after it settled.</summary>
public enum ElementProbeStatus
{
    /// <summary>The reply was not the shape the probe script writes. Treated as a render failure.</summary>
    Invalid = 0,

    /// <summary>[S-477] The selector matched nothing, or matched something with no size.</summary>
    NotFound,

    Found,
}

/// <summary>The probe's verdict, and the box when there is one.</summary>
public readonly record struct ElementProbe(ElementProbeStatus Status, ElementBox Box);

/// <summary>What the picker window said.</summary>
public enum PickerMessageKind
{
    /// <summary>Not a message the picker script sends. Ignored, never acted on.</summary>
    Invalid = 0,

    /// <summary>[S-479] An element was clicked in pick mode.</summary>
    Picked,

    /// <summary>The page left pick mode on its own (Escape), so the toolbar should follow.</summary>
    Browsing,
}

/// <summary>A checked picker message. <see cref="Url"/> has passed <see cref="WebInput.Url"/>.</summary>
public readonly record struct PickerMessage(PickerMessageKind Kind, string Url, string Selector, string Label);

/// <summary>
/// The pure rules behind the web element widget.
/// <para>
/// Everything that crosses into the app from the rendering browser comes through here and is
/// parsed into a typed value: the probe's JSON, the screenshot reply, the picker's messages. The
/// page that produced them is arbitrary third-party code — it can call
/// <c>chrome.webview.postMessage</c> itself, with anything it likes — so a message that is not
/// exactly the expected shape is <see cref="PickerMessageKind.Invalid"/>, never half-read.
/// </para>
/// </summary>
public static class WebElementRules
{
    public const int DefaultRefreshMinutes = 15;
    public const int MinRefreshMinutes = 5;
    public const int MaxRefreshMinutes = 120;

    /// <summary>A selector the picker produces is a short chain; past this it is not one.</summary>
    public const int MaxSelectorLength = 1024;

    /// <summary>The settings page shows the label on one line.</summary>
    public const int MaxLabelLength = 80;

    /// <summary>
    /// The tallest element captured, in CSS pixels. A selector that lands on a whole page body would
    /// otherwise produce a screenshot tens of thousands of pixels tall, rendered into a widget that
    /// can show a few hundred.
    /// </summary>
    public const double MaxCaptureHeight = 4000;

    public const double MaxCaptureWidth = 4000;

    /// <summary>[S-475] Absent and corrupt both fall back to the clamp, never to a stalled timer.</summary>
    public static int ClampRefreshMinutes(int minutes) => Math.Clamp(minutes, MinRefreshMinutes, MaxRefreshMinutes);

    /// <summary>
    /// [S-461] The header title when the user has not named the widget: the page's host, without a
    /// leading <c>www.</c>. Empty when the address is unusable, so the card shows the widget's name.
    /// </summary>
    public static string Title(string? url)
    {
        var resolved = WebInput.Url(url);

        if (!resolved.IsValid || !Uri.TryCreate(resolved.Url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// Whether a selector is worth handing to the browser. It is passed as a JSON string literal,
    /// never spliced into script, so this is about being a selector rather than about being safe:
    /// a line break or a control character means a mangled paste, not a selector.
    /// </summary>
    public static bool IsPlausibleSelector(string? selector)
    {
        var text = selector?.Trim() ?? string.Empty;

        if (text.Length is 0 or > MaxSelectorLength)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Read the probe script's reply: the JSON <c>Runtime.evaluate</c> returns by value.
    /// <para>
    /// The box is clamped to <see cref="MaxCaptureWidth"/> by <see cref="MaxCaptureHeight"/> and to
    /// non-negative coordinates, because it becomes the clip of a screenshot and a page can report
    /// any number it likes. A zero-sized element is "not found": there is nothing to show.
    /// </para>
    /// </summary>
    public static ElementProbe ParseProbe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ElementProbe(ElementProbeStatus.Invalid, default);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("found", out var found)
                || found.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new ElementProbe(ElementProbeStatus.Invalid, default);
            }

            if (!found.GetBoolean())
            {
                return new ElementProbe(ElementProbeStatus.NotFound, default);
            }

            if (!TryNumber(root, "x", out var x) || !TryNumber(root, "y", out var y)
                || !TryNumber(root, "w", out var w) || !TryNumber(root, "h", out var h))
            {
                return new ElementProbe(ElementProbeStatus.Invalid, default);
            }

            x = Math.Max(0, x);
            y = Math.Max(0, y);
            w = Math.Min(w, MaxCaptureWidth);
            h = Math.Min(h, MaxCaptureHeight);

            // Under one CSS pixel either way is a hidden or collapsed element: matched, but empty.
            if (w < 1 || h < 1)
            {
                return new ElementProbe(ElementProbeStatus.NotFound, default);
            }

            return new ElementProbe(ElementProbeStatus.Found, new ElementBox(x, y, w, h));
        }
        catch (JsonException)
        {
            return new ElementProbe(ElementProbeStatus.Invalid, default);
        }
    }

    /// <summary>
    /// The string a DevTools <c>Runtime.evaluate</c> call returned by value, or null when the script
    /// threw, returned something else, or the reply is not that shape. The probe script returns its
    /// verdict as a JSON string, which <see cref="ParseProbe"/> then reads.
    /// </summary>
    public static string? ParseEvaluateString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("exceptionDetails", out _)
                || !root.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The PNG bytes from a DevTools <c>Page.captureScreenshot</c> reply, or null when the reply has
    /// none. The reply is base64 inside JSON; both layers are checked.
    /// </summary>
    public static byte[]? ParseScreenshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.String
                || !data.TryGetBytesFromBase64(out var bytes)
                || bytes.Length == 0)
            {
                return null;
            }

            return bytes;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// [S-479] Read one message from the picker page. Only two shapes are accepted, every field is
    /// checked, and the page address goes through <see cref="WebInput.Url"/>: the picked page is the
    /// one the user navigated to, and it becomes the address the widget renders from then on.
    /// </summary>
    public static PickerMessage ParsePickerMessage(string? json)
    {
        var invalid = new PickerMessage(PickerMessageKind.Invalid, string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024)
        {
            return invalid;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !TryString(root, "kind", out var kind))
            {
                return invalid;
            }

            if (kind == "themia-browsing")
            {
                return new PickerMessage(PickerMessageKind.Browsing, string.Empty, string.Empty, string.Empty);
            }

            if (kind != "themia-picked"
                || !TryString(root, "selector", out var selector)
                || !TryString(root, "url", out var url))
            {
                return invalid;
            }

            if (!IsPlausibleSelector(selector))
            {
                return invalid;
            }

            var checkedUrl = WebInput.Url(url);
            if (!checkedUrl.IsValid)
            {
                return invalid;
            }

            var label = TryString(root, "label", out var raw) ? CleanLabel(raw) : string.Empty;
            return new PickerMessage(PickerMessageKind.Picked, checkedUrl.Url, selector.Trim(), label);
        }
        catch (JsonException)
        {
            return invalid;
        }
    }

    /// <summary>Collapse whitespace and cap the length: the label is page text, shown on one line.</summary>
    public static string CleanLabel(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(Math.Min(text.Length, MaxLabelLength));
        var space = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);

            if (builder.Length >= MaxLabelLength)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static bool TryNumber(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }
}
