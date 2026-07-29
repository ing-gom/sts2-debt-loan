using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay, PileType
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // CalculationBaseVar, ExtraDamageVar, CalculatedDamageVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 부실채권 (Bad Debt) — 1코 Attack, 소멸. <b>손에 든 저주 1장당</b> 피해 <see cref="DamagePerCurse"/>.
///
/// 이 카드의 의의는 수치가 아니라 <b>부호를 뒤집는 것</b>이다. 지금까지 티어 저주(연체 → 차압 → 신용 불량 →
/// 강제 징수)는 순수 페널티였다: 빚이 커질수록 손패가 막혀 게임이 답답해지기만 하고, 그 답답함에 대한 보상이
/// 없었다. 이 카드가 들어오면 <b>막힌 손패 자체가 탄약</b>이 된다 — 빚 빌드가 "버티다 죽는다"에서
/// "쌓을수록 한 방이 커진다"로 바뀐다.
///
/// <code>
///   손의 저주   0    1    2    3    4
///   피해        5   10   15   20   25      (1코 중앙 8 / Q3 10 / max 30)
/// </code>
/// 저주 0장에서도 5는 나오므로 완전한 죽은 패는 아니지만, 그때는 대출 강타(14)가 명백히 낫다 — 이 카드를
/// 집는 이유는 빚을 안고 갈 때뿐이어야 한다.
///
/// ★★<b>저주를 소모하지 않는다 — 세기만 한다.</b> 이건 타협이 아니라 필수 안전장치다. 티어 저주는 매 전투
/// 재주입되므로, 소모하게 만드는 순간 <b>무한 연료</b>가 된다. 같은 지뢰를 이미 밟아서 돌려막기·차환은
/// 소모 대상을 네이티브 빚 <b>한 종류로만</b> 좁혀 놨다(LoanService.IsDebtCurseCard 참조). 여기서 그 제한을
/// 우회하면 안 된다.
///
/// <see cref="BankruptcyCard"/>(파산 선언)와 겹치지 않는다: 저쪽은 <i>네이티브 빚만</i> 소멸시켜 힘으로 바꾸고
/// 그 전투의 골드를 막는 큰 스윙이고, 이쪽은 <i>모든 저주를</i> 세기만 하며 소멸도 페널티도 없는 매 전투용
/// 공격이다.
///
/// 손패는 피어마다 다르지 않고(같은 덱·같은 드로우 순서) 피해는 정상 경로로 해결된다 → co-op 안전.
/// Colorless/Event; 자동 등록.
/// </summary>
public sealed class BadDebtCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 부실채권 vs 부실채권+ (저주당 5 → 8)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/bad_debt.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int BaseDamage = 5;
    private const int DamagePerCurse = 5, DamagePerCurseUpgraded = 8;

    private static int PerCurseFor(CardModel card) => card.IsUpgraded ? DamagePerCurseUpgraded : DamagePerCurse;

    /// <summary>손에 든 저주 장수. <b>이 카드 자신은 저주가 아니므로</b> 제외 걱정이 없다. CANONICAL 모델
    /// (도감·상점 미리보기)에서는 <c>Owner</c> 가 던지므로 0 으로 떨어뜨린다.</summary>
    private static int CursesInHand(CardModel card)
    {
        try
        {
            var pile = PileType.Hand.GetPile(card.Owner);
            if (pile?.Cards == null) return 0;
            return pile.Cards.Count(c => c.Type == CardType.Curse);   // ★CardModel 의 프로퍼티 이름은 Type 이다(CardType 아님)
        }
        catch { return 0; }
    }

    // damage = base(5) + extra(perCurse) × 손의 저주 수. RENDER 시점 평가라 저주를 뽑는 순간 얼굴 숫자가 오른다.
    // ★extra 는 반드시 ExtraDamageVar 여야 한다 — CalculatedDamageVar 가 GetExtraVar 를 오버라이드해
    // DynamicVars.ExtraDamage 를 읽기 때문에, CalculationExtraVar 를 쓰면 피해가 조용히 base 만 남는다
    // (레버리지 주석의 같은 함정. 방어도 계열인 담보는 반대로 CalculationExtra 가 맞다).
    // ★extra 를 1 로 두고 **비율까지 곱셈기에 접는다**(레버리지와 같은 수법). ExtraDamageVar 는 생성 시점에
    // 값이 굳고 UpgradeBy 가 없어서, 강화로 저주당 피해를 바꾸려면 이 방법뿐이다 — 곱셈기 람다는 카드
    // 인스턴스를 받으므로 IsUpgraded 를 라이브로 읽는다. 덕분에 OnUpgrade 본문도 필요 없다.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(1),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier((CardModel card, Creature? _) => PerCurseFor(card) * CursesInHand(card)),
    };

    /// <summary>{amount} = 지금 이 카드를 내면 나오는 **실제 수치**. ★<c>{Calculated*}</c> 를 쓰면 안 된다 —
    /// 엔진의 <c>CalculatedVar.Calculate</c> 가
    ///   <c>num = (CombatManager.IsInProgress &amp;&amp; card.CombatState != null) ? multiplier(...) : 0</c>
    /// 이라 <b>전투 밖에서는 곱셈기를 아예 호출하지 않고 0 을 돌려준다</b>. 그러면 빚 상점·덱 화면에서
    /// "현재 0" 이 떠서, 카드를 사는 바로 그 순간에 가장 중요한 숫자가 거짓말을 한다. 직접 계산해 주입하면
    /// 전투 안팎 모두 정확하다.</summary>
    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("per", PerCurseFor(this).ToString());
        description.Add("amount", (BaseDamage + PerCurseFor(this) * CursesInHand(this)).ToString());
    }

    public BadDebtCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || cardPlay.Target == null) return;
        await DamageCmd.Attack(DynamicVars.CalculatedDamage).FromCard(this)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

}
