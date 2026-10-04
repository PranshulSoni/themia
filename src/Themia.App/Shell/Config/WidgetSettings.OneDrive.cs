using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.WidgetUi_AccountLabel.Text()));

        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem
        {
            Content = accounts.Count > 0 ? Str.WidgetsC_OneDrive_Settings_FirstAccountNamed.Format(("account", accounts[0].Label)) : Str.WidgetsC_OneDrive_Settings_FirstAccount.Text(),
            Tag = string.Empty,
        });

        foreach (var account in accounts)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = account.Problem is null ? account.Label : Str.WidgetsC_OneDrive_Settings_AccountSignInAgain.Format(("account", account.Label)),
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

        rows.Add(FluentLayout.Row(Glyphs.OneDrive, Str.WidgetsC_OneDrive_Settings_OneDriveOf_Label.Text(),
            (accounts.Count == 0 ? Str.WidgetsC_OneDrive_Settings_NoAccount : Str.WidgetsC_OneDrive_Settings_OneDriveOf_Desc).Text(),
            combo));

        if (accounts.Count == 0 || accounts.Any(a => a.Problem is not null))
        {
            rows.Add(FluentLayout.NavRow(Glyphs.Contact, Str.WidgetUi_AccountsLabel.Text(),
                (accounts.Count == 0 ? Str.WidgetsC_OneDrive_Settings_SignInMicrosoft : Str.WidgetsC_OneDrive_Settings_AccountNeedsSignIn).Text(),
                () => navigate(new ConfigTarget.App(AppPage.Accounts))));
        }

        rows.Add(FluentLayout.Header(Str.WidgetsC_OneDrive_Settings_Files.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Folder, Str.WidgetsC_OneDrive_Settings_StartIn_Label.Text(), Str.WidgetsC_OneDrive_Settings_StartIn_Desc.Text(),
            FluentLayout.Choice<DriveStart>(
                [(DriveStart.Recent, Str.WidgetsC_OneDrive_RecentFiles.Text()), (DriveStart.Root, Str.WidgetsC_OneDrive_MyFiles.Text())],
                config.StartIn,
                value => { config.StartIn = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Open, Str.WidgetsC_OneDrive_Settings_OpenWith_Label.Text(), Str.WidgetsC_OneDrive_Settings_OpenWith_Desc.Text(),
            FluentLayout.Choice<ItemClickMode>(
                [(ItemClickMode.Double, Str.WidgetsC_OneDrive_Settings_DoubleClick.Text()), (ItemClickMode.Single, Str.WidgetsC_OneDrive_Settings_SingleClick.Text())],
                config.ClickMode,
                value => { config.ClickMode = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsC_OneDrive_Settings_Items_Label.Text(), Str.WidgetsC_OneDrive_Settings_Items_Desc.Text(),
            FluentLayout.Slider(config.EffectiveFileCount, OneDriveConfig.MinFiles, OneDriveConfig.MaxFiles, 5, string.Empty,
                value => { config.FileCount = (int)Math.Round(value); save(); })));
    }
}
