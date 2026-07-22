using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 혈납 (Blood Payment) — a Skill (event pool). Pay your debt with your body: lose 2 HP and make a 20-gold
/// 납부 (Payment) — no gold needed. It counts as a full 납부, so it amortizes the loan AND fires your payment
/// powers (납부 혜택 → Plating, 환급 → a 성실 납부 card). A way to trigger the payment engine when you're broke.
/// Exhausts. Colorless/Event; auto-registered.
/// </summary>
public sealed class BloodPaymentCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 혈납 vs 혈납+ (bleeds less)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/blood_payment_plus.png"
                   : "res://Sts2DebtLoan/card_art/blood_payment.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int BaseHp = 2, UpgradedHp = 1;   // 혈납+ bleeds less for the same Payment
    private const int PaymentAmount = 20;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("hp", BaseHp), new DynamicVar("pay", PaymentAmount) };

    // Hover: explain the 납부 (Payment) this card makes.
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { DebtLoanHoverTips.Payment() };

    public BloodPaymentCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int hp = DynamicVars.TryGetValue("hp", out var v) ? v.IntValue : BaseHp;   // 1 once upgraded (OnUpgrade)
        await CreatureCmd.Damage(choiceContext, Owner.Creature, hp, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this);
        await LoanService.RecordPayment(Owner, choiceContext, PaymentAmount);   // 20-gold Payment, paid in HP
    }

    // 혈납+ bleeds LESS (2 → 1) for the same Payment. Mutate the DynamicVar here (DynamicVars are cached from
    // CanonicalVars, so an IsUpgraded-based var wouldn't take — see JobPlacementCard.OnUpgrade).
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("hp", out var v)) { v.BaseValue = UpgradedHp; v.WasJustUpgraded = true; }
    }
}
