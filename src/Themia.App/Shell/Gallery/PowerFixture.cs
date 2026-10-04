#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1253] The power tile's gallery sample: a laptop on battery with every action chosen, so the
/// gallery shows the full grid, the status strip with time left and battery saver, and the plan list.
/// <para>
/// Read only by a power view that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>), so
/// a desktop widget in the same Debug process never sees made-up power state. Debug builds only.
/// </para>
/// </summary>
internal static class PowerFixture
{
    /// <summary>The snapshot preview cards read instead of the machine's, or null for the machine's.</summary>
    public static PowerSnapshot? Snapshot { get; private set; }

    /// <summary>The plan list preview cards read instead of the machine's.</summary>
    public static IReadOnlyList<PowerPlanEntry>? Plans { get; private set; }

    public static void Install(bool populated)
    {
        Snapshot = populated
            ? PowerRules.Read(acLineStatus: 0, batteryFlag: 0, batteryLifePercent: 64, systemStatusFlag: 1, batteryLifeTime: 7800, "Balanced")
            : null;

        Plans = populated
            ? [new PowerPlanEntry("Balanced", true), new PowerPlanEntry("Best performance", false), new PowerPlanEntry("Power saver", false)]
            : null;
    }

    public static PowerConfig Config() => new() { Actions = [.. PowerActionRules.All] };
}
#endif
