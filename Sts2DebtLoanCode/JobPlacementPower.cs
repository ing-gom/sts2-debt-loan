using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, CardCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel, CardModel

namespace Sts2DebtLoan;

/// <summary>
/// 취업알선 (Job Placement) — the power the 취업알선 card applies. At the start of each of the owner's turns it
/// slips a 품삯 (Wages) card into their hand — steady income from the job you were placed in. Upgraded (Amount
/// ≥ 2, from 취업알선+) it feeds the 품삯+ form (0-cost, 15 gold). Mirrors 독촉장 → 빚 독촉. Card add rides the
/// lockstep turn start; PreviewCardPileAdd is LocalContext-gated → co-op safe.
/// </summary>
public sealed class JobPlacementPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;
        var combat = player.Creature.CombatState;
        if (combat == null) return;

        var card = combat.CreateCard<WagesCard>(player);
        if (card == null) return;
        if (Amount >= 2) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }   // 품삯+ (0-cost, 15 gold)

        var results = await CardPileCmd.AddGeneratedCardsToCombat(
            new List<CardModel> { card }, PileType.Hand, player, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);
    }
}
