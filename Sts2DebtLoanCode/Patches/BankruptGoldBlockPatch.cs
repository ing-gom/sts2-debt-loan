using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;                // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Players;        // Player
using MegaCrit.Sts2.Core.Rewards;                 // Reward, GoldReward, RewardsSet

namespace Sts2DebtLoan;

/// <summary>
/// While 파산 (Bankruptcy) is active on a player (set by 파산 선언 / Declare Bankruptcy), swallow ALL gold gains —
/// not just the in-combat ones the BankruptcyPower's ModifyGoldGained already zeroes, but the POST-COMBAT victory
/// reward too (by then the power is gone, so only this flag can stop it). Prefix on <see cref="PlayerCmd"/>.GainGold:
/// if the player is bankrupt, skip the original (no gold added) and return a completed Task. The flag clears at the
/// next combat start (LoanService.ResetPaymentsThisCombat), so it's exactly "this fight + its reward".
/// Reads a deterministic per-player flag → co-op safe.
/// </summary>
[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold))]
internal static class BankruptGoldBlockPatch
{
    private static bool Prefix(ref decimal amount, Player player, ref Task __result)
    {
        if (LoanService.IsBankrupt(player))
        {
            __result = Task.CompletedTask;   // bankrupt → gain nothing
            return false;                    // skip the original GainGold
        }
        // Garnishment: while a high-interest loan is active, the creditor withholds a share of this income and
        // applies it straight to the debt (forced repayment). The player receives the remainder. Rate scales with
        // interest (LoanService.GarnishIncome), so the deeper you are, the less you keep.
        if (amount > 0m)
        {
            int garnished = LoanService.GarnishIncome(player, (int)amount);
            if (garnished > 0) amount -= garnished;
        }
        return true;
    }
}

/// <summary>
/// Also REMOVE the gold entry from the combat-victory rewards screen when bankrupt — otherwise a gold reward is
/// still listed (it just adds nothing when collected, which looks broken). Postfix on
/// <c>RewardsSet.GenerateRewardsFor</c> strips every <see cref="GoldReward"/> from the generated list for a
/// bankrupt player, so the screen shows no gold at all. Deterministic per-player flag → co-op safe.
/// </summary>
[HarmonyPatch(typeof(RewardsSet), "GenerateRewardsFor")]
internal static class BankruptRewardGoldRemovePatch
{
    private static void Postfix(Player player, List<Reward> __result)
    {
        if (__result == null || !LoanService.IsBankrupt(player)) return;
        __result.RemoveAll(r => r is GoldReward);
    }
}
