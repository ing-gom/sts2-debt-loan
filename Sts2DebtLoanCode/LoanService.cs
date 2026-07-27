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

    /// <summary>How many times gold has actually been DRAWN on this loan (the first borrow + every top-up).
    /// Capped by <see cref="DebtLoanConfig.MaxLoanDraws"/>: the gold cap alone let a debtor nibble a shop clean in
    /// unlimited small loans, so the draws themselves are the scarce thing now — 300 gold split across 3 decisions.
    /// Counts MERCHANT-ITEM loans only; debt-shop card buys have their own per-visit credit line. Dies with the
    /// record on full repay, so clearing the debt restores a fresh set of draws (that's what 신용 회복 means).
    /// Persisted on the relic.</summary>
    internal int LoanDraws;

    /// <summary>Whether this loan's ONE 채무 조정 (Restructuring) write-off has been spent. Once true the card can no
    /// longer be played and the debt shop stops stocking it — without that gate it would be re-buyable at every shop
    /// (the sold-set clears per visit) for far less debt than it forgives, i.e. an infinite principal deleter. Dies
    /// with the record on full repay, so a LATER loan is a new agreement and gets its own. Persisted on the relic.</summary>
    internal bool RestructuringUsed;

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
            relic.RestructuringUsed = rec.RestructuringUsed;
            relic.LoanDraws = rec.LoanDraws;
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
        rec.RestructuringUsed = relic.RestructuringUsed;
        rec.LoanDraws = relic.LoanDraws;
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

    internal static bool ActAllowsLoan(Player player)
        => player.RunState.CurrentActIndex <= DebtLoanConfig.MaxLoanActIndex;

    internal static int RemainingRoom(Player player)
    {
        // ★MaxLoan ≤ 0 = NO gold cap: the DRAW COUNT is the only limit, so three draws can theoretically finance
        // three relics. The old 300/400 cap made the draw limit nearly redundant — MinLoan is 100, so the hard cap
        // already allowed at most 4 draws and "3" only shaved one off. Now the question is purely "is this item
        // worth one of my three?", which is the decision the limit was added to create.
        if (DebtLoanConfig.MaxLoan <= 0) return int.MaxValue;
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
        if (DrawsLeft(rec) <= 0) return false;               // spent all draws on this loan → the merchant is done
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
        // One call = one DRAW (the first borrow and every top-up both land here). Counted on the APPLIED path, which
        // SP runs directly and co-op replays on BOTH peers via dl_sync — so the count converges without widening the
        // wire (adding a 5th broadcast arg would break version parity, see coop-guard).
        rec.LoanDraws++;
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
    // Price band (before sale). Raised 40/70 → 60/95 when slot 0 became a FREE gift: two paid offers must NOT fit
    // inside one visit's credit line (2 × 60 = 120 > 120 is false by 0 — see ShopCreditLimit's note, the min pair is
    // 60+65 = 125 once the cheapest tier is 65), while a SALE card plus one more must fit.
    private const int PriceMin = 60, PriceMax = 95;   // shop price band (before sale)

    /// <summary>Base tier price (centre of the band; the shown base is this ± variance, clamped to [40,70]).
    /// Tiers lowered ~10 so card debt piles up more slowly (paired with the tighter per-shop credit limit).</summary>
    internal static int CardDebtPrice(System.Type t)
    {
        // ★Prices were lifted ~20 when slot 0 became a FREE gift: one offer per visit is now given away, so the paid
        // slots must cost enough that the per-visit credit line buys ONE of them (see the band + ShopCreditLimit).
        if (t == typeof(RestructuringCard)) return 90;   // 채무 조정: once-per-loan write-off — the priciest offer (eats a whole visit's credit line)
        if (t == typeof(InvoiceCard) || t == typeof(GarnishmentCard) || t == typeof(BankruptcyCard) || t == typeof(RefinanceCard)
            || t == typeof(PromissoryNoteCard) || t == typeof(LeverageCard)) return 85;   // 고급: scaling attack / AoE / debt payoff / tempo / principal-scaled attack
        if (t == typeof(JobPlacementCard) || t == typeof(KitingCard)) return 75;   // 취업알선 / 돌려막기: income skills
        // ★파워 엔진 6종을 성능에 따라 3단으로 벌린다. 예전엔 전부 80이었는데, 빚 상점은 한 방문에 유료
        // 카드를 딱 한 장만 살 수 있으므로(ShopCreditLimit), 값이 같으면 플레이어는 매번 상위 두 장만 집고
        // 나머지 넷은 영구히 팔리지 않는다. 값을 벌려야 "명세서를 95에 살까, 추심을 75에 사고 남길까"가
        // 실제 선택이 된다. 등급 근거는 BALANCE_AUDIT.md(게임 본편 548장 분포 대조).
        // 차입: 턴당 에너지는 이 세트에서 가장 큰 효과라 최고가. 영수증 4라는 자체 관문이 이미 세지만,
        // 골드 가격까지 최상단에 둬야 "이번 방문의 신용 한도를 통째로 여기 쓸 것인가"가 성립한다.
        if (t == typeof(BorrowingCard)) return 95;
        // 경비 처리: 나머지 영수증 카드를 전부 싸게 만드는 인에이블러 → 강 티어. 단독으론 전투 효과가 0이라
        // 차입보다는 아래.
        if (t == typeof(ExpensingCard)) return 90;
        if (t == typeof(StatementCard) || t == typeof(PaymentBenefitCard)) return 95;   // 강: 매 턴 드로우 / 판금 순 +2턴 누적
        if (t == typeof(RefundCard) || t == typeof(CounterclaimCard)) return 85;        // 중: 성실 납부 공급 / 납부마다 5피해
        if (t == typeof(CollectionCard) || t == typeof(InterestSupportCard)) return 75; // 약: 2코 선불+영수증 재지불 / 전투 효과 0
        // ⚠️약 티어를 65로 내리지 말 것: 서로 다른 두 장이 60+60=120이 되어 "유료 정가 2장은 한 방문에
        // 들어가지 않는다"는 불변식이 깨진다(75면 최저 조합이 60+65=125로 유지). solo-verify가 잡아낸다.
        if (t == typeof(SettlementCard) || t == typeof(LoanStrikeCard) || t == typeof(MortgageCard)) return 75;   // 중급
        if (t == typeof(BloodPaymentCard)) return 65;   // 기본: HP-payment utility
        return 75;
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

    /// <summary>Is this a 빚 (Debt) card — the ONE card type 차환 / 돌려막기 may consume? ONLY the game's native
    /// <see cref="MegaCrit.Sts2.Core.Models.Cards.Debt"/>, matching what 파산 선언 already eats. Deliberately
    /// EXCLUDES:
    /// <list type="bullet">
    /// <item>our tier curses (연체 / 차압 / 신용 불량 / 강제 징수) — those are the loan's escalating pressure and
    /// must not be cashable away; they are re-injected every combat anyway, which would make them an infinite
    /// fuel supply for 돌려막기.</item>
    /// <item>납부 (<see cref="DebtCurseCard"/>) — the repayment card, not a debt.</item>
    /// <item>every OTHER curse in the game (후회, 부상, …).</item>
    /// </list>
    /// Single source of truth so the consumers can never drift apart. Native Debt enters the deck only from a
    /// debt-shop visit or from 차환 itself, so this is a CLOSED loop: 차환 mints the fuel that 차환/돌려막기 burn.</summary>
    internal static bool IsDebtCurseCard(CardModel c) => c is MegaCrit.Sts2.Core.Models.Cards.Debt;

    // ── The FREE offer (leftmost slot) ────────────────────────────────────────────────────────────────────
    // Slot 0 of every visit's row is a GIFT: no debt added, no credit-line spend, and — crucially — no native Debt
    // curse. The debt shop stops being a pure "pay to get deeper" screen and always has one thing worth walking in
    // for, while the loan pressure still lives entirely on the paid slots to its right. Which card lands there is
    // the same deterministic shuffle as the rest, so it varies per visit (a guaranteed-floor variable reward) and
    // both co-op peers agree without any extra wire.

    /// <summary>Cards that must never occupy the free slot: anything that rewrites the LOAN RECORD itself. 채무 조정
    /// forgives 250 principal — free, that's a −250 gift you could just wait for, so it stays a paid offer.
    /// (Everything else is safe: the payment-set cards only pay off while you're in debt, and 돌려막기's native-Debt
    /// fuel is minted by PAID purchases only, so a free copy can't bootstrap anything.)</summary>
    private static bool FreeSlotIneligible(System.Type t) => t == typeof(RestructuringCard);

    /// <summary>Is this the visit's FREE offer (slot 0)? Single source of truth for the price, the credit gate,
    /// the sale/upgrade exclusions and the panel's "FREE" tag.</summary>
    internal static bool IsFreeOffer(LoanRecord rec, System.Type t)
    {
        var offers = RevealedPurchasable(rec);
        return offers.Length > 0 && offers[0] == t;
    }

    /// <summary>Which of this visit's offers is ON SALE — a deterministic pick from the revealed set (per LoanFloor
    /// + visit), like the merchant's discounted card. Its <see cref="ShopPriceFor"/> is knocked down ~30%.
    /// Slot 0 is excluded: it's already free, and a discount tag on a free card reads as a bug.</summary>
    internal static System.Type? SaleCardFor(LoanRecord rec)
    {
        var offers = RevealedPurchasable(rec);
        if (offers.Length <= 1) return null;   // only the free slot exists → nothing to discount
        var rng = new System.Random(unchecked(rec.LoanFloor * 333 + rec.DebtShopVisits * 97 + 3));
        return offers[1 + rng.Next(offers.Length - 1)];
    }

    /// <summary>Which of this visit's offers is stocked ALREADY UPGRADED (강화판) — a deterministic pick (per
    /// LoanFloor + visit) from the revealed set, restricted to cards that can actually be upgraded and, when there
    /// is a choice, never the sale card so the two shop perks land on different offers. Its
    /// <see cref="ShopPriceFor"/> carries a <see cref="UpgradePremiumPct"/> surcharge. Deterministic → co-op peers
    /// and a reload agree on which offer is the enhanced one.</summary>
    internal static System.Type? UpgradedCardFor(LoanRecord rec)
    {
        var offers = RevealedPurchasable(rec);
        if (offers.Length == 0) return null;
        // ★Slot 0 IS eligible: landing the 강화판 on the free gift is the shop's jackpot (free AND upgraded), and it
        // costs nothing to allow — ShopPriceFor returns 0 for slot 0 before the premium is ever applied, so the
        // surcharge simply doesn't exist there. (The SALE tag stays paid-only: ~30% off a free card is nonsense.)
        var sale = SaleCardFor(rec);
        var pool = new List<System.Type>();
        foreach (var t in offers) if (t != sale && IsUpgradable(t)) pool.Add(t);
        if (pool.Count == 0)   // the sale card is the only upgradable offer → let it be both
            foreach (var t in offers) if (IsUpgradable(t)) pool.Add(t);
        if (pool.Count == 0) return null;
        var rng = new System.Random(unchecked(rec.LoanFloor * 617 + rec.DebtShopVisits * 149 + 11));
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>Surcharge on the visit's upgraded offer (percent of its base price, rounded to 5). The band tops
    /// out at <see cref="PriceMax"/> 95, so +20% (114) stays under the per-visit credit line (120). ★Do not raise
    /// this back to 30%: 95 × 1.3 = 124 would put the visit's 강화판 offer permanently out of reach.</summary>
    private const int UpgradePremiumPct = 20;

    /// <summary>Can this card type be upgraded at all? (Curse-ish / fixed cards have MaxUpgradeLevel 0 and must
    /// never be picked as the visit's 강화판 — the buy would silently grant a normal copy at a premium price.)</summary>
    private static bool IsUpgradable(System.Type t)
    {
        var m = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(t));
        return m != null && m.MaxUpgradeLevel > 0;
    }

    /// <summary>The actual debt price of a card at the shop THIS visit: its tier base ± a deterministic variance
    /// (−10%..+15%, rounded to 5), +30% if it's the visit's UPGRADED offer, then ~30% off if it's the visit's sale
    /// card. Deterministic per (LoanFloor, visit, card) → co-op peers + a reload agree; the shown price == the
    /// charged price (BuyCardOnDebt uses this).</summary>
    internal static int ShopPriceFor(LoanRecord rec, System.Type t)
    {
        if (IsFreeOffer(rec, t)) return 0;   // slot 0 is the gift — charged nothing, and ApplyBuyCard skips the whole debt path
        int price = ShopBasePrice(rec, t);
        if (UpgradedCardFor(rec) == t) price = (int)Math.Round(price * (100 + UpgradePremiumPct) / 500.0) * 5;   // 강화판 프리미엄
        if (SaleCardFor(rec) == t) price = Math.Max(5, (int)Math.Round(price * 0.55 / 5.0) * 5);   // sale card ~45% off (deep enough that sale + one more fits the credit line)
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
        // 채무 조정 leaves the shelf for good once its once-per-loan use is spent — the sold-set clears every visit,
        // so without this it would be re-stocked and re-buyable for far less debt than it forgives.
        foreach (var t in PurchasablePool)
            if (!rec.PurchasedCards.Contains(t.Name) && !(t == typeof(RestructuringCard) && rec.RestructuringUsed))
                available.Add(t);
        System.Type[] offers;
        if (available.Count <= ShopOfferCount) offers = available.ToArray();
        else
        {
            var rng = new System.Random(unchecked(rec.LoanFloor * 31 + rec.DebtShopVisits * 101 + 13));
            for (int i = available.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (available[i], available[j]) = (available[j], available[i]); }
            offers = available.GetRange(0, ShopOfferCount).ToArray();
        }
        // Slot 0 is the FREE gift, so it must not land on a free-slot-ineligible card (see FreeSlotIneligible).
        // Swap it rightward with the first eligible offer instead of dropping the gift — deterministic, so co-op
        // peers and a reload land on the same arrangement.
        if (offers.Length > 1 && FreeSlotIneligible(offers[0]))
            for (int i = 1; i < offers.Length; i++)
                if (!FreeSlotIneligible(offers[i])) { (offers[0], offers[i]) = (offers[i], offers[0]); break; }
        // ★Publish the memo BEFORE the affordability pass. That pass asks for prices, and ShopPriceFor →
        // SaleCardFor/UpgradedCardFor call back into THIS method — without the memo already set that is infinite
        // recursion (it killed the whole run, not just the shop). With it set, the callbacks hit the cache and
        // return; the pass then mutates `offers` in place, which IS rec.CurrentOffers, so both stay consistent.
        rec.CurrentOffers = offers;
        rec.OfferVisit = rec.DebtShopVisits;
        EnsureAffordablePair(rec, offers, available);
        return offers;
    }

    /// <summary>Guarantee the shop's core promise: <b>one paid card per visit — unless you take the sale card, then
    /// two.</b> That only holds if some PAID offer is cheap enough that sale + it fits the visit's credit line.
    /// <para>★Why this exists: the roll is a uniform shuffle of the pool, so a visit can legitimately deal five
    /// expensive cards and silently make the sale tag worthless. It was rare while the pool skewed cheap; adding
    /// 경비 처리(90)/차입(95) made it common enough that solo-verify's price invariant caught it. Rather than widen
    /// the credit line (which changes every other price relationship), swap the priciest paid offer for the cheapest
    /// card left in the pool whenever the pair doesn't fit.</para>
    /// Deterministic (reads only the already-shuffled list + the fixed price table) → co-op peers and a reload land
    /// on the same row. Mutates <paramref name="offers"/> in place; slot 0 (the free gift) is never touched.
    /// <para>⚠️★co-op 제약: 이 메서드는 <see cref="DebtLoanConfig.ShopCreditLimit"/> 을 읽는데 그건 <b>클라이언트별
    /// ModConfig 슬라이더</b>다. 즉 오퍼 목록은 더 이상 (LoanFloor, DebtShopVisits) 만의 함수가 아니고, 슬라이더가
    /// 다른 두 피어는 <b>다른 목록</b>을 만들 수 있다. 지금은 안전한데, 이유는 딱 하나다 — 구매가 와이어에 카드
    /// 타입과 가격을 실어 보내고(<c>dl_sync buy</c>) 재생 경로 <see cref="ApplyBuyCard"/> 가 오퍼 목록을
    /// <b>게이트로 쓰지 않기 때문</b>이다. 그래서 갈라지는 건 화면뿐이고 결과는 수렴한다.
    /// <b>오퍼 목록(RevealedPurchasable / IsPurchasable)을 네트워크 재생 경로의 조건으로 쓰는 순간 진짜 desync가
    /// 된다.</b> 그렇게 바꿔야 한다면 먼저 ShopCreditLimit 을 호스트 권위로 브로드캐스트하라(RelicForge rf_config
    /// 패턴). 근거: coop-guard 클래스3.</para></summary>
    private static void EnsureAffordablePair(LoanRecord rec, System.Type[] offers, List<System.Type> pool)
    {
        if (offers.Length < 2) return;
        var sale = SaleCardFor(rec);
        int limit = DebtLoanConfig.ShopCreditLimit;

        int cheapestPaid = int.MaxValue, priciestIdx = -1, priciest = -1;
        for (int i = 1; i < offers.Length; i++)
        {
            if (offers[i] == sale) continue;                       // the sale card is the OTHER half of the pair
            int p = ShopPriceFor(rec, offers[i]);
            if (p < cheapestPaid) cheapestPaid = p;
            if (p > priciest) { priciest = p; priciestIdx = i; }
        }
        if (priciestIdx < 0) return;                               // nothing but the sale card among the paid slots

        int salePrice = sale != null ? ShopPriceFor(rec, sale) : 0;
        if (salePrice + cheapestPaid <= limit) return;             // the promise already holds this visit

        // Find the cheapest card NOT already on the row and swap it in for the priciest one.
        System.Type? best = null; int bestPrice = int.MaxValue;
        foreach (var t in pool)
        {
            if (System.Array.IndexOf(offers, t) >= 0) continue;
            int p = ShopPriceFor(rec, t);
            if (p < bestPrice) { bestPrice = p; best = t; }
        }
        if (best != null && salePrice + bestPrice <= limit) offers[priciestIdx] = best;
    }

    /// <summary>An offered card is buyable if the loan is active, it's been revealed, and it hasn't been bought yet.</summary>
    internal static bool IsPurchasable(LoanRecord rec, System.Type t)
        => rec.Active && !rec.PurchasedCards.Contains(t.Name)
           && !(t == typeof(RestructuringCard) && rec.RestructuringUsed)   // spent its once-per-loan use → off the shelf
           && System.Array.IndexOf(RevealedPurchasable(rec), t) >= 0;

    /// <summary>Gold of debt-shop credit still available THIS visit (limit minus what's already been spent on cards
    /// this visit). Separate from the initial loan's HardCap. Never negative.</summary>
    internal static int RemainingShopCredit(LoanRecord rec)
        => System.Math.Max(0, DebtLoanConfig.ShopCreditLimit - rec.ShopSpentThisVisit);

    /// <summary>Can this offer be bought right now given the per-visit credit line? (Its price fits the remaining
    /// credit.) The panel greys offers that fail this, and BuyCardOnDebt refuses them.</summary>
    internal static bool CanAffordCredit(LoanRecord rec, System.Type t)
        => IsFreeOffer(rec, t) || ShopPriceFor(rec, t) <= RemainingShopCredit(rec);   // the gift never touches the credit line

    /// <summary>Buy a revealed non-power card on debt: adds its price onto what you owe and drops the card into the
    /// deck (like every other debt card — removed on full repay). Marks it sold so the shop won't re-sell it.
    /// internal so the self-test can invoke it directly (what clicking a shop offer does).</summary>
    internal static async Task<bool> BuyCardOnDebt(Player player, System.Type type)
    {
        var rec = For(player);
        if (rec == null || !IsPurchasable(rec, type)) return false;
        if (!CanAffordCredit(rec, type)) return false;   // over this shop's credit line → refuse (panel already greys it)
        int price = ShopPriceFor(rec, type);          // the shown price (tier ± variance, upgrade premium / sale applied)
        bool upgraded = UpgradedCardFor(rec) == type; // this visit's 강화판 offer → grant the card already upgraded

        // Only the shopper's OWN peer initiates the buy (the panel is local to the player who opened it). Then:
        // SP → apply here; co-op → broadcast so the deck-add + owed-increase replay identically on BOTH peers
        // (a local-only purchase would leave the partner's replica of this player's owed/deck/sold-set diverged
        // → checksum drop). Mirrors GrantLoanDirect / Repay. The price rides the wire so it can't drift.
        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        // The upgraded flag rides the wire next to the price for the same reason: the remote peer's offer cache may
        // not have been built (it never opened the panel), so it must NOT re-derive which offer was the 강화판.
        if (sp) await ApplyBuyCard(player, type.Name, price, upgraded);
        else    DebtLoanNet.BroadcastBuy(player, type.Name, price, upgraded);
        MainFile.Logger.Info($"[{MainFile.ModId}] buy {type.Name}{(upgraded ? "+" : "")} on debt for {price} ({(sp ? "SP local" : "co-op broadcast")}).");
        return true;
    }

    /// <summary>Apply a debt-shop purchase on THIS peer: add the price onto what's owed, mark it sold, and drop
    /// the card into the deck. Runs directly in SP, or once per peer via the networked <c>dl_sync buy</c> replay
    /// in co-op. Idempotent — the sold-mark guards a double-apply on the initiator's own replay / any re-delivery,
    /// so each peer charges the price and grants the card exactly once.</summary>
    internal static async Task ApplyBuyCard(Player player, string typeName, int price, bool upgraded = false)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return;
        if (rec.PurchasedCards.Contains(typeName)) return;      // already bought → no-op (idempotent)
        var type = System.Array.Find(PurchasablePool, t => t.Name == typeName);
        if (type == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] dl_sync buy: unknown card '{typeName}'."); return; }

        // price 0 = the visit's FREE offer (slot 0). It is a pure gift: no principal, no card debt, no credit-line
        // spend — and, below, no native Debt curse either. The price rides the wire (dl_sync buy … 0 …) rather than
        // being re-derived here, because a remote peer never opened the panel and has no CurrentOffers cache.
        bool free = price <= 0;
        rec.Principal += price;                                 // owed goes up; no gold gained (bought on credit)
        rec.CardDebt += price;                                  // card debt = principal that also accrues node interest
        rec.ShopSpentThisVisit += price;                        // count against this shop's per-visit credit line
        rec.PurchasedCards.Add(typeName);
        await DebtLoanGrants.GrantCard(player, type, upgraded: upgraded);   // fly-in shows again now the panel sits at the shop's layer depth
        // Every debt-shop VISIT you actually BUY ON CREDIT leaves a native Debt curse in your deck — the price of
        // leaning on the credit line. Once per floor (= per shop visit), no matter how many cards you buy that visit;
        // swept on repay. ★The free offer is exempt: taking only the gift and walking out must cost nothing, so the
        // per-floor guard is not even stamped on a free take (a later PAID buy that same visit still gets the curse).
        // Runs in the same per-peer networked buy replay as the card grant, and reads shared floor state → co-op consistent.
        if (!free && player.RunState != null && rec.LastDebtGrantFloor != player.RunState.TotalFloor)
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
        typeof(SettlementCard), typeof(InvoiceCard), typeof(GarnishmentCard), typeof(KitingCard),          // receipt spenders (돌려막기: 빚 저주 → 골드)
        typeof(LoanStrikeCard), typeof(MortgageCard), typeof(BloodPaymentCard),                            // borrow / HP
        typeof(PromissoryNoteCard),                                                                        // 어음: borrow for ENERGY (the third borrow currency)
        typeof(LeverageCard),                                                                              // 레버리지: damage scaled by the principal you still owe
        typeof(RestructuringCard),                                                                         // 채무 조정: once-per-loan principal write-off
        typeof(ExpensingCard),                                                                             // 경비 처리: 영수증 비용 -1 (지출 병목 완화)
        typeof(BorrowingCard),                                                                             // 차입: 턴당 에너지 (영수증 4 = 엔진 초반 산출 전부)
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

    /// <summary>What a 영수증-costing card ACTUALLY costs right now, after 경비 처리 (ExpensingPower) discounts.
    /// ★Single source of truth — the playable gate, the consume call and the cost badge must all read this, or the
    /// card greys out at a price different from the one printed on it.
    /// <para>X-cards (<see cref="IUsesPaymentTally.TallyCost"/> &lt; 0 = 청구서/정산, "spend the whole tally") are
    /// returned unchanged: there is no fixed price to discount, and clamping their sentinel to 0 would turn them into
    /// free no-ops.</para></summary>
    internal static int EffectiveTallyCost(int rawCost, Player? owner)
    {
        if (rawCost < 0) return rawCost;                                     // X = spend-all sentinel, not a price
        int cut = (int)(owner?.Creature?.GetPower<ExpensingPower>()?.Amount ?? 0m);
        return Math.Max(0, rawCost - cut);
    }

    /// <summary>Convenience overload for the cards themselves (they know their own raw cost and owner).</summary>
    internal static int EffectiveTallyCost(IUsesPaymentTally card, Player? owner)
        => EffectiveTallyCost(card.TallyCost, owner);

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

    /// <summary>Draws still available on this loan (∞ when <see cref="DebtLoanConfig.MaxLoanDraws"/> ≤ 0).
    /// Single source of truth for the <see cref="CanLoanCover"/> gate and the shop chip.</summary>
    internal static int DrawsLeft(LoanRecord? rec)
    {
        int max = DebtLoanConfig.MaxLoanDraws;
        if (max <= 0) return int.MaxValue;               // 0 = unlimited (ModConfig)
        return Math.Max(0, max - (rec?.LoanDraws ?? 0));
    }

    /// <summary>Draws left for this player, for UI. No loan yet ⇒ the full allowance.</summary>
    internal static int DrawsLeftFor(Player? p)
    {
        var rec = For(p);
        return DrawsLeft(rec != null && rec.Active ? rec : null);
    }

    /// <summary>Principal still owed on this player's ACTIVE loan (0 if there is no live loan). The read-only view
    /// 레버리지 scales its damage off and 어음 gates on. Never negative.</summary>
    internal static int PrincipalOf(Player? player)
    {
        var rec = For(player);
        return rec != null && rec.Active ? Math.Max(0, rec.Principal) : 0;
    }

    /// <summary>Is this player's once-per-loan 채무 조정 (Restructuring) still available? Needs a live loan with
    /// principal left AND an unspent use. Single source of truth for the card's IsPlayable, the shop's offer list
    /// and <see cref="IsPurchasable"/>, so the three can't drift apart.</summary>
    internal static bool CanRestructure(Player? player)
    {
        var rec = For(player);
        return rec != null && rec.Active && rec.Principal > 0 && !rec.RestructuringUsed;
    }

    /// <summary>Burn this loan's single 채무 조정 use. Called BEFORE the write-off, because a write-off that clears
    /// the loan destroys the record (SettleLoanInCombat → ApplyRepay → ResetFor).</summary>
    internal static void MarkRestructuringUsed(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return;
        rec.RestructuringUsed = true;
        SyncToRelic(player!);
    }

    /// <summary>Write principal off the ledger as FORGIVENESS — no gold and no HP changed hands (채무 조정). Retires
    /// outstanding interest first, exactly like a payment, so the relic hover's "interest vs principal" split stays
    /// coherent. ★It deliberately does NOT touch <see cref="LoanRecord.TotalPaid"/>: that is the "gold you actually
    /// paid" line the 신용 회복 reward gates on (ApplyRepay's RewardMinPaid), so escaping via restructuring clears the
    /// debt but forfeits the medal for having worked it off. Clearing the principal settles the loan mid-combat (the
    /// curses lift right there) instead of leaving it for the next shop. Pure record math off a lockstep card play →
    /// co-op safe.</summary>
    internal static async Task ForgivePrincipal(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        int cut = Math.Min(rec.Principal, amount);
        rec.InterestPaid += Math.Min(cut, InterestRemaining(rec));   // interest first (same order as a payment)
        rec.Principal = Math.Max(0, rec.Principal - cut);
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        MainFile.Logger.Info($"[{MainFile.ModId}] forgave {cut} principal (owed now {rec.Principal}).");
        if (rec.Principal <= 0) await SettleLoanInCombat(player);
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
