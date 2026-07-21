using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                // RelicCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;          // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;         // RelicRarity
using MegaCrit.Sts2.Core.HoverTips;               // HoverTipFactory, IHoverTip (Debt-card preview in tooltip)
using MegaCrit.Sts2.Core.Localization.DynamicVars; // DynamicVar (per-relic hover values)
using MegaCrit.Sts2.Core.Models;                  // RelicModel, ModelDb
using MegaCrit.Sts2.Core.Saves.Runs;              // SavedProperty, SerializationCondition

namespace Sts2DebtLoan;

/// <summary>
/// The "Merchant's Ledger" relic. Granted the instant you take a loan; it carries the whole loan state
/// (as [SavedProperty] fields) and, at the START of each combat, injects the current number of Debt
/// curse cards (1/2/3 by rooms since the loan) into the draw pile — temporary cards that vanish at
/// combat end rather than clogging the deck. Disabled (kept, greyed) if the loan defaults at 200%.
/// </summary>
public sealed class DebtLoanRelic : RelicModel
{
    // Event rarity = grant-only: reward/shop pools only roll Common/Uncommon/Rare/Shop, so it never drops.
    public override RelicRarity Rarity => RelicRarity.Event;

    public override string PackedIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";
    protected override string PackedIconOutlinePath => "res://Sts2DebtLoan/icons/debt_loan_relic_outline.png";
    protected override string BigIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";

    private int _borrowed, _principal, _totalPaid, _loanFloor;
    private bool _active;
    private int _cards;   // transient (not saved): current per-combat Debt-card count, for the hover {cards}

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Borrowed { get => _borrowed; set { AssertMutable(); _borrowed = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Principal { get => _principal; set { AssertMutable(); _principal = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int TotalPaid { get => _totalPaid; set { AssertMutable(); _totalPaid = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LoanFloor { get => _loanFloor; set { AssertMutable(); _loanFloor = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool Active { get => _active; set { AssertMutable(); _active = value; InvokeDisplayAmountChanged(); } }

    /// <summary>Live badge: the gold you currently owe (hidden once the loan is settled).</summary>
    public override int DisplayAmount => _active ? _principal : 0;

    /// <summary>NRelic renders the amount badge only when this is true — show it while a loan is active.</summary>
    public override bool ShowCounter => _active;

    // Per-relic dynamic hover: the loc description is the static template "Borrowed [gold]{borrowed} Gold[/gold]…
    // Paid [gold]{paid} Gold[/gold]…", and these DynamicVars fill {borrowed}/{paid} from THIS relic's own
    // state. RelicModel.DynamicDescription applies DynamicVars per-instance, so two players' Ledgers each show
    // their own numbers — unlike the old global loc-table overwrite (which was a co-op display bug).
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("borrowed", _borrowed), new DynamicVar("paid", _totalPaid), new DynamicVar("cards", _cards) };

    /// <summary>Show a preview of the Debt curse card (plus its keyword tips) in the relic's hover tooltip
    /// — the same mechanism vanilla Soot uses. So hovering the Ledger reveals exactly what the injected
    /// Debt cards look like.</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<DebtCurseCard>();

    /// <summary>Push the current borrowed/paid values + per-combat Debt-card count into the cached DynamicVars
    /// so the hover shows live, per-relic numbers. Called by LoanService.SyncToRelic on every state change
    /// (<paramref name="cards"/> is the current injection count, computed from rooms-since-loan). DynamicVars
    /// is built lazily from CanonicalVars and then cached, so we update the vars in place.</summary>
    internal void RefreshVars(int cards)
    {
        _cards = cards;
        try
        {
            var vars = DynamicVars;
            if (vars.TryGetValue("borrowed", out var b)) b.BaseValue = _borrowed;
            if (vars.TryGetValue("paid", out var p)) p.BaseValue = _totalPaid;
            if (vars.TryGetValue("cards", out var c)) c.BaseValue = _cards;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] ledger var refresh failed: {e.Message}"); }
    }
}

/// <summary>Grant/remove/disable helpers, kept out of the model so it stays a pure data type.</summary>
internal static class DebtLoanGrants
{
    internal static async Task GrantRelic(Player player)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(typeof(DebtLoanRelic)));
            if (model == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] DebtLoanRelic model not found."); return; }
            await RelicCmd.Obtain(model.ToMutable(), player);
            MainFile.Logger.Info($"[{MainFile.ModId}] granted Merchant's Ledger relic.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic grant failed: {e.Message}"); }
    }

    /// <summary>Repay path: remove the relic entirely (clean slate → can borrow again).</summary>
    internal static async Task RemoveRelic(Player player)
    {
        try
        {
            var relic = LoanService.LedgerRelicOf(player);
            if (relic != null) { await RelicCmd.Remove(relic); MainFile.Logger.Info($"[{MainFile.ModId}] removed Ledger relic (repaid)."); }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic remove failed: {e.Message}"); }
    }
}
