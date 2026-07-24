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
/// 추심 (Collections) — the power the 추심 card applies. At the start of each of the owner's turns it slips a
/// 집행 (Shakedown) card into their hand — a 0-cost token that spends 1 영수증 (Receipt) for 활력 (Vigor). This is
/// the OFFENSIVE mirror of 환급 → 성실 납부 (block): payments fuel a per-turn Vigor option WITHOUT a free scaling
/// attack, and the receipt cost makes it compete with 정산/청구서. Card add rides the lockstep turn start;
/// PreviewCardPileAdd is LocalContext-gated → co-op safe (same shape as 정기 납부/환급).
/// </summary>
public sealed class CollectionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;
        var combat = player.Creature.CombatState;
        if (combat == null) return;

        var card = combat.CreateCard<ShakedownCard>(player);
        if (card == null) return;
        var results = await CardPileCmd.AddGeneratedCardsToCombat(
            new List<CardModel> { card }, PileType.Hand, player, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);
    }
}
