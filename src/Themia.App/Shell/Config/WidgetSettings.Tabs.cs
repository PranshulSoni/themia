using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>A tab strip has no settings of its own: its page is the list of what it holds (<see cref="ContainerChildrenRows"/>).</summary>
internal static partial class WidgetSettings
{
    public static void Tabs(List<FrameworkElement> rows, TabsConfig config, Action save)
    {
        // ignore: the shown tab is chosen on the strip itself, and its members are listed by the page.
    }
}
