using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;   // Hook
using MegaCrit.Sts2.Core.Runs;    // IRunState

namespace Sts2DebtLoan;

/// <summary>
/// At the start of every combat, inject the run-wide Debt-card total into EACH player's draw pile
/// (temporary cards, gone at combat end). Global — not bound to the relic owner — so in co-op one
/// player's debt is felt in the partner's combats too, and stacks if both carry loans. Chained onto the
/// awaited <see cref="Hook.BeforeCombatStart"/> Task (in-order, co-op-lockstep-safe).
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
internal static class CombatStartInjectPatch
{
    private static void Postfix(ref Task __result, IRunState runState) => __result = After(__result, runState);

    private static async Task After(Task original, IRunState runState)
    {
        await original;
        try
        {
            if (runState?.Players == null) return;
            int total = LoanService.RunWideDebtTotal(runState);
            if (total <= 0) return;
            foreach (var p in new List<Player>(runState.Players))
                await LoanService.InjectDebtCardsForCombat(p, total);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] combat-start injection failed: {e.Message}"); }
    }
}
