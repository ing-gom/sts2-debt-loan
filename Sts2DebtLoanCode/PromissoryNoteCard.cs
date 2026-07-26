using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 어음 (Promissory Note) — a 0-cost Skill that BUYS TEMPO on credit: gain [b]{energy}[/b] Energy right now and add
/// [b]{debt}[/b] onto what you OWE (<see cref="LoanService.AddCombatDebt"/>). Exhausts.
///
/// This is a THIRD borrowing currency for the set. 대출 강타 borrows for damage, 저당 borrows for Block — 어음
/// borrows for ENERGY, which is qualitatively different: it doesn't add a number to one card, it raises the ceiling
/// of the whole turn (the combo turn is the payoff). Same family as MTG's Pact cycle / Hearthstone's Overload:
/// free now, billed later.
///
/// PRICE: 100 gold of principal, calibrated off 대출 강타's rate (빚 30 ≈ 14-damage-worth of soft cost → 2 Energy
/// ≈ 14~20 damage → 100). It is a SOFT cost like the other borrow cards — it does NOT add curse tiers or
/// compounding interest, it just makes the settle bigger. Note it feeds 레버리지: +100 principal is roughly
/// +3 permanent damage on every 레버리지 you play afterwards.
///
/// 어음+ ALSO DRAWS 2. The upgrade deliberately is NOT a cheaper price and NOT +3 Energy:
///   • cheaper price = no dopamine, and the card's identity is the explosive turn;
///   • +3 Energy would blow past the ceiling this whole set is balanced against;
///   • dropping Exhaust would be FATAL — a 0-cost net-positive-Energy card that stays in the deck loops with any
///     draw engine into infinite Energy. <b>The Exhaust keyword is a hard requirement, never remove it.</b>
/// Anchor for the upgraded form = STS1 Adrenaline+ (0-cost Exhaust, +1 Energy, draw 2); this gives one more Energy
/// and charges 100 principal for the difference.
///
/// Gated on an ACTIVE loan with principal left: with no loan <see cref="LoanService.AddCombatDebt"/> is a no-op and
/// the card would be free Energy. (Repaying sweeps the whole debt kit, so in practice the gate only matters for the
/// instant between a mid-combat settle and the sweep.) Colorless/Event; auto-registered.
/// </summary>
public sealed class PromissoryNoteCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 어음 vs 어음+ (adds draw 2; cost and Exhaust unchanged)

    // One portrait for both forms (돌려막기 / 차환's convention) — the upgrade adds a draw line, not a new subject.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/promissory_note.png";
    public override string BetaPortraitPath => PortraitPath;

    // ★ Exhaust is NOT optional — see the class summary. A 0-cost card that nets +2 Energy must never be repeatable.
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int EnergyGained = 2;    // the whole point: a turn with 2 more Energy than you had
    private const int DebtIncurred = 100;  // added onto what you OWE (borrowed, not gained as gold)
    private const int UpgradedDraw = 2;    // 어음+ only

    // {draw} is 0 on the base card and 2 once upgraded; the description hides the draw line at 0 via SmartFormat's
    // choose() — the same conditional-line pattern 성실 납부 uses for its refund line.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("energy", EnergyGained),
        new DynamicVar("debt", DebtIncurred),
        new DynamicVar("draw", 0),
    };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    };

    public PromissoryNoteCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.None) { }

    /// <summary>No live loan ⇒ the debt half of the deal silently vanishes and this becomes free Energy. Grey it out
    /// instead. (Owner is only touched on the in-combat mutable copy, never a canonical preview model.)</summary>
    protected override bool IsPlayable => Owner != null && LoanService.PrincipalOf(Owner) > 0;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        LoanService.AddCombatDebt(Owner, DebtIncurred);                 // sign the note first — owed goes up, no gold arrives
        await PlayerCmd.GainEnergy(EnergyGained, Owner);                // …then spend what it bought
        if (IsUpgraded)
            await CardPileCmd.Draw(choiceContext, UpgradedDraw, Owner, fromHandDraw: false);
        MainFile.Logger.Info($"[{MainFile.ModId}] promissory note: +{EnergyGained} energy for {DebtIncurred} debt (upgraded={IsUpgraded}).");
    }

    /// <summary>어음+ : draw 2 on top. Energy gain, energy cost and Exhaust all stay put (see the class summary for
    /// why each of those is load-bearing). DynamicVars are cached from CanonicalVars, so the draw count has to be
    /// mutated here rather than expressed as an IsUpgraded-dependent canonical var (JobPlacementCard's pattern).</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("draw", out var v)) { v.BaseValue = UpgradedDraw; v.WasJustUpgraded = true; }
    }
}
