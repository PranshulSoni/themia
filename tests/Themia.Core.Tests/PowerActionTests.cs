using Themia.Core.Model;
using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The power tile's decisions: which actions confirm, the arm state machine, what each action calls,
/// how failures read, and the ladder arithmetic. [S-1240..S-1253]
/// <para>
/// Nothing here touches the machine: the Win32 seam only executes <see cref="PowerActionRules.Plan"/>.
/// </para>
/// </summary>
public class PowerActionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // ---- confirm set -----------------------------------------------------------------------

    [Theory]
    [InlineData(PowerAction.Shutdown, true)]
    [InlineData(PowerAction.Restart, true)]
    [InlineData(PowerAction.SignOut, true)]
    [InlineData(PowerAction.Sleep, false)]
    [InlineData(PowerAction.Hibernate, false)]
    [InlineData(PowerAction.Lock, false)]
    [InlineData(PowerAction.ScreenOff, false)]
    public void S1245_Only_session_ending_actions_need_confirm(PowerAction action, bool confirms)
    {
        Assert.Equal(confirms, PowerActionRules.NeedsConfirm(action, confirmEnabled: true));
        Assert.False(PowerActionRules.NeedsConfirm(action, confirmEnabled: false));
    }

    // ---- arm state machine -----------------------------------------------------------------

    [Fact]
    public void S1245_First_press_on_shutdown_arms_and_second_runs()
    {
        var arm = new PowerArm();

        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.Shutdown, true, T0));
        Assert.True(arm.IsArmed(PowerAction.Shutdown, T0.AddSeconds(1)));
        Assert.Equal(PowerPress.Run, arm.Press(PowerAction.Shutdown, true, T0.AddSeconds(1)));
        Assert.Null(arm.Armed);
    }

    [Fact]
    public void S1245_Confirm_off_runs_shutdown_on_one_press()
        => Assert.Equal(PowerPress.Run, new PowerArm().Press(PowerAction.Shutdown, false, T0));

    [Fact]
    public void S1245_Sleep_runs_on_one_press_and_disarms_an_armed_tile()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.Restart, true, T0);

        Assert.Equal(PowerPress.Run, arm.Press(PowerAction.Sleep, true, T0.AddSeconds(1)));
        Assert.Null(arm.Armed);
    }

    [Fact]
    public void S1247_A_double_click_is_one_intent_not_two()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.Shutdown, true, T0);

        Assert.Equal(PowerPress.Ignored, arm.Press(PowerAction.Shutdown, true, T0.AddMilliseconds(150)));
        Assert.True(arm.IsArmed(PowerAction.Shutdown, T0.AddMilliseconds(150)));
        Assert.Equal(PowerPress.Run, arm.Press(PowerAction.Shutdown, true, T0 + PowerArm.MinimumDelay));
    }

    [Fact]
    public void S1247_An_armed_tile_expires_after_five_seconds_without_its_timer()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.Shutdown, true, T0);

        // The timer never fired; the press after the window must arm again, not run.
        var late = T0 + PowerArm.Window;
        Assert.False(arm.IsArmed(PowerAction.Shutdown, late));
        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.Shutdown, true, late));
    }

    [Fact]
    public void S1247_Arming_another_tile_disarms_the_first()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.Shutdown, true, T0);

        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.Restart, true, T0.AddSeconds(1)));
        Assert.Equal(PowerAction.Restart, arm.Armed);

        // The first tile's second press is now a first press again.
        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.Shutdown, true, T0.AddSeconds(2)));
    }

    [Fact]
    public void S1247_Disarm_and_a_clock_jumping_back_both_cancel_the_arm()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.SignOut, true, T0);
        arm.Disarm();
        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.SignOut, true, T0.AddSeconds(1)));

        // A clock that went backwards cannot say how long ago the arm was: treat it as too long.
        Assert.Equal(PowerPress.Armed, arm.Press(PowerAction.SignOut, true, T0));
    }

    [Fact]
    public void S1247_Expire_reports_a_change_only_once()
    {
        var arm = new PowerArm();
        arm.Press(PowerAction.Shutdown, true, T0);

        Assert.False(arm.Expire(T0.AddSeconds(1)));
        Assert.True(arm.Expire(T0.AddSeconds(6)));
        Assert.False(arm.Expire(T0.AddSeconds(7)));
    }

    // ---- call plan -------------------------------------------------------------------------

    [Fact]
    public void S1243_Shutdown_and_restart_power_off_or_reboot_with_the_privilege_and_force_if_hung()
    {
        var shutdown = PowerActionRules.Plan(PowerAction.Shutdown);
        Assert.Equal(PowerCallKind.ExitWindows, shutdown.Kind);
        Assert.Equal(0x8u | 0x10u, shutdown.ExitFlags);
        Assert.True(shutdown.NeedsShutdownPrivilege);

        var restart = PowerActionRules.Plan(PowerAction.Restart);
        Assert.Equal(0x2u | 0x10u, restart.ExitFlags);
        Assert.True(restart.NeedsShutdownPrivilege);
    }

    [Fact]
    public void S1243_Sign_out_lock_and_display_off_need_no_privilege()
    {
        var signOut = PowerActionRules.Plan(PowerAction.SignOut);
        Assert.Equal(PowerCallKind.ExitWindows, signOut.Kind);
        Assert.Equal(0x10u, signOut.ExitFlags);
        Assert.False(signOut.NeedsShutdownPrivilege);

        Assert.Equal(new PowerCall(PowerCallKind.Lock, 0, false, false), PowerActionRules.Plan(PowerAction.Lock));
        Assert.Equal(new PowerCall(PowerCallKind.MonitorOff, 0, false, false), PowerActionRules.Plan(PowerAction.ScreenOff));
    }

    [Fact]
    public void S1243_Sleep_and_hibernate_suspend_with_the_privilege()
    {
        Assert.Equal(new PowerCall(PowerCallKind.Suspend, 0, false, true), PowerActionRules.Plan(PowerAction.Sleep));
        Assert.Equal(new PowerCall(PowerCallKind.Suspend, 0, true, true), PowerActionRules.Plan(PowerAction.Hibernate));
    }

    [Fact]
    public void S1243_Every_action_has_a_plan_label_and_verb()
    {
        foreach (var action in Enum.GetValues<PowerAction>())
        {
            _ = PowerActionRules.Plan(action);
            Assert.False(string.IsNullOrWhiteSpace(PowerActionRules.Label(action)));
            Assert.False(string.IsNullOrWhiteSpace(PowerActionRules.Verb(action)));
            Assert.Contains(action, PowerActionRules.All);
        }
    }

    [Fact]
    public void S1243_An_undefined_action_has_no_plan()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PowerActionRules.Plan((PowerAction)99));

    // ---- privilege and failure mapping -----------------------------------------------------

    [Fact]
    public void S1243_Adjust_success_with_not_all_assigned_is_not_allowed()
    {
        // AdjustTokenPrivileges "succeeds" and assigns nothing: the only signal is the last error.
        Assert.Equal(PowerFailure.NotAllowed, PowerActionRules.PrivilegeOutcome(true, PowerActionRules.NotAllAssigned).Failure);
        Assert.True(PowerActionRules.PrivilegeOutcome(true, 0).Succeeded);
        Assert.Equal(PowerFailure.Win32, PowerActionRules.PrivilegeOutcome(false, PowerActionRules.AccessDenied).Failure);
        Assert.Equal(PowerFailure.NotAllowed, PowerActionRules.PrivilegeOutcome(false, PowerActionRules.PrivilegeNotHeld).Failure);
    }

    [Fact]
    public void S1246_Failures_name_the_action_and_the_reason()
    {
        Assert.Null(PowerActionRules.FailureMessage(PowerAction.Sleep, PowerOutcome.Ok));
        Assert.Equal("Could not hibernate: hibernation is turned off on this PC",
            PowerActionRules.FailureMessage(PowerAction.Hibernate, PowerActionRules.SuspendOutcome(false, hibernate: true)));
        Assert.Equal("Could not sleep: Windows refused to sleep",
            PowerActionRules.FailureMessage(PowerAction.Sleep, PowerActionRules.SuspendOutcome(false, hibernate: false)));
        Assert.Equal("Could not lock: access denied",
            PowerActionRules.FailureMessage(PowerAction.Lock, PowerOutcome.Error(5)));
        Assert.Equal("Could not restart: Windows error 87",
            PowerActionRules.FailureMessage(PowerAction.Restart, PowerOutcome.Error(87)));
        Assert.StartsWith("Could not shut down: this account is not allowed",
            PowerActionRules.FailureMessage(PowerAction.Shutdown, PowerActionRules.PrivilegeOutcome(true, PowerActionRules.NotAllAssigned)), StringComparison.Ordinal);
    }

    // ---- config and settings order ----------------------------------------------------------

    [Fact]
    public void S1242_Defaults_are_the_original_four()
        => Assert.Equal([PowerAction.Shutdown, PowerAction.Restart, PowerAction.Sleep, PowerAction.Lock], new PowerConfig().Actions);

    [Fact]
    public async Task S1241_Action_list_round_trips_as_names_in_order()
    {
        var dir = Path.Combine(Path.GetTempPath(), "themia-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "state.json");

        try
        {
            var state = AppState.CreateDefault();
            var widget = state.ActiveView().Widgets[0];
            widget.Kind = WidgetKind.Power;
            widget.Config = new PowerConfig { Actions = [PowerAction.Lock, PowerAction.ScreenOff, PowerAction.Shutdown] };

            var store = new AppStateStore(path, TimeSpan.Zero);
            store.SaveSoon(state);
            await store.FlushAsync();

            Assert.Contains("screenOff", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

            var loaded = new AppStateStore(path, TimeSpan.Zero).Load();
            var config = Assert.IsType<PowerConfig>(loaded.State.ActiveView().Widgets[0].Config);
            Assert.Equal([PowerAction.Lock, PowerAction.ScreenOff, PowerAction.Shutdown], config.Actions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void S1241_Repair_drops_unknown_and_repeated_actions_loudly()
    {
        var config = new PowerConfig { Actions = [PowerAction.Sleep, (PowerAction)42, PowerAction.Sleep, PowerAction.Lock] };

        var notes = config.Repair();

        Assert.Single(notes);
        Assert.Equal([PowerAction.Sleep, PowerAction.Lock], config.Actions);
        Assert.Empty(config.Repair());
    }

    [Fact]
    public void S1242_Toggle_appends_and_removes_and_settings_lists_every_action_once()
    {
        var actions = new List<PowerAction> { PowerAction.Lock };

        Assert.True(PowerActionRules.Toggle(actions, PowerAction.Hibernate, on: true));
        Assert.False(PowerActionRules.Toggle(actions, PowerAction.Hibernate, on: true));
        Assert.Equal([PowerAction.Lock, PowerAction.Hibernate], actions);

        var order = PowerActionRules.SettingsOrder(actions);
        Assert.Equal(PowerAction.Lock, order[0]);
        Assert.Equal(PowerAction.Hibernate, order[1]);
        Assert.Equal(7, order.Distinct().Count());

        Assert.True(PowerActionRules.Toggle(actions, PowerAction.Lock, on: false));
        Assert.Equal([PowerAction.Hibernate], actions);
    }

    // ---- ladder ------------------------------------------------------------------------------

    [Fact]
    public void S1248_XS_shows_the_four_defaults_as_a_two_by_two_of_icons()
    {
        // XS slot 120x76, compact inset 8 and gap 4.
        var grid = PowerTileLayout.FitGrid(4, 104, 68, labelsWanted: true, gap: 4);

        Assert.Equal(4, grid.Shown);
        Assert.False(grid.Labels);
        Assert.Equal((2, 2), (grid.Columns, grid.Rows));
    }

    [Fact]
    public void S1248_A_wide_strip_puts_one_labelled_row_beside_the_status()
    {
        // Wide slot 480x116, compact inset 8.
        var plan = PowerTileLayout.Plan(4, 464, 108, true, stripWanted: 2, allowStrip: true, allowBeside: true, allowPanel: false, gap: 8);

        Assert.Equal(PowerStripPlacement.Beside, plan.Strip);
        Assert.True(plan.Grid.Labels);
        Assert.Equal((4, 1), (plan.Grid.Columns, plan.Grid.Rows));
        Assert.Equal(2, plan.StripLines);
    }

    [Fact]
    public void S1248_M_keeps_labels_and_adds_the_strip_below()
    {
        // M slot 320x196, inset 12.
        var plan = PowerTileLayout.Plan(4, 296, 184, true, stripWanted: 2, allowStrip: true, allowBeside: false, allowPanel: false, gap: 8);

        Assert.Equal(PowerStripPlacement.Below, plan.Strip);
        Assert.Equal(2, plan.StripLines);
        Assert.True(plan.Grid.Labels);
        Assert.Equal(4, plan.Grid.Shown);
    }

    [Fact]
    public void S1248_The_strip_never_costs_a_label_or_a_button()
    {
        // A box where labels fit alone but not with even one strip line under them.
        var alone = PowerTileLayout.FitGrid(4, 340, 64, true, 8);
        var plan = PowerTileLayout.Plan(4, 340, 64, true, stripWanted: 3, allowStrip: true, allowBeside: false, allowPanel: false, gap: 8);

        Assert.True(alone.Labels);
        Assert.Equal(PowerStripPlacement.None, plan.Strip);
        Assert.True(plan.Grid.Labels);
    }

    [Fact]
    public void S1248_Too_small_for_every_icon_shows_the_first_ones()
    {
        var grid = PowerTileLayout.FitGrid(7, 70, 30, labelsWanted: true, gap: 4);

        Assert.Equal(2, grid.Shown);
        Assert.False(grid.Labels);
    }

    [Fact]
    public void S1249_The_largest_tile_gets_a_side_panel()
    {
        // XL slot 800x516, inset 12.
        var plan = PowerTileLayout.Plan(7, 776, 504, true, stripWanted: 4, allowStrip: true, allowBeside: false, allowPanel: true, gap: 8);

        Assert.Equal(PowerStripPlacement.Panel, plan.Strip);
        Assert.Equal(7, plan.Grid.Shown);
        Assert.True(plan.Grid.Labels);
        Assert.InRange(plan.StripWidth, PowerTileLayout.PanelMinWidth, PowerTileLayout.PanelMaxWidth);
    }

    // ---- status strip --------------------------------------------------------------------------

    [Fact]
    public void S1250_A_desktop_strip_is_source_then_plan_and_no_saver()
    {
        var desktop = PowerRules.Read(1, 128, 255, 0, uint.MaxValue, "Balanced");
        var facts = PowerStrip.Facts(desktop, showPlan: true, showTimeRemaining: true, showSaver: true);

        Assert.Equal([PowerFactKind.Source, PowerFactKind.Plan], facts.Select(f => f.Kind));
        Assert.Equal("Plugged in", facts[0].Line);
        Assert.Equal("Balanced plan", facts[1].Line);
    }

    [Fact]
    public void S1250_A_laptop_on_battery_shows_charge_time_left_and_saver()
    {
        var laptop = PowerRules.Read(0, 0, 64, 1, 7800, "Balanced");
        var facts = PowerStrip.Facts(laptop, showPlan: false, showTimeRemaining: true, showSaver: true);

        Assert.Equal([PowerFactKind.Source, PowerFactKind.Remaining, PowerFactKind.Saver], facts.Select(f => f.Kind));
        Assert.Equal("On battery · 64%", facts[0].Line);
        Assert.Equal("Battery saver on", facts[2].Line);
    }

    [Fact]
    public void S1250_An_unreadable_state_is_one_line_that_says_so()
    {
        var facts = PowerStrip.Facts(PowerSnapshot.Unreadable, true, true, true);
        Assert.Equal(PowerFactKind.Unreadable, Assert.Single(facts).Kind);
    }
}
