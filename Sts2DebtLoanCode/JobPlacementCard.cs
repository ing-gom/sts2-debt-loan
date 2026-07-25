using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, CardCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, PileType, CardPilePosition, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization;                // LocString
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 취업알선 (Job Placement) — a one-shot SKILL card (not a power). It is gated behind diligent payment: you must
/// have banked at least <see cref="ReceiptCost"/> 영수증 (Payment tally) this combat to play it, and playing it
/// SPENDS them (the same tally 정산/청구서 spend — so it trades combat power for gold). On play it adds a small
/// <see cref="Fee"/>-gold placement fee onto what you OWE (no gold gained), then hands you a lump of 품삯 (Wages):
/// 1 straight into your HAND (cash it now) and <see cref="DrawWages"/> shuffled into your DRAW pile (they arrive
/// over the next draws). Base feeds 품삯 (0-cost, 15 gold); upgraded (취업알선+) feeds 품삯+ (0-cost, 25 gold), so the
/// payout is 3×15 = 45 base / 3×25 = 75 upgraded. Because it's a ONE-SHOT burst with no ongoing engine, it can't be
/// stalled for infinite gold — and spending 영수증 makes it wholly distinct from 이자 지원's passive per-payment
/// trickle. "성실 납부해야 취업을 알선해준다." Colorless/Event; the guaranteed 5th-shop grant. Auto-registered.
/// </summary>
public sealed class JobPlacementCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => ReceiptCost;   // shows the 영수증 cost badge (2) — the "성실 납부" gate, same as 자본 타격

    public override int MaxUpgradeLevel => 1;   // 취업알선 vs 취업알선+ (feeds 품삯 vs 품삯+)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/job_placement_plus.png"
                   : "res://Sts2DebtLoan/card_art/job_placement.png";
    public override string BetaPortraitPath => PortraitPath;

    private const int Fee = 20;          // placement fee added onto what you OWE when played (no gold gained)
    private const int ReceiptCost = 2;   // 영수증 (Payment tally) required AND spent to play — the "성실 납부" gate
    private const int DrawWages = 2;      // 품삯 shuffled into the DRAW pile (plus 1 handed straight to you)

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("fee", Fee) };
    // The 영수증 cost (ReceiptCost) is NOT a description var — it's shown by the PaymentCostOverlay badge (like 자본 타격).

    /// <summary>Inject {card} = the localized 품삯 (Wages) name this hands out — and append "+" when THIS card is
    /// upgraded (취업알선+), so the description reads "품삯+" (the form it actually grants). Mirrors
    /// <see cref="DunningLetterCard.AddExtraArgsToDescription"/>.</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        string card = new LocString("cards", "WAGES_CARD.title").GetFormattedText();
        if (IsUpgraded) card += "+";
        description.Add("card", card);
    }

    // Hover: preview the 품삯 (Wages) card it hands out (품삯+ once upgraded).
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<WagesCard>(IsUpgraded);

    public JobPlacementCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    /// <summary>Gate: you can't take the placement unless you've paid diligently — at least <see cref="ReceiptCost"/>
    /// 영수증 banked this combat. Grayed out otherwise (BlockedByCardLogic), like 빚 독촉's gold gate.</summary>
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= ReceiptCost;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        var combat = Owner.Creature.CombatState;
        if (combat == null) return;

        await LoanService.SpendTally(Owner, ReceiptCost);   // consume 영수증 (competes with 정산/청구서 for the tally)
        LoanService.AddCombatDebt(Owner, Fee);              // +Fee onto what you OWE — a small placement fee, no gold gained

        // 1 품삯 into HAND (cash it this turn) — all 품삯+ when THIS card is 취업알선+.
        if (combat.CreateCard<WagesCard>(Owner) is WagesCard handWage)
        {
            if (IsUpgraded) { handWage.UpgradeInternal(); handWage.FinalizeUpgradeInternal(); }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                new List<CardModel> { handWage }, PileType.Hand, Owner, CardPilePosition.Random));
        }

        // DrawWages 품삯 shuffled into the DRAW pile — the income arrives over the next draws (soft draw-dilution cost).
        var drawCards = new List<CardModel>();
        for (int i = 0; i < DrawWages; i++)
        {
            if (combat.CreateCard<WagesCard>(Owner) is WagesCard w)
            {
                if (IsUpgraded) { w.UpgradeInternal(); w.FinalizeUpgradeInternal(); }
                drawCards.Add(w);
            }
        }
        if (drawCards.Count > 0)
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                drawCards, PileType.Draw, Owner, CardPilePosition.Random));
    }
}
