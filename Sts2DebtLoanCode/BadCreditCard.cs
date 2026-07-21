using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 신용 불량 (Bad Credit) — the top-tier Debt curse (~30 rooms in debt). Unplayable, it lodges in your hand
/// and, every turn it stays there, hands you a <see cref="ForcedCollectionCard"/> (강제 징수) at the loan's
/// current collection level — then ratchets that level up (0→3). So the collections get worse each turn:
/// 1 HP/5 gold → 2/10 → 4/30 → 8/80 of principal. It is the engine of the debt spiral, and it self-
/// terminates: every collection writes off principal, so once the loan is cleared the spawns stop.
/// The level lives on the loan record (reset to 0 at each combat start by the injector), and the spawn runs
/// in the awaited AfterPlayerTurnStart hook (setup-end = the co-op-safe injection point) off deterministic
/// per-loan state, so both peers spawn an identical card. Temporary (gone at combat end).
/// </summary>
public sealed class BadCreditCard : CardModel
{
    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };

    public BadCreditCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner == null || !ReferenceEquals(player, Owner)) return;
        if (Pile?.Type != PileType.Hand) return;                 // only while it actually clogs the hand
        var combat = Owner.Creature?.CombatState;
        if (combat == null) return;
        var rec = LoanService.For(Owner);
        if (rec == null || !rec.Active || rec.Principal <= 0) return;

        int level = rec.CollectionLevel;
        var forced = combat.CreateCard<ForcedCollectionCard>(Owner);
        if (forced is ForcedCollectionCard fc)
        {
            fc.Level = level;
            await CardPileCmd.AddGeneratedCardToCombat(forced, PileType.Hand, Owner, CardPilePosition.Bottom);
        }
        rec.CollectionLevel = Math.Min(3, level + 1);            // deterministic ratchet, same on both peers
    }
}
