using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, PileType
using MegaCrit.Sts2.Core.Entities.Players;            // Player
using MegaCrit.Sts2.Core.Models;                      // CardModel

namespace Sts2DebtLoan;

/// <summary>
/// State + rule for the 차압 (Seizure) card-type lock. Tracks the first card type each player played this
/// turn (set by <see cref="SeizureCard"/> on the first play, cleared at turn start), and answers whether a
/// given card is blocked: only when the owner has a Seizure card in hand, a first-type is already set, and
/// this card's type differs. Everything runs off lockstep combat events (card plays / turn starts), so both
/// co-op peers compute the same lock.
/// </summary>
internal static class SeizureLock
{
    // Keyed by the Player instance; entries live only for the current turn (cleared each turn start).
    private static readonly Dictionary<Player, CardType> _firstType = new();

    internal static void OnTurnStart(Player? p) { if (p != null) _firstType.Remove(p); }

    internal static void OnCardPlayed(Player? p, CardModel? card)
    {
        if (p == null || card == null) return;
        if (!_firstType.ContainsKey(p)) _firstType[p] = card.Type;   // lock to the first type played this turn
    }

    /// <summary>True if the type-lock should block playing <paramref name="card"/> right now.</summary>
    internal static bool IsBlocked(CardModel? card)
    {
        var p = card?.Owner;
        if (p == null) return false;
        if (!_firstType.TryGetValue(p, out var locked)) return false;   // nothing played yet this turn → free
        if (card!.Type == locked) return false;                          // matches the locked type → allowed
        return HasSeizureInHand(p);                                      // otherwise blocked only if Seizure is in hand
    }

    private static bool HasSeizureInHand(Player p)
    {
        var hand = PileType.Hand.GetPile(p);
        if (hand?.Cards == null) return false;
        foreach (var c in hand.Cards) if (c is SeizureCard) return true;
        return false;
    }
}
