using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;      // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Gold;       // GoldLossType
using MegaCrit.Sts2.Core.Entities.Merchant;   // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;   // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;               // RoomType
using MegaCrit.Sts2.Core.Runs;

namespace Sts2DebtLoan;

/// <summary>One player's outstanding loan for the run. The Debt cards are NOT stored here — they are
/// injected fresh into each combat's draw pile before the opening hand (see
/// <see cref="BeforeHandDrawInjectPatch"/>), so this only tracks the numeric state, which is persisted
/// onto the relic as [SavedProperty] fields.</summary>
internal sealed class LoanRecord
{
    /// <summary>Total gold ever borrowed this run (fixed once taken; rises on a top-up). Shown in the
    /// hover as "borrowed X" — does NOT shrink as the loan amortizes (that's <see cref="Principal"/>).</summary>
    internal int Borrowed;

    /// <summary>Gold still owed = the shop repay cost + the relic's badge. Starts equal to
    /// <see cref="Borrowed"/> and shrinks as each Debt-card payment retires a share of it (amortization).</summary>
    internal int Principal;

    /// <summary>Cumulative gold the Debt cards have drained = total paid so far (interest + amortized principal).</summary>
    internal int TotalPaid;

    /// <summary>How many rooms of node-interest have already been baked into <see cref="Principal"/> (0..
    /// MaxNodeInterestRooms). Tracked so room-entry accrual is idempotent and survives save/load.</summary>
    internal int InterestRoomsApplied;

    /// <summary>TotalFloor of the shop where the loan was taken. Top-ups are allowed only at THAT shop.
    /// Rooms-since-loan (which drives the Debt-card count) is computed as TotalFloor − LoanFloor.</summary>
    internal int LoanFloor = -1;

    /// <summary>False once settled (repaid in full).</summary>
    internal bool Active = true;

    internal bool RelicGranted;

    /// <summary>Whether the 정기 납부 (Standing Order) leverage card has been handed to the deck this loan (once,
    /// on the first visit to a shop other than the loan shop). Persisted on the relic so a reload keeps it.</summary>
    internal bool DunningLetterGranted;

    /// <summary>How many of the 7 debt event cards have been handed out (one per shop-revisit). Drives the fixed
    /// order — 1st = 정기 납부, 5th = 취업알선, the rest a per-run shuffle of the payment cards. Persisted.</summary>
    internal int EventGrantCount;

    /// <summary>PER-COMBAT transient: the 신용 불량 (Bad Credit) collection level 0..3. Reset to 0 at each
    /// combat start (by the injector) and ratcheted up by BadCreditCard every turn it sits in hand. Not
    /// persisted (it's a within-combat spiral, and it's deterministic from lockstep turn starts).</summary>
    internal int CollectionLevel;
}

/// <summary>
/// The loan mechanic's brain. The Harmony patches are thin — they call into here. Gold mutations follow
/// the co-op host-authoritative pattern (LOCAL player + RewardSynchronizer); loans are single-player
/// gated for now (see <see cref="CanLoanCover"/>).
/// </summary>
internal static class LoanService
{
    private static readonly ConditionalWeakTable<Player, LoanRecord> Records = new();

    internal static LoanRecord? For(Player? player)
        => player != null && Records.TryGetValue(player, out var r) ? r : null;

    private static LoanRecord GetOrCreate(Player player)
        => Records.GetValue(player, _ => new LoanRecord());

    /// <summary>Debt cards from one player's loan = the schedule count for rooms-since-loan, COMPUTED as
    /// TotalFloor − LoanFloor. Deriving it from shared game state (not a stored counter) makes it identical
    /// on every co-op peer automatically — no per-room broadcast needed.</summary>
    internal static int DebtCardCountFor(Player? p)
    {
        var rec = For(p);
        if (rec == null || !rec.Active || rec.Principal <= 0 || p?.RunState == null) return 0;
        return DebtLoanConfig.TargetDebtCards(p.RunState.TotalFloor - rec.LoanFloor);   // 1 / 2 / 3 by rooms
    }

    /// <summary>Kept for the shop surcharge + relic tooltip: the highest per-combat curse-tier across all
    /// active loans (1/2/3). The injection itself composes per-loan (see <see cref="InjectAllDebtsForCombat"/>).</summary>
    internal static int RunWideDebtTotal(IRunState run)
    {
        if (run?.Players == null) return 0;
        int total = 0;
        foreach (var p in run.Players) total += DebtCardCountFor(p);
        return total;
    }

    /// <summary>Inject the DISTINCT Debt curse cards for EVERY active loan in the run into one player's draw
    /// pile — the run-wide contagion (a partner's loan seeps into your combat too; multiple loans stack).
    /// Each loan contributes an escalating SET by rooms-since-loan: 빚 독촉 (Dunning, upgraded to '+' once
    /// that loan is over the soft cap) always; +연체 (Delinquency) at 10 rooms; +차압 (Seizure) at 20. The
    /// cards are SHUFFLED into the draw pile (random positions) BEFORE the opening hand is dealt (this runs at
    /// BeforeHandDraw), so the normal draw pulls them in naturally from turn 1 — sometimes several land in the
    /// opening hand, sometimes they trickle in over the next turns, but they're never all forced onto turn 1.
    /// Temporary — gone at combat end.</summary>
    internal static async Task InjectAllDebtsForCombat(Player injectee, IRunState run)
    {
        var combat = injectee?.Creature?.CombatState;
        if (combat == null || run?.Players == null) return;

        await ResetPaymentsThisCombat(injectee!);   // fresh 납부 실적 each combat (drives 정산/청구서 scaling)

        var cards = new List<CardModel>();
        foreach (var owner in run.Players)
        {
            var rec = For(owner);
            if (rec == null || !rec.Active || rec.Principal <= 0 || owner.RunState == null) continue;
            int tier = DebtLoanConfig.TargetDebtCards(owner.RunState.TotalFloor - rec.LoanFloor);   // 1/2/3

            // Tier 1 (rooms 0-12) injects NOTHING — a loan you're paying ON TIME isn't cursed. It only costs
            // interest (which accrues) + shop-price inflation; the 정기 납부 (Standing Order) power still feeds
            // 납부 cards to work it down. The penalty escalation only starts once you fall BEHIND:
            if (tier >= 4)
            {
                // Tier 4: 신용 불량 (Bad Credit) ALONE — it spawns a 강제 징수 (Forced Collection) every turn (the
                // escalating gold/HP drain that IS the tier-4 pressure), so nothing else is injected.
                rec.CollectionLevel = 0;   // fresh spiral each combat; BadCredit ratchets it up per turn
                var c = combat.CreateCard<BadCreditCard>(injectee); if (c != null) cards.Add(c);
            }
            else if (tier >= 2)
            {
                // Tier 2: 연체 (Delinquency, "you're late"). Tier 3: + 차압 (Seizure). Cumulative.
                var c = combat.CreateCard<DelinquencyCard>(injectee); if (c != null) cards.Add(c);
                if (tier >= 3) { var s = combat.CreateCard<SeizureCard>(injectee); if (s != null) cards.Add(s); }
            }
        }
        if (cards.Count == 0) return;

        // Random positions → shuffled into the draw pile before the opening deal, so how many land in the
        // opening hand varies (not always all of them). The reveal shows them seeping into the pile. Random
        // uses the lockstep combat RNG → deterministic across co-op peers.
        var results = await CardPileCmd.AddGeneratedCardsToCombat(cards, PileType.Draw, injectee, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);
        MainFile.Logger.Info($"[{MainFile.ModId}] shuffled {cards.Count} Debt curse card(s) into the draw pile.");
    }

    /// <summary>True if the player already carries the Merchant's Ledger relic.</summary>
    internal static bool PlayerHasLedger(Player player)
    {
        try
        {
            string entry = ModelDb.GetId(typeof(DebtLoanRelic)).Entry;
            foreach (var r in player.Relics)
                if (r.Id.Entry == entry) return true;
        }
        catch { /* models not ready */ }
        return false;
    }

    /// <summary>Test-only (solo-verify): drop the run's loan record for a fresh scenario.</summary>
    internal static void ResetFor(Player player) => Records.Remove(player);

    internal static DebtLoanRelic? LedgerRelicOf(Player player)
    {
        if (player?.Relics == null) return null;
        foreach (var r in player.Relics)
            if (r is DebtLoanRelic dl) return dl;
        return null;
    }

    // ── Persistence (the relic carries [SavedProperty] fields; rebuilt on load) ─────────────────────

    internal static void SyncToRelic(Player player)
    {
        var rec = For(player);
        var relic = LedgerRelicOf(player);
        if (rec == null || relic == null) return;
        try
        {
            relic.Borrowed = rec.Borrowed;
            relic.Principal = rec.Principal;
            relic.TotalPaid = rec.TotalPaid;
            relic.InterestRoomsApplied = rec.InterestRoomsApplied;
            relic.LoanFloor = rec.LoanFloor;
            relic.Active = rec.Active;
            relic.DunningLetterGranted = rec.DunningLetterGranted;
            relic.EventGrantCount = rec.EventGrantCount;
            relic.RefreshVars(DebtCardCountFor(player));   // borrowed/paid/cards into the relic's own DynamicVars (per-relic hover)
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic sync failed: {e.Message}"); }
    }

    // The hover text is no longer built here: it's a per-language STATIC template ("Borrowed {borrowed} …
    // Paid {paid} …") injected once by LocInjectionPatch, with the numbers filled from each relic's OWN
    // DynamicVars (see DebtLoanRelic.CanonicalVars / RefreshVars). That makes the hover per-relic, which is
    // co-op-safe — the old global loc-table overwrite showed the last-synced player's status on both relics.

    /// <summary>Rebuild the transient record from the relic on load. A repaid loan removed the relic, so
    /// no relic ⇒ no record ⇒ free to borrow again.</summary>
    internal static void RestoreFromRelic(Player player)
    {
        var relic = LedgerRelicOf(player);
        if (relic == null) return;
        var rec = GetOrCreate(player);
        rec.Borrowed = relic.Borrowed;
        rec.Principal = relic.Principal;
        rec.TotalPaid = relic.TotalPaid;
        rec.InterestRoomsApplied = relic.InterestRoomsApplied;
        rec.LoanFloor = relic.LoanFloor;
        rec.Active = relic.Active;
        rec.DunningLetterGranted = relic.DunningLetterGranted;
        rec.EventGrantCount = relic.EventGrantCount;
        rec.RelicGranted = true;
        EnsureRoomWatch();   // resubscribe the shop-revisit grant watcher after a load
        relic.RefreshVars(DebtCardCountFor(player));
        MainFile.Logger.Info($"[{MainFile.ModId}] restored loan: borrowed {rec.Borrowed}, owed {rec.Principal}, paid {rec.TotalPaid}, loanFloor {rec.LoanFloor}, active {rec.Active}.");
    }

    /// <summary>DEBUG (dl_tier console cmd): force the loan to a given rooms-since-loan so the Ledger shows a
    /// target escalation tier — grants a starter loan if none exists, back-dates LoanFloor to hit the tier,
    /// then refreshes badge + hover + the evolving-icon overlay. SP/preview only; not for real play.</summary>
    internal static async Task DebugSetTier(Player player, int rooms)
    {
        if (player?.RunState == null) return;
        var rec = For(player);
        if (rec == null || !rec.Active) { await GrantLoanDirect(player, 200); rec = For(player); }
        if (rec == null) return;
        rec.LoanFloor = player.RunState.TotalFloor - rooms;   // rooms-since-loan = TotalFloor − LoanFloor
        if (rec.Principal <= 0) rec.Principal = 200;
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        LedgerOverlay.Refresh();
    }

    /// <summary>Display-only: push the current tier count into the relic's DynamicVars so the hover's per-tier
    /// text keeps pace (the badge is already computed live). No networked/SavedProperty mutation → safe to run
    /// per-client (e.g. at combat start). Also self-heals via SyncToRelic whenever a Debt card drains gold.</summary>
    internal static void RefreshRelicDisplay(Player? player)
    {
        if (player == null) return;
        var relic = LedgerRelicOf(player);
        if (relic != null) relic.RefreshVars(DebtCardCountFor(player));
    }

    // ── Eligibility ──────────────────────────────────────────────────────────

    private static bool ActAllowsLoan(Player player)
        => player.RunState.CurrentActIndex <= DebtLoanConfig.MaxLoanActIndex;

    internal static int RemainingRoom(Player player)
    {
        var rec = For(player);
        int used = rec?.Borrowed ?? 0;      // cap is on lifetime borrowed, not the amortized outstanding
        return Math.Max(0, DebtLoanConfig.HardCap - used);   // may overshoot the soft cap up to the hard cap
    }

    /// <summary>Can this merchant item be bought on loan now? First loan: any Act-1 shop. Top-up: only at
    /// the same shop (until the borrow cap is reached).</summary>
    internal static bool CanLoanCover(MerchantEntry entry, Player player)
    {
        if (entry == null || player == null) return false;
        // Co-op: loans replicate via the networked dl_sync command (relic + record on both peers), and the
        // gold rides the reward-sync. Only the LOCAL player may take a loan (others' shops are theirs).
        bool sp = RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;
        if (!ActAllowsLoan(player)) return false;

        var rec = For(player);

        int cost = entry.Cost;
        int shortfall = cost - (int)player.Gold;
        if (shortfall <= 0) return false;                    // can already afford → not a loan
        if (RemainingRoom(player) < shortfall) return false; // over the cap

        if (rec == null || !rec.RelicGranted) return true;   // FIRST loan (or fresh after a repay)
        if (!rec.Active) return false;
        return player.RunState.TotalFloor == rec.LoanFloor;  // top-up ONLY at the same shop
    }

    /// <summary>How much a loan advances for this item: at least <see cref="DebtLoanConfig.MinLoan"/> (so a
    /// tiny shortfall still borrows a meaningful amount), never below the actual shortfall, and capped by the
    /// remaining borrow room. The extra over the shortfall lands in the player's pocket as change.</summary>
    internal static int LoanAmountFor(MerchantEntry entry, Player player)
    {
        int shortfall = entry.Cost - (int)player.Gold;
        if (shortfall <= 0) return 0;
        int want = Math.Max(shortfall, DebtLoanConfig.MinLoan);
        return Math.Max(0, Math.Min(want, RemainingRoom(player)));
    }

    /// <summary>Price multiplier the merchant applies to a player carrying debt at a DIFFERENT shop than
    /// the one they borrowed at: +10% (1 card) / +15% (2) / +20% (3). 1.0 = no change.</summary>
    internal static double DebtPriceMultiplier(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return 1.0;
        if (player.RunState.TotalFloor == rec.LoanFloor) return 1.0;   // no surcharge at your own shop
        int tier = DebtCardCountFor(player);                            // 1..4
        return 1.0 + Math.Min(20, 5 + 5 * tier) / 100.0;                // 10% / 15% / 20% (capped — tier 4 bites via HP)
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    internal static Task GrantLoanFor(MerchantEntry entry, Player player)
        => GrantLoanDirect(player, LoanAmountFor(entry, player));

    /// <summary>Credit the loan gold, record the debt, and grant the Ledger relic on the first loan. The
    /// gold is a LOCAL mutation + reward-sync (so it shows on the partner too); the relic + loan record are
    /// applied LOCALLY in SP, or dispatched to BOTH peers via the networked <c>dl_sync</c> command in co-op
    /// (RelicCmd.Obtain is a local mutation — see <see cref="ApplyActiveLoan"/> — so running dl_sync on each
    /// peer grants exactly one relic per peer, no doubling).</summary>
    internal static async Task GrantLoanDirect(Player player, int amount)
    {
        if (amount <= 0) return;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return;

        await PlayerCmd.GainGold(amount, player, false);
        run?.RewardSynchronizer?.SyncLocalObtainedGold(amount);

        var existing = For(player);
        int oldBorrowed = existing?.Borrowed ?? 0;
        int borrowed  = oldBorrowed + amount;                  // lifetime borrowed (drives the cap + hover)
        // Repayable > borrowed: you owe the gold you took PLUS interest. 20% ORIGINATION is added right now on
        // this amount; the rest accrues per-room (see AccrueNodeInterest). Borrowed is what you received (drives
        // the cap); Principal is what you must repay (shop cost + badge), amortized 1:1 by payments.
        int origination = (int)Math.Round(amount * (DebtLoanConfig.BorrowOriginationPct / 100.0));
        int principal = (existing?.Principal ?? 0) + amount + origination;
        int totalPaid = existing?.TotalPaid ?? 0;
        int loanFloor = (existing != null && existing.RelicGranted)
                        ? existing.LoanFloor                   // top-up keeps the original shop floor
                        : player.RunState.TotalFloor;          // first loan: rooms = TotalFloor − here (0 → 1 card)

        if (sp) await ApplyActiveLoan(player, borrowed, principal, totalPaid, loanFloor);
        else    DebtLoanNet.BroadcastLoan(player, borrowed, principal, totalPaid, loanFloor);

        MainFile.Logger.Info($"[{MainFile.ModId}] loan +{amount}g (borrowed {borrowed}/{DebtLoanConfig.MaxLoan}, owed {principal}=+30%).");

        // (No merchant bark on the loan itself any more — the merchant now speaks when he HANDS you a payoff card,
        //  see the 정기 납부 loan-time grant + TryGrantDunningLetter, hinting another card comes next visit.)
    }

    /// <summary>Apply an ACTIVE loan state locally: set the record, grant the Ledger relic if the player
    /// doesn't have it, and write the state through to the relic. Runs on EACH peer — directly in SP, or
    /// once per peer via the networked <c>dl_sync</c> replay in co-op (idempotent: re-grants only if missing).</summary>
    internal static async Task ApplyActiveLoan(Player player, int borrowed, int principal, int totalPaid, int loanFloor)
    {
        var rec = GetOrCreate(player);
        rec.Borrowed     = borrowed;
        rec.Principal    = principal;
        rec.TotalPaid    = totalPaid;
        rec.LoanFloor    = loanFloor;
        rec.Active       = true;
        rec.RelicGranted = true;
        if (!PlayerHasLedger(player))
            await DebtLoanGrants.GrantRelic(player);
        // Hand the 정기 납부 (Standing Order) card AT LOAN TIME (not the first shop revisit) so you have immediate
        // counterplay to the injected 빚 (Debt) curse — play it and the power feeds 납부 cards to work the debt
        // down. Consumes sequence slot 0 (EventGrantCount → 1); the remaining payoff cards still come at shop
        // revisits. Once per loan (DunningLetterGranted guards top-ups). Local per-peer, like the revisit grant.
        if (!rec.DunningLetterGranted)
        {
            rec.DunningLetterGranted = true;
            rec.EventGrantCount = System.Math.Max(rec.EventGrantCount, 1);
            _ = DebtLoanGrants.GrantDunningLetter(player);
            MerchantBark.SayGrant(NextEventCardHintKey(rec));   // hand the 정기 납부 + hint the SPECIFIC next card
        }
        EnsureRoomWatch();   // still watch shop revisits for the REMAINING payoff cards (slots 1-6)
        SyncToRelic(player);
    }

    // ── 정기 납부 (Standing Order) shop-revisit grant ─────────────────────────────
    private static bool _roomWatchSubscribed;

    /// <summary>Subscribe (once) to room changes so we can hand the 정기 납부 leverage card to a debtor the first
    /// time they shop somewhere OTHER than where they borrowed. Fires per-peer (like the ledger overlay's own
    /// RoomEntered hook); the grant is flag-guarded + deterministic from synced loan state → converges in co-op.
    /// ⚠️ co-op: verify with coop-verify before release (local deck mutation off a per-peer event).</summary>
    internal static void EnsureRoomWatch()
    {
        if (_roomWatchSubscribed) return;
        var rm = RunManager.Instance;
        if (rm == null) return;
        rm.RoomEntered += OnRoomEntered;
        _roomWatchSubscribed = true;
    }

    /// <summary>Accrue per-room interest into the owed Principal: +NodeInterestPct% of Borrowed for each new room
    /// carried, up to MaxNodeInterestRooms. Idempotent (only adds the rooms not yet applied) so re-fires and
    /// reloads don't double-charge. Room count is deterministic (TotalFloor − LoanFloor) → co-op peers match.</summary>
    internal static void AccrueNodeInterest(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || player?.RunState == null) return;
        int rooms = Math.Min(DebtLoanConfig.MaxNodeInterestRooms, Math.Max(0, player.RunState.TotalFloor - rec.LoanFloor));
        if (rooms <= rec.InterestRoomsApplied) return;
        int deltaRooms = rooms - rec.InterestRoomsApplied;
        int add = (int)Math.Round(rec.Borrowed * (DebtLoanConfig.NodeInterestPct / 100.0) * deltaRooms);
        rec.Principal += add;
        rec.InterestRoomsApplied = rooms;
        SyncToRelic(player);
    }

    private static void OnRoomEntered()
    {
        try
        {
            var run = RunManager.Instance?.State;
            if (run?.Players == null) return;
            // Every room: refresh each ledger's badge (rooms-until-next-tier) so it visibly counts DOWN as you
            // walk the map — TotalFloor changed, so the live DisplayAmount must be re-pushed to the widget.
            foreach (var p in run.Players) { AccrueNodeInterest(p); RefreshRelicDisplay(p); }
            if (run.CurrentRoom?.RoomType != RoomType.Shop) return;
            foreach (var p in run.Players) TryGrantDunningLetter(p);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] room-watch grant failed: {e.Message}"); }
    }

    /// <summary>Grant the 정기 납부 once per loan, when the debtor enters a shop that isn't the one they borrowed
    /// at (TotalFloor != LoanFloor). Deck mutation is local + deterministic → the same card lands on each peer.</summary>
    // The event-card grant SEQUENCE across shop revisits — 10 cards, each once. Slot 0 (정기 납부) is handed at LOAN
    // TIME; slots 1-9 come on shop revisits. The FIRST slots are a FIXED priority order so even a short run gets the
    // core of the 영수증 (Receipt) loop early — 정기 납부(repay engine), 정산 + 청구서(the Receipt spenders), 이자 지원
    // (cheap engine), 취업알선 — then the REMAINING five cards come SHUFFLED per run (variety; they're secondary).
    private static readonly System.Type[] FixedOrder =
    {
        typeof(DunningLetterCard),    // slot 0 — granted at loan time
        typeof(SettlementCard),       // slot 1
        typeof(InvoiceCard),          // slot 2
        typeof(InterestSupportCard),  // slot 3 (cheapest engine)
        typeof(JobPlacementCard),     // slot 4
    };
    private static readonly System.Type[] RemainderPool =
    {
        typeof(PaymentBenefitCard), typeof(RefundCard), typeof(BloodPaymentCard),
        typeof(CounterclaimCard), typeof(StatementCard),
    };
    private const int TotalEventCards = 10;   // FixedOrder(5) + RemainderPool(5)

    /// <summary>Deterministic per-run shuffle of the remainder pool (seeded from the loan floor → same order on both
    /// co-op peers with no networking).</summary>
    private static System.Type[] ShuffledRemainder(int seed)
    {
        var arr = (System.Type[])RemainderPool.Clone();
        var rng = new System.Random(seed);
        for (int i = arr.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (arr[i], arr[j]) = (arr[j], arr[i]); }
        return arr;
    }

    /// <summary>The INDIRECT-hint key for the NEXT event card the merchant will hand out (the merchant alludes to
    /// its effect without naming it), or null if the sequence is finished. Deterministic (same LoanFloor → same
    /// order + persists across save/load), so the hint always matches what actually arrives next. Call AFTER
    /// EventGrantCount has advanced past the current card.</summary>
    private static string? NextEventCardHintKey(LoanRecord rec)
    {
        int pos = rec.EventGrantCount;   // the NEXT slot
        if (pos >= TotalEventCards) return null;
        var type = pos < FixedOrder.Length ? FixedOrder[pos] : ShuffledRemainder(rec.LoanFloor)[pos - FixedOrder.Length];
        if (type == typeof(SettlementCard))       return "SETTLEMENT";
        if (type == typeof(InvoiceCard))          return "INVOICE";
        if (type == typeof(InterestSupportCard))  return "INTEREST_SUPPORT";
        if (type == typeof(JobPlacementCard))     return "JOB_PLACEMENT";
        if (type == typeof(PaymentBenefitCard))   return "PAYMENT_BENEFIT";
        if (type == typeof(RefundCard))           return "REFUND";
        if (type == typeof(BloodPaymentCard))     return "BLOOD_PAYMENT";
        if (type == typeof(CounterclaimCard))     return "COUNTERCLAIM";
        if (type == typeof(StatementCard))        return "STATEMENT";
        return null;   // 정기 납부 (never "next") or unmapped → generic bark
    }

    private static void TryGrantDunningLetter(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || player.RunState == null) return;
        if (player.RunState.TotalFloor == rec.LoanFloor) return;   // still the loan shop → not a "revisit"
        if (rec.EventGrantCount >= TotalEventCards) return;         // all event cards handed out

        int pos = rec.EventGrantCount;   // 0-based sequence index
        System.Type cardType = pos < FixedOrder.Length
            ? FixedOrder[pos]
            : ShuffledRemainder(rec.LoanFloor)[pos - FixedOrder.Length];

        rec.EventGrantCount++;
        if (cardType == typeof(DunningLetterCard)) rec.DunningLetterGranted = true;   // repay-vanish still keys on this
        _ = DebtLoanGrants.GrantCard(player, cardType);
        MerchantBark.SayGrant(NextEventCardHintKey(rec));   // hand a payoff card + hint the SPECIFIC next one
        SyncToRelic(player);
    }

    /// <summary>A Debt card drained gold. The payment splits: a share pays DOWN the principal, the rest is
    /// interest — so the loan amortizes and the shop repay cost shrinks. The share is
    /// <see cref="DebtLoanConfig.PrincipalRepayShare"/> (20%) for the passive on-draw drain, or an override
    /// (50% when the player voluntarily PLAYS a Dunning card = faster repayment). Pure record math; runs
    /// deterministically on both co-op peers in the lockstep combat.</summary>
    internal static async Task AccrueInterest(Player player, int drained, double? principalShareOverride = null)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || drained <= 0) return;
        rec.TotalPaid += drained;
        double share = principalShareOverride ?? DebtLoanConfig.PrincipalRepayShare;
        int principalCut = Math.Min(rec.Principal, (int)Math.Round(drained * share));
        rec.Principal = Math.Max(0, rec.Principal - principalCut);
        SyncToRelic(player);
        await Task.CompletedTask;
    }

    /// <summary>취업알선 (Job Placement) placement fee: add <paramref name="amount"/> gold straight onto what you
    /// OWE (the shop repay cost / relic badge). You do NOT receive the gold — it's a fee, not a loan, so no gold
    /// enters your pocket and there is no surcharge. The payoff is the 품삯 (Wages) the power feeds you each turn.
    /// Needs an active loan. Pure record math off a lockstep card play → co-op safe.</summary>
    internal static void AddCombatDebt(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        rec.Principal += amount;   // owed goes up; the player gains no gold (it's a fee, not a loan)
        SyncToRelic(player);
    }

    // ── 납부 (Payment) resource system ─────────────────────────────────────────
    // 납부 실적 (payment tally) is a CUSTOM combat resource, shown on its own HUD counter near the energy orb (like
    // Regent's Stars but our own, so no collision). It is NOT a power buff — it lives here as a per-combat value and
    // raises TallyChanged so the custom NPaymentTallyCounter node updates. Cards read it and CONSUME it. The value is
    // computed the same way on every peer (payments are lockstep), so the display stays in sync; the counter is local.
    private static readonly ConditionalWeakTable<Player, int[]> _tally = new();

    /// <summary>Fired whenever a player's 납부 실적 changes → the HUD counter re-renders. (player, newValue).</summary>
    internal static event Action<Player, int>? TallyChanged;

    /// <summary>Banked 납부 (Payment) count this combat. Read by 정산 (block × tally) and 청구서 (damage × tally),
    /// which then spend it via <see cref="ConsumePaymentStack"/>.</summary>
    internal static int PaymentsThisCombat(Player? p)
        => p != null && _tally.TryGetValue(p, out var a) ? a[0] : 0;

    private static void SetTally(Player p, int value)
    {
        var cell = _tally.GetValue(p, _ => new int[1]);
        if (cell[0] == value) return;
        cell[0] = value < 0 ? 0 : value;
        TallyChanged?.Invoke(p, cell[0]);
    }

    /// <summary>Spend the WHOLE 영수증 tally (called by 청구서/정산 after they pay out). No-op if none.</summary>
    internal static Task ConsumePaymentStack(Player? p)
    {
        if (p != null) SetTally(p, 0);
        return Task.CompletedTask;
    }

    /// <summary>Spend a FIXED amount of 영수증 — used by power cards that cost N to install. Clamps at 0.</summary>
    internal static Task SpendTally(Player? p, int n)
    {
        if (p != null && n > 0) SetTally(p, PaymentsThisCombat(p) - n);
        return Task.CompletedTask;
    }

    /// <summary>Clear the tally at combat start (fresh each fight).</summary>
    internal static Task ResetPaymentsThisCombat(Player p)
    {
        SetTally(p, 0);
        return Task.CompletedTask;
    }

    /// <summary>The unified 납부 (Payment) entry: pay the loan's PRINCIPAL down 1:1 (the whole payment goes to
    /// principal — the interest is the up-front 50% surcharge baked in at loan time, not a per-payment cut),
    /// bump the per-combat payment counter, then fire the payment-reactive powers (납부 혜택 → Plating, 환급 → a
    /// 성실 납부 card). Called by the Debt cards after the gold is taken (or, for the HP-payment card, after the
    /// HP loss). The AccrueInterest math is deterministic on both peers; the power effects are self-appliers →
    /// co-op safe.</summary>
    internal static async Task RecordPayment(Player player, PlayerChoiceContext cc, int amount)
    {
        var rec0 = For(player);
        bool wasOwing = rec0 != null && rec0.Active && rec0.Principal > 0;   // did this payment have a debt to clear?
        await AccrueInterest(player, amount, principalShareOverride: 1.0);   // 100% to principal (interest = the surcharge)
        if (player?.Creature == null) return;
        SetTally(player, PaymentsThisCombat(player) + 1);   // 납부 실적 +1 → HUD counter updates
        var benefit = player.Creature.GetPower<PaymentBenefitPower>();
        if (benefit != null) await benefit.OnPayment(cc, player);
        var refund = player.Creature.GetPower<RefundPower>();
        if (refund != null) await refund.OnPayment(cc, player);
        var counterclaim = player.Creature.GetPower<CounterclaimPower>();
        if (counterclaim != null) await counterclaim.OnPayment(cc, player);
        var statement = player.Creature.GetPower<StatementPower>();
        if (statement != null) await statement.OnPayment(cc, player);
        var interestSupport = player.Creature.GetPower<InterestSupportPower>();
        if (interestSupport != null) await interestSupport.OnPayment(cc, player, amount);   // refunds half the payment

        // Paid the loan off mid-combat? Lift the whole debt right now (see SettleLoanInCombat).
        if (wasOwing)
        {
            var rec = For(player);
            if (rec != null && rec.Active && rec.Principal <= 0) await SettleLoanInCombat(player);
        }
    }

    /// <summary>A payment drove the principal to 0 DURING combat — settle the loan immediately instead of waiting
    /// for a shop. Strips the injected Debt curses from combat (so 강제 징수 stops collecting the moment you're
    /// square), removes the 신용 불량 spawner power, then runs the normal repay settle (remove the Ledger relic +
    /// clear the record so credit is restored). The satisfying "debt cleared, curse lifted" beat — and it fixes
    /// collections continuing after you no longer owe anything. Runs in the lockstep payment path (principal hits
    /// 0 identically on both peers; relic/card removals are local per-peer). ⚠️ co-op contagion: this clears ALL
    /// Debt curses in your combat, including any seeped from a partner's still-active loan — verify coop-verify.</summary>
    internal static async Task SettleLoanInCombat(Player player)
    {
        MainFile.Logger.Info($"[{MainFile.ModId}] loan paid off in combat — lifting the debt immediately.");
        await DebtLoanGrants.RemoveDebtCardsFromCombat(player);      // stop the injected curses taxing/debuffing NOW
        if (player.Creature != null && player.Creature.GetPower<BadCreditPower>() != null)
            await PowerCmd.Remove<BadCreditPower>(player.Creature);  // kill the 강제 징수 spawner so its icon clears too
        await ApplyRepay(player);                                   // Active=false + deck sweep + remove relic + reset record
    }

    /// <summary>The 강제 징수 (Forced Collection) writes principal off DIRECTLY — no gold, it's paid in HP. So
    /// the whole amount retires principal (all "principal", no interest split), counts toward TotalPaid, and
    /// once principal hits 0 the loan is settled (record only — the relic drops at the next shop). Pure record
    /// math off the lockstep turn-end, identical on both peers.</summary>
    internal static void ForceRepayPrincipal(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        int cut = Math.Min(rec.Principal, amount);
        rec.Principal = Math.Max(0, rec.Principal - cut);
        rec.TotalPaid += cut;
        if (rec.Principal <= 0) rec.Active = false;   // spiral self-terminates; relic removed at next shop
        SyncToRelic(player);
    }

    /// <summary>Repay the outstanding principal at a shop → good credit: relic removed, borrow again later.</summary>
    internal static async Task<bool> Repay(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return false;
        if ((int)player.Gold < rec.Principal) return false;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        int tier = DebtCardCountFor(player);   // how deep in debt they were → tier-specific repay bark
        await PlayerCmd.LoseGold(rec.Principal, player, GoldLossType.Spent);
        run?.RewardSynchronizer?.SyncLocalGoldLost(rec.Principal);
        MainFile.Logger.Info($"[{MainFile.ModId}] repaid principal {rec.Principal}g — credit restored.");

        if (sp) await ApplyRepay(player);
        else    DebtLoanNet.BroadcastRepay(player);
        MerchantBark.SayRepay(tier);           // merchant reacts to being paid off (varies by how deep you were)
        return true;
    }

    /// <summary>Apply the repay settle locally: stop the Debt cards, REMOVE the relic, and clear the record
    /// so a fresh loan can be taken at a future shop. Runs on EACH peer — directly in SP, or once per peer
    /// via the networked <c>dl_sync repaid</c> replay in co-op (RelicCmd.Remove is a local mutation).</summary>
    internal static async Task ApplyRepay(Player player)
    {
        var rec = For(player);
        // The tier the loan REACHED, computed from rooms directly (DebtCardCountFor returns 0 once Principal hits
        // 0, so it can't be used here — the loan is being cleared). tier ≥3 earns the 신용 회복 reward card.
        int rewardTier = (rec != null && player?.RunState != null)
            ? DebtLoanConfig.TargetDebtCards(player.RunState.TotalFloor - rec.LoanFloor) : 0;
        if (rec != null) { rec.Active = false; SyncToRelic(player); }   // reflect "settled" for one frame
        await DebtLoanGrants.RemoveAllDebtLoanCards(player);            // the WHOLE debt kit evaporates with the loan
        await DebtLoanGrants.RemoveRelic(player);                        // clean slate — no inert relic left behind
        // Reward for genuinely working off a DEEP, BIG debt: a permanent 신용 회복 (Credit Restored) card — but ONLY
        // if the loan hit tier 4 AND you actually PAID at least 500 gold total over its life (갚은 금액 = TotalPaid).
        // Both gates (deep + paid-a-lot) mean it's a real achievement, not farmable. tier 4 → upgraded (신용 회복+).
        if (rec != null && rewardTier >= DebtLoanConfig.RewardMinTier && rec.TotalPaid >= DebtLoanConfig.RewardMinPaid)
            await DebtLoanGrants.GrantRewardCard(player, upgraded: rewardTier >= 4);
        ResetFor(player);                                               // record gone → next loan is a fresh first loan
    }
}
