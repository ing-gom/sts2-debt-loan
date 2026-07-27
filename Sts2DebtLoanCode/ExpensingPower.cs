using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 경비 처리 (Expensing) — the power the 경비 처리 card applies. For the rest of combat every 영수증 (Receipt)
/// cost is reduced by <see cref="PowerModel.Amount"/> (1 at base). Unlike the other payment powers this one has NO
/// OnPayment hook: it is read PASSIVELY by <see cref="LoanService.EffectiveTallyCost"/>, which the playable gate,
/// the consume call and the cost badge all route through.
/// <para>★Why a discount and not "an extra 납부": the engine's receipt INCOME is hard-capped at 1/turn (독촉장 is
/// the only source), while most payoff cards cost 2 — so every one of them is a two-turn wait. Raising income would
/// instead multiply all five on-payment powers (판금·드로우·피해·골드) at once and make this a mandatory pick;
/// cutting the PRICE fixes the same bottleneck without touching that multiplier.</para>
/// <para>X-cards (청구서/정산) are deliberately unaffected — they spend the whole tally, so there is no fixed price
/// to discount (see EffectiveTallyCost).</para>
/// Purely passive + self-applier → no commands, no hooks, nothing to order → co-op safe.
/// </summary>
public sealed class ExpensingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
}
