namespace Sts2DebtLoan;

/// <summary>
/// Runtime-tunable knobs for the loan mechanic. Populated from ModConfig in
/// <see cref="MainFile.RegisterConfig"/>; read everywhere else. Defaults here are the
/// spec defaults so the mod behaves correctly even before ModConfig initializes.
/// </summary>
internal static class DebtLoanConfig
{
    /// <summary>Hard cap on the total principal a run can borrow (spec: 300).</summary>
    internal static int MaxLoan = 300;

    /// <summary>Floor on a single loan. A loan always advances at least this much (capped by the remaining
    /// borrow room), so being 1 gold short doesn't create a trivial 1-gold debt — you borrow a meaningful
    /// amount and keep the change. Spec: 100.</summary>
    internal static int MinLoan = 100;

    /// <summary>How much you may borrow BEYOND <see cref="MaxLoan"/> (the soft cap) to afford a purchase.
    /// Total borrowing is allowed up to MaxLoan + this (the hard cap), but once your lifetime borrowed
    /// exceeds the soft cap the per-combat Debt-card count starts one higher (2 instead of 1). Spec: 100
    /// (so 300 soft / 400 hard).</summary>
    internal static int OverCapAllowance = 100;

    /// <summary>Absolute hard cap on lifetime borrowing = soft cap + over-cap allowance.</summary>
    internal static int HardCap => MaxLoan + OverCapAllowance;

    /// <summary>Gold each Debt card drains when it triggers = the interest rate.
    /// Spec default matches the vanilla Debt curse (10). Config-adjustable.</summary>
    internal static int InterestPerDraw = 10;

    /// <summary>Share of every Debt-card payment that goes toward paying DOWN the principal (the rest is
    /// interest). At 0.2, a 10-gold drain retires 2 gold of the loan and 8 counts as interest — so the debt
    /// slowly amortizes and the shop repay cost shrinks over time.</summary>
    internal static double PrincipalRepayShare = 0.2;

    /// <summary>Highest act (0-based) where the merchant still lends: 0 = Act 1 only (default), 1 = through
    /// Act 2, 2 = through Act 3. Compared against <c>RunState.CurrentActIndex</c>.</summary>
    internal static int MaxLoanActIndex = 0;

    // Debt-card schedule: (roomsSinceLoan, cardsInjectedPerCombat). Taking the loan injects 1 immediately
    // (room 0); every 10 further rooms adds one, capped at 3. These cards are injected fresh into the draw
    // pile at the START of each combat (temporary — they vanish at combat end), NOT added to the deck.
    internal static readonly (int Room, int Cards)[] Schedule =
    {
        (0, 1),
        (10, 2),
        (20, 3),
    };

    /// <summary>Target Debt-card count for a given rooms-since-loan count.</summary>
    internal static int TargetDebtCards(int roomsSinceLoan)
    {
        int target = 0;
        foreach (var (room, cards) in Schedule)
            if (roomsSinceLoan >= room) target = cards;
        return target;
    }
}
