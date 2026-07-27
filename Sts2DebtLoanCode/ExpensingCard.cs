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
/// 경비 처리 (Expensing) — a Power card (event pool). Play it and, for the rest of combat, every 영수증 (Receipt)
/// cost is [b]{cut}[/b] lower. 1 energy + 2 영수증 to install; 경비 처리+ installs for [b]0[/b] energy.
/// <para>★What it actually fixes: receipt INCOME is hard-capped at 1/turn (독촉장 is the only source) while most
/// payoff cards cost 2 — so every one of them is a two-turn wait. This turns the 2-cost cards into 1-cost, i.e.
/// one payoff card per turn instead of one per two. It pays for itself after the second discounted card.</para>
/// <para>★Why the discount and not "an extra 납부 per turn": an extra payment would multiply ALL FIVE on-payment
/// powers at once (판금·드로우·피해·골드·성실 납부) and become a mandatory pick. Cutting the price relieves the same
/// bottleneck without touching that multiplier.</para>
/// <para>★The upgrade is NOT {cut} 2. At 2 the discount would zero out the entire receipt economy — 명세서/자본
/// 타격/가압류/돌려막기 (all 2) and 집행/이자 지원 (both 1) would all become free, and the resource stops existing.
/// The energy discount keeps the price structure intact.</para>
/// Colorless/Event; auto-registered.
/// </summary>
public sealed class ExpensingCard : CardModel, IUsesPaymentTally
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    /// <summary>2 영수증 to install — deliberately equal to the cards it discounts, so it breaks even on the
    /// second one. Its own cost is discounted too if another 경비 처리 is already up (EffectiveTallyCost).</summary>
    public int TallyCost => 2;
    protected override bool IsPlayable => Owner != null && LoanService.PaymentsThisCombat(Owner) >= LoanService.EffectiveTallyCost(this, Owner);

    public override int MaxUpgradeLevel => 1;   // upgrade = 1코 → 0코

    public override string PortraitPath => "res://Sts2DebtLoan/card_art/expensing.png";
    public override string BetaPortraitPath => PortraitPath;

    /// <summary>{cut} = 영수증 비용 감소량. Carried on the applied power's Amount so the tooltip and
    /// EffectiveTallyCost read the same number.</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("cut", 1) };

    public ExpensingCard() : base(canonicalEnergyCost: 1, CardType.Power, CardRarity.Event, TargetType.None) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null) return;
        // ★Spend BEFORE applying: otherwise this card's own install price is discounted by the very power it is
        // installing, and it would cost 1 instead of the printed 2.
        await LoanService.SpendTally(Owner, LoanService.EffectiveTallyCost(this, Owner));
        await PowerCmd.Apply<ExpensingPower>(choiceContext, Owner.Creature, DynamicVars["cut"].IntValue, Owner.Creature, null);
    }

    /// <summary>경비 처리+ : 1코 → 0코. See the class summary for why the discount stays at 1.</summary>
    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
