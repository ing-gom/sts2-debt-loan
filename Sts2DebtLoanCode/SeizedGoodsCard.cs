using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 압류품 (Seized Goods) — 0코 Skill, 소멸. 전투 시작 시 <b>남은 빚의 크기에 따라</b> 드로우 더미에 섞여 들어오는
/// 임시 카드다(<see cref="LoanService.SeizedGoodsFor"/>: 250/500/750 골드 → 1/2/3장). 살 수 없고, 덱에 남지도
/// 않는다 — 이번 전투에서만 존재한다.
///
/// WHY THIS EXISTS. 빚 빌드에는 <b>진행 보상이 없었다</b>. 갚기 쪽은 신용 사다리(누적 상환 300/600/900/1200 +
/// 무한)가 영구 카드·강화·제거를 래칫으로 쌓아 주는데, 빚 쪽은 카드가 세지는 <i>상태</i>뿐이라 "쌓이는 맛"도
/// 목표도 없었다. 이 카드는 그 빈자리를 <b>매 전투 눈에 보이는 보상</b>으로 채운다.
///
/// ★설계 결정 세 가지 (전부 함정을 피하려고 이렇게 된 것):
/// <list type="number">
/// <item><b>수량이 계단, 효과는 고정.</b> 방어도를 빚에 비례시키면 <see cref="CollateralCard"/>(빚 45당 방어도 1)
/// 와 같은 카드가 되어 담보가 사장된다. 수량을 계단으로 두면 둘이 확실히 구별된다.</item>
/// <item><b>방 수가 아니라 금액</b>에 건다. 방 수 기준이면 최소 대출로 천천히 걸어다닌 사람이 900을 안고 버틴
/// 사람과 같은 보상을 받는다 — "미루면 보상"이라는, DOPAMINE_BACKLOG 이 금지한 모양이다. 금액 기준이면
/// 감수한 만큼 받는다. 그리고 <b>비용은 방 수(저주 티어), 보상은 금액</b>으로 갈려서 "얼마나 빌릴까"와
/// "얼마나 끌까"가 서로 다른 결정이 된다.</item>
/// <item><b>저주 타입이 아니다</b>(Skill). 저주로 만들면 <see cref="BadDebtCard"/>(손의 저주 1장당 피해)가
/// 이 카드를 세어 <b>보상이 보상을 키우는</b> 이중 취득이 된다.</item>
/// </list>
///
/// 0코 + 소멸인 이유 = <b>손패 오염 관리</b>. 저주가 이미 최대 3~4장 섞이는데 여기에 보상 카드까지 얹으면
/// 드로우가 전부 주입 카드로 채워져 정작 자기 덱이 안 나온다. 0코라 손을 막지 않고 바로 흘려보낼 수 있다.
///
/// 골드를 주지도, 빚을 깎지도 않는다 — 둘 다 DOPAMINE_BACKLOG 의 금지 항목이다(무한 수도꼭지 / 무조건 감면).
/// 전투 효과로만 환산한다. Colorless/Event; 자동 등록. 상점에서 팔지 않는다(PurchasablePool 에 없음).
/// </summary>
public sealed class SeizedGoodsCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 0;   // 주입 전용 임시 카드 — 강화 대상이 아니다

    public override bool GainsBlock => true;

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/seized_goods.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Block = 8;   // 고정. 스케일은 '수량' 이 담당한다(위 설계 결정 ① 참조)

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("block", Block),
    };

    public SeizedGoodsCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars["block"].BaseValue, ValueProp.Move, cardPlay);
    }
}
