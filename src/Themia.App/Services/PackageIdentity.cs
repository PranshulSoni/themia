using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// Whether this process runs with an MSIX package identity. Decided once: identity cannot be gained
/// or lost while a process runs, and several features switch on it (autostart, [S-933]).
/// </summary>
internal static partial class PackageIdentity
{
    private const string Scope = "package";
    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE
    private const int InsufficientBuffer = 122; // ERROR_INSUFFICIENT_BUFFER

    /// <summary>True when installed from an MSIX package (the Store build), false for the unpackaged build.</summary>
    public static bool IsPackaged { get; } = Detect();

    private static unsafe bool Detect()
    {
        uint length = 0;
        var status = GetCurrentPackageFullName(ref length, null);

        if (status == NoPackage)
        {
            return false;
        }

        if (status == InsufficientBuffer)
        {
            return true;
        }

        // Any other answer is unexpected; unpackaged is the shape whose features need no identity.
        Log.Warn(Scope, $"could not tell whether Themia is packaged (error {status}); treating it as not");
        return false;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint length, char* name);
}
