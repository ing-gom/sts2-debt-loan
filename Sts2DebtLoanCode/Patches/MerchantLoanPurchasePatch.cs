using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;    // MerchantEntry, MerchantInventory
using MegaCrit.Sts2.Core.Entities.Players;

namespace Sts2DebtLoan;

/// <summary>
/// Opens the merchant's <c>!EnoughGold</c> gate for a loanable purchase. When the player is short on
/// gold but a loan can cover the shortfall (Act-1, under the 300 cap, before the deadline), we:
///   1. credit the shortfall as loan gold (co-op-safe GainGold + sync),
///   2. record the debt and grant the Merchant's Ledger relic on the first loan,
///   3. re-run the purchase — now affordable — so the normal spend/obtain path completes.
///
/// We take over the async <c>OnTryPurchaseWrapper</c> (return false + set <c>__result</c>) and re-enter
/// it once, guarded by <see cref="Reentrant"/>, so the second pass runs vanilla. The private
/// <c>_player</c> field is read via Harmony field injection (<c>____player</c>).
///
/// TODO (verify in-game with solo-verify): this is the most version-/co-op-sensitive patch. Confirm the
/// re-entry spends exactly the credited gold (player lands at ~0), and that ignoreCost/out-of-stock
/// paths are untouched.
/// </summary>
[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
internal static class MerchantLoanPurchasePatch
{
    private static readonly HashSet<MerchantEntry> Reentrant = new();

    private static bool Prefix(MerchantEntry __instance, MerchantInventory inventory, bool ignoreCost,
                               Player ____player, ref Task<bool> __result)
    {
        try
        {
            if (Reentrant.Contains(__instance)) return true;   // second pass → vanilla
            if (ignoreCost) return true;                       // free purchase → not a loan
            if (____player == null) return true;
            if (__instance.EnoughGold) return true;            // already affordable → vanilla
            if (!LoanService.CanLoanCover(__instance, ____player)) return true; // ineligible → vanilla rejects

            __result = BuyOnLoan(__instance, inventory, ignoreCost, ____player);
            return false;                                      // we own the call
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] loan purchase prefix failed: {e.Message}");
            return true;
        }
    }

    private static async Task<bool> BuyOnLoan(MerchantEntry entry, MerchantInventory inventory, bool ignoreCost, Player player)
    {
        await LoanService.GrantLoanFor(entry, player);
        Reentrant.Add(entry);
        try { return await entry.OnTryPurchaseWrapper(inventory, ignoreCost); }
        finally { Reentrant.Remove(entry); }
    }
}
