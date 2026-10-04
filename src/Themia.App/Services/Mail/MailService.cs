using System.Net.Http;
using Themia.App.Services.Accounts;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Mail;
using Themia.Core.Widgets;

namespace Themia.App.Services.Mail;

/// <summary>
/// [S-546] Mail for every Mail widget: one per app, reached through <see cref="AppServices.Mail"/>,
/// built on Themia's own accounts (<see cref="AccountService"/>).
/// <para>
/// Widgets open <b>feeds</b> — an account set, a folder, a count — and widgets asking the same thing
/// share one. Each account in a feed is probed for its folder's counts every
/// <see cref="MailRefresh.ProbeInterval"/> (one small request, or one short IMAP session) and its
/// list is fetched only when the counts moved or the list is old (<see cref="MailRefresh"/>). A
/// failure backs off on <see cref="Backoff"/>'s schedule, a 429 much longer. The cadence rides the
/// shared <see cref="Ticker"/>, so it stops while the desktop is hidden and costs nothing once the
/// last Mail widget is gone.
/// </para>
/// <para>
/// Everything here runs on the UI thread: the ticker fires there, and every await resumes there, so
/// the feed state has one writer and needs no lock. IMAP socket work runs on the thread pool and
/// hands back a finished list.
/// </para>
/// </summary>
internal sealed class MailService : IMailSource, IDisposable
{
    private const string Scope = "mail";

    private readonly AccountService _accounts;
    private readonly Ticker _ticker;
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.Ordinal);
    private IDisposable? _tick;
    private bool? _outlookInstalled;
    private bool _disposed;

    public MailService(AccountService accounts, Ticker ticker)
    {
        _accounts = accounts;
        _ticker = ticker;
        _accounts.AccountsChanged += OnAccountsChanged;
    }

    public event Action? AccountsChanged;

    public IReadOnlyList<MailAccount> Accounts =>
        [.. _accounts.WithFeature(AccountFeatures.Mail).Select(a => new MailAccount(a.Id, a.Provider, a.Label, a.Address, a.Problem))];

    public IMailFeed Open(MailQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_feeds.TryGetValue(query.Key, out var feed))
        {
            feed = new Feed(this, query);
            _feeds[query.Key] = feed;
            feed.Reconcile();
        }

        var handle = new Handle(feed);
        feed.Handles.Add(handle);

        _tick ??= _ticker.Subscribe(TimeSpan.FromMinutes(1), OnTick);

        // A new feed starts now rather than at the next tick; an account already in flight is
        // skipped, so this never doubles a request.
        feed.Poll(force: false);

        return handle;
    }

    private void Release(Handle handle)
    {
        var feed = handle.Feed;
        feed.Handles.Remove(handle);

        if (feed.Handles.Count > 0)
        {
            return;
        }

        feed.Stop();
        _feeds.Remove(feed.Query.Key);

        if (_feeds.Count == 0)
        {
            _tick?.Dispose();
            _tick = null;
        }
    }

    private void OnTick()
    {
        foreach (var feed in _feeds.Values.ToArray())
        {
            feed.Poll(force: false);
        }
    }

    private void OnAccountsChanged()
    {
        foreach (var feed in _feeds.Values.ToArray())
        {
            feed.Reconcile();
        }

        AccountsChanged?.Invoke();
    }

    public async Task<string?> ChangeAsync(MailMessage message, MailChange change)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await ApplyAsync(message, change, CancellationToken.None).ConfigureAwait(true);
        }
        catch (AccountException ex)
        {
            Log.Warn(Scope, $"could not {change} a message in {message.AccountId}: {ex.Message}");
            return ex.Message;
        }

        Log.Info(Scope, $"{change} done for a message in {message.AccountId}");

        foreach (var feed in _feeds.Values)
        {
            feed.Changed(message, change);
        }

        return null;
    }

    private async Task ApplyAsync(MailMessage message, MailChange change, CancellationToken cancellation)
    {
        switch (message.Provider)
        {
            case AccountProvider.Microsoft:
                var url = change switch
                {
                    MailChange.MarkRead or MailChange.MarkUnread => MailWire.GraphMessage(message.RawId),
                    _ => MailWire.GraphMove(message.RawId),
                };

                if (change is MailChange.MarkRead or MailChange.MarkUnread)
                {
                    await _accounts.SendJsonAsync(message.AccountId, HttpMethod.Patch, url, MailWire.GraphReadBody(change == MailChange.MarkRead), cancellation).ConfigureAwait(true);
                }
                else if (change == MailChange.Delete && message.Folder == MailFolder.Deleted)
                {
                    // Already in Deleted Items: Graph's DELETE moves it to Recoverable Items.
                    await _accounts.SendJsonAsync(message.AccountId, HttpMethod.Delete, MailWire.GraphMessage(message.RawId), null, cancellation).ConfigureAwait(true);
                }
                else
                {
                    await _accounts.SendJsonAsync(message.AccountId, HttpMethod.Post, url, MailWire.GraphMoveBody(Destination(change)), cancellation).ConfigureAwait(true);
                }

                return;

            case AccountProvider.Imap:
                if (ImapMessageId.Parse(message.RawId) is not { } id)
                {
                    throw new AccountException(AccountError.Refused, "That message's handle is not one this version understands. Refresh and try again.");
                }

                var login = _accounts.GetImapCredentials(message.AccountId);
                await Task.Run(() => change switch
                {
                    MailChange.MarkRead => ImapMailClient.MarkReadAsync(login, id, true, cancellation),
                    MailChange.MarkUnread => ImapMailClient.MarkReadAsync(login, id, false, cancellation),
                    MailChange.Delete when id.Folder == MailFolder.Deleted => ImapMailClient.ExpungeAsync(login, id, cancellation),
                    _ => ImapMailClient.MoveAsync(login, id, Destination(change), cancellation),
                }, cancellation).ConfigureAwait(true);
                return;

            default:
                // [S-544] Gmail is read through gmail.readonly: the menu never offers a change, and
                // nothing else may ask for one.
                throw new AccountException(AccountError.Refused, "Themia can read this account's mail but not change it.");
        }
    }

    private static MailFolder Destination(MailChange change) => change switch
    {
        MailChange.Archive => MailFolder.Archive,
        MailChange.Junk => MailFolder.Junk,
        _ => MailFolder.Deleted,
    };

    public bool Open(MailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (MailLinks.Open(message, OutlookInstalled) is not { } link)
        {
            // issue #45a: an IMAP message has no web view. Say so in the log rather than hand the
            // shell an empty string, which opens Explorer.
            Log.Info(Scope, $"nothing to open for a {message.Provider.WireName()} message (no web view)");
            return false;
        }

        return ShellLauncher.OpenMailLink(link) == LaunchOutcome.Opened;
    }

    public bool Reply(MailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (MailLinks.Reply(message, OutlookInstalled) is not { } link)
        {
            Log.Info(Scope, "no usable address to reply to");
            return false;
        }

        return ShellLauncher.OpenMailLink(link) == LaunchOutcome.Opened;
    }

    /// <summary>Whether Outlook's <c>ms-outlook:</c> protocol is registered. Read once; installing Outlook mid-session is rare.</summary>
    private bool OutlookInstalled
    {
        get
        {
            if (_outlookInstalled is { } known)
            {
                return known;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(MailLinks.OutlookScheme);
                _outlookInstalled = key is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Warn(Scope, "could not tell whether Outlook is installed; opening messages on the web", ex);
                _outlookInstalled = false;
            }

            return _outlookInstalled.Value;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accounts.AccountsChanged -= OnAccountsChanged;
        _tick?.Dispose();
        _tick = null;

        foreach (var feed in _feeds.Values)
        {
            feed.Stop();
        }

        _feeds.Clear();
        AccountsChanged = null;
    }

    // -----------------------------------------------------------------------------------------
    // Fetching
    // -----------------------------------------------------------------------------------------

    private async Task<MailFolderStatus> ProbeAsync(Slot slot, MailFolder folder, CancellationToken cancellation)
    {
        switch (slot.Provider)
        {
            case AccountProvider.Microsoft:
                var graph = await _accounts.GetJsonAsync(slot.AccountId, MailWire.GraphFolder(folder), MailWireContext.Default.GraphMailFolder, cancellation).ConfigureAwait(true);
                return new MailFolderStatus(graph.TotalItemCount ?? 0, graph.UnreadItemCount ?? 0);

            case AccountProvider.Google:
                var label = await _accounts.GetJsonAsync(slot.AccountId, MailWire.GmailLabelCounts(folder), MailWireContext.Default.GmailLabel, cancellation).ConfigureAwait(true);
                return new MailFolderStatus(label.MessagesTotal ?? 0, label.MessagesUnread ?? 0);

            case AccountProvider.Imap:
                var login = _accounts.GetImapCredentials(slot.AccountId);
                return await Task.Run(() => ImapMailClient.StatusAsync(login, folder, cancellation), cancellation).ConfigureAwait(true);

            default:
                throw new AccountException(AccountError.Refused, $"{slot.Provider.Label()} accounts have no mail.");
        }
    }

    private async Task<IReadOnlyList<MailMessage>> FetchAsync(Slot slot, MailFolder folder, int count, CancellationToken cancellation)
    {
        switch (slot.Provider)
        {
            case AccountProvider.Microsoft:
                var graph = await _accounts.GetGraphPagesAsync(
                    slot.AccountId, MailWire.GraphMessages(folder, count), MailWireContext.Default.GraphMailMessage, count, cancellation).ConfigureAwait(true);
                return [.. graph.Select(m => MailWire.FromGraph(m, slot.AccountId, folder)).OfType<MailMessage>()];

            case AccountProvider.Google:
                return await FetchGmailAsync(slot, folder, count, cancellation).ConfigureAwait(true);

            case AccountProvider.Imap:
                var login = _accounts.GetImapCredentials(slot.AccountId);
                return await Task.Run(() => ImapMailClient.FetchAsync(login, slot.AccountId, folder, count, cancellation), cancellation).ConfigureAwait(true);

            default:
                throw new AccountException(AccountError.Refused, $"{slot.Provider.Label()} accounts have no mail.");
        }
    }

    /// <summary>
    /// Gmail lists ids only, so each message is a second request (metadata: three headers and the
    /// snippet, never a body). Four at a time: quick enough for fifty, gentle on the quota.
    /// </summary>
    private async Task<IReadOnlyList<MailMessage>> FetchGmailAsync(Slot slot, MailFolder folder, int count, CancellationToken cancellation)
    {
        var ids = await _accounts.GetGooglePagesAsync(
            slot.AccountId, MailWire.GmailList(folder, count), "messages", MailWireContext.Default.GmailListItem, count, cancellation).ConfigureAwait(true);

        using var gate = new SemaphoreSlim(4);

        async Task<MailMessage?> One(string id)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var message = await _accounts.GetJsonAsync(slot.AccountId, MailWire.GmailMetadata(id), MailWireContext.Default.GmailMessage, cancellation).ConfigureAwait(false);
                return MailWire.FromGmail(message, slot.AccountId, slot.Address, folder);
            }
            catch (AccountException ex) when (ex.Error == AccountError.NotFound)
            {
                // Deleted between the list and this request: simply not there any more.
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        var messages = await Task.WhenAll(ids.Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => One(i.Id!))).ConfigureAwait(true);
        return [.. messages.OfType<MailMessage>()];
    }

    /// <summary>One feed: one query, one slot per account it reads, and the widgets watching it.</summary>
    private sealed class Feed(MailService service, MailQuery query)
    {
        private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);

        public MailQuery Query { get; } = query;

        public MailService Owner => service;

        public List<Handle> Handles { get; } = [];

        public MailFeedSnapshot Snapshot { get; private set; } = MailFeedSnapshot.Empty;

        /// <summary>Follow the accounts: a new or mended account gets a slot, a gone or broken one loses it.</summary>
        public void Reconcile()
        {
            var wanted = MailScreens.Targets(service.Accounts, Query.AccountIds).Where(a => a.Problem is null).ToList();
            var keep = new HashSet<string>(wanted.Select(a => a.Id), StringComparer.Ordinal);

            foreach (var id in _slots.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _slots[id].Cancel();
                _slots.Remove(id);
            }

            var added = false;
            foreach (var account in wanted)
            {
                if (!_slots.ContainsKey(account.Id))
                {
                    _slots[account.Id] = new Slot(account.Id, account.Provider, account.Address);
                    added = true;
                }
            }

            Publish();

            if (added && Handles.Count > 0)
            {
                Poll(force: false);
            }
        }

        public void Poll(bool force)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var slot in _slots.Values.ToArray())
            {
                if (slot.Busy || (!force && now < slot.NextAttempt) || (force && slot.Status == FetchStatus.RateLimited && now < slot.NextAttempt))
                {
                    continue;
                }

                _ = RunAsync(slot, force);
            }
        }

        private async Task RunAsync(Slot slot, bool force)
        {
            slot.Busy = true;
            Publish();
            var token = slot.Token;

            try
            {
                var counts = await service.ProbeAsync(slot, Query.Folder, token).ConfigureAwait(true);
                var now = DateTimeOffset.UtcNow;

                if (force || MailRefresh.NeedsFetch(slot.Known, counts, slot.LastFetch, now))
                {
                    slot.Messages = await service.FetchAsync(slot, Query.Folder, Query.Count, token).ConfigureAwait(true);
                    slot.LastFetch = DateTimeOffset.UtcNow;
                }

                slot.Known = counts;
                slot.Record(FetchStatus.Ok, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // ignore: the account left this feed, or the feed closed; nobody is waiting for it.
                return;
            }
            catch (AccountException ex)
            {
                if (ex.Error == AccountError.SignInRequired && slot.Provider == AccountProvider.Imap)
                {
                    // An IMAP refusal happens on our socket, not in the account service's HTTP path,
                    // so the service has to be told for the account to say "sign in again".
                    service._accounts.ReportImapLoginRefused(slot.AccountId);
                }

                slot.Record(ex.ToFetchStatus(), ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or ObjectDisposedException)
            {
                // Should already be an AccountException; anything that slipped through is still one
                // account's failure, never the desktop's.
                Log.Error(Scope, $"unexpected failure reading {slot.AccountId}", ex);
                slot.Record(FetchStatus.BadResponse, ex.Message);
            }
            finally
            {
                slot.Busy = false;
            }

            if (_slots.ContainsKey(slot.AccountId))
            {
                Publish();
            }
        }

        /// <summary>A change the user just made, shown at once rather than after the next fetch.</summary>
        public void Changed(MailMessage message, MailChange change)
        {
            if (!_slots.TryGetValue(message.AccountId, out var slot))
            {
                return;
            }

            slot.Messages = change switch
            {
                MailChange.MarkRead or MailChange.MarkUnread => [.. slot.Messages.Select(m => m.Key == message.Key ? m with { IsRead = change == MailChange.MarkRead } : m)],
                _ => [.. slot.Messages.Where(m => m.Key != message.Key)],
            };

            // The counts moved because of us; the next probe sees that and fetches the truth.
            Publish();
        }

        public void Publish()
        {
            Snapshot = new MailFeedSnapshot(
                MailList.Merge(_slots.Values.Select(s => s.Messages), Query.Count, MailSortOrder.NewestFirst),
                [.. _slots.Values.Select(s => new MailAccountOutcome(s.AccountId, s.State, s.Status, s.Detail))],
                _slots.Values.Any(s => s.Busy));

            foreach (var handle in Handles.ToArray())
            {
                handle.Publish();
            }
        }

        public void Stop()
        {
            foreach (var slot in _slots.Values)
            {
                slot.Cancel();
            }

            _slots.Clear();
        }
    }

    /// <summary>One account's part of one feed.</summary>
    private sealed class Slot(string accountId, AccountProvider provider, string address)
    {
        private readonly CancellationTokenSource _cancel = new();

        public string AccountId { get; } = accountId;

        public AccountProvider Provider { get; } = provider;

        public string Address { get; } = address;

        public IReadOnlyList<MailMessage> Messages { get; set; } = [];

        public MailAccountState State { get; private set; } = MailAccountState.Pending;

        public FetchStatus Status { get; private set; } = FetchStatus.Loading;

        public string? Detail { get; private set; }

        public MailFolderStatus? Known { get; set; }

        public DateTimeOffset? LastFetch { get; set; }

        public DateTimeOffset NextAttempt { get; private set; } = DateTimeOffset.MinValue;

        public bool Busy { get; set; }

        public CancellationToken Token => _cancel.Token;

        private int _failures;

        /// <summary>Record how an attempt went and when the next may run; log only a change of state.</summary>
        public void Record(FetchStatus status, string? detail)
        {
            var now = DateTimeOffset.UtcNow;
            var changed = status != Status;

            if (status == FetchStatus.Ok)
            {
                _failures = 0;
                State = MailAccountState.Ok;
                NextAttempt = now + MailRefresh.ProbeInterval;
            }
            else
            {
                _failures++;
                State = MailAccountState.Failed;
                NextAttempt = FetchState.IsRetryable(status)
                    ? now + Backoff.Delay(MailRefresh.ProbeInterval, _failures, status)
                    : now + Backoff.Ceiling;
            }

            if (changed)
            {
                if (status == FetchStatus.Ok)
                {
                    Log.Info(Scope, $"{AccountId}: reading mail");
                }
                else
                {
                    Log.Warn(Scope, $"{AccountId}: {FetchState.Message(status)} ({detail}); next try in {(NextAttempt - now).TotalMinutes:F0} min");
                }
            }

            Status = status;
            Detail = detail;
        }

        public void Cancel()
        {
            try
            {
                _cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore: already torn down.
            }
        }
    }

    /// <summary>A widget's view of a feed.</summary>
    private sealed class Handle(Feed feed) : IMailFeed
    {
        private bool _disposed;

        public Feed Feed { get; } = feed;

        public event Action? Updated;

        public MailFeedSnapshot Snapshot => Feed.Snapshot;

        public void Refresh()
        {
            if (!_disposed)
            {
                Feed.Poll(force: true);
            }
        }

        public void Publish()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Updated?.Invoke();
            }
            catch (Exception ex)
            {
                // One widget's handler must not stop the others hearing about the same feed.
                Log.Error(Scope, "a Mail widget's update handler threw", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Updated = null;
            Feed.Owner.Release(this);
        }
    }
}
