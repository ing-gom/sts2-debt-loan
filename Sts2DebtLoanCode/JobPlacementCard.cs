using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization;                // LocString
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 취업알선 (Job Placement) — a Power card. Playing it adds a 50-gold placement fee straight ONTO your debt (you
/// do NOT receive the gold — it's a fee, so what you owe goes up by 50), hands you a 품삯 (Wages) card RIGHT NOW,
/// and then for the rest of combat the start of each turn slips ANOTHER 품삯 into your hand: steady income to work
/// off what you owe. The immediate 품삯 means it pays off from the turn you play it (no dead first turn). Upgraded
/// (취업알선+) it keeps the same 50-gold fee but feeds the 품삯+ form (0-cost, 20 gold). Same 1-energy cost as 정기 납부.
/// Colorless/Event; the guaranteed 5th-shop grant. Auto-registered.
/// </summary>
public sealed class JobPlacementCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 취업알선 vs 취업알선+ (feeds 품삯 vs 품삯+; fee unchanged)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/job_placement_plus.png"
                   : "res://Sts2DebtLoan/card_art/job_placement.png";
    public override string BetaPortraitPath => PortraitPath;

    private const int Fee = 50;   // gold borrowed onto the loan when played (unchanged by upgrade)

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("fee", Fee) };

    /// <summary>Inject {card} = the localized 품삯 (Wages) name this feeds — and append "+" when THIS card is
    /// upgraded (취업알선+), so the description reads "품삯+" (the form it actually hands out). Mirrors
    /// <see cref="DunningLetterCard.AddExtraArgsToDescription"/>.</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        string card = new LocString("cards", "WAGES_CARD.title").GetFormattedText();
        if (IsUpgraded) card += "+";
        description.Add("card", card);
    }

    // Hover: preview the 품삯 (Wages) card it feeds you each turn (품삯+ once upgraded).
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<WagesCard>(IsUpgraded);

    public JobPlacementCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        LoanService.AddCombatDebt(Owner, Fee);   // add the 50-gold placement fee onto what you OWE (no gold gained)
        await PowerCmd.Apply<JobPlacementPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null);   // 2 = 품삯+
        // Hand over the FIRST 품삯 immediately so it pays off THIS turn (the power then feeds one every turn start).
        var combat = Owner.Creature.CombatState;
        if (combat != null && combat.CreateCard<WagesCard>(Owner) is WagesCard wages)
        {
            if (IsUpgraded) { wages.UpgradeInternal(); wages.FinalizeUpgradeInternal(); }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                new List<CardModel> { wages }, PileType.Hand, Owner, CardPilePosition.Random));
        }
    }
}
