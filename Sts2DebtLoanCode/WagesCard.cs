using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 품삯 (Wages) — a "work for gold" card the 취업알선 (Job Placement) skill hands out (1 into hand + 2 into the draw
/// pile per play). Play it for free to gain gold — a shift's pay. Base: 0 energy → 15 gold. Upgraded (품삯+, fed by
/// 취업알선+): 0 energy → 25 gold. Exhausts, so the earned wages don't clog the deck. Colorless/Event; auto-registered.
/// </summary>
public sealed class WagesCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 15 → 25 gold (already 0-cost both)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/wages_plus.png"
                   : "res://Sts2DebtLoan/card_art/wages.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    // ★15 → 25 (강화 25 → 35). 취업알선은 영수증 2 + 1에너지 + 빚 20을 내는데, 품삯 3장 중 2장은 뽑을
    // 더미로 가서 짧은 전투에선 안 나온다. 15일 때 4턴 전투 실수령이 순 −5 ~ +10으로 마이너스가 날 수
    // 있었다(같은 영수증 2를 정산에 쓰면 방어도 8이 즉시 들어온다). 25면 +5 ~ +30이 된다.
    // ★3장 생성 구조는 건드리지 않는다 — 레전트의 무기고/창조의 기둥/초질량이 "카드 생성"을 세므로
    // 한 번에 3장을 만드는 것 자체가 이 카드의 천장이다(CHARACTER_SYNERGY.md).
    private int Gold => IsUpgraded ? 35 : 25;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("gold", Gold) };

    public WagesCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        await PlayerCmd.GainGold(Gold, Owner, false);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        DynamicVars["gold"].BaseValue = Gold;      // 25 → 35 (energy stays 0)
    }
}
