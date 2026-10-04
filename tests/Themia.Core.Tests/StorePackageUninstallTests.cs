using Themia.Core.Settings;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The uninstall plan as it meets the Store package ([S-1070], [S-1071]).</summary>
public sealed class StorePackageUninstallTests
{
    private const string Roaming = @"C:\Users\someone\AppData\Roaming";
    private const string Local = @"C:\Users\someone\AppData\Local";

    [Fact]
    public void S1070_packaged_data_always_goes_with_the_package()
    {
        var plan = UninstallPlan.Create(packaged: true, deleteData: false, Roaming, Local, "Themia", "1.0.0");

        Assert.True(plan.DataGoesWithPackage);
        Assert.Equal([$@"{Roaming}\Themia", $@"{Local}\Themia"], plan.FoldersToDelete);
    }

    [Fact]
    public void S1070_loose_build_keeps_the_choice()
    {
        var plan = UninstallPlan.Create(packaged: false, deleteData: false, Roaming, Local, "Themia", "1.0.0");

        Assert.False(plan.DataGoesWithPackage);
        Assert.Empty(plan.FoldersToDelete);
    }

    [Fact]
    public void S1071_packaged_steps_clear_autostart_then_data_then_package()
    {
        var plan = UninstallPlan.Create(packaged: true, deleteData: false, Roaming, Local, "Themia", "1.0.0");

        Assert.Equal(
            [UninstallStep.RemoveAutostart, UninstallStep.DeleteDataNow, UninstallStep.RemovePackage],
            plan.Steps);
    }

    [Fact]
    public void S1071_loose_steps_clear_autostart_first()
    {
        var keep = UninstallPlan.Create(packaged: false, deleteData: false, Roaming, Local, "Themia", "1.0.0");
        Assert.Equal([UninstallStep.RemoveAutostart, UninstallStep.OpenInstalledApps], keep.Steps);

        var wipe = UninstallPlan.Create(packaged: false, deleteData: true, Roaming, Local, "Themia", "1.0.0");
        Assert.Equal(
            [UninstallStep.RemoveAutostart, UninstallStep.StartDataPurge, UninstallStep.OpenInstalledApps],
            wipe.Steps);
    }

    [Fact]
    public void S1071_loose_with_nothing_safe_to_delete_starts_no_purge()
    {
        // A missing profile root yields no folders: then there is nothing for the helper to do.
        var plan = UninstallPlan.Create(packaged: false, deleteData: true, null, null, "Themia", "1.0.0");
        Assert.DoesNotContain(UninstallStep.StartDataPurge, plan.Steps);
    }
}
