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

    /// <summary>Interest surcharge baked into the outstanding principal at loan time: you must repay the gold
    /// you borrowed PLUS this fraction of it. At 0.3, borrowing 200 means owing 260 at the shop (before any
    /// amortization). This is what separates "borrowed" (the cap-driving amount you received) from the higher
    /// "repayable" principal shown on the Ledger badge / repay button.</summary>
    internal static double RepaySurcharge = 0.3;

    /// <summary>Highest act (0-based) where the merchant still lends: 0 = Act 1 only (default), 1 = through
    /// Act 2, 2 = through Act 3. Compared against <c>RunState.CurrentActIndex</c>.</summary>
    internal static int MaxLoanActIndex = 0;

    // ── Debt LEVERAGE (독촉장 / Dunning Letter payoff) ───────────────────────────────────────────────
    /// <summary>Plating (판금) granted when a 빚 독촉 is played WHILE the 독촉장 (Dunning Letter) power is active.
    /// The reward lives on the power, not the curse — a plain forced Debt card gives no Plating. The 독촉장
    /// feeds you a 빚 독촉 each turn, so this is a repeatable defensive/repayment engine (no offense).</summary>
    internal static int LeveragePlating = 3;

    // Debt-curse ESCALATION tier by rooms-since-loan. Each tier UNLOCKS a new curse in the injected set (see
    // LoanService.InjectAllDebtsForCombat): 1=빚 독촉(Dunning), 2=+연체(Delinquency), 3=+차압(Seizure),
    // 4=+신용 불량(Bad Credit). Injected fresh into the draw pile each combat (temporary — gone at combat end).
    // ACCELERATING gaps (10 → 7 → 5): a 10-room grace period, then the spiral snowballs — once you're
    // delinquent the collections come faster and faster (full spiral by ~1 act after borrowing).
    internal static readonly (int Room, int Cards)[] Schedule =
    {
        (0, 1),
        (10, 2),
        (17, 3),
        (22, 4),
    };

    /// <summary>Target Debt-card count for a given rooms-since-loan count.</summary>
    internal static int TargetDebtCards(int roomsSinceLoan)
    {
        int target = 0;
        foreach (var (room, cards) in Schedule)
            if (roomsSinceLoan >= room) target = cards;
        return target;
    }

    /// <summary>Rooms remaining until the NEXT escalation tier — the relic badge countdown ("N rooms until it
    /// gets worse"). Returns 0 once you're already at the top tier (badge hidden / "—").</summary>
    internal static int RoomsUntilNextTier(int roomsSinceLoan)
    {
        foreach (var (room, _) in Schedule)
            if (roomsSinceLoan < room) return room - roomsSinceLoan;
        return 0;   // past the last threshold → max tier, no further escalation
    }
}
