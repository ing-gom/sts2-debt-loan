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
/// 취업알선 (Job Placement) — an EXHAUST income SKILL (not a power). No 영수증 gate: it's the reliable way to make
/// gold when you're broke and the payment engine has stalled (so it doesn't get caught in the "no gold → no
/// payment → no receipt" deadlock). Playing it (1 energy) adds a small <see cref="Fee"/>-gold placement fee onto
/// what you OWE (no gold gained), then hands you a lump of 품삯 (Wages): 1 straight into your HAND (cash it now) and
/// <see cref="DrawWages"/> shuffled into your DRAW pile. Base feeds 품삯 (0-cost, 25 gold); upgraded (취업알선+) feeds
/// 품삯+ (0-cost, 35 gold) → 3×25 = 75 base / 3×35 = 105 upgraded. EXHAUST caps it at one payout per combat so it
/// can't be replayed for an infinite gold stall (the reason it stopped being a per-turn power). Colorless/Event;
/// the guaranteed free grant on the first shop revisit. Auto-registered.
/// </summary>
public sealed class JobPlacementCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 취업알선 vs 취업알선+ (feeds 품삯 vs 품삯+)

    // Exhaust = ONE-SHOT per combat. This is the anti-stall gate now that the 영수증 cost is gone: a reusable
    // income card with no receipt gate could be replayed every turn for infinite gold (the old stalling exploit).
    // Exhaust caps it at one payout per fight — a reliable kickstart when broke, not a farm.
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/job_placement_plus.png"
                   : "res://Sts2DebtLoan/card_art/job_placement.png";
    public override string BetaPortraitPath => PortraitPath;

    private const int Fee = 20;          // placement fee added onto what you OWE when played (no gold gained)
    private const int DrawWages = 3;      // 품삯 shuffled into the DRAW pile (all of them — none handed straight to you)

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("fee", Fee) };

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

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        var combat = Owner.Creature.CombatState;
        if (combat == null) return;

        LoanService.AddCombatDebt(Owner, Fee);              // +Fee onto what you OWE — a small placement fee, no gold gained

        // ★품삯 3장 전부를 뽑을 더미에 섞는다(예전엔 1장을 손에 직접 줬다). 품삯 값을 15 → 25로 올린
        // 대가로, 즉시 현금화되는 한 장을 없애고 "수입은 이후 드로우로 들어온다"는 성격을 분명히 했다.
        // 3장이 한 번에 생성되는 것은 그대로라, 레전트의 무기고/창조의 기둥/초질량 연동도 유지된다.
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
