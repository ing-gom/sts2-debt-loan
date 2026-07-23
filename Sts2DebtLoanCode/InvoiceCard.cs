using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DamageVar, CalculationBaseVar, CalculationExtraVar, CalculatedVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 청구서 (Invoice) — an Attack (event pool). A TRUE multi-hit: deal [b]{Damage}[/b] damage once for EACH 납부
/// (Payment) made this combat — the bill comes due one strike at a time. Because it's real multi-hit (not one
/// lump sum), each hit is checked against the enemy's Block separately, exactly like Sword Boomerang / Barrage.
/// The hit count (<c>CalculatedHits</c>) is computed LIVE from the payment counter (Barrage's orb-count pattern),
/// so the card face shows "{Damage} damage × {CalculatedHits}" and updates as you pay. Upgraded it drops to 0
/// energy. Exhausts. Colorless/Event; auto-registered. (Offensive twin of 정산, which scales block.)
/// </summary>
public sealed class InvoiceCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/invoice_plus.png"
                   : "res://Sts2DebtLoan/card_art/invoice.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int DamagePerPayment = 4;
    private const string CalculatedHitsKey = "CalculatedHits";

    // {Damage} = damage PER HIT; {CalculatedHits} = hit count = 1 base hit + 1 per 납부 실적 (Barrage's
    // CalculatedVar pattern — base 1 + extra 1 × payments = payments + 1). The base hit means it always lands at
    // least one strike even at 0 tally (no dead card). The multiplier is evaluated at render time, so the face's
    // multi-hit "{Damage} × {CalculatedHits}" tracks payments with no preview patch needed.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(DamagePerPayment, ValueProp.Move),
        new CalculationBaseVar(1),   // guaranteed base hit → never a dead card at 0 납부 실적
        new CalculationExtraVar(1),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier((CardModel card, Creature? _) => LoanService.PaymentsThisCombat(card.Owner)),
    };

    // Hover: explain the 납부 (Payment) count this scales off.
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        new[] { DebtLoanHoverTips.Payment() };

    public InvoiceCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || CombatState == null || cardPlay.Target == null) return;
        int hits = (int)((CalculatedVar)DynamicVars[CalculatedHitsKey]).Calculate(cardPlay.Target);
        if (hits <= 0) return;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(hits).FromCard(this)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
        await LoanService.ConsumePaymentStack(Owner);   // spend the whole 납부 실적 stack (bank → unleash)
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
