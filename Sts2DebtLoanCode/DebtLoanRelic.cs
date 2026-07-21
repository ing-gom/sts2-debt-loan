using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                // RelicCmd
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;         // RelicRarity
using MegaCrit.Sts2.Core.Models;                  // RelicModel, ModelDb
using MegaCrit.Sts2.Core.Saves.Runs;              // SavedProperty, SerializationCondition

namespace Sts2DebtLoan;

/// <summary>
/// The "Merchant's Ledger" relic. Granted the instant you take your first loan; it is the
/// carrier for the whole debt state. It has no combat effect — it is a status marker whose
/// hover text (TODO: live) shows the principal, remaining borrow headroom, rooms until the
/// next Debt card, and interest paid vs the 200% ceiling.
///
/// Auto-registered as a model (Entry "DEBT_LOAN_RELIC"). It must NOT roll as a normal reward —
/// see <see cref="Patches.RelicInjectionPatches"/> for the pool handling (TODO: guarantee
/// grant-only so it never appears in a relic reward).
/// </summary>
public sealed class DebtLoanRelic : RelicModel
{
    // Event rarity = grant-only: the reward/shop pools only roll Common/Uncommon/Rare/Shop, so the
    // Ledger never drops as a random reward — it can only be obtained by taking a loan (RelicCmd.Obtain).
    public override RelicRarity Rarity => RelicRarity.Event;

    public override string PackedIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";
    protected override string PackedIconOutlinePath => "res://Sts2DebtLoan/icons/debt_loan_relic_outline.png";
    protected override string BigIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";

    // The whole loan state lives here as [SavedProperty] fields. Because we OWN this relic, they
    // round-trip through RelicModel.ToSerializable/FromSerializable automatically — no serialization
    // Harmony patch needed. On load, LoanService.RestoreFromRelic rebuilds the transient LoanRecord
    // from these. SaveIfNotTypeDefault keeps a vanilla/retired relic's save clean.
    private int _principal, _interestPaid, _roomsSinceLoan;
    private bool _active;

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Principal { get => _principal; set { AssertMutable(); _principal = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int InterestPaid { get => _interestPaid; set { AssertMutable(); _interestPaid = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int RoomsSinceLoan { get => _roomsSinceLoan; set { AssertMutable(); _roomsSinceLoan = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool Active { get => _active; set { AssertMutable(); _active = value; InvokeDisplayAmountChanged(); } }

    /// <summary>Live badge on the relic icon: the gold you currently owe (hidden once the loan is settled).</summary>
    public override int DisplayAmount => _active ? _principal : 0;

    /// <summary>NRelic only renders the amount badge when this is true — show it while a loan is outstanding.</summary>
    public override bool ShowCounter => _active;

    /// <summary>Nudge the relic's on-screen state after the loan changes (retire, top-up).
    /// TODO: rebuild the dynamic hover text / flash the relic. Stub for now.</summary>
    internal static void RefreshDisplay(Player player)
    {
        // Placeholder — live relic text refresh is a follow-up (needs the NRelic UI node).
    }
}

/// <summary>Grant helper kept out of the model so the model stays a pure data type.</summary>
internal static class DebtLoanGrants
{
    internal static async Task GrantRelic(Player player)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(typeof(DebtLoanRelic)));
            if (model == null)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] DebtLoanRelic model not found — cannot grant.");
                return;
            }
            await RelicCmd.Obtain(model.ToMutable(), player);
            MainFile.Logger.Info($"[{MainFile.ModId}] granted Merchant's Ledger relic.");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] relic grant failed: {e.Message}");
        }
    }
}
