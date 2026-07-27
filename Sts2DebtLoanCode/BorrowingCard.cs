using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 차입 (Borrowing) — a Power card (event pool). Play it and, for the rest of combat, you gain [b]{energy}[/b]
/// Energy at the start of each turn. 2 energy + [b]4[/b] 영수증 to install; 차입+ gives 2 Energy per turn.
/// <para>★The 4-영수증 price IS the balance. Receipt income is capped at 1/turn (독촉장 is the only source), so this
/// cannot land before turn 4 even on a perfect draw — and only if you spent nothing else on the way. With 경비 처리
/// up it lands a turn sooner (3). Permanent +1 Energy/turn is boss-relic tier, so it has to cost the engine's whole
/// early output; anything cheaper and every deck takes it.</para>
/// <para>★It also pays for itself twice over: at 2 energy to install it is net-negative on the turn you play it and
/// only breaks even two turns later, which keeps it a commitment rather than a tempo play.</para>
/// The repeatable twin of 어음 (one-shot +2 Energy for 100 principal). Colorless/Event; auto-registered.
/// </summary>
public sealed class BorrowingCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public int TallyCost => 4;   // ★the gate — 4 turns of engine output at the 1/turn income cap
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= LoanService.EffectiveTallyCost(this, Owner);

    public override int MaxUpgradeLevel => 1;   // upgrade = 턴당 에너지 1 → 2

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/borrowing.png";
    public override string BetaPortraitPath => PortraitPath;

    /// <summary>{energy} = 턴 시작 시 얻는 에너지(기본 1, 차입+ 2). Carried on the applied power's Amount, the same
    /// way 명세서 carries its draw count.</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("energy", 1) };

    public BorrowingCard() : base(canonicalEnergyCost: 2, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        await PowerCmd.Apply<BorrowingPower>(choiceContext, Owner.Creature, DynamicVars["energy"].IntValue, Owner.Creature, null);
        await LoanService.SpendTally(Owner, LoanService.EffectiveTallyCost(this, Owner));
    }

    /// <summary>차입+ : 턴당 에너지 1 → 2. The receipt price stays at 4 — the upgrade buys throughput, not access.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        if (DynamicVars.TryGetValue("energy", out var v)) { v.BaseValue = 2; v.WasJustUpgraded = true; }
    }
}
