using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;   // Player, PlayerCombatState
using MegaCrit.Sts2.Core.Hooks;              // Hook

namespace Sts2DebtLoan;

/// <summary>
/// Injects the run-wide Debt-card total into a player's DRAW pile at the start of their FIRST turn,
/// <b>before the opening hand is dealt</b>, so the Debt cards are DRAWN into that opening hand.
///
/// We hook <see cref="Hook.BeforeHandDraw"/> (guarded to <c>TurnNumber == 1</c>) rather than
/// <see cref="Hook.AfterPlayerTurnStart"/>. In <c>CombatManager.StartTurn</c> the sequence is:
/// AfterEnergyReset → <b>BeforeHandDraw</b> → ModifyHandDraw → (turn-1 Innate reorder) →
/// <c>CardPileCmd.Draw</c> → AfterPlayerTurnStart. The old AfterPlayerTurnStart injection ran AFTER the
/// draw, so the cards sat in the draw pile and only appeared on turn 2. Injecting here — awaited before the
/// Draw within the same StartTurn — places them at the TOP of the draw pile (see
/// <see cref="LoanService.InjectAllDebtsForCombat"/>) so the opening Draw pulls them straight into hand.
///
/// Global (not bound to the relic owner) and run-wide, so in co-op one player's debt is felt in every
/// player's combats too, stacking if both carry loans. Chained onto the awaited hook Task (in-order,
/// lockstep-deterministic; top-of-pile insertion is fully deterministic, unlike a random position). The
/// cards are temporary — they vanish at combat end, never joining the deck.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeHandDraw))]
internal static class BeforeHandDrawInjectPatch
{
    private static void Postfix(ref Task __result, Player player) => __result = After(__result, player);

    private static async Task After(Task original, Player player)
    {
        await original;
        try
        {
            // Only the FIRST turn's hand draw (subsequent turns also draw), once per player per combat.
            if (player?.PlayerCombatState == null || player.PlayerCombatState.TurnNumber != 1) return;
            if (player.RunState == null) return;

            // Inject every active loan's escalating curse SET at the TOP of this player's draw pile so the
            // opening hand draws them (run-wide contagion).
            await LoanService.InjectAllDebtsForCombat(player, player.RunState);
            LoanService.RefreshRelicDisplay(player);   // keep the ledger hover's per-tier text current (display-only)
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] opening-hand debt injection failed: {e.Message}"); }
    }
}
