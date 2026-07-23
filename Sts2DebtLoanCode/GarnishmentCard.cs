using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DamageVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 가압류 (Distraint) — an Attack (event pool, EARNED via the combat payment-milestone). Unlike 청구서 (which spends
/// the WHOLE tally and scales), 가압류 has a FIXED 영수증 (Receipt) cost: pay [b]{Damage}[/b] damage to ALL enemies
/// for a flat 2 Receipts. Gated on holding enough. Upgraded it drops to 0 energy. Exhausts. Colorless/Event.
/// </summary>
public sealed class GarnishmentCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 2;   // FIXED: spends exactly 2 영수증 (not X)
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= TallyCost;

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/garnishment_plus.png"
                   : "res://Sts2DebtLoan/card_art/garnishment.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Damage = 10;   // fixed damage to ALL enemies (flat 2-Receipt cost, not scaling)

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(Damage, ValueProp.Move),
    };

    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        new[] { DebtLoanHoverTips.Payment() };

    public GarnishmentCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || CombatState == null) return;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this)
            .TargetingAllOpponents(CombatState)
            .Execute(choiceContext);
        await LoanService.SpendTally(Owner, TallyCost);   // spend the fixed 영수증 cost
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
