using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // CalculationBaseVar, CalculationExtraVar, CalculatedBlockVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 담보 (Collateral) — 레버리지의 <b>방어 짝</b>. 남은 원금 <see cref="DivisorBase"/> 골드마다 방어도 1.
///
/// WHY THIS EXISTS. 이 세트는 오랫동안 <b>갚기에만</b> 보상을 걸고 있었다: 영수증(=납부 횟수)에 비례해 강해지는
/// 카드가 10장인데, 원금(=안 갚은 만큼)에 비례하는 건 <see cref="LeverageCard"/> 하나뿐이었다. 저주 티어
/// (연체 → 차압 → 신용 불량)·상점 할증·HP 압박이라는 <b>대가는 이미 다 지불돼 있는데 보상이 없어서</b>,
/// "빚을 안고 간다"가 전략이 아니라 그냥 손해였다. 창작마당 소개글이 내건 "언제 털 것인가"가 성립하려면
/// 버티는 쪽에도 길이 있어야 한다.
///
/// 공격(레버리지)만으로는 빌드가 안 된다 — 저주로 손패가 막히는 쪽을 버텨낼 생존 축이 없으면 원금을 키울수록
/// 그냥 죽는다. 그래서 이 카드는 비소멸(레버리지와 대칭)이고, 매 턴 낼 수 있게 1코다.
///
/// 수치 (본편 548장 기준: 1코 방어도 중앙 6 / Q3 8 / max 15):
/// <code>
///   principal   300    550(표준)   900(극단)
///   block         6         12          20
/// </code>
/// 표준 원금에서 Q3 를 넘고 max 는 안 넘는다. 900 에서 max 를 넘는 건 저주 4종을 안고 가는 대가다.
///
/// 강화는 <b>비율 인하</b>(45 → 34)지 코스트 인하가 아니다 — 레버리지 주석의 논리와 같다. 1코를 0코로 만들면
/// 턴당 처리량이 배가 되어 무상한 스케일러에서 복리가 된다.
///
/// 원금은 모든 피어가 동일하게 읽는 런 값이고 방어도는 정상 경로로 해결된다 → co-op 안전. 원금 0이면 0이고
/// (자연 브레이크), ★청산해도 카드는 덱에 남는다 — 재대출하면 다시 살아난다. Colorless/Event; 자동 등록.
/// </summary>
public sealed class CollateralCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 담보 vs 담보+ (45 → 34 골드당 1; 코스트는 그대로 1)

    public override bool GainsBlock => true;

    // 한 장의 초상을 두 형태가 공유한다(레버리지·돌려막기·차환의 관례) — 강화는 대상이 아니라 비율을 바꾼다.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/collateral.png";
    public override string BetaPortraitPath => PortraitPath;

    // 키워드 없음: 매 턴 낼 수 있어야 '버티는 빌드'가 성립한다. (소멸 없음 — 레버리지와 대칭)

    private const int DivisorBase = 45, DivisorUpgraded = 34;   // 방어도 1당 원금 골드

    private static int DivisorFor(CardModel card) => card.IsUpgraded ? DivisorUpgraded : DivisorBase;

    /// <summary>원금 ÷ 비율. try/catch 는 <b>필수</b> — 카드 도감·상점 미리보기·보상 화면은 CANONICAL 모델이라
    /// <c>Owner</c> 게터가 <c>CanonicalModelException</c> 을 던진다(런 밖엔 읽을 장부가 없다). 정산/청구서가
    /// 0 영수증에서 0 으로 렌더되는 것과 같은 처리.</summary>
    private static int PrincipalSteps(CardModel card)
    {
        try { return LoanService.PrincipalOf(card.Owner) / DivisorFor(card); }
        catch { return 0; }
    }

    // block = base(0) + extra(1) × (원금 ÷ 비율).
    // ★★곱셈기는 **전투 중에만** 돈다. 엔진(CalculatedVar.Calculate) 이
    //     num = (CombatManager.IsInProgress && card.CombatState != null) ? multiplier(...) : 0
    //   이라 전투 밖에서는 람다를 호출조차 하지 않고 0 으로 강제한다 → 덱 화면·상점 미리보기·카드 도감의
    //   카드 얼굴은 항상 base(=0) 로 보인다. 정상이다. 이걸 모르고 전투 밖에서 수치를 재면 멀쩡한 카드가
    //   깨진 것처럼 보인다(실측 중 이미 배포된 레버리지까지 0 으로 나와 오진할 뻔했다).
    // ★방어도 계열은 damage 계열과 var 삼종이 다르다: CalculatedBlockVar 는 CalculationExtra 를 읽으므로 여기선
    // CalculationExtraVar 가 맞다(레버리지는 ExtraDamageVar 를 써야 했다 — CalculatedDamageVar 가 GetExtraVar 를
    // 오버라이드해 DynamicVars.ExtraDamage 를 읽기 때문). 바꿔 쓰면 값이 조용히 0 이 된다.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(0),
        new CalculationExtraVar(1),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier((CardModel card, Creature? _) => PrincipalSteps(card)),
    };

    /// <summary>{per} = 방어도 1당 남은 상환 금액. 설명이 교환비를 말하고 강화에 따라 바뀌게 한다.</summary>
    /// <summary>{amount} = 지금 이 카드를 내면 나오는 **실제 수치**. ★<c>{Calculated*}</c> 를 쓰면 안 된다 —
    /// 엔진의 <c>CalculatedVar.Calculate</c> 가
    ///   <c>num = (CombatManager.IsInProgress &amp;&amp; card.CombatState != null) ? multiplier(...) : 0</c>
    /// 이라 <b>전투 밖에서는 곱셈기를 아예 호출하지 않고 0 을 돌려준다</b>. 그러면 빚 상점·덱 화면에서
    /// "현재 0" 이 떠서, 카드를 사는 바로 그 순간에 가장 중요한 숫자가 거짓말을 한다. 직접 계산해 주입하면
    /// 전투 안팎 모두 정확하다.</summary>

    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("per", (IsUpgraded ? DivisorUpgraded : DivisorBase).ToString());
        description.Add("amount", PrincipalSteps(this).ToString());
    }

    public CollateralCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int block = (int)DynamicVars.CalculatedBlock.Calculate(cardPlay.Target);
        if (block <= 0) return;   // 빚이 없거나 아주 적으면 아무 일도 없다 — 자연 브레이크
        await CreatureCmd.GainBlock(Owner.Creature, block, DynamicVars.CalculatedBlock.Props, cardPlay);
    }

    // OnUpgrade 본문 없음: 비율은 곱셈기 안에서 IsUpgraded 로 라이브로 읽고 {per} 는 렌더마다 주입된다.
    // 코스트는 의도적으로 그대로 둔다(위 주석 참조).
}
