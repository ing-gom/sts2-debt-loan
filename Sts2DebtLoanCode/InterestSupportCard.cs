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
/// 이자 지원 (Interest Support) — a Power card (event pool). Play it and, for the rest of combat, every 납부
/// (Payment) you make refunds you HALF its amount in gold — the payment engine costs half as much to run.
/// 1 energy; upgrade grants Innate (선천성) so it opens in your starting hand. Colorless/Event; auto-registered.
/// </summary>
public sealed class InterestSupportCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 1;   // costs 1 영수증 (cheap economy engine — entry point of the receipt loop)

    /// <summary>{pct} = 납부액 중 돌려받는 비율(기본 50, 이자 지원+ 100). 파워엔 Amount(1/2)로 전달되고 분모는
    /// 항상 2 — 표시와 실제 지급이 갈라지지 않게 한 값에서 파생시킨다.</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("pct", 50) };
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= LoanService.EffectiveTallyCost(this, Owner);

    public override int MaxUpgradeLevel => 1;   // upgrade = 절반 보조 → 전액 보조

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/interest_support_plus.png"
                   : "res://Sts2DebtLoan/card_art/interest_support.png";
    public override string BetaPortraitPath => PortraitPath;

    public InterestSupportCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<InterestSupportPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null);   // Amount/2 = 보조 비율
        await LoanService.SpendTally(Owner, LoanService.EffectiveTallyCost(this, Owner));   // spend the 영수증 cost
    }

    /// <summary>이자 지원+ : 납부액의 <b>절반</b> 보조 → <b>전액</b> 보조. 납부가 20골드니 강화하면 엔진을 돌리는
    /// 골드 비용이 <b>0</b>이 된다 — 빚은 계속 줄어드는데 지갑은 그대로다.
    /// ★예전 강화는 선천성이었는데 사실상 죽은 강화였다: 영수증 1을 무는데 영수증은 전투 시작 시 0이라, 첫 턴 손에
    /// 있어도 납부를 한 번 하기 전엔 못 낸다. 셋의 강화 정체성을 갈랐다 — 명세서+는 카드, 자본 타격+는 화력,
    /// 이자 지원+는 골드 회수율.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("pct", out var v)) { v.BaseValue = 100; v.WasJustUpgraded = true; }
    }
}
