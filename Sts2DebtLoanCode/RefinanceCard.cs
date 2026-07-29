using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, CardCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType, CardPilePosition, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using NativeDebt = MegaCrit.Sts2.Core.Models.Cards.Debt;   // the game's Unplayable Debt curse

namespace Sts2DebtLoan;

/// <summary>
/// 차환 (Refinance) — a Skill. EXHAUST every 빚 (<see cref="NativeDebt"/>) card you're holding this combat — that
/// card type ONLY, exactly like 파산 선언 — and for each one exhausted, add a 납부 (Payment /
/// <see cref="DebtCurseCard"/>) card to your DISCARD pile. Turns the debt clog into repayment fuel: the Debt
/// vanishes this fight and comes back as cards you can play for Receipts / to pay the loan down. Unlike
/// 파산 선언 (which trades the debt for Strength but blocks gold), this keeps the payment engine turning.
///
/// THE PRICE: refinancing rolls the debt over rather than erasing it — when combat ends one native Debt joins the
/// DECK permanently (swept only by repaying the loan). So each use leaves you one card deeper, and the pile it
/// clears next time is the pile it built. 돌려막기 burns the same fuel for gold; the two compete.
/// Upgraded (차환+): the same conversion, but on BETTER TERMS — every card it hands back is a 납부+ (0-energy),
/// so the refinanced debt can be cleared without paying energy for it. The card itself stays 1 energy.
/// Colorless/Event, Exhaust; auto-registered.
/// </summary>
public sealed class RefinanceCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 차환 vs 차환+ (hands back 납부+ instead of 납부)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/refinance.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    /// <summary>Inject {card} = the localized 납부 (Payment) name this hands out — with "+" appended once THIS card
    /// is upgraded (차환+), so the description reads "납부+", the form it actually creates. Same arg-injection
    /// pattern as <see cref="DunningLetterCard.AddExtraArgsToDescription"/> / <see cref="JobPlacementCard"/>
    /// (Description is non-virtual, so this is how upgraded text differs).</summary>
    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString description)
    {
        base.AddExtraArgsToDescription(description);
        string card = new MegaCrit.Sts2.Core.Localization.LocString("cards", "DEBT_CURSE_CARD.title").GetFormattedText();
        if (IsUpgraded) card += "+";
        description.Add("card", card);
    }

    // Hover: preview the 납부 (Payment) card it hands out — 납부+ once THIS card is upgraded.
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromCardWithCardHoverTips<DebtCurseCard>(IsUpgraded);

    public RefinanceCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        var combat = Owner.Creature.CombatState;
        if (combat == null) return;

        // Exhaust every Debt curse in play this combat, counting them (deck copies are untouched — they return next
        // fight, like 파산 선언's Exhaust). Guarded so a pile hiccup can't abort before the payment cards are added.
        int converted = 0;
        try
        {
            foreach (var pt in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
            {
                var pile = pt.GetPile(Owner);
                if (pile == null) continue;
                foreach (var c in new List<CardModel>(pile.Cards))
                    if (LoanService.IsDebtCurseCard(c)) { await CardPileCmd.RemoveFromCombat(c); converted++; }
            }
        }
        catch (System.Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] refinance exhaust failed: {e.Message}"); }

        if (converted <= 0) return;

        // Add one 납부 (Payment) card to the DISCARD pile per curse exhausted. 차환+ refinances onto BETTER TERMS:
        // every card handed back is a 납부+ (0 energy), so the whole refinanced block can be cleared in one turn.
        var payments = new List<CardModel>();
        for (int i = 0; i < converted; i++)
            if (combat.CreateCard<DebtCurseCard>(Owner) is DebtCurseCard pay)
            {
                if (IsUpgraded) { pay.UpgradeInternal(); pay.FinalizeUpgradeInternal(); }
                payments.Add(pay);
            }
        if (payments.Count > 0)
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                payments, PileType.Discard, Owner, CardPilePosition.Random));

        // THE PRICE OF THE SWAP: refinancing does not erase the debt, it rolls it over. One native 빚 (Debt) curse
        // joins the DECK permanently — the clog you cleared this fight follows you home, and only repaying the loan
        // sweeps it (RemoveNativeDebtCards). Deck adds do NOT enter the current combat's piles (CardPileCmd.Add
        // targets PileType.Deck only), so the card text "전투가 끝나면" is literally true: this fight stays clean,
        // every future one is one card deeper. Runs only when something was actually converted (see the early
        // return above) → a whiffed play costs nothing.
        await DebtLoanGrants.GrantNativeDebt(Owner, $"refinance rolled over {converted} debt curse(s)");
    }

    // No OnUpgrade cost change: 차환+ keeps its 1 energy. The upgrade is the QUALITY of what comes back
    // (납부 → 납부+, see OnPlay), not the price of the swap — a free swap that also handed back free cards would
    // make the whole debt pile cost nothing to clear.
}
