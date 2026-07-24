using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay, CardMultiplayerConstraint
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 대납 (Bailout) — a MULTIPLAYER-ONLY 1-cost (0 upgraded) colorless card. When a teammate leaves a 납부 (Payment)
/// card UNPAID (it Ethereal-exhausts for nothing), the richest teammate who can afford it is handed one of these
/// (see <see cref="LoanService.GrantBailoutForMissedPayment"/>) — Ethereal+Exhaust, a fleeting chance to help.
/// Target the indebted ally and pay <see cref="BailoutGold"/> gold OUT OF YOUR OWN pocket to make a 납부 ON THEIR
/// BEHALF (their 영수증 tally accumulates, their payment powers fire, and it settles their loan mid-combat if it
/// clears — see <see cref="LoanService.ApplyBailout"/>).
///
/// Ally targeting is native + lockstep: <see cref="TargetType.AnyAlly"/> runs the game's target-selection UI on the
/// player who plays it, and the chosen ally's CombatId rides the play action to every peer, so both peers resolve
/// the same debtor and apply the same write-down — no custom sync (identical path to any AnyEnemy card). Unplayable
/// in singleplayer (no living allies), which is correct: this is a co-op tool. Colorless pool (avoids the
/// MockCardPool "You monster!" getter). Auto-registered, but NEVER added to any shop/reward pool — only 성실 납부
/// grants it in co-op — so it can't be drafted.
/// </summary>
public sealed class BailoutCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    /// <summary>Co-op only — never appears in singleplayer pools/rewards.</summary>
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;

    /// <summary>Gold the caster pays out of pocket, knocked off the targeted ally's owed principal.</summary>
    public const int BailoutGold = 20;

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/diligent_payment.png";   // TODO: dedicated 대납 art
    public override string BetaPortraitPath => PortraitPath;

    // Ethereal = vanishes if not used this turn (a fleeting chance to cover your ally); Exhaust = gone once played.
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal, CardKeyword.Exhaust };

    public override int MaxUpgradeLevel => 1;   // 대납 (1코) → 대납+ (0코)

    public BailoutCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.AnyAlly) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var debtor = cardPlay.Target.Player;
        if (Owner == null || debtor == null) return;
        await LoanService.ApplyBailout(choiceContext, Owner, debtor, BailoutGold);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
