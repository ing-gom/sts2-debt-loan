using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // IHoverTip
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 이자 지원 (Interest Support) — a Power card (event pool). Play it and, for the rest of combat, every 납부
/// (Payment) you make refunds you HALF its amount in gold — the payment engine costs half as much to run.
/// 1 energy; upgrade grants Innate (선천성) so it opens in your starting hand. Colorless/Event; auto-registered.
/// </summary>
public sealed class InterestSupportCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = gain Innate (선천성)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/interest_support_plus.png"
                   : "res://Sts2DebtLoan/card_art/interest_support.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { DebtLoanHoverTips.Payment() };

    public InterestSupportCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<InterestSupportPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        AddKeyword(CardKeyword.Innate);   // 강화 = 선천성 (starts in the opening hand)
    }
}
