using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Entities.Powers;             // PowerType, PowerStackType
using MegaCrit.Sts2.Core.Models;                      // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// 파산 (Bankruptcy) — the debuff 파산 선언 leaves on you for the rest of combat: you've defaulted, so you can't
/// earn a single coin. Overrides <see cref="MegaCrit.Sts2.Core.Models.AbstractModel.ModifyGoldGained"/> to zero
/// out any gold the OWNER would gain — <c>PlayerCmd.GainGold</c> bails when the modified amount ≤ 0, so this
/// natively shuts off 품삯/이자 지원 income this fight (no card-by-card patching). The ReferenceEquals guard scopes
/// it to my owner so a co-op partner's gold is untouched. Deterministic model override (no Harmony) → co-op safe.
/// </summary>
public sealed class BankruptcyPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>Zero out gold the owner would gain (bankrupt = no income this combat). Other players unaffected.</summary>
    public override decimal ModifyGoldGained(Player player, decimal amount)
        => ReferenceEquals(player?.Creature, Owner) ? 0m : amount;
}
