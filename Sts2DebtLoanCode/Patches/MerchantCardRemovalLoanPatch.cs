using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;    // MerchantCardRemovalEntry, MerchantInventory
using MegaCrit.Sts2.Core.Entities.Players;

namespace Sts2DebtLoan;

/// <summary>
/// Card removal wasn't loanable: the shop's purge slot (<see cref="MerchantCardRemovalEntry"/>) OVERLOADS
/// OnTryPurchaseWrapper with its own 3-arg version (inventory, ignoreCost, <c>cancelable</c>) and calls that
/// directly — so it never hits the 2-arg <c>MerchantEntry.OnTryPurchaseWrapper</c> that
/// <see cref="MerchantLoanPurchasePatch"/> intercepts; when short on gold it just returned FailureGold.
/// This mirrors that patch onto the card-removal overload so removing a card can be covered by a loan too.
/// </summary>
[HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper),
    new Type[] { typeof(MerchantInventory), typeof(bool), typeof(bool) })]
internal static class MerchantCardRemovalLoanPatch
{
    private static readonly HashSet<MerchantCardRemovalEntry> Reentrant = new();

    private static bool Prefix(MerchantCardRemovalEntry __instance, MerchantInventory inventory,
                               bool ignoreCost, bool cancelable, Player ____player, ref Task<bool> __result)
    {
        try
        {
            if (Reentrant.Contains(__instance)) return true;   // second pass → vanilla
            if (ignoreCost) return true;
            if (____player == null) return true;
            if (__instance.EnoughGold) return true;            // already affordable
            if (!LoanService.CanLoanCover(__instance, ____player)) return true;

            __result = BuyOnLoan(__instance, inventory, ignoreCost, cancelable, ____player);
            return false;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] card-removal loan prefix failed: {e.Message}");
            return true;
        }
    }

    private static async Task<bool> BuyOnLoan(MerchantCardRemovalEntry entry, MerchantInventory inventory,
                                              bool ignoreCost, bool cancelable, Player player)
    {
        await LoanService.GrantLoanFor(entry, player);
        Reentrant.Add(entry);
        try { return await entry.OnTryPurchaseWrapper(inventory, ignoreCost, cancelable); }
        finally { Reentrant.Remove(entry); }
    }
}
