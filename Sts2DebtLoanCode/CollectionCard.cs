using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization;                // LocString
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 추심 (Collections) — a Power card (event pool), the OFFENSIVE counterpart to 환급 (Refund). Play it and, for
/// the rest of combat, the start of each of your turns adds a 집행 (Shakedown) card to your hand — a 0-cost token
/// that spends 1 영수증 (Receipt) for 활력 (Vigor, a one-shot next-attack boost). Unlike 환급's free block stream,
/// this has a hard receipt cost and no scaling, so it turns collections into offense without runaway. Costs
/// [b]2[/b] energy; 추심+ drops it to [b]1[/b]. Colorless/Event; auto-registered.
/// </summary>
public sealed class CollectionCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 추심 (2 energy) vs 추심+ (1 energy)

    // TODO(art): placeholder — reuses 자본 타격 art. Generate dedicated 추심 sprites before release.
    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/counterclaim_plus.png"
                   : "res://Sts2DebtLoan/card_art/counterclaim.png";
    public override string BetaPortraitPath => PortraitPath;

    /// <summary>Inject {card} = the localized 집행 (Shakedown) name this feeds. The fed token has no upgraded form
    /// (the upgrade is on THIS card's energy cost), so no "+" is appended.</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("card", new LocString("cards", "SHAKEDOWN_CARD.title").GetFormattedText());
    }

    // Hover: explain the 납부→영수증 it reads, AND preview the 집행 card it feeds each turn. (집행 grants Vigor via a
    // PowerVar — safe to preview out of combat, unlike a CalculatedDamage card.)
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            var tips = new List<IHoverTip> { DebtLoanHoverTips.Payment() };
            tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<ShakedownCard>(false));
            return tips;
        }
    }

    public CollectionCard() : base(canonicalEnergyCost: 2, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<CollectionPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 2 → 1
    }
}
