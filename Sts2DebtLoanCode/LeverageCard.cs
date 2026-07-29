using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardPlay
using MegaCrit.Sts2.Core.Entities.Creatures;          // Creature
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // CalculationBaseVar, CalculationExtraVar, CalculatedDamageVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool
using MegaCrit.Sts2.Core.ValueProps;                  // ValueProp

namespace Sts2DebtLoan;

/// <summary>
/// 레버리지 (Leverage) — an Attack whose damage IS your debt: deal [b]{CalculatedDamage}[/b], one damage per
/// <see cref="DivisorBase"/> gold of principal still owed. No Exhaust, no cap — the number on the card face just
/// tracks the ledger, live, every time you look at it (AshenStrike's CalculatedDamageVar pattern).
///
/// This is the set's missing axis. Everything else treats debt as pure punishment; this is the one card where the
/// ledger is an asset, and it inverts the mod's core loop on purpose: <b>paying the loan down makes it weaker.</b>
/// That tension is safe because the tier curses (연체 → 차압 → 신용 불량) escalate on a room timer, so "just never
/// repay" walks into 강제 징수 every turn. Riding the debt is a real option with a real bill.
///
/// WHY 2 ENERGY. Uncapped + repeatable is a large budget; the cost is where it gets paid. At a typical principal
/// (~550) this deals 18, which sits on the 2-cost curve — at 1 energy the same 18 would tower over 대출 강타
/// (1-cost Exhaust, 14). Reference points at <see cref="DivisorBase"/>=30 / <see cref="DivisorUpgraded"/>=22:
/// <code>
///   principal   120    360    550    900
///   base          4     12     18     30
///   upgraded      5     16     25     40
/// </code>
///
/// WHY THE UPGRADE IS A DIVISOR CUT, NOT A COST CUT. 2→1 energy would double THROUGHPUT (two casts a turn), which
/// on an uncapped scaler compounds; 30→22 is a flat +36% on the same curve. <b>Never make this a 1-cost card.</b>
///
/// PUMP-LOOP CHECK (저당+ / 대출 강타+ drop Exhaust on upgrade and add 30 principal per play): at divisor 30 that
/// is 1 Energy for +1 damage on future 레버리지 plays — strictly worse than spending the same Energy on 레버리지
/// itself, so there is no stalling loop. ⚠️ That inequality flips if the divisor ever drops below ~15; treat 15 as
/// a hard floor when tuning. 어음 (+100 principal) is the intended, honest version of the same interaction.
///
/// Principal is a run value read identically on every peer, and damage resolves through the normal attack path →
/// co-op safe. At principal 0 the damage is 0 (a natural brake), but ★the card STAYS in the deck after you repay
/// — it comes back to life on your next loan. Colorless/Event; auto-registered.
/// </summary>
public sealed class LeverageCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // 레버리지 vs 레버리지+ (divisor 30 → 22; cost stays 2)

    // One portrait for both forms (돌려막기 / 차환's convention) — the upgrade changes the rate, not the subject.
    public override string PortraitPath => "res://Sts2DebtLoan/card_art/leverage.png";
    public override string BetaPortraitPath => PortraitPath;

    // No keywords on purpose: this card is meant to be played every turn you can afford it. (No Exhaust.)

    private const int DivisorBase = 30, DivisorUpgraded = 22;   // gold of principal per 1 damage. FLOOR: 15 (see summary)

    private static int DivisorFor(CardModel card) => card.IsUpgraded ? DivisorUpgraded : DivisorBase;

    /// <summary>The live multiplier: principal ÷ divisor. Wrapped because CANONICAL models (card library, shop
    /// preview, reward screen) throw <c>CanonicalModelException</c> from the <c>Owner</c> getter — outside a run
    /// there is no ledger to read, so those render as 0, exactly like 정산/청구서 do at 0 영수증.</summary>
    private static int PrincipalSteps(CardModel card)
    {
        try { return LoanService.PrincipalOf(card.Owner) / DivisorFor(card); }
        catch { return 0; }
    }

    // damage = base(0) + extra(1) × (principal ÷ divisor), evaluated at RENDER time — so the face number moves the
    // moment the ledger does (a 어음, a 저당, a 납부, a room's interest). Same var trio as AshenStrike.
    // ★ The extra MUST be an ExtraDamageVar, not a CalculationExtraVar: CalculatedVar.Calculate() is
    // `GetBaseVar() + GetExtraVar() × multiplier`, and CalculatedDamageVar OVERRIDES GetExtraVar() to read
    // DynamicVars.ExtraDamage (only the block/plain flavours read CalculationExtra — hence 정산's different trio).
    // With a CalculationExtraVar here the damage silently resolves to 0.
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(0),
        new ExtraDamageVar(1),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier((CardModel card, Creature? _) => PrincipalSteps(card)),
    };

    /// <summary>Inject {per} = the gold-per-damage divisor so the description can state the exchange rate and change
    /// with the upgrade (Description is non-virtual — same arg-injection route as 차환's {card}).</summary>
    /// <summary>{amount} = 지금 이 카드를 내면 나오는 **실제 수치**. ★<c>{Calculated*}</c> 를 쓰면 안 된다 —
    /// 엔진의 <c>CalculatedVar.Calculate</c> 가
    ///   <c>num = (CombatManager.IsInProgress &amp;&amp; card.CombatState != null) ? multiplier(...) : 0</c>
    /// 이라 <b>전투 밖에서는 곱셈기를 아예 호출하지 않고 0 을 돌려준다</b>. 그러면 빚 상점·덱 화면에서
    /// "현재 0" 이 떠서, 카드를 사는 바로 그 순간에 가장 중요한 숫자가 거짓말을 한다. 직접 계산해 주입하면
    /// 전투 안팎 모두 정확하다.</summary>
    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("per", (IsUpgraded ? DivisorUpgraded : DivisorBase).ToString());
        description.Add("amount", PrincipalSteps(this).ToString());
    }

    public LeverageCard() : base(canonicalEnergyCost: 2, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || cardPlay.Target == null) return;
        if ((int)DynamicVars.CalculatedDamage.Calculate(cardPlay.Target) <= 0) return;   // no loan / tiny debt → nothing to swing
        // Pass the VAR (not a plain int) so Strength/Vulnerable and the rest of the damage pipeline apply normally.
        await DamageCmd.Attack(DynamicVars.CalculatedDamage).FromCard(this)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // No OnUpgrade body: the divisor is read live from IsUpgraded inside the multiplier (the lambda gets the card
    // instance), and {per} is injected per-render — so nothing needs mutating here. Cost deliberately unchanged.
}
