using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar, GoldVar
using MegaCrit.Sts2.Core.Models;                      // CardModel

namespace Sts2DebtLoan;

/// <summary>
/// The Debt card the loan seeps into your deck. A near-verbatim copy of the base-game
/// <c>Debt</c> curse (Unplayable; at end of turn while in hand, lose gold), with two changes:
///   • the drained amount is <see cref="DebtLoanConfig.InterestPerDraw"/> (config-tunable), and
///   • the drain is booked as interest via <see cref="LoanService.AccrueInterest"/>, which retires
///     the loan once the 200% ceiling is reached.
///
/// Auto-registered: the game reflects over mod assemblies for <c>CardModel</c> subtypes and assigns
/// a ModelId (Entry = "DEBT_CURSE_CARD"); localization is injected at runtime by LocInjectionPatch.
/// Like the vanilla Debt, it needs no custom art — it uses the shared Curse frame.
/// </summary>
public sealed class DebtCurseCard : CardModel
{
    public override int MaxUpgradeLevel => 0;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[] { new GoldVar(DebtLoanConfig.InterestPerDraw) };

    public override bool HasTurnEndInHandEffect => true;

    public DebtCurseCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        // Spec: at 0 gold there is nothing to drain, so no interest is paid this trigger.
        int drain = Mathf.Min(DynamicVars.Gold.IntValue, Owner.Gold);
        if (drain <= 0) return;
        await PlayerCmd.LoseGold(drain, Owner);
        await LoanService.AccrueInterest(Owner, drain);
    }
}
