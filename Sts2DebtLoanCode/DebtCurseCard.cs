using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip, HoverTip
using MegaCrit.Sts2.Core.Localization;                // LocString
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // PlatingPower

namespace Sts2DebtLoan;

/// <summary>
/// 빚 독촉 (Dunning) — the base Debt curse the loan seeps into combat. It is injected into the DRAW pile
/// BEFORE the opening hand is dealt (see <see cref="BeforeHandDrawInjectPatch"/>), so you draw it turn 1.
/// Unlike a plain unplayable curse, it gives the player AGENCY, with NO passive drain:
///   • It is ETHEREAL — if you leave it in hand it simply exhausts at end of turn, costing nothing.
///     There is no automatic collection any more (the old end-of-turn 10-gold drain was removed).
///   • You may PLAY it (energy cost 1) to pay 20 gold, split 50/50 principal/interest — VOLUNTARY faster
///     repayment, and it's gone (Exhaust). You can only play it if you have the gold (IsPlayable gate).
///     While the 독촉장 (Dunning Letter) power is active, playing it also grants Plating (판금) — the power is
///     what makes dumping your debt cards worthwhile (a defensive/repayment engine, no offense).
/// The UPGRADED form (빚 독촉+, fed by the 독촉장+ power) is identical but 0-energy. Auto-registered;
/// localization injected by LocInjectionPatch.
/// </summary>
public sealed class DebtCurseCard : CardModel
{
    private static CardPoolModel? _cursePool;
    // Belongs to no pool → CardModel.Pool would hit the MockCardPool fallback ("You monster!"); borrow the
    // shared curse pool, cached. (Same fix as before, needed here too for the card preview/EnergyIcon path.)
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 1;   // base vs '+' (over-cap) form

    // Custom curse art from the mod pck (base vs '+'). Renderer reads Model.Portrait => Load(PortraitPath).
    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/debt_dunning_plus.png"
                   : "res://Sts2DebtLoan/card_art/debt_dunning.png";
    public override string BetaPortraitPath => PortraitPath;

    // Ethereal = exhausts at end of turn if left in hand; Exhaust = also leaves after being played.
    // NOT Unplayable — the player may choose to play it (to repay faster).
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal, CardKeyword.Exhaust };

    private int PlayCost => 20;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("play", PlayCost) };

    // Hover tips: 휘발(Ethereal) + 소멸(Exhaust) keywords, and a custom 납부 (Payment) tip explaining the
    // 50% principal / 50% interest split. (Plating is no longer granted by the card — see the 납부 혜택 power.)
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        new HoverTip(new LocString("relics", "DEBT_PAYMENT.title"), new LocString("relics", "DEBT_PAYMENT.description")),
    };

    public DebtCurseCard() : base(canonicalEnergyCost: 1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Gold gate: you can't play it unless you can actually pay the play cost (energy alone isn't
    /// enough). Grayed out when broke (BlockedByCardLogic), like Grand Finale's draw-pile check.</summary>
    protected override bool IsPlayable => Owner != null && (int)Owner.Gold >= PlayCost;

    /// <summary>Fire the missed-payment co-op hook: if this card is still in hand at turn end (you didn't play it),
    /// it's about to Ethereal-exhaust for 0 payment — in co-op that becomes an ally's chance to cover you.</summary>
    public override bool HasTurnEndInHandEffect => true;

    /// <summary>미납 (missed payment): unplayed at turn end. In co-op, hand the richest teammate who can afford it a
    /// 대납 (Bailout) card (upgraded to match a 빚 독촉+) so they can pay on your behalf this turn. SP: nothing — the
    /// Ethereal keyword just exhausts it as before. We do NOT remove the card here; Ethereal still exhausts it.
    /// Runs in the lockstep turn-end path over shared state → co-op-safe (see GrantBailoutForMissedPayment).</summary>
    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Owner == null) return;
        await LoanService.GrantBailoutForMissedPayment(Owner, IsUpgraded);
    }

    /// <summary>Playing it: pay PlayCost gold at a 50% principal split (voluntary FAST repayment). If the 독촉장
    /// (Dunning Letter) power is active, ALSO gain Plating (판금) — the power is what turns playing your debt
    /// cards into a defensive engine (the reward lives on the power, not on the curse itself). The Exhaust
    /// keyword removes it afterward. IsPlayable already guaranteed the gold. Self-applier → co-op re-entry safe.</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int drain = Mathf.Min(PlayCost, (int)Owner.Gold);
        if (drain <= 0) return;
        await PlayerCmd.LoseGold(drain, Owner);
        // Payment: 50/50 split + counter + fire the payment-reactive powers (납부 혜택 → Plating, 환급 → card…).
        // The 판금 reward no longer lives here — it moved to the 납부 혜택 power (fired via RecordPayment).
        await LoanService.RecordPayment(Owner, choiceContext, drain);
    }

    /// <summary>빚 독촉+ (the upgraded form the 독촉장+ power injects): drop to 0 energy (vanilla cost-reduction
    /// path). Otherwise identical — PlayCost is a flat 20.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
