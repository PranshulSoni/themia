using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>
/// [S-1108] Windows' preferred display languages, most preferred first (<c>de-CH</c>, <c>en-US</c>…),
/// for "follow Windows". Asked of Windows directly: the app runs with InvariantGlobalization, so
/// <c>CultureInfo.CurrentUICulture</c> is always the invariant culture and says nothing.
/// </summary>
internal static partial class SystemLanguages
{
    private const string Scope = "i18n";
    private const uint MuiLanguageName = 0x8;

    /// <summary>
    /// Read once: changing the Windows display language takes a sign-out, so a running app never
    /// sees it change — and the setting re-resolves on every state change, which must stay cheap.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<string>> Cached = new(Read);

    public static IReadOnlyList<string> Preferred() => Cached.Value;

    private static unsafe IReadOnlyList<string> Read()
    {
        uint size = 0;
        if (!GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref size) || size == 0)
        {
            Log.Warn(Scope, $"could not read the Windows display languages (error {Marshal.GetLastPInvokeError()}); using English");
            return [];
        }

        var buffer = new char[size];
        fixed (char* chars = buffer)
        {
            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, chars, ref size))
            {
                Log.Warn(Scope, $"could not read the Windows display languages (error {Marshal.GetLastPInvokeError()}); using English");
                return [];
            }
        }

        // A double-null-terminated list: "de-CH\0en-US\0\0".
        var tags = new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Log.Debug(Scope, $"Windows display languages: {string.Join(", ", tags)}");
        return tags;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserPreferredUILanguages(uint flags, out uint count, char* buffer, ref uint bufferSize);
}
