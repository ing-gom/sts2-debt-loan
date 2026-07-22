using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                    // DamageCmd
using MegaCrit.Sts2.Core.Entities.Cards;              // CardType, CardRarity, TargetType, CardKeyword, CardPlay
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // PlayerChoiceContext
using MegaCrit.Sts2.Core.Localization.DynamicVars;    // DynamicVar
using MegaCrit.Sts2.Core.Models;                      // CardModel, CardPoolModel, ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;            // ColorlessCardPool

namespace Sts2DebtLoan;

/// <summary>
/// 청구서 (Invoice) — an Attack (event pool). Deal damage to a single enemy equal to the number of 납부
/// (Payments) you've made this combat × 5 — the bill comes due. Upgraded it drops to 0 energy. Exhausts.
/// Colorless/Event; auto-registered. (Offensive twin of 정산, which scales block.)
/// </summary>
public sealed class InvoiceCard : CardModel
{
    private static CardPoolModel? _pool;
    public override CardPoolModel Pool => _pool ??= ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 1;   // upgrade = 0 energy

    public override string PortraitPath =>
        IsUpgraded ? "res://Sts2DebtLoan/card_art/invoice_plus.png"
                   : "res://Sts2DebtLoan/card_art/invoice.png";
    public override string BetaPortraitPath => PortraitPath;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    private const int DamagePerPayment = 5;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("mult", DamagePerPayment) };

    public InvoiceCard() : base(canonicalEnergyCost: 1, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner?.Creature == null || CombatState == null) return;
        int dmg = LoanService.PaymentsThisCombat(Owner) * DamagePerPayment;
        if (dmg > 0)
            await DamageCmd.Attack(dmg).FromCard(this).Targeting(cardPlay.Target).Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
        EnergyCost.UpgradeBy(-1);   // 1 → 0
    }
}
