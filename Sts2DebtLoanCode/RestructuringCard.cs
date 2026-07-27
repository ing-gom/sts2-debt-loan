using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 채무 조정 (Restructuring) — a 3-cost Skill that writes [gold]{cut}[/gold] of principal off the ledger for free:
/// no gold, no HP, no 영수증. ONCE PER LOAN. After it resolves it deletes itself from the deck and stops being
/// stocked at the debt shop.
///
/// WHY ONCE PER LOAN IS LOAD-BEARING. Exhaust only removes a card for the current combat, and the debt shop clears
/// its sold-set on every new shop (<see cref="LoanService.CountShopVisit"/>), so without this gate the card would be
/// re-buyable at ~70–90 gold of debt to forgive 250 — a −160-per-shop infinite principal deleter, the same shape as
/// the 돌려막기 fuel loop. Raising the price past 250 is impossible (the per-visit credit line is 100) and dropping
/// the forgiveness below the price makes the card pointless, so the run-once flag is the only honest lever. It
/// lives on <see cref="LoanRecord.RestructuringUsed"/>, persisted on the Ledger relic, and gates three places:
/// this card's <see cref="IsPlayable"/>, the shop's offer list, and <see cref="LoanService.IsPurchasable"/>.
///
/// WHY IT DOESN'T TRIVIALISE THE MOD. The forgiveness is NOT a payment: it never touches
/// <see cref="LoanRecord.TotalPaid"/>. Since 신용 회복 (Credit Restored) requires TotalPaid ≥ DebtLoanConfig.CreditRewardCard, clearing
/// your loan this way clears the debt but forfeits the reward for having actually worked it off — restructuring gets
/// you out, it doesn't get you the medal. Three energy is also a whole turn spent on a card that deals no damage and
/// gains no Block, so it can only be cast on a turn you were already safe.
///
/// 채무 조정+ is 선천성 (Innate). For a once-per-run 3-cost the real problem is drawing it in the fight you need it,
/// so a guaranteed opening hand is the strongest upgrade available without touching the number — and it matches the
/// set's existing Innate upgrades (자본 타격 / 명세서 / 이자 지원).
///
/// If the write-off clears the loan outright, <see cref="LoanService.ForgivePrincipal"/> routes into
/// <see cref="LoanService.SettleLoanInCombat"/> so the curses lift mid-fight instead of quietly waiting for a shop.
/// Colorless/Event; auto-registered.
/// </summary>
public sealed class RestructuringCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 채무 조정 vs 채무 조정+ (Innate)

    // One portrait for both forms (돌려막기 / 차환's convention) — the upgrade adds Innate, not a new subject.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/restructuring.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Forgiven = 250;   // gold of principal written off. Tuning knob #1 — see DOPAMINE_BACKLOG.md

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("cut", Forgiven) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    };

    public RestructuringCard() : base(canonicalEnergyCost: 3, CardType.Skill, CardRarity.Event, TargetType.None) { }

    /// <summary>Playable only while there is an active loan with principal left AND the once-per-loan use is unspent
    /// — so a second copy bought before the first was played can't double-dip, and it greys out instead of burning a
    /// whole turn for nothing.</summary>
    protected override bool IsPlayable => Owner != null && LoanService.CanRestructure(Owner);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        if (!LoanService.CanRestructure(Owner)) return;   // belt-and-braces: never spend 3 energy for nothing

        // Mark BEFORE forgiving: a write-off big enough to clear the loan runs SettleLoanInCombat → ApplyRepay →
        // ResetFor(player), which drops the record entirely. Setting the flag afterwards would either resurrect a
        // dead record or silently no-op. (Losing the flag along with the record is correct — a future, separate loan
        // is a new agreement and gets its own restructuring.)
        LoanService.MarkRestructuringUsed(Owner);
        await LoanService.ForgivePrincipal(Owner, Forgiven);

        // Take the card out of the DECK for good (Exhaust alone is only this combat). If the loan just settled,
        // ApplyRepay's sweep already removed the whole debt kit and this loop simply finds nothing.
        try
        {
            foreach (var c in new List<CardModel>(Owner.Deck.Cards))
                if (c is RestructuringCard) await CardPileCmd.RemoveFromDeck(c);
        }
        catch (System.Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] restructuring deck removal failed: {e.Message}"); }

        MainFile.Logger.Info($"[{MainFile.ModId}] restructuring: wrote off up to {Forgiven} principal (once per loan).");
    }

    /// <summary>채무 조정+ : 선천성 (Innate) — guaranteed in the opening hand. Added here rather than via
    /// CanonicalKeywords because _keywords is cached once from CanonicalKeywords, so an IsUpgraded-conditional
    /// canonical set is unreliable; AddKeyword writes straight into the cached set (engine-expansion precedent).</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        AddKeyword(CardKeyword.Innate);
    }
}
