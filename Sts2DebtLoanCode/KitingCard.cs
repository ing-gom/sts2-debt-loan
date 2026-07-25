using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, PlayerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 돌려막기 (Kiting) — pay one hole by digging another. Spend [b]2[/b] 영수증 (Receipt) to EXHAUST one 빚 (native
/// Debt) card from your hand and take [gold]{gold} Gold[/gold] cash for it. 0 energy.
///
/// The consumed card is judged by <see cref="LoanService.IsDebtCurseCard"/> — the game's native Debt and NOTHING
/// else. Not the tier curses (연체 / 차압 / 신용 불량 / 강제 징수): those are re-injected every combat, so allowing
/// them would make this an infinite gold faucet. Not 납부 (the repayment card). Not the game's other curses.
/// Native Debt reaches the deck only via a debt-shop visit or 차환, so the fuel supply is finite and player-made.
///
/// Deliberately competes with 차환 for the same fuel: 차환 converts the whole pile into 납부 cards at once, this
/// cashes them out one at a time. It also competes with 정산 / 청구서 / 가압류 for the 영수증 they all spend, so
/// the gold is never free — it is 2 receipts of block or damage you chose not to take.
/// Upgraded (돌려막기+): {gold} 30 → 40. Colorless/Event, Exhaust; auto-registered.
/// </summary>
public sealed class KitingCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 2;   // costs 2 영수증 (shown as a cost badge, like 가압류's 2)

    public override int MaxUpgradeLevel => 1;   // 돌려막기 vs 돌려막기+ (30 → 40 gold)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/kiting.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private int Gold => IsUpgraded ? 40 : 30;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("gold", Gold) };

    // Hover: 소멸(Exhaust) keyword + the 납부 (Payment) tip, since the 영수증 this spends come from playing 납부.
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        new HoverTip(new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_PAYMENT.title"),
                     new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_PAYMENT.description")),
    };

    /// <summary>Gray it out unless you can pay BOTH prices: the 영수증 cost (like 가압류) and a 빚 card in hand to
    /// burn. Without the second gate the card would play for nothing and waste the receipts.</summary>
    protected override bool IsPlayable =>
        Owner != null && LoanService.PaymentsThisCombat(Owner) >= TallyCost && FindDebtCurseInHand() != null;

    public KitingCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.None) { }

    /// <summary>The 빚 card this play will burn: the first one in hand order. Deterministic (hand order is
    /// identical on every co-op peer), so both peers exhaust the same card with no networking.</summary>
    private CardModel? FindDebtCurseInHand()
    {
        if (Owner == null) return null;
        var hand = PileType.Hand.GetPile(Owner);
        if (hand == null) return null;
        foreach (var c in hand.Cards)
            if (c != this && LoanService.IsDebtCurseCard(c)) return c;
        return null;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        var victim = FindDebtCurseInHand();
        if (victim == null) return;   // IsPlayable already gated this; belt-and-braces so we never eat the receipts for nothing

        await CardPileCmd.RemoveFromCombat(victim);              // the debt note is gone for THIS combat (deck copy, if any, returns)
        await PlayerCmd.GainGold(DynamicVars["gold"].IntValue, Owner, false);
        await LoanService.SpendTally(Owner, TallyCost);          // spend the 2 영수증 (competes with 정산/청구서/가압류)
        MainFile.Logger.Info($"[{MainFile.ModId}] kiting: burned {victim.GetType().Name} for {DynamicVars["gold"].IntValue} gold.");
    }

    /// <summary>돌려막기+: bigger cash advance (30 → 40). Energy stays 0. Mirrors 품삯's gold-only upgrade.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        DynamicVars["gold"].BaseValue = Gold;   // 30 → 40 (IsUpgraded is already true here)
    }
}
