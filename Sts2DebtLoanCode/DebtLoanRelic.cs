using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                // RelicCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;          // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;         // RelicRarity, RelicStatus
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

    private int _principal, _interestPaid, _roomsSinceLoan, _loanFloor;
    private bool _active, _defaulted;

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Principal { get => _principal; set { AssertMutable(); _principal = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int InterestPaid { get => _interestPaid; set { AssertMutable(); _interestPaid = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int RoomsSinceLoan { get => _roomsSinceLoan; set { AssertMutable(); _roomsSinceLoan = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LoanFloor { get => _loanFloor; set { AssertMutable(); _loanFloor = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool Active { get => _active; set { AssertMutable(); _active = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool Defaulted { get => _defaulted; set { AssertMutable(); _defaulted = value; } }

    /// <summary>Live badge: the gold you currently owe (hidden once the loan is settled).</summary>
    public override int DisplayAmount => _active ? _principal : 0;

    /// <summary>NRelic renders the amount badge only when this is true — show it while a loan is active.</summary>
    public override bool ShowCounter => _active;

    /// <summary>At the start of each combat, inject the current Debt-card count into the draw pile. These
    /// are combat-temporary (gone at combat end). Runs on both peers in co-op (combat setup is lockstep).</summary>
    public override async Task BeforeCombatStart()
    {
        try
        {
            var rec = LoanService.For(Owner);
            if (rec == null || !rec.Active) return;
            int count = LoanService.CurrentDebtCardCount(rec);
            for (int i = 0; i < count; i++)
            {
                var card = Owner.RunState.CreateCard<DebtCurseCard>(Owner);
                if (card != null)
                    await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, (Player?)null, CardPilePosition.Random);
            }
            if (count > 0) MainFile.Logger.Info($"[{MainFile.ModId}] injected {count} Debt card(s) at combat start.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] combat-start Debt injection failed: {e.Message}"); }
    }

    internal static void RefreshDisplay(Player player) { /* badge/desc refresh handled by the setters + loc update */ }
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

    /// <summary>Default path: grey the relic out (kept as a frozen-credit marker) rather than removing it.</summary>
    internal static void DisableRelic(RelicModel relic)
    {
        try { relic.Status = RelicStatus.Disabled; }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic disable failed: {e.Message}"); }
    }
}
