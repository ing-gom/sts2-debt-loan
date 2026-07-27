using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 차입 (Borrowing) — the power the 차입 card applies. At the start of each of the owner's turns you gain
/// <see cref="PowerModel.Amount"/> Energy (1 at base, 2 from 차입+). The repeatable twin of 어음 (one-shot +2 Energy
/// for 100 principal): where 어음 buys a single turn of tempo, this buys every turn of it.
/// <para>★The price is the balance, not the effect. +1 Energy/turn is boss-relic tier in any Spire, so this is gated
/// behind 4 영수증 — and receipt income is capped at 1/turn (독촉장 is the only source), so it cannot land before
/// turn 4 even in a perfect draw, and only if you have spent nothing else. That is the whole design: the payoff is
/// large, and you pay for it with the engine's entire early output.</para>
/// Fires on <see cref="AfterPlayerTurnStart"/> — the mod's verified-safe hook (setup is finished, so no race with
/// the opening-draw loop). Self-applier, energy gain rides the lockstep turn start → co-op safe.
/// </summary>
public sealed class BorrowingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // Same guard shape as 추심(CollectionPower): owner-only so each co-op peer's turn start feeds only its own
        // player, plus the CombatState null check the house pattern uses.
        if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;
        if (player.Creature.CombatState == null) return;
        int gain = (int)Amount;
        if (gain <= 0) return;
        await PlayerCmd.GainEnergy(gain, player);
    }
}
