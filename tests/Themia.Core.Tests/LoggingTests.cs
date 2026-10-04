using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Logging and developer mode (WP7, [S-980..S-990]): rotation and retention, the level policy,
/// redaction, the record format, the event log mirror's limits and the session buffer.
/// </summary>
public sealed class LoggingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------------------------
    // [S-980] Rotation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S980_rotates_before_the_file_would_pass_the_cap()
    {
        Assert.False(LogFileRules.ShouldRotate(LogFileRules.MaxFileBytes - 10, 10));
        Assert.True(LogFileRules.ShouldRotate(LogFileRules.MaxFileBytes - 10, 11));
    }

    [Fact]
    public void S980_an_empty_file_is_never_rotated_even_for_an_oversized_record()
    {
        Assert.False(LogFileRules.ShouldRotate(0, LogFileRules.MaxFileBytes * 3));
    }

    [Fact]
    public void S980_archive_names_sort_by_time_and_are_recognised()
    {
        var first = LogFileRules.ArchiveName(Now);
        var later = LogFileRules.ArchiveName(Now.AddSeconds(1));
        var collision = LogFileRules.ArchiveName(Now, attempt: 2);

        Assert.Equal("themia-20261004-120000-000.log", first);
        Assert.Equal("themia-20261004-120000-000-2.log", collision);
        Assert.True(string.CompareOrdinal(first, later) < 0);
        Assert.True(LogFileRules.IsArchive(first));
        Assert.True(LogFileRules.IsArchive(collision));
    }

    [Theory]
    [InlineData("themia.log")]
    [InlineData("notes.txt")]
    [InlineData("themia-old.log")]
    [InlineData("themia-20261004-120000-000.log.txt")]
    [InlineData("../themia-20261004-120000-000.log")]
    public void S980_only_archives_are_candidates_for_deletion(string name)
    {
        Assert.False(LogFileRules.IsArchive(name));
    }

    // ---------------------------------------------------------------------------------------
    // [S-985] Retention
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S985_keeps_the_newest_archives_up_to_the_count()
    {
        var files = Enumerable.Range(0, LogFileRules.MaxArchives + 3)
            .Select(i => Archive(Now.AddHours(-i)))
            .Append(new LogSegment(LogFileRules.CurrentName, 10, Now.AddDays(-100)))
            .Append(new LogSegment("readme.txt", 10, Now.AddDays(-100)))
            .ToList();

        var doomed = LogFileRules.ToDelete(files, Now);

        Assert.Equal(3, doomed.Count);
        Assert.All(doomed, name => Assert.True(LogFileRules.IsArchive(name)));
        Assert.DoesNotContain(LogFileRules.CurrentName, doomed);
        Assert.Contains(Archive(Now.AddHours(-(LogFileRules.MaxArchives + 2))).Name, doomed);
        Assert.DoesNotContain(Archive(Now).Name, doomed);
    }

    [Fact]
    public void S985_deletes_archives_past_the_age_limit_even_under_the_count()
    {
        var fresh = Archive(Now.AddDays(-1));
        var stale = Archive(Now - LogFileRules.MaxAge - TimeSpan.FromMinutes(1));

        var doomed = LogFileRules.ToDelete([fresh, stale], Now);

        Assert.Equal([stale.Name], doomed);
    }

    [Fact]
    public void S985_an_empty_folder_deletes_nothing()
    {
        Assert.Empty(LogFileRules.ToDelete([], Now));
    }

    private static LogSegment Archive(DateTimeOffset at) => new(LogFileRules.ArchiveName(at), 1024, at);

    // ---------------------------------------------------------------------------------------
    // [S-981] [S-982] Level policy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S981_a_release_build_records_warnings_and_errors_until_verbose_is_on()
    {
        Assert.Equal(LogLevel.Warn, LogPolicy.Floor(verbose: false, debugBuild: false));
        Assert.Equal(LogLevel.Debug, LogPolicy.Floor(verbose: true, debugBuild: false));
        Assert.Equal(LogLevel.Debug, LogPolicy.Floor(verbose: false, debugBuild: true));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void S982_verbose_counts_only_in_developer_mode(bool devMode, bool verbose, bool expected)
    {
        var preferences = new AppPreferences { DevMode = devMode, VerboseLogging = verbose };
        Assert.Equal(expected, LogPolicy.Verbose(preferences));
    }

    // ---------------------------------------------------------------------------------------
    // [S-983] Redaction
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("signed in as jane.doe@example.com", "signed in as j***@example.com")]
    [InlineData("imap:jane@example.com@imap.example.com: sign in again", "imap:j***@example.com@imap.example.com: sign in again")]
    [InlineData("from <a@b.co>", "from <a***@b.co>")]
    public void S983_email_addresses_keep_only_their_first_character_and_domain(string input, string expected)
    {
        Assert.Equal(expected, LogRedactor.Redact(input));
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi_jkl123")]
    [InlineData("header bearer QWxhZGRpbjpvcGVuIHNlc2FtZQ==")]
    [InlineData("refresh_token=0.AXkA-very-secret-value&scope=mail")]
    [InlineData("{\"access_token\":\"EwBwA8l6BAAUSecret\",\"expires_in\":3600}")]
    [InlineData("password: hunter22")]
    [InlineData("client_secret=s3cr3t")]
    [InlineData("GET https://login.example.com/callback?code=M.R3_BAY.abc123&state=xyz")]
    [InlineData("token ghp_1234567890abcdefghijABCDEFGHIJ0123 rejected")]
    [InlineData("key sk-ant-api03-AbCdEfGh12345678 refused")]
    [InlineData("google ya29.a0AfH6SMBxyzxyzxyz expired")]
    [InlineData("jwt eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.c2lnbmF0dXJl")]
    [InlineData("https://user:pa55word@dav.example.com/cal/")]
    public void S983_secrets_never_reach_a_sink(string input)
    {
        var redacted = LogRedactor.Redact(input);

        Assert.Contains(LogRedactor.Mask, redacted, StringComparison.Ordinal);
        foreach (var secret in new[] { "abc.def-ghi_jkl123", "QWxhZGRpbjpvcGVuIHNlc2FtZQ", "very-secret-value", "EwBwA8l6BAAUSecret", "hunter22", "s3cr3t", "M.R3_BAY.abc123", "xyz", "ghp_1234567890", "sk-ant-api03", "ya29.a0AfH6", "eyJhbGciOiJSUzI1NiJ9", "pa55word" })
        {
            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void S983_a_long_opaque_mixed_case_run_is_masked()
    {
        const string token = "M8xQ2vP0rLk9Zt7Yw6Uq5Rn4Sm3Tl2Vk1Wj0Xi9Yh8Zg7";
        Assert.Equal($"token {LogRedactor.Mask} refused", LogRedactor.Redact($"token {token} refused"));
    }

    [Theory]
    [InlineData("could not open C:\\Users\\Jane\\Documents\\Quarterly Report 2026.xlsx")]
    [InlineData("graph.microsoft.com answered 429; asking again in 30s (2/5)")]
    [InlineData("GET https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$top=25")]
    [InlineData("exit code: 5")]
    [InlineData("commit 3f786850e387550fdab836ed7e6dc881de23001b pushed")]
    [InlineData("the token stays on api.github.com")]
    [InlineData("relayout \\\\.\\DISPLAY1: view='Desktop' widgets=4")]
    public void S983_ordinary_lines_pass_unchanged(string input)
    {
        Assert.Equal(input, LogRedactor.Redact(input));
    }

    [Fact]
    public void S983_Log_redacts_the_message_and_the_exception_before_sinks()
    {
        var seen = new List<LogRecord>();
        Log.AddSink(record =>
        {
            if (record.Scope == "S983-test")
            {
                lock (seen)
                {
                    seen.Add(record);
                }
            }
        });

        Log.Error("S983-test", "failed for jane@example.com", new InvalidOperationException("refused password=hunter22"));

        var record = Assert.Single(seen);
        Assert.DoesNotContain("jane@", record.Message, StringComparison.Ordinal);
        Assert.NotNull(record.Detail);
        Assert.DoesNotContain("hunter22", record.Detail, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), record.Detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // [S-984] Format
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S984_a_record_is_one_header_line_with_its_detail_indented()
    {
        var record = new LogRecord(Now, LogLevel.Error, "mail", "could not read", "System.IO.IOException: gone\r\n   at X.Y()");

        var text = LogFormat.Record(record);

        Assert.Equal(
            "2026-10-04 12:00:00.000 +00:00 ERROR mail: could not read\n    System.IO.IOException: gone\n       at X.Y()",
            text);
    }

    [Fact]
    public void S984_a_message_cannot_forge_a_second_record()
    {
        var record = new LogRecord(Now, LogLevel.Info, "rss", "title\n2026-10-04 12:00:00.000 +00:00 ERROR fake: boom", null);

        var text = LogFormat.Record(record);

        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void S984_an_oversized_record_is_cut()
    {
        var record = new LogRecord(Now, LogLevel.Warn, "web", new string('x', LogFileRules.MaxRecordChars * 2), null);

        var text = LogFormat.Record(record);

        Assert.True(text.Length < LogFileRules.MaxRecordChars + 64);
        Assert.EndsWith("(record truncated)", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // [S-986] Event log mirror
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S986_the_event_log_gets_the_cap_then_one_notice_then_nothing()
    {
        Assert.Equal(EventLogEmit.Record, LogPolicy.EventDecision(0));
        Assert.Equal(EventLogEmit.Record, LogPolicy.EventDecision(LogPolicy.EventLogCap - 1));
        Assert.Equal(EventLogEmit.FinalNotice, LogPolicy.EventDecision(LogPolicy.EventLogCap));
        Assert.Equal(EventLogEmit.Suppressed, LogPolicy.EventDecision(LogPolicy.EventLogCap + 1));
        Assert.Equal(EventLogEmit.Suppressed, LogPolicy.EventDecision(int.MaxValue));
    }

    [Fact]
    public void S986_event_text_is_cut_without_splitting_a_surrogate_pair()
    {
        // A pair straddling the cut: the high half sits at the last kept position.
        var text = new string('a', LogPolicy.EventLogMaxChars - 1) + "😀" + "tail";

        var cut = LogPolicy.ForEventLog(text);

        Assert.False(char.IsHighSurrogate(cut[LogPolicy.EventLogMaxChars - 2]));
        Assert.StartsWith(new string('a', LogPolicy.EventLogMaxChars - 1) + "…", cut, StringComparison.Ordinal);
        Assert.Equal("short", LogPolicy.ForEventLog("short"));
    }

    // ---------------------------------------------------------------------------------------
    // [S-987] [S-988] Session buffer and the Logs page filter
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void S987_the_buffer_keeps_the_newest_records_oldest_first()
    {
        var buffer = new LogBuffer(capacity: 3);
        for (var i = 0; i < 5; i++)
        {
            buffer.Add(Record(LogLevel.Info, "s", $"m{i}"));
        }

        Assert.Equal(["m2", "m3", "m4"], buffer.Snapshot().Select(r => r.Message));
        Assert.Equal(5, buffer.Total);
    }

    [Fact]
    public void S987_clear_empties_the_list_and_says_so()
    {
        var buffer = new LogBuffer(capacity: 3);
        var changes = 0;
        buffer.Changed += () => changes++;

        buffer.Add(Record(LogLevel.Info, "s", "m"));
        buffer.Clear();
        buffer.Add(Record(LogLevel.Info, "s", "after"));

        Assert.Equal(3, changes);
        Assert.Equal(["after"], buffer.Snapshot().Select(r => r.Message));
    }

    [Fact]
    public void S988_the_filter_is_by_level_and_source_newest_first_and_limited()
    {
        IReadOnlyList<LogRecord> records =
        [
            Record(LogLevel.Debug, "mail", "d1"),
            Record(LogLevel.Warn, "mail", "w1"),
            Record(LogLevel.Error, "rss", "e1"),
            Record(LogLevel.Error, "mail", "e2"),
            Record(LogLevel.Info, "mail", "i1"),
        ];

        Assert.Equal(["e2", "e1", "w1"], LogBuffer.Filter(records, LogLevel.Warn, null, 10).Select(r => r.Message));
        Assert.Equal(["i1", "e2", "w1", "d1"], LogBuffer.Filter(records, LogLevel.Debug, "mail", 10).Select(r => r.Message));
        Assert.Equal(["i1", "e2"], LogBuffer.Filter(records, LogLevel.Debug, "mail", 2).Select(r => r.Message));
        Assert.Empty(LogBuffer.Filter(records, LogLevel.Error, "weather", 10));
    }

    private static LogRecord Record(LogLevel level, string scope, string message) => new(Now, level, scope, message, null);
}
