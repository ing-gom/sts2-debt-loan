using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace Sts2DebtLoan;

/// <summary>
/// While you carry an active loan, merchants at OTHER shops (not the one you borrowed at) charge more:
/// +10% with 1 Debt card, +15% with 2, +20% with 3. A pure deterministic postfix on the price
/// aggregator <see cref="Hook.ModifyMerchantPrice"/> (same hook Sts2RelicForge scales), so it flows into
/// both the shown and the paid price. No surcharge at your own loan shop, or once the loan is settled.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyMerchantPrice))]
internal static class MerchantPriceInflatePatch
{
    private static void Postfix(ref decimal __result, Player player)
    {
        try
        {
            if (player == null || __result <= 0m) return;
            double mult = LoanService.DebtPriceMultiplier(player);
            if (mult > 1.0)
                __result = decimal.Round(__result * (decimal)mult, 0);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt price surcharge failed: {e.Message}"); }
    }
}
