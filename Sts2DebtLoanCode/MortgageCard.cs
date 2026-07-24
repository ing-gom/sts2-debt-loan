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
/// 저당 (Mortgage) — a Skill (event pool, EARNED via the combat payment-milestone). The defensive twin of
/// 대출 강타: same BORROW axis — playing it adds [b]{debt}[/b] onto what you OWE (via
/// <see cref="LoanService.AddCombatDebt"/>) in exchange for [b]{block}[/b] Block. No receipt cost, no gate; the
/// cost is future debt (higher repay cost when you settle — it does NOT add curse tiers, shop inflation, or
/// compounding interest, so it's a SOFT cost). Exhausts so the base card is a one-shot; upgraded it DROPS Exhaust
/// (repeatable). Colorless/Event. Requires an active loan for the debt to land (always true for a milestone card).
/// </summary>
public sealed class MortgageCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = drop Exhaust (repeatable); energy stays 1

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/mortgage_plus.png"
                   : "res://Sts2DebtLoan/card_art/mortgage.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Block = 12;          // solid Block for a 1-cost exhaust (debt is a soft cost)
    private const int DebtIncurred = 30;   // added onto what you OWE when played (borrowed, not gained as gold)

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("block", Block),
        new DynamicVar("debt", DebtIncurred),
    };

    public MortgageCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        LoanService.AddCombatDebt(Owner, DebtIncurred);   // borrow — owed goes up, no gold gained
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars["block"].BaseValue, ValueProp.Move, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        RemoveKeyword(CardKeyword.Exhaust);   // upgrade = repeatable (no longer a one-shot); energy stays 1
    }
}
