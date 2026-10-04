using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-606]/[S-607] The OneDrive widget's settings: which Microsoft account, where it starts, how a
/// file opens ([S-1291]) and how many items it lists at least ([S-1292]). Accounts are signed in on the Accounts page; this one links there.
/// </summary>
internal static partial class WidgetSettings
{
    public static void OneDrive(List<FrameworkElement> rows, OneDriveConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        var accounts = AppServices.Drive?.Accounts ?? [];

        rows.Add(FluentLayout.Header("Account"));

        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem
        {
            Content = accounts.Count > 0 ? $"First Microsoft account ({accounts[0].Label})" : "First Microsoft account to sign in",
            Tag = string.Empty,
        });

        foreach (var account in accounts)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = account.Problem is null ? account.Label : $"{account.Label} — sign in again",
                Tag = account.Id,
            });
        }

        combo.SelectedIndex = Math.Max(0, IndexOfTag(combo, config.AccountId));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id } && id != config.AccountId)
            {
                config.AccountId = id;
                save();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.OneDrive, "OneDrive of",
            accounts.Count == 0 ? "No Microsoft account is signed in" : "Personal, work or school",
            combo));

        if (accounts.Count == 0 || accounts.Any(a => a.Problem is not null))
        {
            rows.Add(FluentLayout.NavRow(Glyphs.Contact, "Accounts",
                accounts.Count == 0 ? "Sign in to a Microsoft account" : "An account needs you to sign in again",
                () => navigate(new ConfigTarget.App(AppPage.Accounts))));
        }

        rows.Add(FluentLayout.Header("Files"));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Start in", "Where the widget opens, and where Back returns to",
            FluentLayout.Choice<DriveStart>(
                [(DriveStart.Recent, "Recent files"), (DriveStart.Root, "My files")],
                config.StartIn,
                value => { config.StartIn = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Open, "Open files with", "Folders always open with a single click",
            FluentLayout.Choice<ItemClickMode>(
                [(ItemClickMode.Double, "Double click"), (ItemClickMode.Single, "Single click")],
                config.ClickMode,
                value => { config.ClickMode = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Items", "At least this many; a bigger widget lists more to fill its space",
            FluentLayout.Slider(config.EffectiveFileCount, OneDriveConfig.MinFiles, OneDriveConfig.MaxFiles, 5, string.Empty,
                value => { config.FileCount = (int)Math.Round(value); save(); })));
    }
}
