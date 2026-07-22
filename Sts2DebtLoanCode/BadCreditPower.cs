using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 신용 불량 (Bad Credit) power — applied once, at once, by the 신용 불량 curse the deep-debt injection lodges in
/// your hand. From then on, every turn it slips a 빚쟁이 (Debtor) card into your hand at the loan's current
/// escalation level, and every 3rd turn it ratchets that level up (빚쟁이's gold + HP grow +10/+2 per level).
/// The counter lives on the loan record (<see cref="LoanService.CollectionLevel"/>, reset to 0 each combat by
/// the injector) so both co-op peers spawn identical cards; the spawn runs in the awaited AfterPlayerTurnStart
/// hook — the setup-end, co-op-safe injection point — off deterministic per-loan state.
/// </summary>
public sealed class BadCreditPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;
        var combat = player.Creature.CombatState;
        if (combat == null) return;
        var rec = LoanService.For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0) return;

        rec.CollectionLevel++;                     // 1,2,3,… deterministic per-turn ratchet (reset per combat)
        int level = rec.CollectionLevel / 3;       // every 3rd turn → +1 (turn 3 = L1, turn 6 = L2, …)
        var card = combat.CreateCard<DebtorCard>(player);
        if (card is DebtorCard d)
        {
            d.Level = level;
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player, CardPilePosition.Bottom);
        }
    }
}
