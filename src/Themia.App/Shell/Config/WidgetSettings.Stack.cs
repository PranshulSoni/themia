using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>A stack has no settings of its own: its page is the list of what it holds (<see cref="ContainerChildrenRows"/>).</summary>
internal static partial class WidgetSettings
{
    public static void Stack(List<FrameworkElement> rows, StackConfig config, Action save)
    {
        // ignore: the open child is chosen on the stack itself, and its members are listed by the page.
    }
}
