namespace Themia.Core.Tasks;

/// <summary>
/// [S-596] The Today fan-out: every task on every list of one account, one list at a time.
/// <para>
/// One list failing — a shared list that lost access, say — drops that list, not the whole view. But
/// when every list fails, the first failure is thrown rather than returning nothing, because an
/// empty Today reads as "nothing due" when the truth is "could not look". Cancellation is never a
/// list failure: it ends the whole fan-out at once.
/// </para>
/// <para>
/// Sequential on purpose. Today turns one refresh into a request per list, and a burst of fifty in
/// parallel is how a dashboard of widgets talks a provider into throttling all of them.
/// </para>
/// </summary>
public static class TaskFanOut
{
    /// <param name="fetch">One list's tasks.</param>
    /// <param name="skipped">Told about each list that failed while others succeeded, for the log.</param>
    public static async Task<IReadOnlyList<TaskItem>> AcrossListsAsync(
        IReadOnlyList<TaskListInfo> lists,
        Func<string, CancellationToken, Task<IReadOnlyList<TaskItem>>> fetch,
        CancellationToken cancellation,
        Action<TaskListInfo, Exception>? skipped = null)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(fetch);

        var result = new List<TaskItem>();
        Exception? first = null;
        var succeeded = 0;

        foreach (var list in lists.Take(TaskRules.TodayMaxLists))
        {
            cancellation.ThrowIfCancellationRequested();

            try
            {
                result.AddRange(await fetch(list.Id, cancellation).ConfigureAwait(false));
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                first ??= ex;
                skipped?.Invoke(list, ex);
            }
        }

        if (first is not null && succeeded == 0)
        {
            // Rethrown with its own stack: the caller maps it to a state like any other failure.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }

        return result;
    }
}
