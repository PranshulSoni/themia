using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Licensing;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1068] The License page: what this copy may do and why, how much of the free allowance is in
/// use, and the Microsoft Store purchase of Pro.
/// <para>
/// Live: the page redraws whenever the licence changes (a purchase here, one made in the Store app,
/// a refresh, the Store coming back), so it never shows a tier the gates no longer apply.
/// </para>
/// </summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildLicense(AppHost host)
    {
        var page = Page("License", []);

        // Said once, under the refusal that opened the page; a later visit starts clean.
        var refusal = host.LastRefusal;
        host.LastRefusal = null;

        void Fill()
        {
            page.Children.Clear();
            page.With(LicenseRows(host, refusal));
        }

        void OnChanged() => Fill();

        Fill();
        page.Loaded += (_, _) => host.License.Changed += OnChanged;
        page.Unloaded += (_, _) => host.License.Changed -= OnChanged;
        return page;
    }

    private static List<FrameworkElement> LicenseRows(AppHost host, string? refusal)
    {
        var license = host.License;
        var status = license.Status;
        var rows = new List<FrameworkElement>();

        if (refusal is not null && status.IsLimited)
        {
            rows.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = true,
                Severity = InfoBarSeverity.Warning,
                Title = "Free limit reached",
                Message = refusal,
                Margin = new Thickness(0, 0, 0, Fluent.Space2),
            });
        }

        rows.Add(FluentLayout.Row(
            status.Tier == LicenseTier.Free ? Glyphs.Certificate : Glyphs.Themia,
            LicenseText.Title(status),
            LicenseText.Detail(status),
            new TextBlock()));

        if (status.Basis == LicenseBasis.DeveloperBuild)
        {
            return rows;
        }

        // [S-1065] The allowance, only where it applies.
        if (status.IsLimited)
        {
            var usage = Themia.Core.Licensing.LicenseUsage.Of(host.State);
            rows.Add(FluentLayout.Header("Free version"));
            rows.Add(FluentLayout.Row(Glyphs.Grid, "Widgets", "Across all views, groups and their members included",
                Quota(usage.Widgets, LicenseRules.FreeWidgetLimit)));
            rows.Add(FluentLayout.Row(Glyphs.View, "Views", null, Quota(usage.Views, LicenseRules.FreeViewLimit)));
        }

        rows.Add(FluentLayout.Header("Microsoft Store"));

        if (status.Tier != LicenseTier.Pro)
        {
            rows.Add(UpgradeRow(license));
        }

        rows.Add(RefreshRow(license));
        return rows;
    }

    /// <summary>"7 of 10" over a bar that fills as the allowance is used.</summary>
    private static StackPanel Quota(int used, int limit)
    {
        var text = new TextBlock
        {
            Text = $"{used} of {limit}",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        FluentLayout.ApplyStyle(text, "SettingsCaptionTextBlockStyle");

        return new StackPanel
        {
            Spacing = Fluent.Space1,
            Width = 160,
            Children =
            {
                text,
                new ProgressBar { Minimum = 0, Maximum = limit, Value = Math.Min(used, limit) },
            },
        };
    }

    private static Border UpgradeRow(Services.Licensing.LicenseService license)
    {
        var verdict = FluentLayout.Verdict();
        var upgrade = new Button { Content = "Upgrade", IsEnabled = license.CanPurchase };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style accent)
        {
            upgrade.Style = accent;
        }

        upgrade.Click += async (_, _) =>
        {
            upgrade.IsEnabled = false;
            try
            {
                var outcome = await license.PurchaseProAsync(OwnerWindow(upgrade)).ConfigureAwait(true);
                var message = LicenseText.Purchase(outcome);
                if (outcome is PurchaseOutcome.Purchased or PurchaseOutcome.AlreadyOwned or PurchaseOutcome.Cancelled)
                {
                    FluentLayout.Confirm(verdict, message);
                }
                else
                {
                    FluentLayout.Refuse(verdict, message);
                }
            }
            catch (Exception ex)
            {
                // An async void handler: nothing may escape it.
                Log.Error(Scope, "the upgrade failed", ex);
                FluentLayout.Refuse(verdict, LicenseText.Purchase(PurchaseOutcome.Unavailable));
            }
            finally
            {
                upgrade.IsEnabled = license.CanPurchase;
            }
        };

        var control = new StackPanel
        {
            Spacing = Fluent.Space1,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { upgrade, verdict },
        };

        var description = license.CanPurchase
            ? "Unlimited widgets and views. One purchase, every PC you sign in to with your Microsoft account."
            : LicenseText.Purchase(PurchaseOutcome.NotConfigured);

        var row = FluentLayout.Row(Glyphs.Shop, "Themia Pro", description, control);

        // The price as the Store shows it to this user, once it is known.
        if (license.CanPurchase)
        {
            _ = ShowPriceAsync(license, upgrade);
        }

        return row;
    }

    private static async Task ShowPriceAsync(Services.Licensing.LicenseService license, Button upgrade)
    {
        try
        {
            if (await license.ProPriceAsync().ConfigureAwait(true) is { } price)
            {
                upgrade.Content = $"Upgrade · {price}";
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not show the Pro price", ex);
        }
    }

    private static Border RefreshRow(Services.Licensing.LicenseService license)
    {
        var verdict = FluentLayout.Verdict();
        var refresh = new Button { Content = "Check again" };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try
            {
                await license.RefreshAsync().ConfigureAwait(true);
                FluentLayout.Confirm(verdict, LicenseText.Title(license.Status));
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "the licence refresh failed", ex);
                FluentLayout.Refuse(verdict, LicenseText.Purchase(PurchaseOutcome.Unavailable));
            }
            finally
            {
                refresh.IsEnabled = true;
            }
        };

        return FluentLayout.Row(Glyphs.Refresh, "Restore purchases",
            "Asks the Microsoft Store again, for example after buying Pro on another PC",
            new StackPanel
            {
                Spacing = Fluent.Space1,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { refresh, verdict },
            });
    }

    /// <summary>The settings window's handle, which the Store's purchase dialog is owned by.</summary>
    private static nint OwnerWindow(FrameworkElement element)
    {
        if (element.XamlRoot?.ContentIslandEnvironment?.AppWindowId is { } id)
        {
            return (nint)Microsoft.UI.Win32Interop.GetWindowFromWindowId(id);
        }

        Log.Warn(Scope, "no window for the Store dialog; using the foreground window");
        return Interop.Win32.GetForegroundWindow();
    }
}
