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
public sealed class CounterclaimCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 2;   // costs 2 영수증 to install (strong offense engine)
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= LoanService.EffectiveTallyCost(this, Owner);

    public override int MaxUpgradeLevel => 1;   // upgrade = 납부당 피해 5 → 8

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/counterclaim_plus.png"
                   : "res://Sts2DebtLoan/card_art/counterclaim.png";
    public override string BetaPortraitPath => PortraitPath;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("dmg", 5) };

    public CounterclaimCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<CounterclaimPower>(choiceContext, Owner.Creature, DynamicVars["dmg"].IntValue, Owner.Creature, null);   // Amount = 납부당 피해
        await LoanService.SpendTally(Owner, LoanService.EffectiveTallyCost(this, Owner));   // spend the 영수증 cost
    }

    /// <summary>자본 타격+ : 납부당 피해 [b]5 → 8[/b].
    /// ★예전 강화는 선천성이었는데 <b>완전히 죽은 강화</b>였다: 이 카드는 영수증 2를 무는데 영수증은 전투 시작 시
    /// 0이라, 첫 턴 손에 있어도 낼 방법이 아예 없다(영수증 2를 모으려면 납부를 두 번 해야 한다). 선천성이 사는
    /// 카드는 첫 턴에 낼 수 있는 카드뿐이다.
    /// 이 카드는 이 세트의 <b>지속 공격</b> 담당이니 강화도 화력으로 준다 — 납부 1회당이라 전투가 길수록 벌어진다
    /// (5턴이면 25 → 40). 명세서+는 카드 이득, 이자 지원+는 골드 회수율을 올리는 식으로 셋의 강화 정체성을 갈랐다.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("dmg", out var v)) { v.BaseValue = 8; v.WasJustUpgraded = true; }
    }
}
