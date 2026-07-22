using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
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

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/blood_payment.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int HpCost = 2;
    private const int PaymentAmount = 20;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("hp", HpCost), new DynamicVar("pay", PaymentAmount) };

    public BloodPaymentCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await CreatureCmd.Damage(choiceContext, Owner.Creature, HpCost, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this);
        await LoanService.RecordPayment(Owner, choiceContext, PaymentAmount);   // 20-gold Payment, paid in HP
    }
}
