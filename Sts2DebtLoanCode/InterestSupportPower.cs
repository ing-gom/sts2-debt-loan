using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 이자 지원 (Interest Support) — the power the 이자 지원 card applies. Every time you make a 납부 (Payment), it
/// SUBSIDIZES half of it: you gain gold equal to half the payment amount back — so working the debt down (and
/// fuelling the payment engine) costs you half as much. Fired by LoanService.RecordPayment, which passes the
/// payment amount. Gold gain is a local mutation off the lockstep payment → ⚠️ verify with coop-verify.
/// </summary>
public sealed class InterestSupportPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task OnPayment(PlayerChoiceContext cc, Player player, int amount)
    {
        if (player == null) return;
        // Amount = 보조 비율의 분자, 분모는 항상 2. 1 ⇒ 절반, 2 ⇒ 전액(이자 지원+). 이렇게 두면 카드의 {pct}
        // 표시와 실제 지급이 한 값에서 나온다.
        int subsidy = amount * (int)Amount / 2;
        if (subsidy <= 0) return;
        await PlayerCmd.GainGold(subsidy, player, false);
    }
}
