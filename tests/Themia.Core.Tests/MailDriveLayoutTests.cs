using System.Text.Json;
using Themia.Core.Accounts;
using Themia.Core.Geometry;
using Themia.Core.Mail;
using Themia.Core.Model;
using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1280]..[S-1294] Mail and OneDrive using their whole tile; the shared list keys and fade.</summary>
public sealed class MailDriveLayoutTests
{
    // ---- [S-1288] list keys ------------------------------------------------------------------------

    [Theory]
    [InlineData(ListKey.Down, 3, 10, ListCommand.Focus, 4)]
    [InlineData(ListKey.Down, 9, 10, ListCommand.Focus, 9)]
    [InlineData(ListKey.Up, 0, 10, ListCommand.Focus, 0)]
    [InlineData(ListKey.Up, 5, 10, ListCommand.Focus, 4)]
    [InlineData(ListKey.PageDown, 3, 25, ListCommand.Focus, 13)]
    [InlineData(ListKey.PageDown, 20, 25, ListCommand.Focus, 24)]
    [InlineData(ListKey.PageUp, 4, 25, ListCommand.Focus, 0)]
    [InlineData(ListKey.Home, 7, 10, ListCommand.Focus, 0)]
    [InlineData(ListKey.End, 2, 10, ListCommand.Focus, 9)]
    [InlineData(ListKey.Enter, 2, 10, ListCommand.Open, 2)]
    [InlineData(ListKey.Delete, 2, 10, ListCommand.Delete, 2)]
    [InlineData(ListKey.Rename, 2, 10, ListCommand.Rename, 2)]
    public void S1288_keys_move_and_act_on_the_focused_row(ListKey key, int index, int count, ListCommand command, int target)
        => Assert.Equal(new ListKeyResult(command, target), ListKeys.Decide(key, index, count));

    [Fact]
    public void S1288_with_no_focus_movement_lands_on_an_end_and_actions_do_nothing()
    {
        Assert.Equal(new ListKeyResult(ListCommand.Focus, 0), ListKeys.Decide(ListKey.Down, -1, 5));
        Assert.Equal(new ListKeyResult(ListCommand.Focus, 0), ListKeys.Decide(ListKey.Up, -1, 5));
        Assert.Equal(new ListKeyResult(ListCommand.Focus, 4), ListKeys.Decide(ListKey.End, -1, 5));
        Assert.Equal(ListCommand.None, ListKeys.Decide(ListKey.Enter, -1, 5).Command);
        Assert.Equal(ListCommand.None, ListKeys.Decide(ListKey.Delete, -1, 5).Command);
    }

    [Fact]
    public void S1288_escape_releases_even_an_empty_list_and_a_shrunk_list_acts_on_its_last_row()
    {
        Assert.Equal(ListCommand.Release, ListKeys.Decide(ListKey.Escape, 3, 0).Command);
        Assert.Equal(ListCommand.None, ListKeys.Decide(ListKey.Down, 0, 0).Command);
        Assert.Equal(new ListKeyResult(ListCommand.Open, 2), ListKeys.Decide(ListKey.Enter, 7, 3));

        Assert.Equal(2, ListKeys.AfterRemoval(2, 5));
        Assert.Equal(3, ListKeys.AfterRemoval(4, 4));
        Assert.Equal(-1, ListKeys.AfterRemoval(0, 0));
    }

    // ---- [S-1281] fade -----------------------------------------------------------------------------

    [Fact]
    public void S1281_rows_fade_toward_the_bottom_only_while_more_is_below()
    {
        Assert.Equal(1, ListFade.Opacity(0, 40, 200, moreBelow: true));
        Assert.Equal(1, ListFade.Opacity(160, 200, 200, moreBelow: false));
        Assert.Equal(ListFade.Floor, ListFade.Opacity(200, 240, 200, moreBelow: true));

        var near = ListFade.Opacity(170, 210, 200, moreBelow: true);
        var nearer = ListFade.Opacity(180, 220, 200, moreBelow: true);
        Assert.InRange(near, ListFade.Floor, 1);
        Assert.True(nearer < near);

        Assert.True(ListFade.MoreBelow(200, 400));
        Assert.False(ListFade.MoreBelow(400.2, 400));
    }

    // ---- [S-1282]..[S-1287] mail -------------------------------------------------------------------

    [Fact]
    public void S1283_a_wide_strip_and_a_very_wide_tile_go_one_line_per_message()
    {
        var config = new EmailConfig();

        Assert.True(MailLayout.Rows(config, 480, 116).Inline);    // Wide strip
        Assert.False(MailLayout.Rows(config, 480, 300).Inline);   // wide but tall: two-line rows still pay
        Assert.True(MailLayout.Rows(config, 800, 516).Inline);    // XL
        Assert.False(MailLayout.Rows(config, 320, 196).Inline);   // M
        Assert.False(MailLayout.Rows(config, 200, 356).Inline);   // Tall

        // The row view is one line already; it keeps its columns.
        Assert.False(MailLayout.Rows(new EmailConfig { ViewMode = MailViewMode.Row }, 800, 516).Inline);
    }

    [Fact]
    public void S1283_one_line_rows_add_preview_then_account_then_provider_as_they_widen()
    {
        var config = new EmailConfig();

        var strip = MailLayout.Rows(config, 480, 116);
        Assert.False(strip.Preview);
        Assert.False(strip.Account);

        var wider = MailLayout.Rows(config, 560, 116);
        Assert.True(wider.Preview);
        Assert.True(wider.Account);
        Assert.False(wider.Provider);

        var xl = MailLayout.Rows(config, 800, 516);
        Assert.True(xl.Provider);
        Assert.Equal(1, xl.PreviewLines);

        // The compact view never shows a preview, on one line or two.
        Assert.False(MailLayout.Rows(new EmailConfig { ViewMode = MailViewMode.Compact }, 800, 516).Preview);
    }

    [Fact]
    public void S1282_the_envelope_shows_from_a_medium_tile()
    {
        var config = new EmailConfig();
        Assert.False(MailLayout.Rows(config, 200, 106).Envelope);
        Assert.True(MailLayout.Rows(config, 320, 196).Envelope);
        Assert.True(MailLayout.Rows(config, 480, 116).Envelope);
    }

    [Fact]
    public void S1286_a_big_tile_fetches_enough_whole_rows_to_fill_it()
    {
        var config = new EmailConfig { MaxCount = 10 };
        Assert.Equal(10, config.FetchCount(4));
        Assert.Equal(16, config.FetchCount(16));
        Assert.Equal(50, config.FetchCount(80));

        var drive = new OneDriveConfig { FileCount = 15 };
        Assert.Equal(15, drive.FetchCount(3));
        Assert.Equal(17, drive.FetchCount(17));
        Assert.Equal(OneDriveConfig.MaxFiles, drive.FetchCount(90));
    }

    [Fact]
    public void S1285_the_footer_sums_folder_counts_and_refuses_a_partial_total()
    {
        MailAccountOutcome Ok(string id, long total, long unread) => new(id, MailAccountState.Ok, FetchStatus.Ok, null, new MailFolderStatus(total, unread));

        Assert.Equal("4 unread of 128 in Inbox", MailLayout.Footer([Ok("a", 100, 3), Ok("b", 28, 1)], MailFolder.Inbox));
        Assert.Equal("28 in Inbox, all read", MailLayout.Footer([Ok("b", 28, 0)], MailFolder.Inbox));

        // A failed account is left out; an answered one with no counts makes the total unknowable.
        var failed = new MailAccountOutcome("c", MailAccountState.Failed, FetchStatus.Offline, null);
        Assert.Equal("1 unread of 28 in Inbox", MailLayout.Footer([Ok("b", 28, 1), failed], MailFolder.Inbox));
        Assert.Null(MailLayout.Footer([Ok("b", 28, 1), new MailAccountOutcome("d", MailAccountState.Ok, FetchStatus.Ok, null)], MailFolder.Inbox));
        Assert.Null(MailLayout.Footer([failed], MailFolder.Inbox));
    }

    [Fact]
    public void S1287_a_wide_row_view_adds_the_preview_after_a_lone_subject()
    {
        var chosen = EmailConfig.DefaultColumns; // From, Subject, Date

        Assert.Equal(chosen, MailLayout.Columns(chosen, 600, 8).Select(c => c.Column));
        Assert.Equal(
            [MailColumn.From, MailColumn.Subject, MailColumn.Preview, MailColumn.Date],
            MailLayout.Columns(chosen, 760, 8).Select(c => c.Column));

        // A user who chose the preview keeps their own order; one without a subject gets none added.
        MailColumn[] own = [MailColumn.Preview, MailColumn.From, MailColumn.Subject];
        Assert.Equal(own, MailLayout.Columns(own, 760, 8).Select(c => c.Column));
        MailColumn[] noSubject = [MailColumn.From, MailColumn.Date];
        Assert.Equal(noSubject, MailLayout.Columns(noSubject, 760, 8).Select(c => c.Column));
    }

    // ---- [S-1290]..[S-1294] OneDrive ---------------------------------------------------------------

    [Fact]
    public void S1290_details_join_the_name_as_the_tile_widens()
    {
        // Text widths: the tile less 32 of row padding and 28 of icon.
        Assert.Equal(DriveRowMode.NameOnly, DriveLayout.Fit(60, 76, compact: true, showLocation: true).Mode);   // XS
        Assert.Equal(DriveRowMode.NameOnly, DriveLayout.Fit(140, 106, compact: true, showLocation: true).Mode); // S
        Assert.Equal(DriveRowMode.TwoLine, DriveLayout.Fit(140, 356, compact: false, showLocation: true).Mode); // Tall

        var medium = DriveLayout.Fit(260, 196, compact: true, showLocation: true);
        Assert.Equal(DriveRowMode.Columns, medium.Mode);
        Assert.Equal([DriveColumn.Modified], medium.Columns.Select(c => c.Column));
        Assert.False(medium.ColumnHeader);

        var wide = DriveLayout.Fit(420, 116, compact: true, showLocation: true);
        Assert.Equal([DriveColumn.Modified, DriveColumn.Size], wide.Columns.Select(c => c.Column));

        var large = DriveLayout.Fit(500, 376, compact: false, showLocation: true);
        Assert.Equal([DriveColumn.Modified, DriveColumn.Size, DriveColumn.ModifiedBy], large.Columns.Select(c => c.Column));
        Assert.True(large.ColumnHeader);

        var huge = DriveLayout.Fit(740, 516, compact: false, showLocation: true);
        Assert.Equal([DriveColumn.Modified, DriveColumn.Size, DriveColumn.ModifiedBy, DriveColumn.Location], huge.Columns.Select(c => c.Column));

        // Inside one folder every item is in the same place: no location column.
        Assert.DoesNotContain(DriveLayout.Fit(740, 516, compact: false, showLocation: false).Columns, c => c.Column == DriveColumn.Location);
    }

    [Fact]
    public void S1290_the_name_keeps_its_minimum_width()
    {
        foreach (var width in new[] { 216.0, 300, 389, 460, 560, 700, 900 })
        {
            var fit = DriveLayout.Fit(width, 300, compact: false, showLocation: true);
            var details = fit.Columns.Sum(c => c.Width + DriveLayout.Gap);
            Assert.True(width - details >= DriveLayout.NameMinWidth - Disclosure.Tolerance, $"{width}: {width - details}");
        }
    }

    [Fact]
    public void S1293_maps_who_changed_an_item_where_it_is_and_what_a_folder_holds()
    {
        var item = JsonSerializer.Deserialize(
            """
            {"id":"F1","name":"Reports","folder":{"childCount":12},"lastModifiedDateTime":"2026-10-04T09:00:00Z",
             "lastModifiedBy":{"user":{"displayName":"Dana Ruiz"}},"parentReference":{"path":"/drive/root:/Work/Q%203"}}
            """,
            DriveWireContext.Default.GraphDriveItem)!;

        var mapped = DriveRules.FromGraph(item)!;
        Assert.Equal("Dana Ruiz", mapped.ModifiedBy);
        Assert.Equal("Work › Q 3", mapped.Location);
        Assert.Equal(12, mapped.ChildCount);

        Assert.Equal(DriveRules.RootName, DriveRules.Location("/drive/root:"));
        Assert.Null(DriveRules.Location(null));
        Assert.Null(DriveRules.Location("/sites/x"));

        Assert.Equal("1 item", DriveRules.ChildCountText(1));
        Assert.Equal("0 items", DriveRules.ChildCountText(0));
        Assert.Equal(string.Empty, DriveRules.ChildCountText(null));
    }

    [Fact]
    public void S1294_reads_the_quota_and_drops_one_that_says_too_little()
    {
        var drive = JsonSerializer.Deserialize("""{"quota":{"used":13314398618,"total":1099511627776}}""", DriveWireContext.Default.GraphDrive)!;
        var quota = DriveRules.FromGraph(drive)!.Value;
        Assert.Equal("12 GB of 1.0 TB used", DriveRules.QuotaText(quota));

        Assert.Null(DriveRules.FromGraph(new GraphDrive()));
        Assert.Null(DriveRules.FromGraph(new GraphDrive { Quota = new GraphQuota { Used = 5, Total = 0 } }));
    }

    [Fact]
    public void S1291_click_mode_round_trips_and_an_unknown_one_is_repaired()
    {
        var state = AppState.CreateDefault();
        var widget = WidgetInstance.Create(WidgetKind.OneDrive, new FracRect(0.1, 0.1, 0.2, 0.2));
        widget.Config = new OneDriveConfig { ClickMode = ClickMode.Single };
        state.Views[0].Widgets.Add(widget);

        var json = SettingsTransfer.Serialize(state);
        Assert.Contains("\"clickMode\": \"Single\"", json, StringComparison.Ordinal);

        var back = AppStateStore.Parse(json).State!.Views[0].Widgets.Single(w => w.Kind == WidgetKind.OneDrive).Config;
        Assert.Equal(ClickMode.Single, Assert.IsType<OneDriveConfig>(back).ClickMode);

        var broken = new OneDriveConfig { ClickMode = (ClickMode)9, FileCount = 400 };
        Assert.Equal(2, broken.Repair().Count);
        Assert.Equal(ClickMode.Double, broken.ClickMode);
        Assert.Equal(OneDriveConfig.MaxFiles, broken.FileCount);
    }
}
