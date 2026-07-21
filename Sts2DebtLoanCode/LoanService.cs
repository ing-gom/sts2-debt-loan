using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;      // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Gold;       // GoldLossType
using MegaCrit.Sts2.Core.Entities.Merchant;   // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2DebtLoan;

/// <summary>One player's outstanding loan for the run. The Debt cards are NOT stored here — they are
/// injected fresh into each combat's draw pile (see <see cref="DebtLoanRelic.BeforeCombatStart"/>), so
/// this only tracks the numeric state, which is persisted onto the relic as [SavedProperty] fields.</summary>
internal sealed class LoanRecord
{
    /// <summary>Total gold borrowed so far this run (≤ <see cref="DebtLoanConfig.MaxLoan"/>).</summary>
    internal int Principal;

    /// <summary>Cumulative gold drained by Debt cards = interest paid.</summary>
    internal int InterestPaid;

    /// <summary>TotalFloor of the shop where the loan was taken. Top-ups are allowed only at THAT shop.
    /// Rooms-since-loan (which drives the Debt-card count) is computed as TotalFloor − LoanFloor.</summary>
    internal int LoanFloor = -1;

    /// <summary>False once settled (repaid, or defaulted at 200%).</summary>
    internal bool Active = true;

    /// <summary>True once interest hit 200% (default): the ledger is frozen for the rest of the run —
    /// the relic is disabled and no new loan can be taken.</summary>
    internal bool Defaulted;

    internal bool RelicGranted;

    /// <summary>Interest ceiling in gold: principal × multiplier (200% by default).</summary>
    internal int InterestCap => (int)Math.Round(Principal * DebtLoanConfig.InterestCapMultiplier);
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
        if (rec == null || !rec.Active || p?.RunState == null) return 0;
        return DebtLoanConfig.TargetDebtCards(p.RunState.TotalFloor - rec.LoanFloor);
    }

    /// <summary>Total Debt cards injected per combat = the SUM of every player's active-loan count. In
    /// co-op this makes one player's debt spread into the partner's combats too, and stack if both borrow.</summary>
    internal static int RunWideDebtTotal(IRunState run)
    {
        if (run?.Players == null) return 0;
        int total = 0;
        foreach (var p in run.Players) total += DebtCardCountFor(p);
        return total;
    }

    /// <summary>Inject <paramref name="count"/> temporary Debt cards into a player's combat draw pile.</summary>
    internal static async Task InjectDebtCardsForCombat(Player player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var card = player.RunState.CreateCard<DebtCurseCard>(player);
            if (card != null)
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, (Player?)null, CardPilePosition.Random);
        }
        if (count > 0) MainFile.Logger.Info($"[{MainFile.ModId}] injected {count} Debt card(s) into a combat.");
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
            relic.Principal = rec.Principal;
            relic.InterestPaid = rec.InterestPaid;
            relic.LoanFloor = rec.LoanFloor;
            relic.Active = rec.Active;
            relic.Defaulted = rec.Defaulted;
            LocInjectionPatch.SetLedgerDescription(BuildLedgerText(player, rec));
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic sync failed: {e.Message}"); }
    }

    /// <summary>Live hover text for the Ledger relic.</summary>
    private static string BuildLedgerText(Player player, LoanRecord rec)
    {
        if (rec.Defaulted)
            return "DEFAULTED — interest reached 200% of the loan. The ledger is frozen for the rest of the run; the merchant will not lend to you again.";
        if (!rec.Active)
            return "The debt is settled.";

        int owed = rec.Principal, interest = rec.InterestPaid, cap = rec.InterestCap;
        int rooms = (player?.RunState?.TotalFloor ?? rec.LoanFloor) - rec.LoanFloor;
        int cards = DebtCardCountFor(player);
        int nextRoom = DebtLoanConfig.NextThresholdRoom(rooms);
        string next = nextRoom < 0
            ? $"[b]{cards}[/b] Debt card(s) injected each combat (max)."
            : $"[b]{cards}[/b] Debt card(s) injected each combat — next in [b]{nextRoom - rooms}[/b] room(s).";
        return $"You owe [gold]{owed} Gold[/gold].  Interest paid [b]{interest}[/b] / [b]{cap}[/b].\n{next}\n" +
               "Repay the principal at a shop (credit restored), or hit 200% interest (default, frozen).";
    }

    /// <summary>Rebuild the transient record from the relic on load. A repaid loan removed the relic, so
    /// no relic ⇒ no record ⇒ free to borrow again; a defaulted relic restores the frozen state.</summary>
    internal static void RestoreFromRelic(Player player)
    {
        var relic = LedgerRelicOf(player);
        if (relic == null) return;
        var rec = GetOrCreate(player);
        rec.Principal = relic.Principal;
        rec.InterestPaid = relic.InterestPaid;
        rec.LoanFloor = relic.LoanFloor;
        rec.Active = relic.Active;
        rec.Defaulted = relic.Defaulted;
        rec.RelicGranted = true;
        if (rec.Defaulted) DebtLoanGrants.DisableRelic(relic);
        MainFile.Logger.Info($"[{MainFile.ModId}] restored loan: principal {rec.Principal}, interest {rec.InterestPaid}, loanFloor {rec.LoanFloor}, active {rec.Active}, defaulted {rec.Defaulted}.");
    }

    // ── Eligibility ──────────────────────────────────────────────────────────

    private static bool ActAllowsLoan(Player player)
        => DebtLoanConfig.AllowLoansOutsideAct1 || player.RunState.CurrentActIndex == 0;

    internal static int RemainingRoom(Player player)
    {
        var rec = For(player);
        int used = rec?.Principal ?? 0;
        return Math.Max(0, DebtLoanConfig.MaxLoan - used);
    }

    /// <summary>Can this merchant item be bought on loan now? First loan: any Act-1 shop. Top-up: only at
    /// the same shop. Blocked entirely once you've DEFAULTED this run.</summary>
    internal static bool CanLoanCover(MerchantEntry entry, Player player)
    {
        if (entry == null || player == null) return false;
        // Co-op: loans replicate via the networked dl_sync command (relic + record on both peers), and the
        // gold rides the reward-sync. Only the LOCAL player may take a loan (others' shops are theirs).
        bool sp = RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;
        if (!ActAllowsLoan(player)) return false;

        var rec = For(player);
        if (rec != null && rec.Defaulted) return false;      // defaulted this run → credit frozen

        int cost = entry.Cost;
        int shortfall = cost - (int)player.Gold;
        if (shortfall <= 0) return false;                    // can already afford → not a loan
        if (RemainingRoom(player) < shortfall) return false; // over the cap

        if (rec == null || !rec.RelicGranted) return true;   // FIRST loan (or fresh after a repay)
        if (!rec.Active) return false;
        return player.RunState.TotalFloor == rec.LoanFloor;  // top-up ONLY at the same shop
    }

    internal static int LoanAmountFor(MerchantEntry entry, Player player)
    {
        int shortfall = entry.Cost - (int)player.Gold;
        return Math.Max(0, Math.Min(shortfall, RemainingRoom(player)));
    }

    /// <summary>Price multiplier the merchant applies to a player carrying debt at a DIFFERENT shop than
    /// the one they borrowed at: +10% (1 card) / +15% (2) / +20% (3). 1.0 = no change.</summary>
    internal static double DebtPriceMultiplier(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return 1.0;
        if (player.RunState.TotalFloor == rec.LoanFloor) return 1.0;   // no surcharge at your own shop
        int cards = DebtCardCountFor(player);                           // 1/2/3
        return 1.0 + (5 + 5 * cards) / 100.0;                           // 10% / 15% / 20%
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
        int principal = (existing?.Principal ?? 0) + amount;
        int interest  = existing?.InterestPaid ?? 0;
        int loanFloor = (existing != null && existing.RelicGranted)
                        ? existing.LoanFloor                   // top-up keeps the original shop floor
                        : player.RunState.TotalFloor;          // first loan: rooms = TotalFloor − here (0 → 1 card)

        if (sp) await ApplyActiveLoan(player, principal, interest, loanFloor);
        else    DebtLoanNet.BroadcastLoan(player, principal, interest, loanFloor);

        MainFile.Logger.Info($"[{MainFile.ModId}] loan +{amount}g (principal {principal}/{DebtLoanConfig.MaxLoan}).");
    }

    /// <summary>Apply an ACTIVE loan state locally: set the record, grant the Ledger relic if the player
    /// doesn't have it, and write the state through to the relic. Runs on EACH peer — directly in SP, or
    /// once per peer via the networked <c>dl_sync</c> replay in co-op (idempotent: re-grants only if missing).</summary>
    internal static async Task ApplyActiveLoan(Player player, int principal, int interestPaid, int loanFloor)
    {
        var rec = GetOrCreate(player);
        rec.Principal    = principal;
        rec.InterestPaid = interestPaid;
        rec.LoanFloor    = loanFloor;
        rec.Active       = true;
        rec.Defaulted    = false;
        rec.RelicGranted = true;
        if (!PlayerHasLedger(player))
            await DebtLoanGrants.GrantRelic(player);
        SyncToRelic(player);
    }

    /// <summary>A Debt card drained gold — book it as interest and DEFAULT the loan at the 200% ceiling.</summary>
    internal static async Task AccrueInterest(Player player, int drained)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || drained <= 0) return;
        rec.InterestPaid += drained;
        SyncToRelic(player);
        if (rec.InterestPaid >= rec.InterestCap)
        {
            MainFile.Logger.Info($"[{MainFile.ModId}] interest {rec.InterestPaid}g ≥ cap {rec.InterestCap}g — DEFAULT.");
            await SettleByDefault(player, rec);
        }
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

        await PlayerCmd.LoseGold(rec.Principal, player, GoldLossType.Spent);
        run?.RewardSynchronizer?.SyncLocalGoldLost(rec.Principal);
        MainFile.Logger.Info($"[{MainFile.ModId}] repaid principal {rec.Principal}g — credit restored.");

        if (sp) await ApplyRepay(player);
        else    DebtLoanNet.BroadcastRepay(player);
        return true;
    }

    /// <summary>Apply the repay settle locally: stop the Debt cards, REMOVE the relic, and clear the record
    /// so a fresh loan can be taken at a future shop. Runs on EACH peer — directly in SP, or once per peer
    /// via the networked <c>dl_sync repaid</c> replay in co-op (RelicCmd.Remove is a local mutation).</summary>
    internal static async Task ApplyRepay(Player player)
    {
        var rec = For(player);
        if (rec != null) { rec.Active = false; SyncToRelic(player); }   // reflect "settled" for one frame
        await DebtLoanGrants.RemoveRelic(player);                        // clean slate — no inert relic left behind
        ResetFor(player);                                               // record gone → next loan is a fresh first loan
    }

    /// <summary>Default settle (200%): freeze the ledger for the rest of the run and DISABLE the relic
    /// (kept, not removed). No re-borrowing until the run ends.</summary>
    private static async Task SettleByDefault(Player player, LoanRecord rec)
    {
        rec.Active = false;
        rec.Defaulted = true;
        var relic = LedgerRelicOf(player);
        if (relic != null) DebtLoanGrants.DisableRelic(relic);
        SyncToRelic(player);
        await Task.CompletedTask;
    }
}
