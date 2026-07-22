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
using MegaCrit.Sts2.Core.Models.Powers;               // PlatingPower

namespace Sts2DebtLoan;

/// <summary>
/// 독촉장 (Dunning Letter) — the leverage payoff card. The merchant slips it into the deck of a debtor who
/// keeps shopping (see the shop-revisit grant). It is a POWER card: play it once and, for the rest of the
/// combat, the start of each of your turns adds a 빚 독촉 (Debt) card to your hand — a repeatable weapon that
/// scales with the loan you're carrying. Upgraded (독촉장+) it instead feeds the 0-cost 빚 독촉+ (a free
/// Plating engine). Playing any 빚 독촉 while this power is out grants Plating (판금). When the loan is repaid
/// the card is removed from the deck along with the Ledger relic (LoanService.ApplyRepay).
///
/// Rarity = Event (kept out of random card rewards); Pool overridden to ColorlessCardPool so the getter never
/// hits the MockCardPool fallback ("You monster!"). Auto-registered as an AbstractModel subtype.
/// </summary>
public sealed class DunningLetterCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 독촉장 vs 독촉장+ (feeds 빚 독촉 vs 빚 독촉+)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/dunning_letter_plus.png"
                   : "res://Sts2DebtLoan/card_art/dunning_letter.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("plate", DebtLoanConfig.LeveragePlating) };

    /// <summary>Inject the {card} placeholder = the localized name of the Debt card this power feeds — and, when
    /// THIS card is upgraded (독촉장+), append "+" so the description reads "빚 독촉+" (the 0-cost form 독촉장+
    /// actually injects). Description is non-virtual, so this arg-injection is how the upgraded text differs.</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        string card = new LocString("cards", "DEBT_CURSE_CARD.title").GetFormattedText();
        if (IsUpgraded) card += "+";
        description.Add("card", card);
    }

    // Hover tips: show the 빚 독촉 (Dunning) card this power feeds you — its preview PLUS its own keyword/power
    // tips (휘발성/소멸/판금), so hovering the 독촉장 explains exactly what you'll get. ★When THIS card is
    // upgraded (독촉장+), pass upgrade:true so the preview is the 빚 독촉+ it actually injects, not the base.
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<DebtCurseCard>(IsUpgraded);

    public DunningLetterCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    /// <summary>Apply the persistent 독촉장 power to yourself. Amount encodes the tier the per-turn injection
    /// reads: 1 = base 빚 독촉, 2 = 빚 독촉+ (upgraded card). Self-applier → co-op re-entry safe.</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int amount = IsUpgraded ? 2 : 1;
        await PowerCmd.Apply<DunningLetterPower>(choiceContext, Owner.Creature, amount, Owner.Creature, null);
    }
}
