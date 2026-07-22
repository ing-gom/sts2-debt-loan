using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 환급 (Refund) — a Power card (event pool). Play it and, for the rest of combat, every 납부 (Payment) slips a
/// 0-cost 성실 납부 (Diligent Payment) card into your hand (free Block). Upgraded (환급+) it feeds the 성실 납부+
/// form (Block + a 5-gold refund) — the tier is carried on the applied power's Amount (1 = base, 2 = upgraded).
/// Colorless/Event; auto-registered.
/// </summary>
public sealed class RefundCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 환급 vs 환급+ (feeds 성실 납부 vs 성실 납부+)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/refund_plus.png"
                   : "res://Sts2DebtLoan/card_art/refund.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("block", 4), new DynamicVar("gold", 5) };   // shown on the card face (성실 납부 payoff)

    public RefundCard() : base(canonicalEnergyCost: 2, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int amount = IsUpgraded ? 2 : 1;   // 2 = 환급+ → 성실 납부+
        await PowerCmd.Apply<RefundPower>(choiceContext, Owner.Creature, amount, Owner.Creature, null);
    }
}
