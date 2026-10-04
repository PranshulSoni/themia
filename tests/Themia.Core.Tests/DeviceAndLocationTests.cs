using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-1610]..[S-1619] The Wi-Fi name's location consent and the audio widgets' default-output switch.
/// Neither can run in CI for real (no package identity, and switching the machine's audio device is
/// never done by a test), so the decisions are pinned here and the one COM call sits behind
/// <see cref="IDefaultOutputSwitch"/>, faked below.
/// </summary>
public class DeviceAndLocationTests
{
    // ---- [S-1611]..[S-1613] Wi-Fi name and location consent -------------------------------------

    [Fact]
    public void S1612_unpackaged_never_asks_and_never_hints()
    {
        foreach (var permission in Enum.GetValues<LocationPermission>())
        {
            Assert.Equal(WifiNameDecision.None, WifiNameRules.Decide(false, permission, lineShown: true, asked: false, hasName: false, nameRefused: true));
        }
    }

    private static readonly WifiNameDecision Ask = new(WifiHintAction.AskWindows);
    private static readonly WifiNameDecision Settings = new(WifiHintAction.OpenSettings);

    [Fact]
    public void S1611_the_hint_asks_windows_once_and_only_from_a_line_on_screen()
    {
        Assert.Equal(Ask, WifiNameRules.Decide(true, LocationPermission.PromptRequired, lineShown: true, asked: false, hasName: false, nameRefused: true));
        Assert.Equal(WifiNameDecision.None, WifiNameRules.Decide(true, LocationPermission.PromptRequired, lineShown: false, asked: false, hasName: false, nameRefused: true));
    }

    [Fact]
    public void S1611_a_closed_prompt_is_not_shown_again_the_hint_opens_settings()
        => Assert.Equal(Settings, WifiNameRules.Decide(true, LocationPermission.PromptRequired, lineShown: true, asked: true, hasName: false, nameRefused: true));

    [Fact]
    public void S1613_denied_hints_with_a_link_to_settings_and_never_asks()
        => Assert.Equal(Settings, WifiNameRules.Decide(true, LocationPermission.Denied, lineShown: true, asked: false, hasName: false, nameRefused: true));

    [Fact]
    public void S1613_no_hint_is_not_a_hint()
    {
        Assert.False(WifiNameDecision.None.Hint);
        Assert.True(Ask.Hint);
        Assert.True(Settings.Hint);
    }

    [Fact]
    public void S1611_a_name_needs_nothing_more()
    {
        foreach (var permission in Enum.GetValues<LocationPermission>())
        {
            Assert.Equal(WifiNameDecision.None, WifiNameRules.Decide(true, permission, lineShown: true, asked: false, hasName: true, nameRefused: false));
        }
    }

    [Fact]
    public void S1613_allowed_but_still_refused_hints()
        => Assert.Equal(Settings, WifiNameRules.Decide(true, LocationPermission.Allowed, lineShown: true, asked: true, hasName: false, nameRefused: true));

    [Fact]
    public void S1613_allowed_and_not_connected_or_hidden_shows_no_hint()
        => Assert.Equal(WifiNameDecision.None, WifiNameRules.Decide(true, LocationPermission.Allowed, lineShown: true, asked: false, hasName: false, nameRefused: false));

    [Fact]
    public void S1613_the_hint_opens_the_location_privacy_page()
        => Assert.Equal("ms-settings:privacy-location", WifiNameRules.PrivacyPage.OriginalString);

    // ---- [S-1614] what a pick means ------------------------------------------------------------

    private static readonly AudioDevice Speakers = new("{a}", "Speakers");
    private static readonly AudioDevice Headphones = new("{b}", "Headphones");

    [Fact]
    public void S1614_a_preview_never_changes_anything()
    {
        Assert.Equal(DevicePickKind.Nothing, AudioDefaultRules.Plan(true, Headphones, null, "{a}").Kind);
        Assert.Equal(DevicePickKind.Nothing, AudioDefaultRules.Plan(true, null, "{b}", "{a}").Kind);
    }

    [Fact]
    public void S1614_another_device_becomes_the_default()
        => Assert.Equal(new DevicePickPlan(DevicePickKind.SwitchDefault, Headphones), AudioDefaultRules.Plan(false, Headphones, null, "{a}"));

    [Fact]
    public void S1614_a_bound_widget_picking_another_device_still_switches_the_default()
        => Assert.Equal(DevicePickKind.SwitchDefault, AudioDefaultRules.Plan(false, Headphones, "{c}", "{a}").Kind);

    [Fact]
    public void S1614_picking_the_current_default_switches_nothing()
    {
        Assert.Equal(DevicePickKind.Nothing, AudioDefaultRules.Plan(false, Speakers, null, "{A}").Kind);
        Assert.Equal(DevicePickKind.Follow, AudioDefaultRules.Plan(false, Speakers, "{b}", "{a}").Kind);
    }

    [Fact]
    public void S1614_follow_the_default_unbinds_without_touching_windows()
    {
        Assert.Equal(DevicePickKind.Follow, AudioDefaultRules.Plan(false, null, "{b}", "{a}").Kind);
        Assert.Equal(DevicePickKind.Nothing, AudioDefaultRules.Plan(false, null, null, "{a}").Kind);
    }

    [Fact]
    public void S1614_with_no_default_at_all_a_device_is_made_the_default()
        => Assert.Equal(DevicePickKind.SwitchDefault, AudioDefaultRules.Plan(false, Speakers, null, null).Kind);

    // ---- [S-1615] roles ------------------------------------------------------------------------

    [Fact]
    public void S1615_every_role_console_first()
        => Assert.Equal([AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications], AudioDefaultRules.Roles);

    [Fact]
    public void S1615_role_values_are_core_audios_erole()
    {
        Assert.Equal(0, (int)AudioRole.Console);
        Assert.Equal(1, (int)AudioRole.Multimedia);
        Assert.Equal(2, (int)AudioRole.Communications);
    }

    [Fact]
    public void S1615_a_refused_console_role_stops_a_later_role_does_not()
    {
        Assert.False(AudioDefaultRules.Continue(AudioRole.Console, unchecked((int)0x80070005)));
        Assert.True(AudioDefaultRules.Continue(AudioRole.Console, 0));
        Assert.True(AudioDefaultRules.Continue(AudioRole.Multimedia, unchecked((int)0x80004005)));
    }

    [Fact]
    public void S1615_the_console_role_decides_the_outcome()
    {
        Assert.Equal(DefaultSwitchResult.Switched, AudioDefaultRules.Outcome([0, unchecked((int)0x80004005), 0]));
        Assert.Equal(DefaultSwitchResult.AccessDenied, AudioDefaultRules.Outcome([unchecked((int)0x80070005)]));
        Assert.Equal(DefaultSwitchResult.Failed, AudioDefaultRules.Outcome([]));
    }

    // ---- [S-1616] what Windows' answers mean ---------------------------------------------------

    [Theory]
    [InlineData(0, DefaultSwitchResult.Switched)]
    [InlineData(1, DefaultSwitchResult.Switched)] // S_FALSE
    [InlineData(unchecked((int)0x80040154), DefaultSwitchResult.NotSupported)] // REGDB_E_CLASSNOTREG
    [InlineData(unchecked((int)0x80040111), DefaultSwitchResult.NotSupported)] // CLASS_E_CLASSNOTAVAILABLE
    [InlineData(unchecked((int)0x80004002), DefaultSwitchResult.NotSupported)] // E_NOINTERFACE
    [InlineData(unchecked((int)0x80004001), DefaultSwitchResult.NotSupported)] // E_NOTIMPL
    [InlineData(unchecked((int)0x80070005), DefaultSwitchResult.AccessDenied)] // E_ACCESSDENIED
    [InlineData(unchecked((int)0x80070490), DefaultSwitchResult.DeviceGone)] // E_NOTFOUND
    [InlineData(unchecked((int)0x80070057), DefaultSwitchResult.DeviceGone)] // E_INVALIDARG
    [InlineData(unchecked((int)0x88890004), DefaultSwitchResult.DeviceGone)] // AUDCLNT_E_DEVICE_INVALIDATED
    [InlineData(unchecked((int)0x80004005), DefaultSwitchResult.Failed)] // E_FAIL
    public void S1616_hresults_classify(int hresult, DefaultSwitchResult expected)
        => Assert.Equal(expected, AudioDefaultRules.Classify(hresult));

    [Fact]
    public void S1616_switched_follows_the_default_again_quietly()
        => Assert.Equal(new DevicePickResult(null, null, null), AudioDefaultRules.After(DefaultSwitchResult.Switched, Headphones, "{c}", "Old"));

    [Theory]
    [InlineData(DefaultSwitchResult.NotSupported, "does not let Themia")]
    [InlineData(DefaultSwitchResult.AccessDenied, "did not allow")]
    [InlineData(DefaultSwitchResult.Failed, "could not change")]
    public void S1616_a_refusal_binds_the_widget_and_says_why(DefaultSwitchResult result, string why)
    {
        var after = AudioDefaultRules.After(result, Headphones, null, null);
        Assert.Equal("{b}", after.DeviceId);
        Assert.Equal("Headphones", after.DeviceName);
        Assert.Contains(why, after.Notice, StringComparison.Ordinal);
        Assert.Contains("Headphones", after.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void S1616_a_device_that_went_away_leaves_the_widget_as_it_was()
    {
        var after = AudioDefaultRules.After(DefaultSwitchResult.DeviceGone, Headphones, "{c}", "Old");
        Assert.Equal(("{c}", "Old"), (after.DeviceId, after.DeviceName));
        Assert.Equal("Headphones is no longer connected.", after.Notice);
    }

    [Fact]
    public void S1616_a_nameless_device_is_still_named_in_the_notice()
        => Assert.Contains("the device", AudioDefaultRules.After(DefaultSwitchResult.Failed, new AudioDevice("{x}", " "), null, null).Notice, StringComparison.Ordinal);

    // ---- [S-1617] the seam: the whole pick against a fake ---------------------------------------

    private sealed class FakeSwitch(DefaultSwitchResult answer) : IDefaultOutputSwitch
    {
        public List<string> Calls { get; } = [];

        public Task<DefaultSwitchResult> SetDefaultOutputAsync(string deviceId)
        {
            Calls.Add(deviceId);
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task S1617_a_preview_pick_never_reaches_windows()
    {
        var fake = new FakeSwitch(DefaultSwitchResult.Switched);
        Assert.Null(await AudioDefaultRules.PickAsync(true, Headphones, null, null, "{a}", fake));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task S1617_a_pick_switches_once_and_follows_the_default()
    {
        var fake = new FakeSwitch(DefaultSwitchResult.Switched);
        var after = await AudioDefaultRules.PickAsync(false, Headphones, "{c}", "Old", "{a}", fake);
        Assert.Equal(["{b}"], fake.Calls);
        Assert.Equal(new DevicePickResult(null, null, null), after);
    }

    [Fact]
    public async Task S1617_a_refused_switch_binds()
    {
        var fake = new FakeSwitch(DefaultSwitchResult.NotSupported);
        var after = await AudioDefaultRules.PickAsync(false, Headphones, null, null, "{a}", fake);
        Assert.Equal("{b}", after?.DeviceId);
        Assert.NotNull(after?.Notice);
    }

    [Fact]
    public async Task S1617_follow_and_the_current_default_never_reach_windows()
    {
        var fake = new FakeSwitch(DefaultSwitchResult.Switched);
        Assert.Equal(new DevicePickResult(null, null, null), await AudioDefaultRules.PickAsync(false, null, "{b}", "Headphones", "{a}", fake));
        Assert.Null(await AudioDefaultRules.PickAsync(false, Speakers, null, null, "{a}", fake));
        Assert.Empty(fake.Calls);
    }
}
