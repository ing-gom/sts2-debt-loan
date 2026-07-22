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
/// 환급 (Refund) — the power the 환급 card applies. Every time you make a 납부 (Payment), it slips a 0-cost
/// 성실 납부 (Diligent Payment) card into your hand — free Block fuel. Upgraded (환급+, Amount ≥ 2) it gives
/// the 성실 납부+ form (Block + a 5-gold refund). Mirrors how 독촉장 feeds 빚 독촉. Card add rides the lockstep
/// payment; PreviewCardPileAdd is LocalContext-gated → co-op safe.
/// </summary>
public sealed class RefundPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task OnPayment(PlayerChoiceContext cc, Player player)
    {
        var combat = player?.Creature?.CombatState;
        if (combat == null) return;
        var card = combat.CreateCard<DiligentPaymentCard>(player);
        if (card == null) return;
        if (Amount >= 2) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }   // 환급+ → 성실 납부+
        var results = await CardPileCmd.AddGeneratedCardsToCombat(
            new List<CardModel> { card }, PileType.Hand, player, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);
    }
}
