using Themia.App.Services.Web;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Tasks;
using Themia.Core.Widgets;

namespace Themia.App.Services.Tasks;

/// <summary>
/// One account task list as every widget showing it shares it: what the provider last said, the
/// changes not yet confirmed on top ([S-597]), and how the last attempt went.
/// <para>
/// Two widgets on the same list, account and filter hold the same feed, so they make one request
/// between them and show the same ticks. Lives on the UI thread; <see cref="Changed"/> is raised
/// there.
/// </para>
/// </summary>
internal sealed class TaskFeed
{
    internal TaskFeed(string accountId, string listId, bool showCompleted)
    {
        AccountId = accountId;
        ListId = listId;
        ShowCompleted = showCompleted;
        Schedule = new RefreshSchedule("tasks", $"task list {listId}");
    }

    public string AccountId { get; }

    /// <summary>A real list id, or <see cref="TaskRules.TodayListId"/>.</summary>
    public string ListId { get; }

    public bool ShowCompleted { get; }

    public bool IsToday => TaskRules.IsToday(ListId);

    public TaskEdits Edits { get; } = new();

    /// <summary>What the widget draws: the provider's answer with the pending changes applied.</summary>
    public IReadOnlyList<TaskItem> Tasks => Edits.Current;

    /// <summary>Whether a fetch has ever landed. Before that there is nothing to draw but "loading".</summary>
    public bool Loaded { get; internal set; }

    /// <summary>A fetch is out.</summary>
    public bool Busy { get; internal set; }

    /// <summary>How the last fetch failed, or null when it worked.</summary>
    public AccountException? Failure { get; internal set; }

    /// <summary>The last change the provider refused, in words, or null. Cleared by the next one that works.</summary>
    public string? WriteProblem { get; internal set; }

    public DateTimeOffset? FetchedAt { get; internal set; }

    public event Action? Changed;

    internal RefreshSchedule Schedule { get; }

    internal int Holders { get; set; }

    /// <summary>A forced refresh arrived while one was out: run once more when it lands.</summary>
    internal bool Again { get; set; }

    internal string Key => Of(AccountId, ListId, ShowCompleted);

    internal static string Of(string accountId, string listId, bool showCompleted) => $"{accountId}\n{listId}\n{showCompleted}";

    internal void Raise()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (var listener in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                listener();
            }
            catch (Exception ex)
            {
                // One widget that throws must not stop the others from hearing about the change.
                Log.Error("tasks", "a task-feed listener threw", ex);
            }
        }
    }
}

/// <summary>
/// [S-591] Tasks for every widget, from one place: Themia's own accounts through
/// <see cref="ITaskRemote"/>, and the lists kept on this PC through <see cref="Local"/>. One
/// instance, owned by the host and reached through <see cref="AppServices.Tasks"/>.
/// <para>
/// <b>Refreshing</b> rides out trouble rather than adding to it: a refresh never stacks on one that
/// is still out ([S-596], the classic app's [S-63]); a widget's tick is skipped when another widget
/// on the same feed refreshed it moments ago; and a failing feed backs off through
/// <see cref="Backoff"/>, a rate limit far longer than anything else.
/// </para>
/// <para>
/// <b>Writing</b> is optimistic and single-flight ([S-597]): the change shows at once, writes to one
/// account go out one at a time and in order, a refusal takes the change back and says so, and a
/// confirmed write nudges the account's other feeds to catch up.
/// </para>
/// </summary>
internal sealed class TaskService : IDisposable
{
    private const string Scope = "tasks";

    private readonly ITaskRemote _remote;
    private readonly Dictionary<string, TaskFeed> _feeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<IReadOnlyList<TaskListInfo>>> _lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _writeGates = new(StringComparer.Ordinal);
    private bool _disposed;

    public TaskService(ITaskRemote remote, LocalTaskPool local)
    {
        _remote = remote;
        Local = local;
        _remote.AccountsChanged += OnAccountsChanged;
    }

    /// <summary>Raised on the UI thread when the accounts that can feed tasks changed.</summary>
    public event Action? AccountsChanged;

    /// <summary>The lists kept on this PC.</summary>
    public LocalTaskPool Local { get; }

    public IReadOnlyList<TaskAccount> Accounts => _remote.Accounts;

    public TaskAccount? Find(string? accountId)
        => string.IsNullOrEmpty(accountId) ? null : Accounts.FirstOrDefault(a => a.Id == accountId);

    /// <summary>
    /// An account's lists. Single-flight and cached: every widget and the settings page asking at
    /// once cause one request; a failure is not cached, so the next ask tries again.
    /// </summary>
    public Task<IReadOnlyList<TaskListInfo>> ListsAsync(string accountId, bool fresh = false)
    {
        if (!fresh && _lists.TryGetValue(accountId, out var cached) && !cached.IsFaulted && !cached.IsCanceled)
        {
            return cached;
        }

        var load = LoadListsAsync(accountId);
        _lists[accountId] = load;
        return load;
    }

    /// <summary>Hold a feed. Every <see cref="Acquire"/> is matched by one <see cref="Release"/>.</summary>
    public TaskFeed Acquire(string accountId, string listId, bool showCompleted)
    {
        var key = TaskFeed.Of(accountId, listId, showCompleted);

        if (!_feeds.TryGetValue(key, out var feed))
        {
            feed = new TaskFeed(accountId, listId, showCompleted);
            _feeds[key] = feed;
        }

        feed.Holders++;
        return feed;
    }

    public void Release(TaskFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (--feed.Holders <= 0 && _feeds.TryGetValue(feed.Key, out var held) && ReferenceEquals(held, feed))
        {
            _feeds.Remove(feed.Key);
        }
    }

    /// <summary>
    /// Ask the provider again. <paramref name="force"/> is a user action or a changed setting, which
    /// goes now (or straight after the fetch that is out); a tick goes only when the back-off allows
    /// and nobody else refreshed this feed within half the interval.
    /// </summary>
    public void Refresh(TaskFeed feed, TimeSpan interval, bool force)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (_disposed)
        {
            return;
        }

        if (feed.Busy)
        {
            // [S-596] Never a second request on top of one still out: that is how a throttled
            // provider is kept throttled. A forced refresh is remembered, not dropped.
            feed.Again |= force;
            return;
        }

        var now = DateTimeOffset.Now;

        if (!force && (!feed.Schedule.MayFetch(now) || feed.FetchedAt is { } at && now - at < interval / 2))
        {
            return;
        }

        if (force)
        {
            feed.Schedule.Reset();
        }

        _ = FetchAsync(feed, interval);
    }

    // ---- writes ----------------------------------------------------------------------------------

    /// <summary>[S-597] Complete or reopen a task, optimistically.</summary>
    public Task SetCompletedAsync(TaskFeed feed, TaskItem task, bool completed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(task);

        if (feed.Edits.IsUnconfirmedCreate(task.Id))
        {
            return Task.CompletedTask; // no real id to write to yet
        }

        var ticket = feed.Edits.Begin(TaskEdit.Complete(task.Id, completed));
        feed.Raise();

        return WriteAsync(feed, ticket, completed ? "complete the task" : "reopen the task", completed ? Str.Shell_Tasks_CompleteFailed : Str.Shell_Tasks_ReopenFailed, async cancellation =>
        {
            await _remote.SetCompletedAsync(feed.AccountId, task.ListId, task.Id, completed, cancellation).ConfigureAwait(true);
            return null;
        });
    }

    /// <summary>[S-600] Rename a task. A blank or unchanged title writes nothing.</summary>
    public Task RenameAsync(TaskFeed feed, TaskItem task, string title)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(task);

        var clean = title.Trim();
        if (clean.Length == 0 || clean == task.Title || feed.Edits.IsUnconfirmedCreate(task.Id))
        {
            return Task.CompletedTask;
        }

        var ticket = feed.Edits.Begin(TaskEdit.Rename(task.Id, clean));
        feed.Raise();

        return WriteAsync(feed, ticket, "rename the task", Str.Shell_Tasks_RenameFailed, async cancellation =>
        {
            await _remote.RenameAsync(feed.AccountId, task.ListId, task.Id, clean, cancellation).ConfigureAwait(true);
            return null;
        });
    }

    /// <summary>[S-597] Delete a task; a refusal puts it back where it was.</summary>
    public Task DeleteAsync(TaskFeed feed, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(task);

        if (feed.Edits.IsUnconfirmedCreate(task.Id))
        {
            return Task.CompletedTask;
        }

        var ticket = feed.Edits.Begin(TaskEdit.Delete(task.Id));
        feed.Raise();

        return WriteAsync(feed, ticket, "delete the task", Str.Shell_Tasks_DeleteFailed, async cancellation =>
        {
            await _remote.DeleteAsync(feed.AccountId, task.ListId, task.Id, cancellation).ConfigureAwait(true);
            return null;
        });
    }

    /// <summary>
    /// [S-599] Create a task at the bottom of the list it was typed into. From Today it is created due
    /// today in the account's default list ([S-596]), or it would vanish from the view it was typed
    /// into. Returns the provider's task, or null when nothing was created.
    /// </summary>
    public async Task<TaskItem?> CreateAsync(TaskFeed feed, string title)
    {
        ArgumentNullException.ThrowIfNull(feed);

        var clean = title.Trim();
        if (clean.Length == 0)
        {
            return null;
        }

        string listId;
        DateOnly? due = null;

        if (feed.IsToday)
        {
            IReadOnlyList<TaskListInfo> lists;
            try
            {
                lists = await ListsAsync(feed.AccountId).ConfigureAwait(true);
            }
            catch (AccountException ex)
            {
                feed.WriteProblem = Str.Shell_Common_ProblemReason.Format(("problem", Str.Shell_Tasks_AddFailed.Text()), ("reason", ex.Message));
                Log.Warn(Scope, "could not read the lists to add a task to", ex);
                feed.Raise();
                return null;
            }

            if (TaskRules.CreateTarget(lists) is not { } target)
            {
                feed.WriteProblem = Str.Shell_Tasks_NoList.Text();
                feed.Raise();
                return null;
            }

            listId = target.Id;
            due = DateOnly.FromDateTime(DateTime.Now);
        }
        else
        {
            listId = feed.ListId;
        }

        var optimistic = new TaskItem(
            $"pending-{Guid.NewGuid():n}",
            listId,
            clean,
            false,
            false,
            due is { } day ? $"{TaskDue.Wire(day)}T00:00:00.0000000" : null);

        var ticket = feed.Edits.Begin(TaskEdit.Create(optimistic));
        feed.Raise();

        TaskItem? created = null;
        await WriteAsync(feed, ticket, "add the task", Str.Shell_Tasks_AddFailed, async cancellation =>
        {
            created = await _remote.CreateAsync(feed.AccountId, listId, clean, due, cancellation).ConfigureAwait(true);
            return created;
        }).ConfigureAwait(true);

        return created;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _remote.AccountsChanged -= OnAccountsChanged;
        AccountsChanged = null;
        _feeds.Clear();
    }

    // ---- plumbing --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<TaskListInfo>> LoadListsAsync(string accountId)
    {
        var lists = await _remote.ListsAsync(accountId, CancellationToken.None).ConfigureAwait(true);
        Log.Debug(Scope, $"{lists.Count} task list(s) on {accountId}");
        return lists;
    }

    private async Task FetchAsync(TaskFeed feed, TimeSpan interval)
    {
        feed.Busy = true;
        feed.Raise();

        var stamp = feed.Edits.FetchStarted();

        try
        {
            IReadOnlyList<TaskItem> tasks;

            if (feed.IsToday)
            {
                // [S-596] Today reads the lists afresh, then each one in turn.
                var lists = await ListsAsync(feed.AccountId, fresh: true).ConfigureAwait(true);
                tasks = await TaskFanOut.AcrossListsAsync(
                    lists,
                    (listId, cancellation) => _remote.TasksAsync(feed.AccountId, listId, feed.ShowCompleted, cancellation),
                    CancellationToken.None,
                    (list, ex) => Log.Warn(Scope, $"Today skipped list {list.Id}: {ex.Message}")).ConfigureAwait(true);
            }
            else
            {
                tasks = await _remote.TasksAsync(feed.AccountId, feed.ListId, feed.ShowCompleted, CancellationToken.None).ConfigureAwait(true);
            }

            var now = DateTimeOffset.Now;
            feed.Edits.Landed(tasks, stamp);
            feed.Loaded = true;
            feed.Failure = null;
            feed.FetchedAt = now;
            feed.Schedule.Record(tasks.Count == 0 ? FetchStatus.Empty : FetchStatus.Ok, interval, null, now);
        }
        catch (AccountException ex)
        {
            feed.Failure = ex;
            feed.Schedule.Record(ex.ToFetchStatus(), interval, ex.Message, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            // Anything the account layer did not already type is a bug or a provider oddity; the
            // widget says the service answered unexpectedly and the log says what it was.
            Log.Error(Scope, $"reading task list {feed.ListId} failed unexpectedly", ex);
            feed.Failure = new AccountException(AccountError.BadResponse, Str.Shell_Tasks_Unreadable.Text(), 0, ex);
            feed.Schedule.Record(FetchStatus.BadResponse, interval, ex.Message, DateTimeOffset.Now);
        }
        finally
        {
            feed.Busy = false;
            feed.Raise();
        }

        if (feed.Again && feed.Holders > 0 && !_disposed)
        {
            feed.Again = false;
            _ = FetchAsync(feed, interval);
        }
    }

    /// <summary>
    /// [S-597] One write, through the account's gate: writes to an account go out one at a time and
    /// in the order they were made, so "complete, then reopen" can never arrive reopened-first.
    /// </summary>
    /// <param name="what">The step, for the log (English).</param>
    /// <param name="failed">The step's failure, as the widget says it.</param>
    private async Task WriteAsync(TaskFeed feed, long ticket, string what, Str failed, Func<CancellationToken, Task<TaskItem?>> write)
    {
        if (!_writeGates.TryGetValue(feed.AccountId, out var gate))
        {
            gate = new SemaphoreSlim(1, 1);
            _writeGates[feed.AccountId] = gate;
        }

        await gate.WaitAsync().ConfigureAwait(true);

        var succeeded = false;
        try
        {
            var created = await write(CancellationToken.None).ConfigureAwait(true);
            feed.Edits.Succeeded(ticket, created);
            feed.WriteProblem = null;
            succeeded = true;
        }
        catch (AccountException ex)
        {
            feed.Edits.Failed(ticket);
            feed.WriteProblem = Str.Shell_Common_ProblemReason.Format(("problem", failed.Text()), ("reason", ex.Message));
            Log.Warn(Scope, $"could not {what} on {feed.AccountId}", ex);
        }
        catch (Exception ex)
        {
            feed.Edits.Failed(ticket);
            feed.WriteProblem = Str.Shell_Common_ProblemSentence.Format(("problem", failed.Text()));
            Log.Error(Scope, $"could not {what} on {feed.AccountId}", ex);
        }
        finally
        {
            gate.Release();
        }

        feed.Raise();

        if (succeeded)
        {
            // Another widget may show this task on another feed (a list and Today); it catches up
            // now rather than on its next tick.
            foreach (var other in _feeds.Values.ToList())
            {
                if (!ReferenceEquals(other, feed) && other.AccountId == feed.AccountId)
                {
                    Refresh(other, TimeSpan.FromMinutes(1), force: true);
                }
            }
        }
    }

    private void OnAccountsChanged()
    {
        // A signed-out account's lists must not be offered; a new one's are not known yet.
        _lists.Clear();

        if (AccountsChanged is not { } changed)
        {
            return;
        }

        foreach (var listener in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                listener();
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "an accounts listener threw", ex);
            }
        }
    }
}
