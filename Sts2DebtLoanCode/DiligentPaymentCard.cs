using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd, PlayerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 성실 납부 (Diligent Payment) — the 0-cost colorless card the 환급 (Refund) power slips into your hand on each
/// 납부. Play it for free Block; the upgraded form (성실 납부+, fed by 환급+) also refunds 5 gold. Exhausts, so
/// it doesn't clog the hand. Playing it is NOT itself a 납부 (no recursion into the payment powers).
/// Colorless pool (avoids the MockCardPool "You monster!" getter). Auto-registered.
/// </summary>
public sealed class DiligentPaymentCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 성실 납부 vs 성실 납부+ (adds the gold refund)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/diligent_payment_plus.png"
                   : "res://Sts2DebtLoan/card_art/diligent_payment.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private int BlockAmount => 4;
    private int RefundGold => IsUpgraded ? 5 : 0;   // 성실 납부+ refunds gold on top of the block

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("block", BlockAmount), new DynamicVar("gold", RefundGold) };

    public DiligentPaymentCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await CreatureCmd.GainBlock(Owner.Creature, BlockAmount, ValueProp.Move, null);
        if (RefundGold > 0)
            await PlayerCmd.GainGold(RefundGold, Owner, false);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        DynamicVars["gold"].BaseValue = RefundGold;
    }
}
