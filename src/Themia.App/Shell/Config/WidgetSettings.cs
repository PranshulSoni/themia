using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Model;
using Themia.App.Shell;

namespace Themia.App.Shell.Config;

/// <summary>
/// The rows each widget kind contributes to its settings page.
/// <para>
/// Split out from the page so the two things that vary — which rows exist, and where the page puts
/// them — stay independent. Each row writes straight to the live config and then saves, because the
/// widget is on the desktop behind the window and the whole point is to watch it change.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
}
