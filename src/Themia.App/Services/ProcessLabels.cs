using System.Collections.Concurrent;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// [S-1145] A process's friendly name — the executable's version-info description, the string Task
/// Manager shows ("Google Chrome" for chrome) — for the rows of a process list, with the image name
/// standing in until it is known and for good when there is none (a protected process that refuses
/// even a limited handle, an exe without a version resource).
/// <para>
/// The first sight of a binary is disk I/O, which never runs on the UI thread: <see cref="For"/>
/// answers with what is known now and resolves the rest on the thread pool, once per process
/// (single-flight per key), so the next sample shows it. Keyed on pid and image name together, so a
/// recycled pid running a different program is not given the old one's name.
/// </para>
/// </summary>
internal static class ProcessLabels
{
    private const string Scope = "process-names";

    /// <summary>Past this many remembered processes the cache starts over rather than grow for weeks.</summary>
    private const int CacheLimit = 1024;

    /// <summary>Marks a key whose lookup is out, so a second sample does not start another.</summary>
    private const string Pending = "\0";

    private static readonly ConcurrentDictionary<(int Pid, string Image), string> Known = new();

    /// <summary>The name to show for <paramref name="usage"/>: the description when known, else the image name.</summary>
    public static string For(ProcessUsage usage)
    {
        var key = (usage.Pid, usage.Name);

        if (Known.TryGetValue(key, out var label))
        {
            return label is Pending || label.Length == 0 ? usage.Name : label;
        }

        if (Known.Count >= CacheLimit)
        {
            Known.Clear();
        }

        if (Known.TryAdd(key, Pending))
        {
            _ = Task.Run(() => Resolve(key));
        }

        return usage.Name;
    }

    private static void Resolve((int Pid, string Image) key)
    {
        try
        {
            var path = ProcessNames.ExePath((uint)key.Pid);
            var name = path is null ? null : ProcessNames.DisplayName(path);

            // Empty, not removed: "has no friendlier name" is an answer, and asking again every
            // sample would reopen the process every two seconds for ever.
            Known[key] = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }
        catch (Exception ex)
        {
            Known[key] = string.Empty;
            Log.Warn(Scope, $"could not name process {key.Pid}", ex);
        }
    }
}
