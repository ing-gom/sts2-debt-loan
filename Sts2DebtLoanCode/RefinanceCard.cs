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
/// 차환 (Refinance) — a Skill. EXHAUST every 빚 저주 (Debt curse) card you're holding this combat — the native
/// <see cref="NativeDebt"/> plus the tier curses (연체/차압/신용 불량/강제 징수) — and for each one exhausted, add a
/// 납부 (Payment / <see cref="DebtCurseCard"/>) card to your DISCARD pile. Turns the debt clog into repayment fuel:
/// the curses vanish this fight and come back as cards you can play for Receipts / to pay the loan down. Unlike
/// 파산 선언 (which trades the debt for Strength but blocks gold), this keeps the payment engine turning.
/// Upgraded (차환+): 0 energy. Colorless/Event, Exhaust; auto-registered.
/// </summary>
public sealed class RefinanceCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 차환 vs 차환+ (0 energy)

    // TODO(art): placeholder portrait (reuses the payment/dunning art) — swap for a bespoke "refinance" portrait.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/debt_dunning.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    // Hover: preview the 납부 (Payment) card it hands out.
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromCardWithCardHoverTips<DebtCurseCard>();

    public RefinanceCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    /// <summary>Is this a 빚 저주 (Debt curse) card that Refinance converts? Native Debt + our tier curses.</summary>
    private static bool IsDebtCurse(CardModel c)
        => c is NativeDebt || c is DelinquencyCard || c is SeizureCard || c is BadCreditCard || c is DebtorCard;

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
                    if (IsDebtCurse(c)) { await CardPileCmd.RemoveFromCombat(c); converted++; }
            }
        }
        catch (System.Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] refinance exhaust failed: {e.Message}"); }

        if (converted <= 0) return;

        // Add one 납부 (Payment) card to the DISCARD pile per curse exhausted.
        var payments = new List<CardModel>();
        for (int i = 0; i < converted; i++)
            if (combat.CreateCard<DebtCurseCard>(Owner) is DebtCurseCard pay) payments.Add(pay);
        if (payments.Count > 0)
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                payments, PileType.Discard, Owner, CardPilePosition.Random));
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
