namespace Themia.App.Services.Licensing;

/// <summary>
/// [S-1067] Themia's ids in the Microsoft Store.
/// <para>
/// PLACEHOLDER until the add-on exists. Partner Center > Themia > Add-ons > New add-on: product type
/// "Durable", lifetime "Forever", product id e.g. <c>themia-pro</c>; once submitted, its page shows the
/// 12-character Store ID (<c>9XXXXXXXXXXX</c>) — paste it below. While this is not a real Store id,
/// <see cref="Themia.Core.Licensing.LicenseRules.IsStoreId"/> is false: the Upgrade button says Pro is
/// not on sale yet and no Store add-on licence can match it, so a placeholder never unlocks Pro.
/// </para>
/// </summary>
internal static class StoreIds
{
    /// <summary>The durable "Themia Pro" add-on. REPLACE with the Store ID from Partner Center.</summary>
    public const string ProAddOn = "PRO-ADDON-STORE-ID";
}
