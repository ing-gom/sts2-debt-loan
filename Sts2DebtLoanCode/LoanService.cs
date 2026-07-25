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
    /// <summary>Total node-interest percent (of Borrowed) already baked into Principal, so it isn't re-charged on
    /// reload or re-fire. Replaces the old rooms counter now that the per-room rate scales with borrower count.</summary>
    internal int InterestPctApplied;

    /// <summary>How much of the charged interest has been paid off so far. Payments retire INTEREST FIRST; once
    /// InterestPaid reaches the total interest charged (origination + node), further payments eat into the borrowed
    /// principal. Drives the "remaining interest vs remaining principal" split on the ledger hover. Persisted.</summary>
    internal int InterestPaid;

    /// <summary>Card/shop debt taken on credit (debt-shop buys + card fees). Counts as PRINCIPAL and joins the node
    /// interest base (Borrowed + CardDebt), but does NOT get the origination fee (that's loans only). Persisted.</summary>
    internal int CardDebt;

    /// <summary>Absolute node interest accrued so far, in gold (base = Borrowed + CardDebt, capped at the node % cap
    /// AND the absolute <see cref="DebtLoanConfig.InterestGoldCap"/>). Tracked as gold (not a %) so a growing base
    /// from card debt doesn't retroactively rescale it. Persisted.</summary>
    internal int NodeInterestGold;

    /// <summary>TotalFloor of the shop where the loan was taken. Top-ups are allowed only at THAT shop.
    /// Rooms-since-loan (which drives the Debt-card count) is computed as TotalFloor − LoanFloor.</summary>
    internal int LoanFloor = -1;

    /// <summary>False once settled (repaid in full).</summary>
    internal bool Active = true;

    internal bool RelicGranted;

    /// <summary>Whether the 정기 납부 (Standing Order) leverage card has been handed to the deck this loan (once,
    /// on the first visit to a shop other than the loan shop). Persisted on the relic so a reload keeps it.</summary>
    internal bool DunningLetterGranted;

    /// <summary>How many of the SHOP power cards have been handed out (one per shop-revisit). Drives the fixed
    /// order — 1st = 정기 납부, the rest a per-run shuffle of the power cards. Persisted.</summary>
    internal int EventGrantCount;

    /// <summary>Total 납부 (Payment) made while this loan was active (a run-wide stat; kept for reference/telemetry).
    /// Persisted.</summary>
    internal int LifetimePayments;

    /// <summary>How many DISTINCT shops (other than the loan shop) the debtor has visited this loan — the debt shop
    /// re-rolls its rotating offer selection on each new visit (see RevealedPurchasable). Persisted.</summary>
    internal int DebtShopVisits;

    /// <summary>The last TotalFloor at which <see cref="DebtShopVisits"/> was incremented — guards against
    /// double-counting the same shop on re-entry/reload. Persisted.</summary>
    internal int LastShopVisitFloor = -1;

    /// <summary>Gold of debt taken on CARD PURCHASES at the debt shop THIS visit. Gated against
    /// <see cref="DebtLoanConfig.ShopCreditLimit"/> so you can't sweep the whole offer; resets when you enter a new
    /// shop (see the visit-tracking in NoteShopVisit). Persisted so a reload mid-shop keeps the spent total.</summary>
    internal int ShopSpentThisVisit;

    /// <summary>Type-names of the cards BOUGHT on debt at the shop this loan (so they drop out of the offer pool and
    /// show sold if still displayed). Persisted as a CSV on the relic. Cleared on repay/reset.</summary>
    internal readonly HashSet<string> PurchasedCards = new();

    /// <summary>Transient (not persisted) cache of the debt shop's current rotating offer selection + which
    /// DebtShopVisits it was rolled for, so the shown cards stay STABLE while you shop and re-roll only on a new
    /// visit. Rebuilt deterministically from (LoanFloor, DebtShopVisits) so it's fine that it resets on reload.</summary>
    internal int OfferVisit = -1;
    internal System.Type[]? CurrentOffers;

    /// <summary>PER-COMBAT transient: the 신용 불량 (Bad Credit) collection level 0..3. Reset to 0 at each
    /// combat start (by the injector) and ratcheted up by BadCreditCard every turn it sits in hand. Not
    /// persisted (it's a within-combat spiral, and it's deterministic from lockstep turn starts).</summary>
    internal int CollectionLevel;

    /// <summary>The last TotalFloor at which a debt-shop purchase dropped a native Debt curse into the deck — so
    /// the "every visit leaves a Debt" grant fires only ONCE per shop visit (per floor), no matter how many cards
    /// you buy that visit. Transient (a reload could re-grant on a re-buy in the same shop — a negligible edge);
    /// set identically on both co-op peers off the networked buy replay.</summary>
    internal int LastDebtGrantFloor = -1;
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

        await ResetPaymentsThisCombat(injectee!);   // fresh 영수증 each combat (drives 정산/청구서 scaling)

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
            relic.InterestPctApplied = rec.InterestPctApplied;
            relic.InterestPaid = rec.InterestPaid;
            relic.CardDebt = rec.CardDebt;
            relic.NodeInterestGold = rec.NodeInterestGold;
            relic.LoanFloor = rec.LoanFloor;
            relic.Active = rec.Active;
            relic.DunningLetterGranted = rec.DunningLetterGranted;
            relic.EventGrantCount = rec.EventGrantCount;
            relic.LifetimePayments = rec.LifetimePayments;
            relic.DebtShopVisits = rec.DebtShopVisits;
            relic.LastShopVisitFloor = rec.LastShopVisitFloor;
            relic.ShopSpentThisVisit = rec.ShopSpentThisVisit;
            relic.PurchasedCardsCsv = string.Join(",", rec.PurchasedCards);
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
        rec.InterestPctApplied = relic.InterestPctApplied;
        rec.InterestPaid = relic.InterestPaid;
        rec.CardDebt = relic.CardDebt;
        rec.NodeInterestGold = relic.NodeInterestGold;
        // Migration: pre-v0.9.16 saves have InterestPctApplied but no NodeInterestGold. Reconstruct it so their
        // interest doesn't reset to just origination on load.
        if (rec.NodeInterestGold == 0 && rec.InterestPctApplied > 0)
            rec.NodeInterestGold = (int)Math.Round((rec.Borrowed + rec.CardDebt) * (rec.InterestPctApplied / 100.0));
        rec.LoanFloor = relic.LoanFloor;
        rec.Active = relic.Active;
        rec.DunningLetterGranted = relic.DunningLetterGranted;
        rec.EventGrantCount = relic.EventGrantCount;
        rec.LifetimePayments = relic.LifetimePayments;
        rec.DebtShopVisits = relic.DebtShopVisits;
        rec.LastShopVisitFloor = relic.LastShopVisitFloor;
        rec.ShopSpentThisVisit = relic.ShopSpentThisVisit;
        rec.PurchasedCards.Clear();
        foreach (var s in (relic.PurchasedCardsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) rec.PurchasedCards.Add(s);
        rec.RelicGranted = true;
        EnsureRoomWatch();   // resubscribe the shop-revisit grant watcher after a load (grants power cards + counts shop visits)
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

        // The loan disbursement itself must NOT be garnished (that would short the borrowed amount on a top-up when
        // interest has already accrued). The guard is read synchronously by the GainGold garnish prefix.
        _grantingFor = player;
        try { await PlayerCmd.GainGold(amount, player, false); }
        finally { _grantingFor = null; }
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
        EnsureRoomWatch();   // watch shop revisits: grant the REMAINING power cards (slots 1-6) + count debt-shop visits
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

    // ── 외상 카드 구매 (buy the non-power cards on debt at the shop) ──────────────────────────────
    // The non-power cards are no longer earned in combat — the debtor BUYS them at a shop, taking on debt.
    // The offer list grows per shop visit (see RevealedPurchasable); price scales with card strength.

    /// <summary>Debt price of a purchasable card, by strength tier. owed is a SOFT cost (it only raises the repay
    /// total — not shop inflation, node interest, or curse tiers), so the real limiter is the per-visit reveal
    /// count × how many shops a run has; the price is the relative signal + a repay-build tax.</summary>
    private const int PriceMin = 40, PriceMax = 70;   // shop price band (before sale) — lowered from 50/80

    /// <summary>Base tier price (centre of the band; the shown base is this ± variance, clamped to [40,70]).
    /// Tiers lowered ~10 so card debt piles up more slowly (paired with the tighter per-shop credit limit).</summary>
    internal static int CardDebtPrice(System.Type t)
    {
        if (t == typeof(InvoiceCard) || t == typeof(GarnishmentCard) || t == typeof(BankruptcyCard) || t == typeof(RefinanceCard)) return 65;   // 고급: scaling attack / AoE / debt payoff
        if (t == typeof(JobPlacementCard)) return 55;   // 취업알선: income skill
        if (t == typeof(RefundCard) || t == typeof(CounterclaimCard)
            || t == typeof(StatementCard) || t == typeof(InterestSupportCard)
            || t == typeof(PaymentBenefitCard)
            || t == typeof(CollectionCard)) return 60;   // 파워 엔진(영구 가치)
        if (t == typeof(SettlementCard) || t == typeof(LoanStrikeCard) || t == typeof(MortgageCard)) return 55;   // 중급
        if (t == typeof(BloodPaymentCard)) return 45;   // 기본: HP-payment utility
        return 55;
    }

    /// <summary>The pre-sale shown price: tier base ± a deterministic variance (−10..+10 in 5s), clamped to the
    /// [50,80] band. Deterministic per (LoanFloor, visit, card). This is the "original" price struck through on a
    /// sale card.</summary>
    internal static int ShopBasePrice(LoanRecord rec, System.Type t)
    {
        int idx = System.Array.IndexOf(PurchasablePool, t);
        var rng = new System.Random(unchecked(rec.LoanFloor * 911 + rec.DebtShopVisits * 277 + idx * 53 + 7));
        int variance = rng.Next(-2, 3) * 5;   // −10, −5, 0, +5, +10
        return Math.Clamp(CardDebtPrice(t) + variance, PriceMin, PriceMax);
    }

    /// <summary>Which of this visit's offers is ON SALE — a deterministic pick from the revealed set (per LoanFloor
    /// + visit), like the merchant's discounted card. Its <see cref="ShopPriceFor"/> is knocked down ~30%.</summary>
    internal static System.Type? SaleCardFor(LoanRecord rec)
    {
        var offers = RevealedPurchasable(rec);
        if (offers.Length == 0) return null;
        var rng = new System.Random(unchecked(rec.LoanFloor * 333 + rec.DebtShopVisits * 97 + 3));
        return offers[rng.Next(offers.Length)];
    }

    /// <summary>The actual debt price of a card at the shop THIS visit: its tier base ± a deterministic variance
    /// (−10%..+15%, rounded to 5), then ~30% off if it's the visit's sale card. Deterministic per (LoanFloor,
    /// visit, card) → co-op peers + a reload agree; the shown price == the charged price (BuyCardOnDebt uses this).</summary>
    internal static int ShopPriceFor(LoanRecord rec, System.Type t)
    {
        int price = ShopBasePrice(rec, t);
        if (SaleCardFor(rec) == t) price = Math.Max(5, (int)Math.Round(price * 0.7 / 5.0) * 5);   // sale card ~30% off
        return price;
    }

    /// <summary>The cards on offer at the shop THIS visit — a rotating <see cref="ShopOfferCount"/>-card selection
    /// drawn from the not-yet-bought pool, deterministic per (LoanFloor, DebtShopVisits) so both co-op peers + a
    /// reload agree, and re-rolled only when you enter a NEW shop. Cached on the record so the selection is STABLE
    /// while you shop (buying one card doesn't reshuffle the rest); a bought card drops out on the next visit.</summary>
    internal static System.Type[] RevealedPurchasable(LoanRecord rec)
    {
        if (rec.CurrentOffers != null && rec.OfferVisit == rec.DebtShopVisits) return rec.CurrentOffers;
        var available = new List<System.Type>();
        foreach (var t in PurchasablePool) if (!rec.PurchasedCards.Contains(t.Name)) available.Add(t);
        System.Type[] offers;
        if (available.Count <= ShopOfferCount) offers = available.ToArray();
        else
        {
            var rng = new System.Random(unchecked(rec.LoanFloor * 31 + rec.DebtShopVisits * 101 + 13));
            for (int i = available.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (available[i], available[j]) = (available[j], available[i]); }
            offers = available.GetRange(0, ShopOfferCount).ToArray();
        }
        rec.CurrentOffers = offers;
        rec.OfferVisit = rec.DebtShopVisits;
        return offers;
    }

    /// <summary>An offered card is buyable if the loan is active, it's been revealed, and it hasn't been bought yet.</summary>
    internal static bool IsPurchasable(LoanRecord rec, System.Type t)
        => rec.Active && !rec.PurchasedCards.Contains(t.Name) && System.Array.IndexOf(RevealedPurchasable(rec), t) >= 0;

    /// <summary>Gold of debt-shop credit still available THIS visit (limit minus what's already been spent on cards
    /// this visit). Separate from the initial loan's HardCap. Never negative.</summary>
    internal static int RemainingShopCredit(LoanRecord rec)
        => System.Math.Max(0, DebtLoanConfig.ShopCreditLimit - rec.ShopSpentThisVisit);

    /// <summary>Can this offer be bought right now given the per-visit credit line? (Its price fits the remaining
    /// credit.) The panel greys offers that fail this, and BuyCardOnDebt refuses them.</summary>
    internal static bool CanAffordCredit(LoanRecord rec, System.Type t)
        => ShopPriceFor(rec, t) <= RemainingShopCredit(rec);

    /// <summary>Buy a revealed non-power card on debt: adds its price onto what you owe and drops the card into the
    /// deck (like every other debt card — removed on full repay). Marks it sold so the shop won't re-sell it.
    /// internal so the self-test can invoke it directly (what clicking a shop offer does).</summary>
    internal static async Task<bool> BuyCardOnDebt(Player player, System.Type type)
    {
        var rec = For(player);
        if (rec == null || !IsPurchasable(rec, type)) return false;
        if (!CanAffordCredit(rec, type)) return false;   // over this shop's credit line → refuse (panel already greys it)
        int price = ShopPriceFor(rec, type);          // the shown price (tier ± variance, sale applied)

        // Only the shopper's OWN peer initiates the buy (the panel is local to the player who opened it). Then:
        // SP → apply here; co-op → broadcast so the deck-add + owed-increase replay identically on BOTH peers
        // (a local-only purchase would leave the partner's replica of this player's owed/deck/sold-set diverged
        // → checksum drop). Mirrors GrantLoanDirect / Repay. The price rides the wire so it can't drift.
        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        if (sp) await ApplyBuyCard(player, type.Name, price);
        else    DebtLoanNet.BroadcastBuy(player, type.Name, price);
        MainFile.Logger.Info($"[{MainFile.ModId}] buy {type.Name} on debt for {price} ({(sp ? "SP local" : "co-op broadcast")}).");
        return true;
    }

    /// <summary>Apply a debt-shop purchase on THIS peer: add the price onto what's owed, mark it sold, and drop
    /// the card into the deck. Runs directly in SP, or once per peer via the networked <c>dl_sync buy</c> replay
    /// in co-op. Idempotent — the sold-mark guards a double-apply on the initiator's own replay / any re-delivery,
    /// so each peer charges the price and grants the card exactly once.</summary>
    internal static async Task ApplyBuyCard(Player player, string typeName, int price)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return;
        if (rec.PurchasedCards.Contains(typeName)) return;      // already bought → no-op (idempotent)
        var type = System.Array.Find(PurchasablePool, t => t.Name == typeName);
        if (type == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] dl_sync buy: unknown card '{typeName}'."); return; }

        rec.Principal += price;                                 // owed goes up; no gold gained (bought on credit)
        rec.CardDebt += price;                                  // card debt = principal that also accrues node interest
        rec.ShopSpentThisVisit += price;                        // count against this shop's per-visit credit line
        rec.PurchasedCards.Add(typeName);
        await DebtLoanGrants.GrantCard(player, type);   // fly-in shows again now the panel sits at the shop's layer depth
        // Every debt-shop VISIT leaves a native Debt curse in your deck — the price of leaning on the credit line.
        // Once per floor (= per shop visit), no matter how many cards you buy that visit; swept on repay. Runs in the
        // same per-peer networked buy replay as the card grant, and reads shared floor state → co-op consistent.
        if (player.RunState != null && rec.LastDebtGrantFloor != player.RunState.TotalFloor)
        {
            rec.LastDebtGrantFloor = player.RunState.TotalFloor;
            await DebtLoanGrants.GrantNativeDebt(player);
        }
        SyncToRelic(player);
        // No refresh event needed: NDebtCardShopPanel polls its refreshers every frame in _Process, so the sold
        // state greys out on the next frame once the (possibly deferred co-op replay) purchase lands here.
    }

    /// <summary>Players in the run currently carrying an ACTIVE loan (Principal &gt; 0). Read from shared run state
    /// so it's identical on every co-op peer — this is the ONLY input to the MP interest scaling, which keeps that
    /// scaling lockstep-deterministic (never read a local/UI value here).</summary>
    internal static int BorrowerCount(IRunState? run)
    {
        if (run?.Players == null) return 0;
        int n = 0;
        foreach (var p in run.Players) { var r = For(p); if (r != null && r.Active && r.Principal > 0) n++; }
        return n;
    }

    /// <summary>Accrue per-room interest into the owed Principal as a percentage of Borrowed, tracked as the total
    /// percent baked so far (<see cref="LoanRecord.InterestPctApplied"/>) — idempotent across re-fires and reloads.
    /// <para>Both the RATE and the CAP scale with how many players carry a loan (N = <see cref="BorrowerCount"/>),
    /// so debt shared by more people is harsher:</para>
    /// <list type="bullet">
    /// <item>rate  = NodeInterestPct × N per room (5% solo → 20% at 4 debtors: accrues faster)</item>
    /// <item>cap   = base (NodeInterestPct × MaxNodeInterestRooms = 40%) + min(MpExtraCapMax, MpExtraCapPerBorrower × (N−1))</item>
    /// </list>
    /// SP is exactly the old behaviour (N=1 → 5%/room, 40% cap). N comes only from shared run state, so both co-op
    /// peers compute the same target; if a partner repays and N drops, we never REFUND already-accrued interest.</summary>
    internal static void AccrueNodeInterest(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || player?.RunState == null) return;
        int roomsCarried = Math.Max(0, player.RunState.TotalFloor - rec.LoanFloor);
        if (roomsCarried <= 0) return;

        int n = Math.Max(1, BorrowerCount(player.RunState));              // this player is active → at least 1
        int perRoomPct = DebtLoanConfig.NodeInterestPct * n;             // 5% × N — faster accrual with more debtors
        int capPct = NodeInterestCapPct(player);                         // 40% SP (grows with borrower count in co-op)
        int targetPct = Math.Min(capPct, perRoomPct * roomsCarried);     // node-interest % that should be baked by now

        // Node interest is charged as GOLD on the whole interest base (loan + card debt), capped two ways: by the
        // node % AND by the absolute InterestGoldCap (total interest = origination + node never exceeds it). Tracked
        // as absolute gold so growing card debt doesn't retroactively rescale already-accrued interest.
        int baseAmt = rec.Borrowed + rec.CardDebt;
        int origination = (int)Math.Round(rec.Borrowed * (DebtLoanConfig.BorrowOriginationPct / 100.0));
        int maxNodeGold = Math.Max(0, DebtLoanConfig.InterestGoldCap - origination);   // absolute ceiling minus origination
        int targetNodeGold = Math.Min(maxNodeGold, (int)Math.Round(baseAmt * (targetPct / 100.0)));
        if (targetNodeGold <= rec.NodeInterestGold) { rec.InterestPctApplied = Math.Max(rec.InterestPctApplied, targetPct); return; }
        int add = targetNodeGold - rec.NodeInterestGold;
        rec.Principal += add;
        rec.NodeInterestGold = targetNodeGold;
        rec.InterestPctApplied = targetPct;   // kept for the garnishment "interest maxed" check
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
            foreach (var p in run.Players) { CountShopVisit(p); TryGrantDunningLetter(p); }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] room-watch grant failed: {e.Message}"); }
    }

    /// <summary>Count a DISTINCT shop visit (other than the loan shop) — drives how many cards the debt-card shop
    /// reveals (visit 1 → 3, 2 → 5, 3+ → all). Guarded by LastShopVisitFloor so re-entry/reload doesn't double-count.</summary>
    private static void CountShopVisit(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || player.RunState == null) return;
        int floor = player.RunState.TotalFloor;
        if (floor == rec.LoanFloor || floor == rec.LastShopVisitFloor) return;
        rec.LastShopVisitFloor = floor;
        rec.DebtShopVisits++;
        rec.ShopSpentThisVisit = 0;   // fresh credit line at each new shop (see DebtLoanConfig.ShopCreditLimit)
        rec.PurchasedCards.Clear();   // "sold" is per-VISIT now → cards can be re-bought next shop (dupes allowed), just not twice in one visit
        SyncToRelic(player);
    }

    /// <summary>Grant the 정기 납부 once per loan, when the debtor enters a shop that isn't the one they borrowed
    /// at (TotalFloor != LoanFloor). Deck mutation is local + deterministic → the same card lands on each peer.</summary>
    // SHOP AUTO-GRANT channel = ONLY 정기 납부 (the repay engine) handed out free, once, at LOAN TIME. Everything
    // else — 취업알선(income), 납부혜택, and the rest — is BOUGHT at the debt shop (see PurchasablePool) and can be
    // re-bought on later visits for duplicates (just not twice in one visit).
    private static readonly System.Type[] FixedOrder =
    {
        typeof(DunningLetterCard),    // slot 0 — granted at loan time (정기 납부, the repay engine); the ONLY free card
    };
    private static readonly System.Type[] RemainderPool = System.Array.Empty<System.Type>();   // 취업알선 moved to the shop
    private const int TotalEventCards = 1;   // FixedOrder(1) only — 정기 납부 is the single free starter card

    // PURCHASABLE pool = everything NOT auto-granted: the 4 remaining POWER engines + the 6 non-power cards. The
    // debtor BUYS these on debt at the shop (see BuyCardOnDebt); removed on repay like every other debt card. The
    // shop shows a rotating ShopOfferCount-card selection per visit (see RevealedPurchasable), like a real merchant.
    private static readonly System.Type[] PurchasablePool =
    {
        typeof(RefundCard), typeof(CounterclaimCard), typeof(StatementCard), typeof(InterestSupportCard),  // power engines
        typeof(PaymentBenefitCard),                                                                         // 납부혜택: payment → block (moved from free grants)
        typeof(CollectionCard),                                                                             // 추심: 공격판 환급 (scaling attack gen)
        typeof(SettlementCard), typeof(InvoiceCard), typeof(GarnishmentCard),                              // receipt spenders
        typeof(LoanStrikeCard), typeof(MortgageCard), typeof(BloodPaymentCard),                            // borrow / HP
        typeof(JobPlacementCard),                                                                          // 취업알선: income skill (moved from free grants)
        typeof(BankruptcyCard), typeof(RefinanceCard),                                                     // debt payoff: Bankruptcy(→Strength) / Refinance(→Payment cards)
    };
    private const int ShopOfferCount = 5;   // cards displayed per shop visit (rotating), like the merchant's card row

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

    /// <summary>Total interest CHARGED on the loan so far = origination (20% of the borrowed loan) + the accrued node
    /// interest gold (on loan + card debt, absolute-capped). Grows as node interest accrues; independent of payments.</summary>
    internal static int InterestChargedNow(LoanRecord rec)
        => (int)Math.Round(rec.Borrowed * (DebtLoanConfig.BorrowOriginationPct / 100.0)) + rec.NodeInterestGold;

    /// <summary>Interest still owed = charged − already paid (never below 0). Payments clear this before principal.</summary>
    internal static int InterestRemaining(LoanRecord rec) => Math.Max(0, InterestChargedNow(rec) - rec.InterestPaid);

    /// <summary>A payment drained gold toward the loan. INTEREST FIRST: the payment retires outstanding interest
    /// before it eats into the borrowed principal (tracked via <see cref="LoanRecord.InterestPaid"/>); the total owed
    /// (<see cref="LoanRecord.Principal"/> = shop repay cost) always drops by the full amount. Pure record math; runs
    /// deterministically on both co-op peers in the lockstep combat. (principalShareOverride is now ignored — kept
    /// for signature compatibility with existing callers.)</summary>
    internal static async Task AccrueInterest(Player player, int drained, double? principalShareOverride = null)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || drained <= 0) return;
        rec.TotalPaid += drained;
        int toInterest = Math.Min(drained, InterestRemaining(rec));   // interest first
        rec.InterestPaid += toInterest;                               // the rest (drained − toInterest) is principal
        rec.Principal = Math.Max(0, rec.Principal - drained);         // total owed drops by the full payment
        SyncToRelic(player);
        await Task.CompletedTask;
    }

    /// <summary>취업알선 (Job Placement) placement fee: add <paramref name="amount"/> gold straight onto what you
    /// OWE (the shop repay cost / relic badge). You do NOT receive the gold — it's a fee, not a loan, so no gold
    /// enters your pocket and there is no surcharge. The payoff is the lump of 품삯 (Wages) the skill hands you on
    /// play. Needs an active loan. Pure record math off a lockstep card play → co-op safe.</summary>
    internal static void AddCombatDebt(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        rec.Principal += amount;   // owed goes up; the player gains no gold (it's a fee, not a loan)
        rec.CardDebt += amount;    // card-fee debt = principal that also accrues node interest
        SyncToRelic(player);
    }

    // ── 납부 (Payment) resource system ─────────────────────────────────────────
    // 영수증 (payment tally) is a CUSTOM combat resource, shown on its own HUD counter near the energy orb (like
    // Regent's Stars but our own, so no collision). It is NOT a power buff — it lives here as a per-combat value and
    // raises TallyChanged so the custom NPaymentTallyCounter node updates. Cards read it and CONSUME it. The value is
    // computed the same way on every peer (payments are lockstep), so the display stays in sync; the counter is local.
    private static readonly ConditionalWeakTable<Player, int[]> _tally = new();

    /// <summary>PER-COMBAT monotonic count of 납부 (Payment) cards played this combat — bumped on every
    /// <see cref="RecordPayment"/> and, UNLIKE the spendable 영수증 tally, NEVER consumed. 성실 납부
    /// (Diligent Payment) scales its Block off this so 정산/청구서 draining the tally doesn't shrink it. Reset
    /// each combat by <see cref="ResetPaymentsThisCombat"/>. Deterministic (payments are lockstep) → co-op safe.</summary>
    private static readonly ConditionalWeakTable<Player, int[]> _paymentCount = new();

    /// <summary>How many 납부 (DebtCurseCard) cards have been PLAYED-and-exhausted this combat. 성실 납부 (Diligent
    /// Payment) scales its Block off this — one Block per Payment card you actually spent (bailouts/other RecordPayment
    /// paths do NOT count; only a real 납부 card leaving via play). Monotonic, never consumed; reset each combat by
    /// <see cref="ResetPaymentsThisCombat"/>. Bumped by <see cref="RecordExhaustedPaymentCard"/> from DebtCurseCard.OnPlay.
    /// Deterministic (card plays are lockstep) → co-op safe.</summary>
    internal static int PaymentCountThisCombat(Player? p)
        => p != null && _paymentCount.TryGetValue(p, out var a) ? a[0] : 0;

    /// <summary>Count one 납부 (DebtCurseCard) card played-and-exhausted this combat (drives 성실 납부's Block).
    /// Called from DebtCurseCard.OnPlay so ONLY spent Payment cards count — not bailouts or other payment paths.</summary>
    internal static void RecordExhaustedPaymentCard(Player? p)
    {
        if (p != null) _paymentCount.GetValue(p, _ => new int[1])[0]++;
    }

    /// <summary>Fired whenever a player's 영수증 changes → the HUD counter re-renders. (player, newValue).</summary>
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

    /// <summary>Bank <paramref name="n"/> extra 영수증 (receipts). 납부 calls this for the BONUS receipt when you
    /// actually paid gold — the base receipt already came from <see cref="RecordPayment"/>. HUD updates via SetTally.</summary>
    internal static void GrantReceipt(Player? p, int n = 1)
    {
        if (p != null && n != 0) SetTally(p, PaymentsThisCombat(p) + n);
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

    // ── 파산 (Bankruptcy) — blocks ALL gold gain (in-combat AND the post-combat reward) until the next fight ──────
    private static readonly ConditionalWeakTable<Player, bool[]> _bankrupt = new();

    /// <summary>True while 파산 선언 (Declare Bankruptcy) has locked this player out of gold. The power's in-combat
    /// ModifyGoldGained covers the fight; this flag is what BankruptGoldBlockPatch reads to ALSO block the
    /// post-combat reward gold (the power is gone by then). Cleared at the next combat start (below).</summary>
    internal static bool IsBankrupt(Player? p) => p != null && _bankrupt.TryGetValue(p, out var a) && a[0];

    /// <summary>Set by 파산 선언 on play — no gold this fight, including the victory reward, until the next combat.</summary>
    internal static void SetBankrupt(Player? p) { if (p != null) _bankrupt.GetValue(p, _ => new bool[1])[0] = true; }

    /// <summary>Clear the tally at combat start (fresh each fight).</summary>
    internal static Task ResetPaymentsThisCombat(Player p)
    {
        SetTally(p, 0);
        if (_paymentCount.TryGetValue(p, out var a)) a[0] = 0;   // reset the monotonic 납부 count too
        if (_bankrupt.TryGetValue(p, out var bk)) bk[0] = false;  // 파산 clears at the next fight (reward of the PAST fight was already blocked)
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
        SetTally(player, PaymentsThisCombat(player) + 1);   // 영수증 +1 → HUD counter updates
        // NOTE: 성실 납부's block counter (_paymentCount) is NOT bumped here — it counts only 납부 CARDS actually
        // played-and-exhausted (RecordExhaustedPaymentCard from DebtCurseCard.OnPlay), not every payment path.
        if (rec0 != null && rec0.Active) { rec0.LifetimePayments++; SyncToRelic(player); }   // milestone counter (combat cards)
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
        // NOTE: 추심 (CollectionPower) no longer triggers on payment — it grants Vigor at each turn start (see
        // CollectionPower.AfterPlayerTurnStart), scaling off the 영수증 tally, so nothing is wired here.

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
        rec.InterestPaid += Math.Min(cut, InterestRemaining(rec));   // interest first (same order as a normal payment)
        rec.Principal = Math.Max(0, rec.Principal - cut);
        rec.TotalPaid += cut;
        if (rec.Principal <= 0) rec.Active = false;   // spiral self-terminates; relic removed at next shop
        SyncToRelic(player);
    }

    // Set only while the loan disbursement's GainGold runs, so garnishment doesn't skim the borrowed gold itself.
    private static Player? _grantingFor;

    /// <summary>The node-interest ceiling (%) for this player — base 40% (NodeInterestPct × MaxNodeInterestRooms),
    /// grown by borrower count in co-op. When <see cref="LoanRecord.InterestPctApplied"/> reaches this, interest is
    /// MAXED. Shared by the interest accrual and the garnishment trigger so they can't drift.</summary>
    internal static int NodeInterestCapPct(Player? player)
    {
        int n = Math.Max(1, player?.RunState != null ? BorrowerCount(player.RunState) : 1);
        int baseCap = DebtLoanConfig.NodeInterestPct * DebtLoanConfig.MaxNodeInterestRooms;   // 40% SP
        return baseCap + Math.Min(DebtLoanConfig.MpInterestExtraCapMaxPct,
                                  DebtLoanConfig.MpInterestExtraCapPerBorrowerPct * (n - 1));
    }

    /// <summary>Creditor garnishment: ONLY once the loan's interest has hit its MAXIMUM (node interest at the cap)
    /// does the creditor start withholding a share (<see cref="DebtLoanConfig.GarnishMaxPct"/>) of GOLD INCOME and
    /// applying it straight to the debt as forced repayment. Below max interest → nothing. Returns the gold garnished
    /// (≤ income, ≤ remaining principal); the caller hands the player income − garnished. Deterministic per-player
    /// record math (ForceRepayPrincipal) → co-op mirrors it as each peer replays the gold gain.</summary>
    internal static int GarnishIncome(Player player, int income)
    {
        if (ReferenceEquals(_grantingFor, player)) return 0;   // don't garnish the loan disbursement itself
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || income <= 0) return 0;
        if (rec.InterestPctApplied < NodeInterestCapPct(player)) return 0;   // only when interest is MAXED
        int ratePct = DebtLoanConfig.GarnishMaxPct;
        if (ratePct <= 0) return 0;
        int garnish = Math.Min(rec.Principal, (int)Math.Floor(income * (ratePct / 100.0)));
        if (garnish <= 0) return 0;
        ForceRepayPrincipal(player, garnish);
        return garnish;
    }

    // ── MP 대납 (Bailout) — help a teammate pay down their debt ────────────────────────────────────────
    /// <summary>Co-op 대납 (Bailout): the <paramref name="payer"/> spends <paramref name="amount"/> gold to make a
    /// 납부 (Payment) on the <paramref name="debtor"/>'s behalf. It routes through <see cref="RecordPayment"/> so it
    /// is a REAL payment FOR THE DEBTOR — their 영수증 (payment tally) accumulates, their payment powers fire, and it
    /// settles their loan mid-combat if it clears (lifts their curses). Only the GOLD comes from the payer; the
    /// principal write-down + tally are the debtor's. Runs INSIDE the lockstep card play (the debtor's CombatId rides
    /// the play action), so both peers resolve the same debtor and apply identically — no broadcast, like any combat
    /// card. The payer covers what they can afford. Returns the gold actually applied (0 if the target owes nothing
    /// or the payer is broke).</summary>
    internal static async Task<int> ApplyBailout(PlayerChoiceContext cc, Player payer, Player debtor, int amount)
    {
        var rec = For(debtor);
        if (rec == null || !rec.Active || rec.Principal <= 0 || amount <= 0) return 0;
        int cut = Math.Min(Math.Min(rec.Principal, amount), (int)payer.Gold);   // pay what you can afford, capped at the debt
        if (cut <= 0) return 0;

        await PlayerCmd.LoseGold(cut, payer, GoldLossType.Spent);   // payer foots the bill (lockstep card play → no RewardSync)
        await RecordPayment(debtor, cc, cut);                       // a real 납부 FOR THE DEBTOR: tally(영수증)++, powers fire, settle-on-zero
        MainFile.Logger.Info($"[{MainFile.ModId}] bailout: {payer.NetId} paid {cut}g toward {debtor.NetId}'s debt (owed now {For(debtor)?.Principal ?? 0}).");
        return cut;
    }

    /// <summary>MP 대납 (Bailout) on a MISSED payment: a 납부 (Payment) card left unplayed is about to Ethereal-exhaust
    /// for nothing, so hand the RICHEST teammate who can afford it (gold ≥ <see cref="BailoutCard.BailoutGold"/>) a
    /// 대납 card — Ethereal+Exhaust, upgraded to match a 빚 독촉+ — a fleeting chance to cover the debtor this turn. If
    /// NO teammate can afford one, nothing is injected. Runs in the lockstep turn-end-in-hand path over shared state
    /// (players + gold), so both peers pick the same recipient and deal the same card — co-op-safe, no broadcast.</summary>
    internal static async Task GrantBailoutForMissedPayment(Player debtor, bool upgraded)
    {
        if (RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true) return;   // co-op only
        var combat = debtor?.Creature?.CombatState;
        var run = RunManager.Instance?.State;
        if (combat == null || run?.Players == null || debtor == null) return;

        // Richest OTHER player who can actually pay a bailout. Deterministic: reads shared gold, strict > keeps the
        // FIRST in the (identical-on-both-peers) player order on a tie.
        Player? recipient = null;
        foreach (var p in run.Players)
        {
            if (p == debtor || p.Creature == null) continue;
            if ((int)p.Gold < BailoutCard.BailoutGold) continue;
            if (recipient == null || p.Gold > recipient.Gold) recipient = p;
        }
        if (recipient == null) return;   // nobody can afford it → no bailout injected

        var card = combat.CreateCard<BailoutCard>(recipient);
        if (card == null) return;
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }   // 빚 독촉+ missed → 대납+ (0-cost)
        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { card }, PileType.Hand, recipient, CardPilePosition.Top);
        MainFile.Logger.Info($"[{MainFile.ModId}] missed payment by {debtor.NetId} → bailout{(upgraded ? "+" : "")} to {recipient.NetId} (gold {(int)recipient.Gold}).");
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
