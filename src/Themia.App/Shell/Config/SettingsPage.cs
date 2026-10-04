using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Themia.App.Shell.Config;

/// <summary>
/// The body of one settings page: its rows, 4 apart, with the page's title kept aside for the
/// window to draw as a breadcrumb above the scrolling area, as Windows Settings does. [S-1541]
/// <para>
/// The title is not drawn here. Settings keeps the page title still while the cards scroll under
/// it, so the window owns the title row and reads <see cref="Title"/> off whatever page it shows.
/// </para>
/// </summary>
internal sealed partial class SettingsPage : StackPanel
{
    public SettingsPage(string title, string? description = null)
    {
        Title = title;
        Description = description;
        Spacing = Application.Current.Resources.TryGetValue("SettingsCardSpacing", out var spacing) && spacing is double value
            ? value
            : 4;

        if (!string.IsNullOrWhiteSpace(description))
        {
            var line = new TextBlock { Text = description, Margin = new Thickness(1, 0, 0, 8) };
            FluentLayout.ApplyStyle(line, "SettingsDescriptionTextBlockStyle");
            Children.Add(line);
        }
    }

    /// <summary>The page's own name: the last crumb of the window's title.</summary>
    public string Title { get; }

    public string? Description { get; }

    /// <summary>
    /// Add the page's rows. The first section heading loses its 30-DIP lead, as Settings' first
    /// heading sits right under the title.
    /// </summary>
    public SettingsPage With(IEnumerable<FrameworkElement> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var row in rows)
        {
            if (Children.Count == (Description is null ? 0 : 1) && FluentLayout.IsHeader(row))
            {
                row.Margin = new Thickness(1, Description is null ? 0 : 8, 0, 6);
            }

            Children.Add(row);
        }

        return this;
    }
}
