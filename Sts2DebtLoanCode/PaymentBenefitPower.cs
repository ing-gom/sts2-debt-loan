using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel
using MegaCrit.Sts2.Core.Models.Powers;               // PlatingPower

namespace Sts2DebtLoan;

/// <summary>
/// 납부 혜택 (Payment Benefit) — the power the 납부 혜택 card applies. Every time you make a 납부 (Payment) —
/// i.e. a 빚 독촉 collects/pays gold, or a HP-payment card pays — you gain Plating (판금). This is the reward
/// that used to live on the 독촉장; it's now its own power so you can build it independently. Fired by
/// LoanService.RecordPayment. Self-applier → co-op safe. (Play-cost reduction to 0 on upgrade — see the card.)
/// </summary>
public sealed class PaymentBenefitPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task OnPayment(PlayerChoiceContext cc, Player player)
    {
        if (Owner == null || DebtLoanConfig.LeveragePlating <= 0) return;
        await PowerCmd.Apply<PlatingPower>(cc, Owner, DebtLoanConfig.LeveragePlating, Owner, null);
    }
}
