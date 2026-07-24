using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // CalculationBaseVar, CalculationExtraVar, CalculatedBlockVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 정산 (Settlement) — a Skill (event pool). Gain [b]{CalculatedBlock}[/b] Block: [b]{CalculationExtra}[/b] for
/// each 납부 (Payment) you've made this combat. The more you've paid down your debt this fight, the bigger the
/// wall. Block can't be dealt "per hit" like 청구서's damage, so this shows the computed TOTAL live — Mirage /
/// Demonic Shield's CalculatedBlockVar pattern (base 0 + extra {CalculationExtra} × payments). The multiplier
/// is evaluated at render, so the block number tracks payments with no preview patch. Upgraded it drops to 0
/// energy. Exhausts. Colorless/Event; auto-registered. (Defensive twin of 청구서, which multi-hits damage.)
/// </summary>
public sealed class SettlementCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => -1;   // X: spends the WHOLE 납부 실적, block scales with it

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override bool GainsBlock => true;

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/settlement_plus.png"
                   : "res://Sts2DebtLoan/card_art/settlement.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int BlockPerPayment = 4;

    // {CalculationExtra} = block PER payment; {CalculatedBlock} = live total = 0 + {CalculationExtra} × payments.
    // The multiplier is evaluated at render time (only in-combat; 0 in the library), so the block number on the
    // face tracks payments automatically — same as Mirage scaling off enemy Poison.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BlockPerPayment),   // guaranteed base block → never a dead card at 0 납부 실적
        new CalculationExtraVar(BlockPerPayment),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier((CardModel card, Creature? _) => LoanService.PaymentsThisCombat(card.Owner)),
    };

    // Hover: explain the 납부 (Payment) count this scales off.
    public SettlementCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int block = (int)DynamicVars.CalculatedBlock.Calculate(cardPlay.Target);
        if (block <= 0) return;
        await CreatureCmd.GainBlock(Owner.Creature, block, DynamicVars.CalculatedBlock.Props, cardPlay);
        await LoanService.ConsumePaymentStack(Owner);   // spend the whole 납부 실적 stack (bank → unleash)
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
