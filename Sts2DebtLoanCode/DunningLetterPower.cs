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
/// 독촉장 (Dunning Letter) — the persistent power the 독촉장 card applies. At the start of each of the owner's
/// turns it slips a 빚 독촉 (Debt) card into their hand, turning the loan into a repeatable engine:
///   • base power (Amount 0)  → a 1-cost 빚 독촉 (leverage nuke: AoE = principal/10)
///   • upgraded  (Amount 1)   → a 0-cost 빚 독촉+ (no AoE) — a free repay/Plating engine
/// The Plating (판금) reward itself lives on <see cref="DebtCurseCard.OnPlay"/>, which grants it whenever a
/// 빚 독촉 is played while this power is present (HasPower&lt;DunningLetterPower&gt;). Auto-registered as an
/// AbstractModel subtype in the mod; localization injected into the "powers" table by LocInjectionPatch.
/// The card add is lockstep (rides the deterministic turn start), and PreviewCardPileAdd is local-gated →
/// co-op safe.
/// </summary>
public sealed class DunningLetterPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // Only fire for MY owner's turn (guard against being called on a partner's turn in co-op).
        if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;
        var combat = player.Creature.CombatState;
        if (combat == null) return;

        var card = combat.CreateCard<DebtCurseCard>(player);
        if (card == null) return;
        if (Amount >= 2) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }   // 빚 독촉+ (0-cost)

        var results = await CardPileCmd.AddGeneratedCardsToCombat(
            new List<CardModel> { card }, PileType.Hand, player, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);   // fly-into-hand animation, LocalContext-gated (visual only)
    }
}
