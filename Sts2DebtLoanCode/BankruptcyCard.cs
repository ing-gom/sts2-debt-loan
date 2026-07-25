using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.HoverTips;                   // HoverTip, IHoverTip
using MegaCrit.Sts2.Core.Localization;                // LocString
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // StrengthPower
using NativeDebt = MegaCrit.Sts2.Core.Models.Cards.Debt;   // the game's Unplayable Debt curse

namespace Sts2DebtLoan;

/// <summary>
/// 파산 선언 (Declare Bankruptcy) — a Skill. EXHAUST every 빚(native <see cref="NativeDebt"/>) card you're
/// holding THIS COMBAT (they return next fight — this is Exhaust, not deck removal), then, with nothing left to
/// lose, gain [b]Strength[/b] equal to how many you exhausted, and gain 파산 (Bankruptcy): you can't earn Gold for
/// the rest of combat. Turns the debt clog the debt shop leaves you into an all-in aggression pivot — the more debt
/// you piled up, the bigger the swing. Upgraded (파산 선언+): 0 energy. Colorless/Event; auto-registered.
/// </summary>
public sealed class BankruptcyCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 파산 선언 vs 파산 선언+ (0 energy)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/bankruptcy.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    // Hover: explain the 파산 (Bankruptcy) power this card grants — its tooltip wasn't shown at all without this
    // (the card had no ExtraHoverTips). Reads the same "powers" loc the power icon's tooltip uses.
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        new HoverTip(new LocString("powers", "BANKRUPTCY_POWER.title"),
                     new LocString("powers", "BANKRUPTCY_POWER.description")),
    };

    public BankruptcyCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;

        // EXHAUST every native Debt card in play THIS COMBAT only (draw/hand/discard hold the deck's Debt during
        // combat), counting them. We do NOT remove them from the run deck — the description says "Exhaust", so they
        // return next combat. Guarded so a pile hiccup can't abort the card before Bankruptcy applies.
        int wiped = 0;
        try
        {
            var combat = Owner.Creature.CombatState;
            if (combat != null)
                foreach (var pt in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
                {
                    var pile = pt.GetPile(Owner);
                    if (pile == null) continue;
                    foreach (var c in new List<CardModel>(pile.Cards))
                        if (c is NativeDebt) { await CardPileCmd.RemoveFromCombat(c); wiped++; }
                }
        }
        catch (System.Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] bankruptcy debt-wipe failed: {e.Message}"); }

        // Nothing left to lose: Strength = how many Debt you wiped.
        if (wiped > 0)
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, wiped, Owner.Creature, null);
        // 파산: no gold this combat (power's ModifyGoldGained) AND no post-combat reward gold (the flag, read by
        // BankruptGoldBlockPatch — the power is gone once combat ends). Cleared at the next fight's start.
        await PowerCmd.Apply<BankruptcyPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
        LoanService.SetBankrupt(Owner);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
