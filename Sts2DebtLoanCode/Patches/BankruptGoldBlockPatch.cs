using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;                // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Players;        // Player

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
    private static bool Prefix(Player player, ref Task __result)
    {
        if (LoanService.IsBankrupt(player))
        {
            __result = Task.CompletedTask;   // bankrupt → gain nothing
            return false;                    // skip the original GainGold
        }
        return true;
    }
}
