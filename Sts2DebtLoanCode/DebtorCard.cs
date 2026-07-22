using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd, CreatureCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 빚쟁이 (Debtor) — the escalating collector the 신용 불량 (Bad Credit) power drops into your hand EVERY turn once
/// you're deep in debt. Costs 1 (0 when upgraded). PLAY it to make a [gold]{gold}[/gold]-gold 납부 (Payment) —
/// you pay the gold. LEAVE it in hand and, at turn end, the collector takes it in BLOOD instead: lose
/// [b]{hp}[/b] HP and it STILL makes the {gold}-gold Payment (no gold needed), then Exhausts. Either path is a
/// 납부, so it amortizes the loan and fires your payment-reactive powers (납부 혜택 → Plating, 환급 → a card).
/// Its gold + HP scale with the level 신용 불량 ratchets up over the fight (every 3rd turn): gold = 20 + 10·level,
/// HP = 2 + 2·level. Curse / temporary; Exhausts.
/// </summary>
public sealed class DebtorCard : CardModel
{
    private const int BaseGold = 20, GoldPerLevel = 10, BaseHp = 2, HpPerLevel = 2;

    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 1;   // 빚쟁이 vs 빚쟁이+ (0-cost)
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };
    public override bool HasTurnEndInHandEffect => true;

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/debtor.png";
    public override string BetaPortraitPath => PortraitPath;

    private int _level;
    /// <summary>Escalation level (set by <see cref="BadCreditPower"/> when it spawns this) — drives gold + HP.</summary>
    internal int Level
    {
        get => _level;
        set
        {
            _level = Mathf.Max(0, value);
            try { var v = DynamicVars; if (v.TryGetValue("gold", out var g)) g.BaseValue = Gold; if (v.TryGetValue("hp", out var h)) h.BaseValue = Hp; }
            catch { }
        }
    }
    private int Gold => BaseGold + GoldPerLevel * _level;
    private int Hp   => BaseHp + HpPerLevel * _level;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("gold", Gold), new DynamicVar("hp", Hp) };

    // Hover: explain the 납부 (Payment) it makes.
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { DebtLoanHoverTips.Payment() };

    public DebtorCard() : base(canonicalEnergyCost: 1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Gold gate: only playable when you can actually pay the gold (like 빚 독촉). Broke → leave it and
    /// pay in blood at turn end instead.</summary>
    protected override bool IsPlayable => Owner != null && (int)Owner.Gold >= Gold;

    /// <summary>Play: pay {gold} gold as a Payment; the Exhaust keyword removes it.</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        int pay = Mathf.Min(Gold, (int)Owner.Gold);
        if (pay > 0) await PlayerCmd.LoseGold(pay, Owner);
        await LoanService.RecordPayment(Owner, choiceContext, Gold);   // 납부: amortize + counter + fire payment powers
    }

    /// <summary>Left in hand at turn end: pay in blood — lose {hp} HP, still make the {gold} Payment, then gone.</summary>
    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Owner?.Creature == null) return;
        await CreatureCmd.Damage(choiceContext, Owner.Creature, Hp, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this);
        await LoanService.RecordPayment(Owner, choiceContext, Gold);   // paid in HP, not gold
        await CardPileCmd.RemoveFromCombat(this);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
