using System.Reflection;

namespace Themia.App.Services;

/// <summary>
/// What the app can say about itself, for the status line and the About page.
/// <para>
/// Read once. None of it changes while the process is alive, and the status bar asks for it every
/// time anything at all happens.
/// </para>
/// </summary>
internal static class AppInfo
{
    /// <summary>The informational version, falling back to the assembly version.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Whether this build was compiled ahead of time. Worth showing: it is the whole point of the
    /// port, and it is the first thing to check when a performance question comes up.
    /// </summary>
    public static bool IsAot { get; } = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    public static string Runtime => IsAot ? "native" : "managed";

    private static string ReadVersion()
    {
        // Reflection over the entry assembly's own attributes is rooted by the AOT compiler, so this
        // is safe here; anything reflecting over *other* assemblies would not be.
        var assembly = Assembly.GetEntryAssembly();

        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the source-revision suffix the SDK appends ("1.0.0+abc123"): a commit hash is
            // not what a status bar is for.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly?.GetName().Version?.ToString(3) ?? "dev";
    }
}
