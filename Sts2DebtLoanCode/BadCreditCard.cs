using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 신용 불량 (Bad Credit) — the top-tier Debt curse (deep in debt). 선천성 (Innate) so it lands in your opening
/// hand, 제거불가 (Eternal) so no shop card-removal can purge it, Unplayable. The instant it sits in your hand
/// it AUTO-CONVERTS into the 신용 불량 power (<see cref="BadCreditPower"/>) and Exhausts — from then on the power
/// spawns an escalating 빚쟁이 (Debtor) card every turn. Temporary (re-injected each combat); the power's
/// per-turn ratchet lives on the loan record (reset each combat by the injector), so co-op peers stay in sync.
/// </summary>
public sealed class BadCreditCard : CardModel
{
    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        new[] { CardKeyword.Innate, CardKeyword.Eternal, CardKeyword.Unplayable };

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/bad_credit.png";
    public override string BetaPortraitPath => PortraitPath;

    private bool _applied;

    public BadCreditCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Auto-apply: the first turn-start it is in your hand, convert to the 신용 불량 power and Exhaust.
    /// Guarded (<see cref="_applied"/>) so it fires exactly once; deterministic per peer (co-op-safe hook).</summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner == null || !ReferenceEquals(player, Owner) || _applied) return;
        if (Pile?.Type != PileType.Hand || Owner.Creature == null) return;
        _applied = true;
        await PowerCmd.Apply<BadCreditPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
        await CardPileCmd.RemoveFromCombat(this);   // exhaust — the power carries on
    }
}
