using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 취업알선 (Job Placement) — a Power card. Playing it takes a 50-gold placement fee ONTO your loan (you gain
/// 50 gold now, but it's borrowed — debt goes up), then for the rest of combat the start of each turn slips a
/// 품삯 (Wages) card into your hand: steady income to work off what you owe. Upgraded (취업알선+) it feeds the
/// 품삯+ form (0-cost, 15 gold). Same 1-energy cost as the 독촉장. Colorless/Event; the guaranteed 5th-shop
/// grant. Auto-registered.
/// </summary>
public sealed class JobPlacementCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 취업알선 vs 취업알선+ (feeds 품삯 vs 품삯+)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/job_placement_plus.png"
                   : "res://Sts2DebtLoan/card_art/job_placement.png";
    public override string BetaPortraitPath => PortraitPath;

    private int Fee => 50;   // gold borrowed onto the loan when played

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("fee", Fee) };

    public JobPlacementCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await LoanService.AddCombatDebt(Owner, Fee);   // borrow the 50-gold placement fee onto the loan
        await PowerCmd.Apply<JobPlacementPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null);
    }
}
