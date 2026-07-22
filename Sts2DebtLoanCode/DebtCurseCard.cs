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
/// 빚 독촉 (Dunning) — the base Debt curse the loan seeps into combat. Unlike a plain unplayable curse, this
/// gives the player AGENCY:
///   • It is ETHEREAL — if you leave it in hand, at END OF TURN it collects 10 gold (20% toward principal,
///     the rest interest) and then vanishes. The turn-end effect fires BEFORE the Ethereal exhaust (confirmed
///     against OnTurnEndInHandWrapper), so it's a clean "collect, then disappear".
///   • You may instead PLAY it (energy cost 1) to pay 20 gold, split 50/50 principal/interest — VOLUNTARY
///     faster repayment, and it's gone (Exhaust). You can only play it if you have the gold (IsPlayable gate).
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

    private int DrawCost => 10;   // the passive turn-end collection ({draw} in loc)
    private int PlayCost => 20;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("draw", DrawCost), new DynamicVar("play", PlayCost) };

    // Hover tips: 휘발(Ethereal) + 소멸(Exhaust) keywords, and a custom 납부 (Payment) tip explaining the
    // 50% principal / 50% interest split. (Plating is no longer granted by the card — see the 납부 혜택 power.)
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        new HoverTip(new LocString("relics", "DEBT_PAYMENT.title"), new LocString("relics", "DEBT_PAYMENT.description")),
    };

    public override bool HasTurnEndInHandEffect => true;

    public DebtCurseCard() : base(canonicalEnergyCost: 1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Gold gate: you can't play it unless you can actually pay the play cost (energy alone isn't
    /// enough). Grayed out when broke (BlockedByCardLogic), like Grand Finale's draw-pile check.</summary>
    protected override bool IsPlayable => Owner != null && (int)Owner.Gold >= PlayCost;

    /// <summary>The passive collection: if it's still in hand at TURN END, lose DrawCost gold (20% toward
    /// principal), then the Ethereal keyword exhausts it (collect → vanish).</summary>
    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Owner == null) return;
        int drain = Mathf.Min(DrawCost, (int)Owner.Gold);
        if (drain <= 0) return;
        await PlayerCmd.LoseGold(drain, Owner);
        await LoanService.RecordPayment(Owner, choiceContext, drain);   // 납부: 50/50 split + counter + fire payment powers
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
    /// path). Otherwise identical — DrawCost/PlayCost are flat.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
