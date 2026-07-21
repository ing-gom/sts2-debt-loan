using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 연체 (Delinquency) — the 2nd-tier Debt curse (injected once you've been in debt ~10 rooms). Unplayable,
/// it just sits in your hand and, WHILE it is there, every attack the enemies land on you hits 50% harder —
/// the merchant has put a bounty on you and the monsters are collecting. Unlike Vulnerable it is not a
/// player debuff, so debuff-removal can't shake it; you have to clear the loan. Temporary (gone at combat
/// end). Auto-registered; localization injected by LocInjectionPatch.
/// </summary>
public sealed class DelinquencyCard : CardModel
{
    private static CardPoolModel? _cursePool;
    public override CardPoolModel Pool => _cursePool ??= ModelDb.CardPool<CurseCardPool>();

    public override int MaxUpgradeLevel => 0;

    // Custom curse art from the mod pck.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/overdue.png";
    public override string BetaPortraitPath => PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Unplayable };

    public DelinquencyCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    /// <summary>While this card is in the OWNER's hand, enemy-dealt damage to the owner is multiplied by 1.5
    /// (the collectors hit harder). The hook returns a multiplier (1 = no change). Gated to Hand so it does
    /// nothing while still in the draw pile.</summary>
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (Pile?.Type != PileType.Hand) return 1m;
        if (Owner?.Creature == null || target != Owner.Creature) return 1m;         // only damage TO us
        if (dealer == null || ReferenceEquals(dealer, Owner.Creature)) return 1m;   // only ENEMY-dealt damage
        return 1.5m;
    }
}
