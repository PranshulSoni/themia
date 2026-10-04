using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Office;

namespace Themia.App.Services.Office;

/// <summary>
/// [S-588] The shared answer for one account request, so every widget asking the same thing costs
/// one request.
/// <list type="bullet">
/// <item>An answer younger than <c>freshFor</c> is handed back without asking.</item>
/// <item>A request already out for the same key is joined, never started twice.</item>
/// <item>
/// A failure keeps the last good answer on screen, annotated with what went wrong, and is held for
/// <see cref="SyncProblems.HoldFor"/> — an hour for a refusal only the user can mend — so a poll
/// does not hammer a provider that has already said no. A forced refresh (the user's Refresh) asks
/// regardless.
/// </item>
/// </list>
/// In memory only: other people's calendars do not belong in Themia's files.
/// </summary>
internal sealed class SyncCache<T>(string scope)
    where T : class
{
    /// <summary>How many distinct requests to remember; a month paged back and forth is one each.</summary>
    private const int MaxEntries = 128;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<Synced<T>>> _inFlight = new(StringComparer.Ordinal);

    /// <summary>The cached answer regardless of age, or null. Never starts a request.</summary>
    public Synced<T>? Peek(string key)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Result : null;
        }
    }

    public async Task<Synced<T>> GetAsync(
        string key,
        TimeSpan freshFor,
        bool force,
        Func<CancellationToken, Task<T>> load,
        CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        Task<Synced<T>> flight;

        lock (_gate)
        {
            if (!_inFlight.TryGetValue(key, out var pending))
            {
                if (!force && _entries.TryGetValue(key, out var entry))
                {
                    var fresh = entry.Result.Problem == SyncProblem.None
                        ? entry.Result.At is { } at && now - at < freshFor
                        : now - entry.FailedAt < SyncProblems.HoldFor(entry.Result.Problem);

                    if (fresh)
                    {
                        return entry.Result;
                    }
                }

                // Not the caller's token: the flight is shared, and a second widget joining it must
                // not lose its answer because the first was torn down. Each caller abandons only
                // its own await.
                pending = Task.Run(() => RunAsync(key, load), CancellationToken.None);
                _inFlight[key] = pending;
            }

            flight = pending;
        }

        return await flight.WaitAsync(cancellation).ConfigureAwait(false);
    }

    private async Task<Synced<T>> RunAsync(string key, Func<CancellationToken, Task<T>> load)
    {
        Synced<T>? previous;
        lock (_gate)
        {
            previous = _entries.TryGetValue(key, out var entry) ? entry.Result : null;
        }

        Entry next;

        try
        {
            var value = await load(CancellationToken.None).ConfigureAwait(false);
            next = new Entry(new Synced<T>(value, SyncProblem.None, null, DateTimeOffset.UtcNow), default);
        }
        catch (AccountException ex)
        {
            var problem = SyncProblems.From(ex.Error);
            Log.Warn(scope, $"{Redact(key)}: {problem} ({ex.Message})");
            next = Failed(previous, problem, ex.Message);
        }
        catch (Exception ex)
        {
            // Anything else — a body that did not parse, a bug in a mapper — must not fault a shared
            // flight that several widgets are awaiting; it is logged in full and shown as "the
            // service sent something unexpected".
            Log.Error(scope, $"{Redact(key)}: the request failed", ex);
            next = Failed(previous, SyncProblem.BadResponse, ex.Message);
        }

        lock (_gate)
        {
            _entries[key] = next;
            _inFlight.Remove(key);

            if (_entries.Count > MaxEntries)
            {
                // Drop the oldest answers; whatever is on screen asks again on its next tick.
                foreach (var stale in _entries.OrderBy(e => e.Value.Result.At ?? DateTimeOffset.MinValue).Take(_entries.Count - MaxEntries).Select(e => e.Key).ToList())
                {
                    _entries.Remove(stale);
                }
            }
        }

        return next.Result;
    }

    private static Entry Failed(Synced<T>? previous, SyncProblem problem, string detail)
        => new(new Synced<T>(previous?.Value, problem, detail, previous?.At), DateTimeOffset.UtcNow);

    /// <summary>The account part of a key only: a calendar id can be a long opaque string.</summary>
    private static string Redact(string key) => key.Split('|')[0];

    private sealed record Entry(Synced<T> Result, DateTimeOffset FailedAt);
}
