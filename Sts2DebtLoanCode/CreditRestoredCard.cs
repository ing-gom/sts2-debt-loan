using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTipFactory, IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // PlatingPower

namespace Sts2DebtLoan;

/// <summary>
/// 신용 회복 (Credit Restored) — the reward for CLIMBING OUT of deep debt. When you fully repay a loan that had
/// escalated to tier 3+ (shop OR mid-combat), this 0-energy Power card is added PERMANENTLY to your deck (tier 4
/// grants the upgraded 신용 회복+). Play it and, for the rest of combat, you gain [b]{plate}[/b] Plating (판금)
/// each turn — a modest, on-theme defensive keepsake for having survived the collections. Base = 3 Plating,
/// upgraded = 5; always 0 energy. Colorless; granted by <see cref="LoanService"/>, never appears in random
/// rewards, and is EXEMPT from the debt-kit sweep so a later loan can't strip it out. Auto-registered.
/// </summary>
public sealed class CreditRestoredCard : CardModel
{
    private const int BasePlate = 3, UpgradedPlate = 5;

    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 신용 회복 (2 Plating) vs 신용 회복+ (3 Plating); both 0-energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/credit_restored_plus.png"
                   : "res://Sts2DebtLoan/card_art/credit_restored.png";
    public override string BetaPortraitPath => PortraitPath;

    // Plating amount lives in a DynamicVar (cloned+cached from CanonicalVars); OnUpgrade rewrites its BaseValue
    // and OnPlay reads it back, so the face + effect both track the upgrade (see the DynamicVar-cache gotcha).
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("plate", BasePlate) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { HoverTipFactory.FromPower<PlatingPower>() };

    public CreditRestoredCard() : base(canonicalEnergyCost: 0, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int plate = DynamicVars.TryGetValue("plate", out var v) ? v.IntValue : BasePlate;
        await PowerCmd.Apply<PlatingPower>(choiceContext, Owner.Creature, plate, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("plate", out var v)) { v.BaseValue = UpgradedPlate; v.WasJustUpgraded = true; }
    }
}
