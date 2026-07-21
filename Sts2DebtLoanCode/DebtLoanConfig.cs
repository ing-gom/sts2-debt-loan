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

    /// <summary>The next room threshold that adds Debt cards, or -1 if already at the last one.</summary>
    internal static int NextThresholdRoom(int roomsSinceLoan)
    {
        foreach (var (room, _) in Schedule)
            if (roomsSinceLoan < room) return room;
        return -1;
    }
}
