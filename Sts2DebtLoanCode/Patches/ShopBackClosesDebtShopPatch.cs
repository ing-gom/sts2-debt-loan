using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;   // NMerchantInventory

namespace Sts2DebtLoan;

/// <summary>
/// Makes the merchant's OWN native back button (which calls <see cref="NMerchantInventory"/>.Close) return you to
/// the shop when the debt shop is open — instead of leaving the shop room. Prefix on Close: if the debt-card panel
/// is open, slide IT closed and skip the shop's Close (so the merchant screen stays). When no debt panel is open,
/// Close runs normally (leaves the shop as usual). This lets us drop the custom in-panel back icon and use the one
/// native back button for both. Display/navigation only → co-op safe.
/// </summary>
[HarmonyPatch(typeof(NMerchantInventory), "Close")]
internal static class ShopBackClosesDebtShopPatch
{
    private static bool Prefix()
    {
        // Debt panel open → close it, keep the shop (skip original Close). Otherwise let the shop close normally.
        return !NDebtCardShopPanel.SlideCloseIfOpen();
    }
}
