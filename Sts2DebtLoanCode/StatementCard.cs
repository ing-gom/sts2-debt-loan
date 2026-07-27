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
/// 명세서 (Statement) — a Power card (event pool). Play it and, for the rest of combat, every 납부 (Payment) you make
/// draws you a card — the payment engine's card-advantage option. 1 energy + 2 영수증 to install; the upgrade drops
/// the energy to 0 and doubles the draw to 2. Colorless/Event; auto-registered.
/// </summary>
public sealed class StatementCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 2;

    /// <summary>{draw} = 납부 1회당 뽑는 장수(기본 1, 명세서+ 2). 설명이 이 값을 읽는다.</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("draw", 1) };   // costs 2 영수증 to install (card-advantage engine)
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= TallyCost;

    public override int MaxUpgradeLevel => 1;   // upgrade = 1코 → 0코 + 납부당 드로우 2

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/statement_plus.png"
                   : "res://Sts2DebtLoan/card_art/statement.png";
    public override string BetaPortraitPath => PortraitPath;

    public StatementCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<StatementPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null);   // Amount = 뽑는 장수 (환급의 티어 전달 방식과 동형)
        await LoanService.SpendTally(Owner, TallyCost);   // spend the 영수증 cost
    }

    /// <summary>명세서+ : 1코 → [b]0코[/b] + 납부마다 카드를 [b]2[/b]장(장수는 적용된 파워의 Amount로 전달 — 환급과 동형).
    /// ★예전 강화는 선천성이었는데 <b>사실상 죽은 강화</b>였다: 이 카드는 영수증 2를 물고 영수증은 전투 시작 시
    /// 0이라, 첫 턴에 손에 들어와도 낼 수가 없다.
    /// 강화가 둘을 한꺼번에 주는 이유 = 이 카드의 진짜 관문은 에너지가 아니라 <b>영수증 2</b>다. 영수증 2를 모으려면
    /// 납부를 두 번 해야 하고, 그 두 턴 동안 같은 에너지를 정산/청구서가 이미 쓰고 있다. 0코가 되면 "영수증이 찬 그
    /// 턴에 곧바로 설치"가 가능해져 관문 하나가 사라지고, 드로우 2가 설치 지연분을 회수한다.
    /// 자체 균형: 드로우가 늘면 덱에 섞인 빚 저주도 같이 딸려 나온다.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
        if (DynamicVars.TryGetValue("draw", out var v)) { v.BaseValue = 2; v.WasJustUpgraded = true; }
    }
}
