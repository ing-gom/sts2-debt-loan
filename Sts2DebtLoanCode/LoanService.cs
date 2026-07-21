using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;      // PileType
using MegaCrit.Sts2.Core.Entities.Gold;       // GoldLossType
using MegaCrit.Sts2.Core.Entities.Merchant;   // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2DebtLoan;

/// <summary>
/// One player's outstanding loan. Tracked per <see cref="Player"/> for the run.
/// NOTE (skeleton): this is IN-MEMORY only. Surviving save/load needs serialization
/// into the run state — see DESIGN.md "Persistence" TODO.
/// </summary>
internal sealed class LoanRecord
{
    /// <summary>Total gold borrowed so far this run (≤ <see cref="DebtLoanConfig.MaxLoan"/>).</summary>
    internal int Principal;

    /// <summary>Cumulative gold drained by Debt cards = interest paid.</summary>
    internal int InterestPaid;

    /// <summary>Map rooms entered since the FIRST loan (drives the Debt-card schedule).</summary>
    internal int RoomsSinceLoan;

    /// <summary>Debt-card instances we injected, so we can remove exactly them on retire.</summary>
    internal readonly List<CardModel> DebtCards = new();

    /// <summary>False once the loan is retired (principal repaid, or interest hit the cap).
    /// A retired relic keeps sitting in the inventory but does nothing.</summary>
    internal bool Active = true;

    internal bool RelicGranted;

    /// <summary>Interest ceiling in gold: principal × multiplier (spec: 200%).</summary>
    internal int InterestCap => (int)Math.Round(Principal * DebtLoanConfig.InterestCapMultiplier);
}

/// <summary>
/// The loan mechanic's brain. The Harmony patches are thin — they call into here.
/// Gold mutations follow the co-op host-authoritative pattern (LOCAL player only +
/// RewardSynchronizer), the same contract as Sts2RelicForge's gold code.
/// </summary>
internal static class LoanService
{
    private static readonly ConditionalWeakTable<Player, LoanRecord> Records = new();

    internal static LoanRecord? For(Player? player)
        => player != null && Records.TryGetValue(player, out var r) ? r : null;

    private static LoanRecord GetOrCreate(Player player)
        => Records.GetValue(player, _ => new LoanRecord());

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

    // ── Persistence (save/load) ──────────────────────────────────────────────
    // The Merchant's Ledger relic carries the state as [SavedProperty] fields (auto round-trip). We
    // write the record through to the relic on every mutation, and rebuild the record from the relic
    // on load (RunLoadPatch → RestoreFromRelic). Debt cards are real deck cards, so they persist on
    // their own and are re-found by scanning the deck.

    internal static DebtLoanRelic? LedgerRelicOf(Player player)
    {
        if (player?.Relics == null) return null;
        foreach (var r in player.Relics)
            if (r is DebtLoanRelic dl) return dl;
        return null;
    }

    /// <summary>Persist the current record onto the granted relic (no-op before the relic exists).</summary>
    private static void SyncToRelic(Player player)
    {
        var rec = For(player);
        var relic = LedgerRelicOf(player);
        if (rec == null || relic == null) return;
        try
        {
            relic.Principal = rec.Principal;
            relic.InterestPaid = rec.InterestPaid;
            relic.RoomsSinceLoan = rec.RoomsSinceLoan;
            relic.Active = rec.Active;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic sync failed: {e.Message}"); }
    }

    /// <summary>Rebuild the transient LoanRecord from the (loaded) relic + deck. Called on run load.</summary>
    internal static void RestoreFromRelic(Player player)
    {
        var relic = LedgerRelicOf(player);
        if (relic == null) return;
        var rec = GetOrCreate(player);
        rec.Principal = relic.Principal;
        rec.InterestPaid = relic.InterestPaid;
        rec.RoomsSinceLoan = relic.RoomsSinceLoan;
        rec.Active = relic.Active;
        rec.RelicGranted = true;
        rec.DebtCards.Clear();
        try
        {
            var pile = PileType.Deck.GetPile(player);
            if (pile != null)
                foreach (var c in pile.Cards)
                    if (c is DebtCurseCard) rec.DebtCards.Add(c);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] deck rescan failed: {e.Message}"); }
        MainFile.Logger.Info($"[{MainFile.ModId}] restored loan: principal {rec.Principal}, interest {rec.InterestPaid}, rooms {rec.RoomsSinceLoan}, active {rec.Active}, cards {rec.DebtCards.Count}.");
    }

    // ── Eligibility ──────────────────────────────────────────────────────────

    /// <summary>Act-1 gate (0-based CurrentActIndex == 0), unless the config opens all acts.</summary>
    private static bool ActAllowsLoan(Player player)
        => DebtLoanConfig.AllowLoansOutsideAct1 || player.RunState.CurrentActIndex == 0;

    /// <summary>Remaining borrow headroom under the 300 cap.</summary>
    internal static int RemainingRoom(Player player)
    {
        var rec = For(player);
        int used = rec?.Principal ?? 0;
        return Math.Max(0, DebtLoanConfig.MaxLoan - used);
    }

    /// <summary>
    /// Can this specific merchant item be bought on loan right now? True when the player
    /// is short on gold but a loan can cover the shortfall. First loan: any Act-1 shop.
    /// Top-up loans: only before the penalty deadline (rooms &lt; PenaltyStartRoom) and while
    /// the loan is still active.
    /// </summary>
    internal static bool CanLoanCover(MerchantEntry entry, Player player)
    {
        if (entry == null || player == null) return false;
        if (!ActAllowsLoan(player)) return false;

        int cost = entry.Cost;
        int shortfall = cost - (int)player.Gold;
        if (shortfall <= 0) return false;                 // can already afford → not a loan
        if (RemainingRoom(player) < shortfall) return false; // over the 300 cap

        var rec = For(player);
        if (rec == null || !rec.RelicGranted) return true;   // FIRST loan — always OK in Act 1
        if (!rec.Active) return false;                       // retired loan → no more borrowing
        return rec.RoomsSinceLoan < DebtLoanConfig.PenaltyStartRoom; // top-up only before the deadline
    }

    /// <summary>Loan needed to buy <paramref name="entry"/> (shortfall, capped to headroom).</summary>
    internal static int LoanAmountFor(MerchantEntry entry, Player player)
    {
        int shortfall = entry.Cost - (int)player.Gold;
        return Math.Max(0, Math.Min(shortfall, RemainingRoom(player)));
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Credit the loan gold so the pending purchase can complete, record the debt, and grant
    /// the Debt relic on the first loan. Co-op: credit the LOCAL player only, then sync.
    /// </summary>
    internal static Task GrantLoanFor(MerchantEntry entry, Player player)
        => GrantLoanDirect(player, LoanAmountFor(entry, player));

    /// <summary>The credit/record/grant guts, decoupled from the merchant entry so it is drivable
    /// headless (solo-verify) and reusable. <paramref name="amount"/> is the gold to borrow now.</summary>
    internal static async Task GrantLoanDirect(Player player, int amount)
    {
        if (amount <= 0) return;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return;   // host-authoritative gold

        await PlayerCmd.GainGold(amount, player, false);
        run?.RewardSynchronizer?.SyncLocalObtainedGold(amount);

        var rec = GetOrCreate(player);
        rec.Principal += amount;

        if (!rec.RelicGranted)
        {
            rec.RelicGranted = true;
            rec.RoomsSinceLoan = 0;               // the room counter starts now
            if (!PlayerHasLedger(player))         // never double-grant (fresh record after repay / load)
                await DebtLoanGrants.GrantRelic(player);
        }

        SyncToRelic(player);
        MainFile.Logger.Info($"[{MainFile.ModId}] loan +{amount}g (principal {rec.Principal}/{DebtLoanConfig.MaxLoan}, cap {rec.InterestCap}g).");
    }

    /// <summary>A new map room was entered — advance the counter and top up Debt cards.</summary>
    internal static async Task OnRoomEntered(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.RelicGranted || !rec.Active) return;
        rec.RoomsSinceLoan++;
        await EscalateIfNeeded(player, rec);
        SyncToRelic(player);
    }

    /// <summary>Inject enough Debt cards to reach the schedule's target for the current room count.</summary>
    private static async Task EscalateIfNeeded(Player player, LoanRecord rec)
    {
        int target = DebtLoanConfig.TargetDebtCards(rec.RoomsSinceLoan);
        int have = rec.DebtCards.Count;
        for (int i = have; i < target; i++)
        {
            var card = player.RunState.CreateCard<DebtCurseCard>(player);
            if (card == null) continue;
            await CardPileCmd.Add(card, PileType.Deck);
            rec.DebtCards.Add(card);
        }
        if (target > have)
            MainFile.Logger.Info($"[{MainFile.ModId}] room {rec.RoomsSinceLoan}: Debt cards {have}→{target}.");
    }

    /// <summary>A Debt card drained <paramref name="drained"/> gold — book it as interest and
    /// retire the loan if the 200% ceiling is reached.</summary>
    internal static async Task AccrueInterest(Player player, int drained)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || drained <= 0) return;
        rec.InterestPaid += drained;
        SyncToRelic(player);
        if (rec.InterestPaid >= rec.InterestCap)
        {
            MainFile.Logger.Info($"[{MainFile.ModId}] interest {rec.InterestPaid}g ≥ cap {rec.InterestCap}g — loan retired.");
            await Retire(player, rec);
        }
    }

    /// <summary>Repay the outstanding principal at a shop (spec: repaying retires the relic).</summary>
    internal static async Task<bool> Repay(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return false;
        if ((int)player.Gold < rec.Principal) return false;   // can't cover the principal

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        await PlayerCmd.LoseGold(rec.Principal, player, GoldLossType.Spent);
        run?.RewardSynchronizer?.SyncLocalGoldLost(rec.Principal);
        MainFile.Logger.Info($"[{MainFile.ModId}] repaid principal {rec.Principal}g — loan retired.");
        await Retire(player, rec);
        return true;
    }

    /// <summary>Retire the loan: deactivate the relic effect and remove EVERY Debt card
    /// (spec: cards are always cleared on retire, whichever trigger fired).</summary>
    private static async Task Retire(Player player, LoanRecord rec)
    {
        rec.Active = false;
        if (rec.DebtCards.Count > 0)
        {
            try { await CardPileCmd.RemoveFromDeck(rec.DebtCards.ToArray()); }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] Debt-card cleanup failed: {e.Message}"); }
            rec.DebtCards.Clear();
        }
        SyncToRelic(player);
        DebtLoanRelic.RefreshDisplay(player);
    }
}
