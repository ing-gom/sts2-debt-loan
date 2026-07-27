using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // PowerModel
using MegaCrit.Sts2.Core.Random;                      // Rng
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 청구 반격 (Counterclaim) — the power the 청구 반격 card applies. Every time you make a 납부 (Payment), you deal
/// <see cref="Damage"/> damage to a random enemy — the payment engine's SUSTAINED offense (the burst twin is
/// 청구서). Fired by LoanService.RecordPayment. The random target is picked with a locally-seeded Rng
/// (CombatId + payment count) so it doesn't consume the shared run RNG yet stays identical on both co-op peers.
/// </summary>
public sealed class CounterclaimPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task OnPayment(PlayerChoiceContext cc, Player player)
    {
        var combat = player?.Creature?.CombatState;
        if (combat == null) return;
        var enemies = combat.Enemies.Where(e => e != null && e.IsAlive).ToList();
        if (enemies.Count == 0) return;
        // ★Damage comes from Amount (the card passes 5 / 8), NOT a const — the old const meant the card could not
        // have a numeric upgrade at all, which is why its upgrade was the dead Innate. Same shape as 명세서's draw.
        int dmg = (int)Amount;
        if (dmg <= 0) return;
        int idx = enemies.Count == 1 ? 0
            : new Rng((uint)(player.Creature.CombatId.Value + LoanService.PaymentsThisCombat(player))).NextInt(enemies.Count);
        await CreatureCmd.Damage(cc, enemies[idx], dmg, ValueProp.Move, player.Creature);
    }
}
