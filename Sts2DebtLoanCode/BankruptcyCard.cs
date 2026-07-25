using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // CardPileCmd, PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, PileType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.Models.Powers;               // StrengthPower
using NativeDebt = MegaCrit.Sts2.Core.Models.Cards.Debt;   // the game's Unplayable Debt curse

namespace Sts2DebtLoan;

/// <summary>
/// 파산 선언 (Declare Bankruptcy) — a Skill. Exhaust EVERY 빚(native <see cref="NativeDebt"/>) card you're
/// holding — permanently (gone from the run deck, not just this fight) — then, with nothing left to lose, gain
/// [b]Strength[/b] equal to how many you wiped, and gain 파산 (Bankruptcy): you can't earn Gold for the rest of
/// combat. Turns the debt clog the debt shop leaves you into an all-in aggression pivot — the more debt you
/// piled up, the bigger the swing. Upgraded (파산 선언+): 0 energy. Colorless/Event; auto-registered.
/// </summary>
public sealed class BankruptcyCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 파산 선언 vs 파산 선언+ (0 energy)

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/bankruptcy.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    public BankruptcyCard() : base(canonicalEnergyCost: 1, CardType.Skill, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;

        // Wipe every native Debt card in play (draw/hand/discard hold the deck's Debt during combat), counting them,
        // then permanently from the run deck. Guarded so a pile hiccup can't abort the card before Bankruptcy applies.
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
            if (Owner.Deck != null)
                foreach (var c in new List<CardModel>(Owner.Deck.Cards))
                    if (c is NativeDebt) await CardPileCmd.RemoveFromDeck(c);
        }
        catch (System.Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] bankruptcy debt-wipe failed: {e.Message}"); }

        // Nothing left to lose: Strength = how many Debt you wiped. Then default → no Gold income this combat.
        if (wiped > 0)
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, wiped, Owner.Creature, null);
        await PowerCmd.Apply<BankruptcyPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
