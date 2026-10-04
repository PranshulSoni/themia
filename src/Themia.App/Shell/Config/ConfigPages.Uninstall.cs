using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Settings;
using Themia.Core.Storage;

namespace Themia.App.Shell.Config;

/// <summary>
/// "Uninstall Themia" at the foot of the About page. [S-1552..S-1555]
/// <para>
/// A danger card, last on the page. Its button asks first, in a dialog that says what will happen
/// and offers — unticked — to also delete the user's widgets, settings and accounts. Nothing is
/// removed or deleted before that dialog's Uninstall is pressed. Then: the goodbye survey opens (as
/// the original's installer opened it on an interactive uninstall), Themia closes and saves, and
/// only after that — so the final save cannot write the folders back — the data goes (when asked)
/// and the app itself: a Store/MSIX install removes its own package, a loose build opens Windows'
/// Installed apps for the user to finish.
/// </para>
/// </summary>
internal static partial class ConfigPages
{
    /// <summary>APPMODEL_ERROR_NO_PACKAGE: the process has no package identity.</summary>
    private const int NoPackage = 15700;

    /// <summary>ERROR_INSUFFICIENT_BUFFER: the call that sizes the buffer.</summary>
    private const int InsufficientBuffer = 122;

    private static Border UninstallRow(AppHost host)
    {
        var button = new Button { Content = "Uninstall" };
        button.Click += (_, _) => _ = UninstallAsync(host, button);

        return FluentLayout.DangerRow(Glyphs.Delete, "Uninstall Themia",
            "Remove Themia from this PC, and if you want, everything it keeps", button);
    }

    private static async Task UninstallAsync(AppHost host, Button source)
    {
#if DEBUG
        if (Gallery.SettingsRender.IsRendering)
        {
            Log.Warn(Scope, "uninstall pressed in a settings render; nothing was done");
            return;
        }
#endif

        var packageName = PackageFullName();
        var preview = UninstallPlan.Create(
            packaged: packageName is not null,
            deleteData: true,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppStateStore.FolderName,
            AppInfo.Version);

        if (await AskUninstallAsync(source, preview) is not { } deleteData)
        {
            return;
        }

        var plan = preview with { FoldersToDelete = deleteData ? preview.FoldersToDelete : [] };
        Log.Info(Scope, $"uninstalling ({plan.Route}, delete data: {deleteData})");

        // The survey first, while there is still a process to open it.
        Launch(plan.Survey);

        host.RequestExit(() => FinishUninstallAsync(plan, packageName));
    }

    /// <summary>
    /// The confirmation. Null for cancel; otherwise whether the data goes too (unticked by default).
    /// </summary>
    private static async Task<bool?> AskUninstallAsync(FrameworkElement source, UninstallPlan plan)
    {
        if (source.XamlRoot is not { } root)
        {
            Log.Warn(Scope, "no XamlRoot for the uninstall dialog; nothing was done");
            return null;
        }

        var what = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = plan.Route == UninstallRoute.RemovePackage
                ? "Themia closes and is removed from this PC."
                : "Themia closes, and Windows' Installed apps opens so you can remove it there.",
        };

        var deleteData = new CheckBox { Content = "Also delete my widgets, settings and accounts" };

        var where = new TextBlock { Text = string.Join(Environment.NewLine, plan.FoldersToDelete) };
        FluentLayout.ApplyStyle(where, "SettingsCaptionTextBlockStyle");
        where.Margin = new Thickness(28, 0, 0, 0);
        where.Visibility = Visibility.Collapsed;

        // The folders are named once the box is ticked, so the user sees exactly what goes.
        deleteData.Checked += (_, _) => where.Visibility = Visibility.Visible;
        deleteData.Unchecked += (_, _) => where.Visibility = Visibility.Collapsed;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = "Uninstall Themia?",
            Content = new StackPanel
            {
                Spacing = Fluent.Space3,
                MaxWidth = 440,
                Children = { what, deleteData, where },
            },
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? deleteData.IsChecked == true : null;
        }
        catch (Exception ex)
        {
            // No dialog, no uninstall: refusing beats removing something unasked.
            Log.Error(Scope, "could not show the uninstall confirmation", ex);
            return null;
        }
    }

    /// <summary>After the app has closed and saved: the data (if asked), then the app itself.</summary>
    private static async Task FinishUninstallAsync(UninstallPlan plan, string? packageName)
    {
        if (plan.Route == UninstallRoute.RemovePackage && packageName is not null)
        {
            // Packaged: the package's removal ends this process, so the data goes first, from here.
            foreach (var folder in plan.FoldersToDelete)
            {
                DataPurge.Delete(folder);
            }

            try
            {
                // No capability needed: a full-trust app runs at medium integrity, and the package's
                // publisher is the caller's own.
                var result = await new Windows.Management.Deployment.PackageManager().RemovePackageAsync(packageName);
                if (result.ExtendedErrorCode is { } error)
                {
                    throw error;
                }
            }
            catch (Exception ex)
            {
                // Removal refused (policy, a pending update): Settings' page for the app has the
                // same Uninstall button.
                Log.Error(Scope, "could not remove the package; opening its page in Settings", ex);
                Launch(plan.Fallback);
            }

            return;
        }

        // Loose build: the folders go once this process (and its browser) has exited.
        if (plan.FoldersToDelete.Count > 0 && !DataPurge.Start())
        {
            Log.Warn(Scope, "could not start the data cleanup; the data folders were left in place");
        }

        Launch(plan.Fallback);
    }

    private static void Launch(Uri uri)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(Scope, $"could not open {uri}", ex);
        }
    }

    /// <summary>This process's package full name, or null when it runs without package identity.</summary>
    private static unsafe string? PackageFullName()
    {
        uint length = 0;
        var status = GetCurrentPackageFullName(ref length, null);

        if (status == NoPackage)
        {
            return null;
        }

        if (status != InsufficientBuffer || length == 0)
        {
            Log.Warn(Scope, $"could not tell whether Themia is packaged (error {status}); treating it as not");
            return null;
        }

        var buffer = new char[length];
        fixed (char* chars = buffer)
        {
            status = GetCurrentPackageFullName(ref length, chars);
        }

        return status == 0 ? new string(buffer, 0, (int)Math.Max(0, length - 1)) : null;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint length, char* name);
}
