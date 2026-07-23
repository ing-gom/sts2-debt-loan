using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 자본 타격 (Money Attack) — a Power card (event pool). Play it and, for the rest of combat, every 납부 (Payment)
/// you make deals [b]{dmg}[/b] damage to a random enemy — the payment engine's sustained offense. 1 energy;
/// upgrade grants Innate (선천성) so it opens in your starting hand. Colorless/Event; auto-registered.
/// </summary>
public sealed class CounterclaimCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = gain Innate (선천성)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/counterclaim_plus.png"
                   : "res://Sts2DebtLoan/card_art/counterclaim.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("dmg", 5) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { DebtLoanHoverTips.Payment() };

    public CounterclaimCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<CounterclaimPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        AddKeyword(CardKeyword.Innate);   // 강화 = 선천성 (starts in the opening hand)
    }
}
