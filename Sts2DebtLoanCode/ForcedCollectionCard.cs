using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // CreatureCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 강제 징수 (Forced Collection) — the payload the 신용 불량 (Bad Credit) generator spawns each turn once your
/// debt hits ~30 rooms. Unplayable. At the end of your turn, if it's still in your hand, the collector takes
/// its cut IN BLOOD AND GOLD: you lose HP (unblockable) AND a chunk of the loan's PRINCIPAL is written off —
/// then the card is exhausted. Both scale with its level (0..3), which 신용 불량 ratchets up over the fight:
/// L0 = 1 HP / 5 principal, L1 = 2/10, L2 = 4/30, L3 = 8/80. Because it keeps paying down the principal, the
/// spiral self-terminates once the loan is cleared — you pay it off whether you like it or not.
/// </summary>
public sealed class ForcedCollectionCard : CardModel
{
    private static readonly int[] HpByLevel   = { 1, 2, 4, 8 };
    private static readonly int[] PrinByLevel = { 5, 10, 30, 80 };

    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };

    // Custom curse art from the mod pck.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/forced_levy.png";
    public override string BetaPortraitPath => PortraitPath;
    public override bool HasTurnEndInHandEffect => true;

    private int _level;
    /// <summary>Collection level 0..3 — set by BadCreditCard when it spawns this. Drives the HP/principal.</summary>
    internal int Level
    {
        get => _level;
        set { _level = Mathf.Clamp(value, 0, HpByLevel.Length - 1);
              try { var v = DynamicVars; if (v.TryGetValue("hp", out var h)) h.BaseValue = HpLoss; if (v.TryGetValue("principal", out var p)) p.BaseValue = PrincipalPay; } catch { } }
    }
    private int HpLoss => HpByLevel[_level];
    private int PrincipalPay => PrinByLevel[_level];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("hp", HpLoss), new DynamicVar("principal", PrincipalPay) };

    public ForcedCollectionCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Owner?.Creature == null) return;
        // Pay in blood: unblockable/unpowered HP loss (mirrors vanilla BadLuck), then write off principal
        // directly (no gold — the collector takes it out of your hide), and the card is gone afterwards.
        await Cmd.Wait(0.25f);
        await CreatureCmd.Damage(choiceContext, Owner.Creature, HpLoss, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this);
        LoanService.ForceRepayPrincipal(Owner, PrincipalPay);
        await CardPileCmd.RemoveFromCombat(this);   // one collection per card, then gone
    }
}
