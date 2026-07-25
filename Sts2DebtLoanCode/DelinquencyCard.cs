using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // CurseCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 연체 (Delinquency) — the 2nd-tier Debt curse (injected once you've been in debt ~10 rooms). Unplayable, and
/// each time it is DRAWN into your hand it applies [b]Vulnerable[/b] 1 to you (you take 50% more damage). The draw
/// trigger lives in <see cref="DelinquencyDrawPatch"/> (a Harmony postfix on CardModel.InvokeDrawn), NOT here:
/// combat cards are CLONED via ToMutable(), so a ctor-bound <c>Drawn</c> event handler points at the ORIGINAL
/// (Owner=null) instance and never fires for the in-combat copy — the patch keys off the real drawn instance
/// (__instance) instead, which is clone-safe. Uses the game's NATIVE VulnerablePower (standard icon, reliably
/// resolves on enemy attacks — unlike a card-local ModifyDamageMultiplicative, which only fired while in HAND and
/// so did nothing on the enemy's turn: the reported "damage lands unchanged" bug). Temporary (gone at combat end).
/// Auto-registered; localization injected by LocInjectionPatch.
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

    /// <summary>Vulnerable applied each time this card is drawn into hand (see <see cref="DelinquencyDrawPatch"/>).</summary>
    internal const int VulnerableOnDraw = 1;

    public DelinquencyCard() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }
}
