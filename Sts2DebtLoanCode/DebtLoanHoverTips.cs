using MegaCrit.Sts2.Core.HoverTips;      // HoverTip, IHoverTip
using MegaCrit.Sts2.Core.Localization;   // LocString

namespace Sts2DebtLoan;

/// <summary>Shared hover tips for DebtLoan cards.</summary>
internal static class DebtLoanHoverTips
{
    /// <summary>The 납부 (Payment) keyword tooltip — what happens to the gold a Payment moves (50% toward the
    /// loan's principal, 50% interest). Shown on every card that makes or reacts to a 납부, so hovering it
    /// explains the term. Loc = DEBT_PAYMENT.* in the "relics" table (injected by LocInjectionPatch).</summary>
    internal static IHoverTip Payment() =>
        new HoverTip(new LocString("relics", "DEBT_PAYMENT.title"), new LocString("relics", "DEBT_PAYMENT.description"));
}
