using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // PlatingPower

namespace Sts2DebtLoan;

/// <summary>
/// 납부 혜택 (Payment Benefit) — a Power card (event pool). Play it and, for the rest of combat, every 납부
/// (Payment) you make grants Plating (판금) — the reward that used to be baked into the 독촉장. Upgraded it
/// drops to 0 energy. Colorless/Event; auto-registered.
/// </summary>
public sealed class PaymentBenefitCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = 1 energy (2 → 1)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/payment_benefit_plus.png"
                   : "res://Sts2DebtLoan/card_art/payment_benefit.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("plate", DebtLoanConfig.LeveragePlating) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.FromPower<PlatingPower>(), DebtLoanHoverTips.Payment() };

    public PaymentBenefitCard() : base(canonicalEnergyCost: 2, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<PaymentBenefitPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 2 → 1
    }
}
