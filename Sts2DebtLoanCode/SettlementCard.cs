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
/// 정산 (Settlement) — a Skill (event pool). Gain Block equal to the number of 납부 (Payments) you've made this
/// combat × 4. The more you've paid down your debt this fight, the bigger the wall. Upgraded it drops to 0
/// energy. Exhausts. Colorless/Event; auto-registered. (Defensive twin of 청구서, which scales damage.)
/// </summary>
public sealed class SettlementCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/settlement_plus.png"
                   : "res://Sts2DebtLoan/card_art/settlement.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int BlockPerPayment = 4;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("mult", BlockPerPayment) };

    public SettlementCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int block = LoanService.PaymentsThisCombat(Owner) * BlockPerPayment;
        if (block > 0)
            await CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Move, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
