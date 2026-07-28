namespace Sts2DebtLoan;

/// <summary>
/// Runtime-tunable knobs for the loan mechanic. Populated from ModConfig in
/// <see cref="MainFile.RegisterConfig"/>; read everywhere else. Defaults here are the
/// spec defaults so the mod behaves correctly even before ModConfig initializes.
/// </summary>
internal static class DebtLoanConfig
{
    /// <summary>Gold cap on total borrowing. ★0 = NO CAP (the default): the per-loan DRAW COUNT
    /// (<see cref="MaxLoanDraws"/>) is the only limit, so three draws can finance three relics if you dare.
    /// The old 300 cap made the draw limit almost meaningless — with <see cref="MinLoan"/> 100, the 400 hard cap
    /// already permitted at most 4 draws. Set a positive value to bring the gold ceiling back.</summary>
    internal static int MaxLoan = 0;

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

    /// <summary>% of GOLD INCOME the creditor garnishes (withholds → applied straight to your debt as forced
    /// repayment) — but ONLY once the loan's interest has reached its MAXIMUM (node interest at the cap). Below max
    /// interest there is no garnishment; at max it kicks in at this flat rate. 0 disables garnishment. The player
    /// receives income minus the garnished share; the garnished gold pays down the principal.</summary>
    internal static int GarnishMaxPct = 40;

    /// <summary>Per-shop-VISIT credit limit for buying cards on debt at the debt shop — SEPARATE from the initial
    /// loan's <see cref="HardCap"/>. Each shop visit gives a fresh line; card purchases that visit may total at most
    /// this. Cards are 40–70 gold, so 100 ≈ 2 cards per shop — you can't sweep the whole 5-card offer. Resets on
    /// entering a new shop. Spec: 150.</summary>
    // ★Raised 100 → 120 alongside the FREE slot-0 gift and the higher price band (60~95). The line is tuned so a
    // visit buys exactly ONE paid offer normally (cheapest pair 60+65 = 125 > 120), but the discounted card plus one
    // more DOES fit (deep sale ≈ 33~52, + 60 = 93~112 ≤ 120) — the sale is what buys you a second card.
    // The visit's 강화판 offer stays reachable too (95 × 1.20 = 114 ≤ 120).
    internal static int ShopCreditLimit = 120;


    /// <summary>Share of every Debt-card payment that goes toward paying DOWN the principal (the rest is
    /// interest). At 0.2, a 10-gold drain retires 2 gold of the loan and 8 counts as interest — so the debt
    /// slowly amortizes and the shop repay cost shrinks over time.</summary>
    internal static double PrincipalRepayShare = 0.2;

    // ── Interest (all of it is baked into the owed Principal = shop repay cost / badge / hover) ──────────
    /// <summary>Origination interest added the MOMENT you borrow (per borrow, on that amount). 20% → borrowing
    /// 100 immediately owes 120. Keeps a quick borrow-and-repay from being nearly free.</summary>
    internal static int BorrowOriginationPct = 20;
    /// <summary>Interest added per ROOM you carry the debt, on the borrowed amount, on top of origination.</summary>
    internal static int NodeInterestPct = 5;

    /// <summary>Absolute ceiling (gold) on the TOTAL interest a loan can ever accrue (origination + node, across the
    /// borrowed loan AND card/shop debt). Interest never exceeds this no matter how much card debt piles onto the
    /// base — so it can't grow without bound. Spec: 100.</summary>
    internal static int InterestGoldCap = 100;
    /// <summary>How many rooms of <see cref="NodeInterestPct"/> accrue before it caps. 8 × 5% = +40% on top of
    /// the 20% origination = a 60% ceiling (the SP node cap; MP raises it, see below).</summary>
    internal static int MaxNodeInterestRooms = 8;

    // ── Co-op interest scaling (a debt shared by more players is harsher; SP = 1 borrower = no change) ────
    // Node interest RATE is NodeInterestPct × (borrower count) per room (accrues faster with more debtors), and
    // the node cap grows on top of the 40% SP base by MpInterestExtraCapPerBorrowerPct per ADDITIONAL borrower,
    // clamped to MpInterestExtraCapMaxPct. With the 4-player max: cap = 40 + min(40, 10×3) = 70% node → +20% origination
    // = 90% total surcharge (owed ≈ 1.9× borrowed). SP (1 borrower): 40% node + 20% = 60%, unchanged.
    /// <summary>Extra node-interest cap % added per ADDITIONAL borrower beyond the first (co-op only).</summary>
    internal static int MpInterestExtraCapPerBorrowerPct = 10;
    /// <summary>Ceiling on the co-op extra node-interest cap (never adds more than this on top of the 40% base).</summary>
    internal static int MpInterestExtraCapMaxPct = 40;

    /// <summary>How many times gold may be DRAWN on one loan (first borrow + top-ups). 0 = unlimited.
    /// The gold cap alone (300/400) let a debtor take unlimited small loans and clean out a shop; with 3 draws the
    /// scarce resource becomes the DECISIONS — 300 gold split three ways, so a 50-gold nibble costs a whole slot.
    /// Resets when the loan is fully repaid (the record dies with it) — that is what 신용 회복 restores.
    /// Debt-shop CARD buys don't count here; they have their own per-visit credit line (ShopCreditLimit).</summary>
    internal static int MaxLoanDraws = 3;

    // ⚠️MaxLoanActIndex 는 제거됐다. 막 제한이 폐지되면서(LoanService.ActAllowsLoan → 항상 true) 이 설정이
    // 아무 일도 하지 않는 죽은 슬라이더가 됐고, 유저가 조정해도 반응이 없어 오히려 오해를 만들었다.
    // 막 제한을 되살리려면 ActAllowsLoan 이 다시 설정을 읽게 하고 여기에 필드를 복원할 것.

    // ── Debt LEVERAGE (독촉장 / Dunning Letter payoff) ───────────────────────────────────────────────
    /// <summary>Plating (판금) that 납부 혜택 (PaymentBenefitPower) grants on every 납부. ★이름은 옛 구조
    /// (독촉장)에서 왔지만 실제 사용처는 납부 혜택 하나뿐이다 — 정기 납부의 {plate} 변수는 loc에서 참조되지
    /// 않는 잔재.
    /// ★3 → 2로 하향. 판금은 매 턴 시작에 1 감소하는데 납부는 매 턴 최소 1회 나오므로, 3이면 순 +2/턴으로
    /// 무한 누적한다(5턴 누적 방어도 35, 이후로도 계속 성장). 비교군 돌 갑옷은 1코 판금 4 일회성이라
    /// 4→3→2→1로 감쇠해 총 10이다. 2로 내려도 순 +1/턴이라 스케일링 엔진의 정체성은 유지된다.
    /// 근거: BALANCE_AUDIT.md (판금 동작은 실 DLL 확인).</summary>
    internal static int LeveragePlating = 2;

    // ── 신용 회복 (Credit Restored) reward gate ──────────────────────────────────────────────────────────
    // ── 신용도 (Credit Score) ────────────────────────────────────────────────────────────────────────
    /// <summary>누적 상환액 몇 골드당 신용도 1인가. 신용도는 <b>절대 리셋되지 않는</b> 런 단위 진행 트랙 —
    /// 청산해도 유물과 기록이 남으므로 여러 번의 대출 사이클이 하나의 신용 이력으로 누적된다.</summary>
    internal static int GoldPerCreditPoint = 100;

    /// <summary>청산 시 받는 보상의 문턱(누적 상환액 골드). 신용도로는 3 / 6 / 9 / 12.
    /// ★티어(빚을 얼마나 깊게 끌고 갔는가)가 아니라 <b>얼마나 갚았는가</b>로 바뀐 이유: 청산이 더는
    /// 유물을 없애지 않으니 "깊은 빚을 한 번 청산" 대신 "신용을 쌓아 올린다"가 진행의 축이 됐다.</summary>
    internal static int CreditRewardCard = 300;          // 신용 회복 카드
    internal static int CreditRewardUpgraded = 600;      // 강화된 신용 회복 카드
    internal static int CreditRewardUpgradeAny = 900;    // + 덱의 다른 카드 1장 강화
    internal static int CreditRewardRemoveAny = 1200;    // + 덱의 다른 카드 1장 제거

    /// <summary>고정 4단계(신용도 12) 뒤로 반복되는 <b>보너스 단계의 간격</b>(신용도 단위).
    /// 매 2 마다 카드 <b>강화 또는 제거</b>를 하나 골라 받는다 — 끝이 없다.
    /// <para>★사다리에 끝을 두면 신용도 12 이후의 상환이 아무 의미가 없어진다. 신용도는 청산해도
    /// 리셋되지 않는 런 단위 트랙이라 후반에도 계속 쌓인다.</para></summary>
    internal static int BonusRewardCredits = 2;

    /// <summary>목돈 상환이 신용도로 인정되는 한도(누적 골드). 여기까지는 한 번에 갚아도 신용이 오르지만,
    /// 그 위로는 <b>납부(및 강제 징수)</b> 로만 오른다. 신용도 6 = 고정 사다리의 두 번째 칸.
    /// <para>★근거: 인출 3회 + 금액 상한 없음이면 유물 3개를 한 번에 질러 갚을 돈이 900을 넘고, 그 <b>한 번의
    /// 청산으로 고정 사다리를 통째로 건너뛴다</b>(실측 825 → 이자 포함 925 → 신용도 9). 사이클을 도는 쪽이
    /// 손해가 되어 재설계 취지와 정반대였다.</para></summary>
    internal static int LumpSumCreditCap = 600;

    // ★신용도 파밍 가드는 상수가 아니라 규칙으로 — 대출을 받은 그 상점에서는 원금을 갚을 수 없다
    // (LoanService.CanRepayHere). 취급 수수료 20% 때문에 "100 빌리고 120 갚기"는 순 골드 20에 신용도
    // 1.2를 주므로, 같은 자리에서 되갚을 수 있으면 순 200골드로 최고 보상(누적 1200)까지 간다.
    // 다음 상점까지 걸어가게 만들면 그 사이 노드 이자(5%/방)가 붙고 방 수도 소모돼 루프가 스스로 비싸진다.

    // ⚠️구 RewardMinTier(4) / RewardMinPaid(400) 은 제거됐다. 보상 게이트가 "등급 4 AND 누적 400" 이라는
    // 두 조건에서 위의 **누적 상환액 사다리 단일 축**(CreditRewardCard/Upgraded/UpgradeAny/RemoveAny)으로
    // 바뀌었기 때문이다. 등급은 청산할 때마다 1로 리셋되므로 더는 진행의 축이 될 수 없다.

    // Debt-curse ESCALATION tier by rooms-since-loan. Each tier UNLOCKS a new curse in the injected set (see
    // LoanService.InjectAllDebtsForCombat): 1=빚 독촉(Dunning), 2=+연체(Delinquency), 3=+차압(Seizure),
    // 4=+신용 불량(Bad Credit). Injected fresh into the draw pile each combat (temporary — gone at combat end).
    // A 13-room GRACE (only 빚 독촉), then the spiral: 연체 at 13, 차압 at 17 (a 4-room 연체-only window), 신용
    // 불량 at 22. The grace softens the early 연체 (+50% incoming) spike; once delinquent, the collections pile
    // on (full spiral by ~1 act after borrowing).
    internal static readonly (int Room, int Cards)[] Schedule =
    {
        (0, 1),
        (13, 2),
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
