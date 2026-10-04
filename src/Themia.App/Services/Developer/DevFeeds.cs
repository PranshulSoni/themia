using System.Net.Http;
using System.Text.Json;
using Themia.Core.Accounts;
using Themia.Core.Developer;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services.Developer;

/// <summary>What a developer widget's feed holds right now.</summary>
/// <param name="Value">The merged content, or null before anything arrived.</param>
/// <param name="Outcomes">How each account's last attempt went; an account still on its first is <see cref="FetchStatus.Loading"/>.</param>
/// <param name="At">When the oldest account's content on screen was fetched, or null.</param>
internal sealed record DevFeedSnapshot<T>(T? Value, IReadOnlyList<DevOutcome> Outcomes, DateTimeOffset? At)
    where T : class
{
    public static DevFeedSnapshot<T> Empty { get; } = new(null, [], null);
}

/// <summary>One widget's handle on a shared feed. Dispose it on <c>Detach</c>; the last one to let go stops the feed.</summary>
internal interface IDevFeed<T> : IDisposable
    where T : class
{
    /// <summary>Raised on the UI thread whenever <see cref="Snapshot"/> changes.</summary>
    event Action? Updated;

    DevFeedSnapshot<T> Snapshot { get; }

    /// <summary>Ask now, whatever the schedule says — except an account waiting out a rate limit, which still waits.</summary>
    void Refresh();
}

/// <summary>One account's answer for a feed, and how soon the server says it may be asked again.</summary>
internal sealed record DevPart<T>(T Value, TimeSpan? PollInterval = null)
    where T : class;

/// <summary>What a feed asks, of which accounts, how often, and how answers become one value.</summary>
/// <param name="Key">Everything that changes the answer. Widgets asking the same key share one feed.</param>
/// <param name="AccountIds">The chosen accounts; empty for all ([S-623]).</param>
/// <param name="Single">Read only the first of them (a repository, a usage meter).</param>
/// <param name="Fetch">One account's request. Throws <see cref="AccountException"/> for a failure it can name.</param>
/// <param name="Combine">The accounts' answers, in account order, as the widget's one value.</param>
internal sealed record DevFeedSpec<TPart, TValue>(
    string Key,
    IReadOnlyList<string> AccountIds,
    bool Single,
    TimeSpan Refresh,
    Func<string, CancellationToken, Task<DevPart<TPart>>> Fetch,
    Func<IReadOnlyList<TPart>, TValue> Combine)
    where TPart : class
    where TValue : class;

/// <summary>
/// [S-624] The shared, polite polling behind every developer widget: one feed per distinct question,
/// one slot per account in it, on the app's one ticker.
/// <para>
/// Two widgets asking the same thing share one feed and so one request; the last one to let go
/// stops it. A slot asks again only when its own schedule says so: the widget's cadence, stretched
/// by the server's poll interval, by a rate-limit wait ([S-625]) or by <see cref="Backoff"/> after a
/// failure. Everything here runs on the UI thread — the ticker fires there and every await resumes
/// there — so each feed has one writer and needs no lock.
/// </para>
/// </summary>
internal sealed class DevFeedHub : IDisposable
{
    private readonly string _scope;
    private readonly Ticker _ticker;
    private readonly Func<IReadOnlyList<DevAccount>> _accounts;
    private readonly Dictionary<string, IFeed> _feeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _blocked = new(StringComparer.Ordinal);
    private IDisposable? _tick;
    private bool _disposed;

    /// <param name="accounts">The accounts of the hub's kind, read fresh whenever a feed reconciles.</param>
    public DevFeedHub(string scope, Ticker ticker, Func<IReadOnlyList<DevAccount>> accounts)
    {
        _scope = scope;
        _ticker = ticker;
        _accounts = accounts;
    }

    private interface IFeed
    {
        void Reconcile();

        void Poll(bool force);

        void Stop();

        bool Reads(string accountId);
    }

    public IDevFeed<TValue> Open<TPart, TValue>(DevFeedSpec<TPart, TValue> spec)
        where TPart : class
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Feed<TPart, TValue> feed;
        if (_feeds.TryGetValue(spec.Key, out var existing))
        {
            feed = (Feed<TPart, TValue>)existing;
        }
        else
        {
            feed = new Feed<TPart, TValue>(this, spec);
            _feeds[spec.Key] = feed;
        }

        var handle = new Handle<TPart, TValue>(feed);
        feed.Handles.Add(handle);

        if (feed.Handles.Count == 1)
        {
            feed.Reconcile();
        }

        // Subscribing fires a tick at once, which starts the first fetch of a new feed.
        _tick ??= _ticker.Subscribe(TimeSpan.FromMinutes(1), OnTick);
        return handle;
    }

    /// <summary>Follow the accounts: a new or mended account gets a slot, a gone or broken one loses it.</summary>
    public void AccountsChanged()
    {
        foreach (var feed in _feeds.Values.ToArray())
        {
            feed.Reconcile();
        }
    }

    /// <summary>Ask again now for every feed reading <paramref name="accountId"/> — after the user changed something there.</summary>
    public void RefreshAccount(string accountId)
    {
        foreach (var feed in _feeds.Values.ToArray())
        {
            if (feed.Reads(accountId))
            {
                feed.Poll(force: true);
            }
        }
    }

    /// <summary>[S-625] Hold every request for an account until <paramref name="until"/>: its budget is spent.</summary>
    public void Block(string accountId, DateTimeOffset until)
    {
        if (!_blocked.TryGetValue(accountId, out var current) || until > current)
        {
            _blocked[accountId] = until;
            Log.Warn(_scope, $"{accountId}: rate limited; holding requests until {until.ToLocalTime():HH:mm}");
        }
    }

    /// <summary>When an account may be asked again, or null when nothing holds it.</summary>
    public DateTimeOffset? BlockedUntil(string accountId, DateTimeOffset now)
    {
        if (_blocked.TryGetValue(accountId, out var until))
        {
            if (until > now)
            {
                return until;
            }

            _blocked.Remove(accountId);
        }

        return null;
    }

    private void OnTick()
    {
        foreach (var feed in _feeds.Values.ToArray())
        {
            feed.Poll(force: false);
        }
    }

    private void Release<TPart, TValue>(Handle<TPart, TValue> handle)
        where TPart : class
        where TValue : class
    {
        var feed = handle.Feed;
        feed.Handles.Remove(handle);

        if (feed.Handles.Count > 0)
        {
            return;
        }

        feed.Stop();
        _feeds.Remove(feed.Spec.Key);

        if (_feeds.Count == 0)
        {
            _tick?.Dispose();
            _tick = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tick?.Dispose();
        _tick = null;

        foreach (var feed in _feeds.Values)
        {
            feed.Stop();
        }

        _feeds.Clear();
    }

    private sealed class Feed<TPart, TValue>(DevFeedHub hub, DevFeedSpec<TPart, TValue> spec) : IFeed
        where TPart : class
        where TValue : class
    {
        private readonly Dictionary<string, Slot<TPart>> _slots = new(StringComparer.Ordinal);

        public DevFeedSpec<TPart, TValue> Spec { get; } = spec;

        public DevFeedHub Hub => hub;

        public List<Handle<TPart, TValue>> Handles { get; } = [];

        public DevFeedSnapshot<TValue> Snapshot { get; private set; } = DevFeedSnapshot<TValue>.Empty;

        public bool Reads(string accountId) => _slots.ContainsKey(accountId);

        public void Reconcile()
        {
            var wanted = DevScreens.Targets(hub._accounts(), Spec.AccountIds, Spec.Single).Where(a => a.Problem is null).ToList();
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
                    _slots[account.Id] = new Slot<TPart>(hub._scope, account.Id);
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
                var throttled = hub.BlockedUntil(slot.AccountId, now) is not null
                    || (slot.Status == FetchStatus.RateLimited && now < slot.NextAttempt);

                if (slot.Busy || throttled || (!force && now < slot.NextAttempt))
                {
                    continue;
                }

                _ = RunAsync(slot);
            }
        }

        private async Task RunAsync(Slot<TPart> slot)
        {
            slot.Busy = true;
            var token = slot.Token;

            try
            {
                var part = await Spec.Fetch(slot.AccountId, token).ConfigureAwait(true);
                slot.Value = part.Value;
                slot.At = DateTimeOffset.UtcNow;
                slot.Record(FetchStatus.Ok, null, null, Spec.Refresh, part.PollInterval, hub.BlockedUntil(slot.AccountId, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // ignore: the account left this feed, or the feed closed; nobody is waiting for it.
                return;
            }
            catch (AccountException ex)
            {
                if (ex.Error == AccountError.RateLimited)
                {
                    hub.Block(slot.AccountId, ex.RetryAt ?? DateTimeOffset.UtcNow + RateLimitRules.DefaultWait);
                }

                slot.Record(ex.ToFetchStatus(), Detail(ex), ex.RetryAt, Spec.Refresh, null, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException or ObjectDisposedException)
            {
                // Should already be an AccountException; anything that slipped through is still one
                // account's failure, never the desktop's.
                Log.Error(hub._scope, $"unexpected failure reading {slot.AccountId}", ex);
                slot.Record(FetchStatus.BadResponse, null, null, Spec.Refresh, null, null);
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

        /// <summary>
        /// A sentence for the screen when the failure has one better than the generic: a refusal
        /// the provider explained. Throttles, timeouts and network trouble use the generic words.
        /// </summary>
        private static string? Detail(AccountException ex) => ex.Error switch
        {
            AccountError.Forbidden or AccountError.NotFound or AccountError.BadResponse or AccountError.ServerError or AccountError.Refused
                => ex.ProviderMessage is { Length: > 0 } said ? said : null,
            _ => null,
        };

        public void Publish()
        {
            var parts = _slots.Values.Where(s => s.Value is not null).Select(s => s.Value!).ToList();
            var at = _slots.Values.Where(s => s.At is not null).Select(s => s.At!.Value).DefaultIfEmpty().Min();

            Snapshot = new DevFeedSnapshot<TValue>(
                parts.Count > 0 ? Spec.Combine(parts) : null,
                [.. _slots.Values.Select(s => s.Outcome)],
                at == default ? null : at);

            foreach (var handle in Handles.ToArray())
            {
                handle.Publish(hub._scope);
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
    private sealed class Slot<TPart>(string scope, string accountId)
        where TPart : class
    {
        private readonly CancellationTokenSource _cancel = new();
        private int _failures;

        public string AccountId { get; } = accountId;

        public TPart? Value { get; set; }

        public DateTimeOffset? At { get; set; }

        public FetchStatus Status { get; private set; } = FetchStatus.Loading;

        public DevOutcome Outcome { get; private set; } = new(accountId, FetchStatus.Loading);

        public DateTimeOffset NextAttempt { get; private set; } = DateTimeOffset.MinValue;

        public bool Busy { get; set; }

        public CancellationToken Token => _cancel.Token;

        /// <summary>Record how an attempt went and when the next may run; log only a change of state.</summary>
        public void Record(FetchStatus status, string? detail, DateTimeOffset? retryAt, TimeSpan refresh, TimeSpan? pollInterval, DateTimeOffset? blockedUntil)
        {
            var now = DateTimeOffset.UtcNow;
            var changed = status != Status;

            if (status == FetchStatus.Ok)
            {
                _failures = 0;
                NextAttempt = GitHubPolling.NextPoll(now, refresh, pollInterval, blockedUntil);
            }
            else
            {
                _failures++;
                NextAttempt = retryAt is { } at && at > now
                    ? at
                    : FetchState.IsRetryable(status)
                        ? now + Backoff.Delay(refresh, _failures, status)
                        : now + Backoff.Ceiling;
            }

            if (changed)
            {
                if (status == FetchStatus.Ok)
                {
                    Log.Info(scope, $"{AccountId}: answering");
                }
                else
                {
                    Log.Warn(scope, $"{AccountId}: {FetchState.Message(status)}{(detail is null ? string.Empty : $" ({detail})")}; next try in {Math.Ceiling((NextAttempt - now).TotalMinutes):F0} min");
                }
            }

            Status = status;
            Outcome = new DevOutcome(AccountId, status, detail, status == FetchStatus.RateLimited ? NextAttempt : null);
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

    private sealed class Handle<TPart, TValue>(Feed<TPart, TValue> feed) : IDevFeed<TValue>
        where TPart : class
        where TValue : class
    {
        private bool _disposed;

        public Feed<TPart, TValue> Feed { get; } = feed;

        public event Action? Updated;

        public DevFeedSnapshot<TValue> Snapshot => Feed.Snapshot;

        public void Refresh()
        {
            if (!_disposed)
            {
                Feed.Poll(force: true);
            }
        }

        public void Publish(string scope)
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
                Log.Error(scope, "a widget's update handler threw", ex);
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
            Feed.Hub.Release(this);
        }
    }
}
