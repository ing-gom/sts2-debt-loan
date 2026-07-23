using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;                                          // Mathf
using MegaCrit.Sts2.Core.Commands;                    // PlayerCmd, CreatureCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // IHoverTip
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 강제 징수 (Forced Collection) — the escalating collector the 신용 불량 (Bad Credit) power drops into your hand
/// EVERY turn once you're deep in debt. It is UNPLAYABLE — there is no dodging it. At the END OF YOUR TURN the
/// collector takes its cut: if you can afford it you make a [gold]{gold}[/gold]-gold 납부 (Payment); if you're
/// short on gold it takes the same Payment out of your HIDE instead — lose [b]{hp}[/b] HP — then the card
/// leaves combat (no Exhaust/Ethereal keyword; it removes itself). Either path counts as a 납부, so it
/// amortizes the loan and fires your payment-reactive powers
/// (납부 혜택 → Plating, 환급 → a card). Its gold + HP scale with the level 신용 불량 ratchets up over the fight
/// (every 3rd turn): gold = 20 + 10·level, HP = 2 + 2·level. Curse / temporary; Exhausts.
/// </summary>
public sealed class DebtorCard : CardModel
{
    private const int BaseGold = 20, GoldPerLevel = 10, BaseHp = 2, HpPerLevel = 2;

    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;
    // Unplayable ONLY — no Ethereal, no Exhaust keyword. It collects on its own at turn end and then quietly
    // leaves via RemoveFromCombat (a temporary generated card), so it needs neither keyword tag on its face.
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };
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

    // Unplayable curse → no energy cost (mirrors vanilla forced curses; ForcedCollectionCard uses the same -1).
    public DebtorCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Forced collection at TURN END: pay {gold} gold if you can afford it, otherwise pay in blood —
    /// lose {hp} HP. Either way it makes the {gold} Payment (amortizes + fires payment powers), then Exhausts.</summary>
    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Owner?.Creature == null) return;
        if ((int)Owner.Gold >= Gold)
            await PlayerCmd.LoseGold(Gold, Owner);                                     // enough gold → pay in coin
        else
            await CreatureCmd.Damage(choiceContext, Owner.Creature, Hp,                 // broke → pay in blood
                                     ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this);
        await LoanService.RecordPayment(Owner, choiceContext, Gold);   // 납부: amortize + counter + fire payment powers
        await CardPileCmd.RemoveFromCombat(this);                      // one collection per card, then gone
    }
}
