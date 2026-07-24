using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DamageVar, DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 대출 강타 (Loan Strike) — an Attack (event pool, EARNED via the combat payment-milestone). A new cost axis for
/// the set: instead of spending 영수증 (Receipts), it BORROWS — playing it adds [b]{debt}[/b] onto what you OWE
/// (like 취업알선's fee, via <see cref="LoanService.AddCombatDebt"/>) in exchange for a big single-target hit of
/// [b]{Damage}[/b]. No receipt cost, no gate — the cost is future debt (higher repay cost when you settle; it does
/// NOT add curse tiers, shop inflation, or compounding interest, so it's a SOFT cost). Exhausts so the base card is a
/// deliberate one-shot; upgraded it DROPS Exhaust (repeatable — keep borrowing to keep hitting, and the debt piles up).
/// Colorless/Event. Requires an active loan for the debt to land (always true for a milestone-earned card).
/// </summary>
public sealed class LoanStrikeCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = drop Exhaust (repeatable); energy stays 1

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/loan_strike_plus.png"
                   : "res://Sts2DebtLoan/card_art/loan_strike.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Damage = 14;         // solid single-target hit for a 1-cost exhaust (debt is a soft cost)
    private const int DebtIncurred = 30;   // added onto what you OWE when played (borrowed, not gained as gold)

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(Damage, ValueProp.Move),
        new DynamicVar("debt", DebtIncurred),
    };

    public LoanStrikeCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || cardPlay.Target == null) return;
        LoanService.AddCombatDebt(Owner, DebtIncurred);   // borrow — owed goes up, no gold gained
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        RemoveKeyword(CardKeyword.Exhaust);   // upgrade = repeatable (no longer a one-shot); energy stays 1
    }
}
