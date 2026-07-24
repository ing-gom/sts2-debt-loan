using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar, PowerVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // VigorPower

namespace Sts2DebtLoan;

/// <summary>
/// 집행 (Shakedown) — the token the 추심 (Collections) power slips into your hand each turn. Spend [b]1[/b]
/// 영수증 (Receipt) to gain [b]{VigorPower}[/b] 활력 (Vigor) — a ONE-SHOT boost to your next attack, then gone.
/// Exhausts (no Ethereal — it lingers until you can afford it). The receipt cost makes it compete with
/// 정산/청구서 for the same resource: turn your collections into a sharpened strike instead of block/multi-hit.
/// Playing it is NOT a 납부 (no RecordPayment). Fed at base by 추심 (no upgraded form). Colorless/Event; auto-registered.
/// </summary>
public sealed class ShakedownCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 1;   // costs 1 영수증 (shown as a cost badge, like 가압류's 2)

    // TODO(art): placeholder — reuses 자본 타격 art. Generate a dedicated 집행 sprite before release.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/counterclaim.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int Vigor = 3;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new PowerVar<VigorPower>(Vigor) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.FromPower<VigorPower>() };

    /// <summary>Gray it out unless you actually hold the 영수증 to spend (like 가압류's tally gate).</summary>
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= TallyCost;

    public ShakedownCard() : base(canonicalEnergyCost: 0, CardType.Skill, CardRarity.Event, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<VigorPower>(choiceContext, Owner.Creature, DynamicVars["VigorPower"].IntValue, Owner.Creature, this);
        await LoanService.SpendTally(Owner, TallyCost);   // spend the 1 영수증 (competes with 정산/청구서)
    }
}
