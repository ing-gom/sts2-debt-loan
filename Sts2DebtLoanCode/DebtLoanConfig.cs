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

    /// <summary>Gold each Debt card drains when it triggers = the interest rate.
    /// Spec default matches the vanilla Debt curse (10). Config-adjustable.</summary>
    internal static int InterestPerDraw = 10;

    /// <summary>Interest ceiling as a multiple of principal; reaching it retires the relic
    /// and clears the Debt cards (spec: 200% ⇒ 2.0).</summary>
    internal static double InterestCapMultiplier = 2.0;

    /// <summary>By default loans are only offered in Act 1. Option to allow every act.</summary>
    internal static bool AllowLoansOutsideAct1 = false;

    /// <summary>Rooms visited (after taking the first loan) before the first Debt card seeps in.
    /// Also the deadline for additional top-up loans. Spec: 14th room ⇒ penalties begin.</summary>
    internal static int PenaltyStartRoom = 14;

    // The escalation schedule: (roomThreshold, totalDebtCards). Crossing a threshold tops the
    // deck up to that many Debt cards. Spec: 14→1, 17→3, 20→5 (capped). Kept as a field so a
    // future config surface can expose it; the room numbers above key off the first entry.
    internal static readonly (int Room, int Cards)[] Schedule =
    {
        (14, 1),
        (17, 3),
        (20, 5),
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
