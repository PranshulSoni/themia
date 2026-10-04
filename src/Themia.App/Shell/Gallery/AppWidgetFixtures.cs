#if DEBUG
using Themia.App.Widgets;
using Themia.Core.Model;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1534] The gallery's populated pages for Apps and Search. Apps reads this machine's Start menu
/// on both pages (read-only); its fixture page shows the other layout, rows, so both ladders are
/// drawn. Search's fixture seeds this session's recent searches with made-up queries, which its
/// plain page clears again.
/// </summary>
internal static class AppWidgetFixtures
{
    public static bool Has(WidgetKind kind) => kind is WidgetKind.Apps or WidgetKind.WebSearch;

    public static WidgetConfig? Create(WidgetKind kind)
    {
        switch (kind)
        {
            case WidgetKind.Apps:
                return new AppsConfig { View = AppsViewMode.List };

            case WidgetKind.WebSearch:
                Reset();
                foreach (var query in (string[])["winui selectorbar", "sunrise tomorrow", "pasta carbonara", "weather zurich", "native aot trimming", "flight LX 318", "dotnet 10 release notes", "rust vs c#", "keyboard shortcuts windows 11", "espresso ratio"])
                {
                    WebSearchView.History.Add(query);
                }

                return new WebSearchConfig();

            default:
                return null;
        }
    }

    /// <summary>The plain pages start with no recent searches.</summary>
    public static void Reset() => WebSearchView.History.Clear();
}
#endif
