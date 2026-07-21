using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;   // Player, PlayerCombatState
using MegaCrit.Sts2.Core.Hooks;              // Hook

namespace Sts2DebtLoan;

/// <summary>
/// Injects the run-wide Debt-card total into a player's DRAW pile at the start of their FIRST turn.
///
/// We hook <see cref="Hook.AfterPlayerTurnStart"/> (guarded to <c>TurnNumber == 1</c>) instead of
/// <see cref="Hook.BeforeCombatStart"/> for two reasons:
///  • VISIBLE — at turn start the combat scene is fully rendered, so <c>AddGeneratedCardToCombat</c>'s
///    native card-into-pile tween/VFX actually plays on screen (the player SEES the Debt cards slide into
///    their draw pile). At BeforeCombatStart the scene is still transitioning in, so the animation is lost.
///  • CO-OP-SAFE — AfterPlayerTurnStart is the setup's last, race-free step (verified safe for command
///    emission in the sister mods), unlike the early BeforeCombatStart. It fires once per player per turn,
///    so the TurnNumber==1 guard makes this exactly one injection per player per combat.
///
/// Global (not bound to the relic owner) and run-wide, so in co-op one player's debt is felt in every
/// player's combats too, stacking if both carry loans. Chained onto the awaited hook Task (in-order,
/// lockstep-deterministic). The cards are temporary — they vanish at combat end, never joining the deck.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class TurnStartInjectPatch
{
    private static void Postfix(ref Task __result, Player player) => __result = After(__result, player);

    private static async Task After(Task original, Player player)
    {
        await original;
        try
        {
            // Only the FIRST turn of the combat (setup/turn-0 frames excluded), once per player.
            if (player?.PlayerCombatState == null || player.PlayerCombatState.TurnNumber != 1) return;
            if (player.RunState == null) return;

            // Inject every active loan's escalating curse SET into this player's draw pile (run-wide contagion).
            await LoanService.InjectAllDebtsForCombat(player, player.RunState);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] turn-start injection failed: {e.Message}"); }
    }
}
