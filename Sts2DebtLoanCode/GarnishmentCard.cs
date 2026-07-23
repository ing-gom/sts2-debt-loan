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
/// 가압류 (Distraint) — an Attack (event pool, EARNED via the combat payment-milestone). The AoE twin of 청구서:
/// deal [b]{Damage}[/b] damage to ALL enemies, once as a base hit plus once per 영수증 (Receipt) you hold, then
/// spend the whole tally. Lower per-hit than 청구서 since it strikes everything (Whirlwind-style multi-hit AoE).
/// Upgraded it drops to 0 energy. Exhausts. Colorless/Event; auto-registered.
/// </summary>
public sealed class GarnishmentCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => -1;   // X: spends the WHOLE 영수증, hit count scales with it

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/garnishment_plus.png"
                   : "res://Sts2DebtLoan/card_art/garnishment.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int DamagePerPayment = 3;   // per hit, to ALL enemies (lower than 청구서's 4 — it's AoE)
    private const string CalculatedHitsKey = "CalculatedHits";

    // Same live-scaling pattern as 청구서: hit count = 1 base + 1 per 영수증 (base 1 + extra 1 × payments), evaluated
    // at render so the face's "{Damage} × {CalculatedHits}" tracks the tally. Every hit strikes all enemies.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(DamagePerPayment, ValueProp.Move),
        new CalculationBaseVar(1),
        new CalculationExtraVar(1),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier((CardModel card, Creature? _) => LoanService.PaymentsThisCombat(card.Owner)),
    };

    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        new[] { DebtLoanHoverTips.Payment() };

    public GarnishmentCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || CombatState == null) return;
        int hits = (int)((CalculatedVar)DynamicVars[CalculatedHitsKey]).Calculate(null);
        if (hits <= 0) return;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(hits).FromCard(this)
            .TargetingAllOpponents(CombatState)
            .Execute(choiceContext);
        await LoanService.ConsumePaymentStack(Owner);   // spend the whole 영수증 tally (bank → unleash, AoE)
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
