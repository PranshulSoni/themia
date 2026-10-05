using System.Diagnostics;
using System.Runtime.InteropServices;
using Themia.Core.Settings;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>
/// Deleting the user's data after Themia has quit, for "Uninstall Themia" with "also delete my
/// widgets, settings and accounts" ticked. [S-1553]
/// <para>
/// The running app cannot delete its own folders: it writes the state and the log on the way out.
/// So a second copy of the executable is started with <see cref="Flag"/> and the app's process id;
/// it waits — on the process handles, not on a clock — for the app and its direct children to
/// exit, then deletes the folders.
/// </para>
/// <para>
/// The helper trusts nothing on its command line but the id it waits on. The folders are worked
/// out again by <see cref="UninstallPlan.DataFolders"/>, never read from the arguments, and the
/// helper does nothing unless that id is a running copy of this same executable — so a stray
/// <c>--purge-data</c> cannot delete anything.
/// </para>
/// </summary>
internal static partial class DataPurge
{
    /// <summary>The helper's command-line switch, followed by the app's process id.</summary>
    public const string Flag = "--purge-data";

    /// <summary>Start the helper. False when it could not be started (the data is then left alone).</summary>
    public static bool Start()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            Debug.WriteLine("purge: no executable path; data left in place");
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                ArgumentList = { Flag, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                UseShellExecute = false,
            });

            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"purge: could not start the helper: {ex}");
            return false;
        }
    }

    /// <summary>
    /// The helper's whole run. Returns false when the arguments are not for it. Writes nothing to
    /// the log file: the log lives in one of the folders being deleted.
    /// </summary>
    public static bool TryRunFromCommandLine(string[] args)
    {
        var at = Array.IndexOf(args, Flag);
        if (at < 0)
        {
            return false;
        }

        if (at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid) || pid <= 0)
        {
            Debug.WriteLine("purge: no process id; nothing deleted");
            return true;
        }

        var waits = new List<Process>();

        try
        {
            using (var app = Process.GetProcessById(pid))
            {
                // Only for a copy of this same program: anything else asking is not the uninstall.
                if (!string.Equals(app.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("purge: the process is not Themia; nothing deleted");
                    return true;
                }

                // The children first, while the parent is alive and they are still its children.
                foreach (var child in ChildrenOf(pid))
                {
                    try
                    {
                        waits.Add(Process.GetProcessById(child));
                    }
                    catch (ArgumentException)
                    {
                        // ignore: the child exited between the snapshot and now
                    }
                }

                app.WaitForExit();
            }
        }
        catch (ArgumentException)
        {
            // ignore: the app had already exited, which is what we were waiting for
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"purge: could not check the process: {ex}");
            return true;
        }

        foreach (var child in waits)
        {
            using (child)
            {
                child.WaitForExit();
            }
        }

        var folders = UninstallPlan.DataFolders(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppStateStore.FolderName);

        foreach (var folder in folders)
        {
            Delete(folder);
        }

        return true;
    }

    /// <summary>Delete one folder; what cannot be deleted is left and said so on the debug output.</summary>
    public static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"purge: could not delete all of {folder}: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Toolhelp: which processes the app started
    // -----------------------------------------------------------------------------------------

    private const uint SnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Process32First(nint snapshot, ProcessEntry* entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Process32Next(nint snapshot, ProcessEntry* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private static unsafe List<int> ChildrenOf(int parent)
    {
        var children = new List<int>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1 || snapshot == 0)
        {
            return children;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
            for (var more = Process32First(snapshot, &entry); more; more = Process32Next(snapshot, &entry))
            {
                // Not this helper: the app started it too, and it would wait on itself for ever.
                if (entry.ParentProcessId == (uint)parent && entry.ProcessId != (uint)Environment.ProcessId)
                {
                    children.Add((int)entry.ProcessId);
                }
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return children;
    }
}
