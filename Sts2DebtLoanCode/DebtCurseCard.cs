using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 빚 독촉 (Dunning) — the base Debt curse the loan seeps into combat. Unlike a plain unplayable curse, this
/// gives the player AGENCY:
///   • ON DRAW it collects 10 gold (20% toward principal, the rest interest) — the unavoidable collection tax.
///   • It is ETHEREAL — if you don't play it, it exhausts at end of turn (you paid only the draw tax).
///   • You may PLAY it (energy cost 1) to pay 20 more gold, split 50/50 principal/interest — VOLUNTARY faster
///     repayment. But you can only play it if you actually have the gold (IsPlayable gates on gold).
/// The UPGRADED form (빚 독촉+, injected once your lifetime borrowing exceeds the soft cap) hits harder:
/// 15 on draw / 30 on play. Auto-registered; localization injected by LocInjectionPatch.
/// </summary>
public sealed class DebtCurseCard : CardModel
{
    private static CardPoolModel? _cursePool;
    // Belongs to no pool → CardModel.Pool would hit the MockCardPool fallback ("You monster!"); borrow the
    // shared curse pool, cached. (Same fix as before, needed here too for the card preview/EnergyIcon path.)
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 1;   // base vs '+' (over-cap) form

    // Ethereal = exhausts at end of turn if left in hand; Exhaust = also leaves after being played.
    // NOT Unplayable — the player may choose to play it (to repay faster).
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal, CardKeyword.Exhaust };

    private int DrawCost => IsUpgraded ? 15 : 10;
    private int PlayCost => IsUpgraded ? 30 : 20;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("draw", DrawCost), new DynamicVar("play", PlayCost) };

    public DebtCurseCard() : base(canonicalEnergyCost: 1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Gold gate: you can't play it unless you can actually pay the play cost (energy alone isn't
    /// enough). Grayed out when broke (BlockedByCardLogic), like Grand Finale's draw-pile check.</summary>
    protected override bool IsPlayable => Owner != null && (int)Owner.Gold >= PlayCost;

    /// <summary>The collection tax on DRAW: lose DrawCost gold (20% toward principal).</summary>
    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (!ReferenceEquals(card, this) || Owner == null) return;
        int drain = Mathf.Min(DrawCost, (int)Owner.Gold);
        if (drain <= 0) return;
        await PlayerCmd.LoseGold(drain, Owner);
        await LoanService.AccrueInterest(Owner, drain);            // 20% principal (passive)
    }

    /// <summary>Playing it: pay PlayCost gold at a 50% principal split — voluntary FAST repayment. The
    /// Exhaust keyword removes it afterward. IsPlayable already guaranteed the gold is present.</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        int drain = Mathf.Min(PlayCost, (int)Owner.Gold);
        if (drain <= 0) return;
        await PlayerCmd.LoseGold(drain, Owner);
        await LoanService.AccrueInterest(Owner, drain, principalShareOverride: 0.5);   // 50% principal
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        DynamicVars["draw"].BaseValue = DrawCost;
        DynamicVars["play"].BaseValue = PlayCost;
    }
}
