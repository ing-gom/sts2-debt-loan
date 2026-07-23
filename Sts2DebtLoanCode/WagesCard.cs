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
/// 품삯 (Wages) — a "work for gold" card the 취업알선 (Job Placement) power slips into your hand each turn (like
/// 빚 독촉 from 독촉장 / 성실 납부 from 환급). Spend the energy — you're putting in a shift mid-fight — and gain
/// gold. Base: 1 energy → 15 gold. Upgraded (품삯+, fed by 취업알선+): 0 energy → 20 gold. Exhausts.
/// Colorless/Event; auto-registered.
/// </summary>
public sealed class WagesCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 15 → 20 gold (and 1 → 0 energy)

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/wages_plus.png"
                   : "res://Sts2DebtLoan/card_art/wages.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private int Gold => IsUpgraded ? 20 : 15;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("gold", Gold) };

    public WagesCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        await PlayerCmd.GainGold(Gold, Owner, false);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);                  // 1 → 0
        DynamicVars["gold"].BaseValue = Gold;      // 15 → 20
    }
}
