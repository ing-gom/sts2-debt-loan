using MegaCrit.Sts2.Core.Entities.Powers;   // PowerType, PowerStackType
using MegaCrit.Sts2.Core.Models;            // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 납부 실적 (Payment tally) — a visible Buff that gains +1 every time you make a 납부 (Payment) this combat.
/// It is the payment engine's AMMO: 청구서 (Invoice) and 정산 (Settlement) scale their payout off this count and
/// then CONSUME the whole stack (bank payments up, then unleash). Being a normal power it is combat-scoped (auto-
/// clears when the fight ends), so it replaces the old invisible per-combat counter as the single source of truth
/// for <see cref="LoanService.PaymentsThisCombat"/>. Applied by <see cref="LoanService.RecordPayment"/> as a self-
/// applier → co-op safe. <c>Counter</c> stack type accumulates additively (same as Strength).
/// </summary>
public sealed class PaymentStackPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
}
