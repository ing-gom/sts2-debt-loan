using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar, CalculationBaseVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // StrengthPower

namespace Sts2DebtLoan;

/// <summary>
/// 부도 위기 (Default Risk) — 2코 Power. 낼 때 남은 원금 <see cref="DivisorBase"/> 골드마다 <b>힘 1</b>.
///
/// 빚 빌드의 <b>곱셈 축</b>이다. <see cref="LeverageCard"/>(원금÷30 피해)와 <see cref="BadDebtCard"/>(저주당 피해)는
/// 둘 다 힘을 타므로, 이 카드가 들어오는 순간 세 장이 서로를 키운다 — 지금까지 빚 쪽에는 시너지라 부를 게
/// 하나도 없었다(레버리지 단독). 갚기 쪽엔 영수증을 공유하는 10장이 이미 그물처럼 얽혀 있었다.
///
/// ★<b>낼 때 1회 부여</b>이지 매 턴이 아니다. 매 턴 힘을 주면 턴을 끌수록 이득인 무한 엔진이 되어
/// DOPAMINE_BACKLOG 의 금지 목록("턴을 끌수록 이득인 생성 엔진" — 취업알선 파워형의 실패 전례)에 정확히
/// 걸린다. 본편의 Inflame(2코 파워, 힘 2 즉시)이 이 카드의 기준점이고, 표준 원금에서 같은 값이 나온다:
/// <code>
///   principal   250    550(표준)   900(극단)
///   strength      1         2           3
/// </code>
///
/// 강화는 비율 인하(250 → 180)다. 900 원금에서 힘 5가 되지만, 그 원금은 저주 4종을 매 전투 뒤집어쓰는 자리다.
///
/// 원금 0이면 힘 0이라 파워만 붙고 아무 일도 없다 — 갚아버린 뒤엔 죽은 카드가 되는 게 의도다(빚 빌드와
/// 갚기 빌드를 동시에 못 먹게 하는 자연 브레이크). 청산하면 덱에서 쓸려나간다. Colorless/Event; 자동 등록.
/// </summary>
public sealed class DefaultRiskCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 부도 위기 vs 부도 위기+ (250 → 180 골드당 힘 1)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/default_risk.png";
    public override string BetaPortraitPath => PortraitPath;

    private const int DivisorBase = 250, DivisorUpgraded = 180;   // 힘 1당 원금 골드

    private int Divisor => IsUpgraded ? DivisorUpgraded : DivisorBase;

    /// <summary>얻게 될 힘. 카드 도감·상점 미리보기는 CANONICAL 모델이라 <c>Owner</c> 가 던지므로 0 으로 떨어뜨린다
    /// (레버리지/담보와 같은 처리).</summary>
    private int StrengthGain => StrengthGainFor(this);

    /// <summary>부여할 힘. <b>파워 카드라 CalculatedVar 를 쓰지 않으므로 전투 밖에서도 값이 정확하다</b>
    /// (CalculatedVar 는 전투 중에만 곱셈기를 돌린다 — 담보/부실채권 주석 참조). 테스트가 이 공식을 직접
    /// 검증할 수 있게 static 으로 노출한다.</summary>
    internal static int StrengthGainFor(DefaultRiskCard card)
    {
        try { return LoanService.PrincipalOf(card.Owner) / card.Divisor; } catch { return 0; }
    }

    // 카드 얼굴에 라이브 수치를 띄운다 — {amount} 는 지금 내면 얼마를 받는지, {per} 는 교환비.
    // 파워 카드라 Calculated* 계열(피해/방어도 파이프라인)을 쓰지 않고 값만 보여준다.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(0),
    };

    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("per", Divisor.ToString());
        description.Add("amount", StrengthGain.ToString());
    }

    public DefaultRiskCard() : base(canonicalEnergyCost: 2, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int gain = StrengthGain;
        if (gain <= 0) return;   // 빚이 없으면 아무 일도 없다
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, gain, Owner.Creature, null);
    }

    // OnUpgrade 본문 없음: 비율은 IsUpgraded 로 라이브로 읽는다. 코스트 인하 금지 — 2→1 은 처리량이 아니라
    // '한 전투에 두 번' 을 열어주는데, 파워라 어차피 한 번만 낼 수 있으므로 비율 인하가 정직한 강화다.
}
