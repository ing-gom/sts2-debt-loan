using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 차압 (Seizure) — the 3rd-tier Debt curse (~20 rooms in debt). Unplayable, it clogs your hand and, while
/// there, LOCKS you to a single card type per turn: the FIRST card type you play that turn is the only type
/// you may keep playing (play a Skill and you're stuck playing Skills; play an Attack and you're stuck with
/// Attacks). Combos and flexible turns are seized. The lock itself is enforced globally by
/// <see cref="SeizureLockPatch"/>; this card just tracks the first-played type each turn (and resets it at
/// turn start) — but only while it is in a combat pile, i.e. exactly when the lock should apply.
/// Temporary (gone at combat end). Auto-registered; localization injected by LocInjectionPatch.
/// </summary>
public sealed class SeizureCard : CardModel
{
    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };

    // Custom curse art from the mod pck.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/seizure.png";
    public override string BetaPortraitPath => PortraitPath;

    public SeizureCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>Record the first card type the owner plays this turn (idempotent per turn).</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        SeizureLock.OnCardPlayed(cardPlay.Card?.Owner, cardPlay.Card);
        await Task.CompletedTask;
    }

    /// <summary>Clear the lock at the start of each turn so the next turn's first-played type re-locks fresh.</summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        SeizureLock.OnTurnStart(player);
        await Task.CompletedTask;
    }
}
