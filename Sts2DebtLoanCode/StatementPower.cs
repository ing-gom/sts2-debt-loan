using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 명세서 (Statement) — the power the 명세서 card applies. Every time you make a 납부 (Payment), you draw a card —
/// the payment engine's card-advantage option. Fired by LoanService.RecordPayment; the draw rides the lockstep
/// payment path → co-op safe.
/// </summary>
public sealed class StatementPower : PowerModel
{
    private const int DrawCount = 1;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task OnPayment(PlayerChoiceContext cc, Player player)
    {
        if (player?.Creature?.CombatState == null) return;
        await CardPileCmd.Draw(cc, DrawCount, player, fromHandDraw: false);
    }
}
