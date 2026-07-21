using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;              // LocalContext
using MegaCrit.Sts2.Core.Entities.Merchant;    // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;     // Player
using MegaCrit.Sts2.Core.Helpers;              // StsColors
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;  // NMerchantSlot + subtypes
using MegaCrit.Sts2.Core.Runs;                 // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// Paints a merchant item's price tag GREEN when the player can't afford it but a loan can cover it
/// (Act-1, under the 300 cap, before the deadline). Each slot subtype's <c>UpdateVisual</c> sets
/// <c>_costLabel.Modulate = EnoughGold ? cream : red</c>; we postfix it and, for a loanable item, flip
/// the red to green. We do NOT touch <c>EnoughGold</c> (that would break the purchase gate) — only the
/// label colour. The purchase itself is handled by <see cref="MerchantLoanPurchasePatch"/>.
/// </summary>
[HarmonyPatch]
internal static class MerchantPriceColorPatch
{
    private static readonly Type[] SlotTypes =
    {
        typeof(NMerchantRelic), typeof(NMerchantCard), typeof(NMerchantPotion), typeof(NMerchantCardRemoval),
    };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var t in SlotTypes)
        {
            var m = AccessTools.Method(t, "UpdateVisual");
            if (m != null) yield return m;
            else MainFile.Logger.Warn($"[{MainFile.ModId}] {t.Name}.UpdateVisual not found — green tags off for it.");
        }
    }

    private static void Postfix(NMerchantSlot __instance)
    {
        try
        {
            var entry = EntryOf(__instance);
            if (entry == null || entry.EnoughGold) return;   // affordable / no entry → leave as-is

            var players = RunManager.Instance?.State?.Players;
            if (players == null) return;
            var player = LocalContext.GetMe(players);
            if (player == null) return;

            if (LoanService.CanLoanCover(entry, player))
                __instance._costLabel.Modulate = StsColors.green;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] green price tag failed: {e.Message}"); }
    }

    /// <summary>The MerchantEntry backing a slot (each subtype holds one: _relicEntry/_cardEntry/…).</summary>
    private static MerchantEntry? EntryOf(NMerchantSlot slot)
    {
        foreach (var f in slot.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (typeof(MerchantEntry).IsAssignableFrom(f.FieldType))
                return f.GetValue(slot) as MerchantEntry;
        return null;
    }
}
