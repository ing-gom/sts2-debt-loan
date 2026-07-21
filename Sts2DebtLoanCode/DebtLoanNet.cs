using System.Globalization;
using MegaCrit.Sts2.Core.DevConsole;                // AbstractConsoleCmd, CmdResult, ConsoleCmdGameAction
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;                   // TaskHelper
using MegaCrit.Sts2.Core.Runs;                      // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// NETWORKED transport that replicates one player's merchant loan (the Ledger relic + its numeric state)
/// to every co-op peer. The out-of-combat shop events — taking a loan and repaying it — mutate only the
/// local peer, so they must be pushed to the partner explicitly; the gold itself rides the reward-sync,
/// and this carries the relic + record.
///
/// Like Sts2RelicForge's <c>rf_sync</c> / <c>rf_counts</c>, this reuses the game's built-in
/// <c>ConsoleCmdGameAction</c> wire type (a plain command string), so the mod adds NO new INetAction
/// subtype and never perturbs the net type-id table — lockstep-safe. The applied mutations
/// (<see cref="LoanService.ApplyActiveLoan"/> / <see cref="LoanService.ApplyRepay"/>) call only LOCAL
/// commands (RelicCmd.Obtain / RelicCmd.Remove both just edit the player's relic list), so running the
/// replay once per peer produces exactly one relic per peer — no doubling.
///
/// Interest accrual and the 200% default are NOT carried here: they happen inside the lockstep combat
/// (the Debt card's OnTurnEndInHand fires deterministically on both peers), so both peers advance the
/// same record and default together without any broadcast.
/// </summary>
public sealed class DebtLoanNetCmd : AbstractConsoleCmd
{
    public const string Verb = "dl_sync";

    public override string CmdName => Verb;
    public override string Args => "<active|repaid> <borrowed> <principal> <totalPaid> <loanFloor>";
    public override string Description =>
        "Internal (networked): replicate a player's merchant loan (Ledger relic + state) to every co-op peer.";
    public override bool IsNetworked => true;   // routes through the synchronized action queue
    public override bool DebugOnly => false;    // must register in normal (non-debug) co-op play

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        // issuingPlayer = the borrower (the action's owner), resolved by NetId on every peer. Runs on the
        // host AND every client; the applied state is idempotent, so re-delivery / the initiator's own
        // replay are harmless.
        if (issuingPlayer == null) return new CmdResult(success: false, "dl_sync: no active player.");
        if (args.Length < 1)       return new CmdResult(success: false, "dl_sync: expected a state.");

        var inv = CultureInfo.InvariantCulture;
        string state = args[0];
        if (state == "repaid")
        {
            TaskHelper.RunSafely(LoanService.ApplyRepay(issuingPlayer));
            return new CmdResult(success: true, "dl_sync repaid.");
        }

        if (args.Length < 5) return new CmdResult(success: false, "dl_sync active: expected 5 args.");
        int.TryParse(args[1], NumberStyles.Integer, inv, out int borrowed);
        int.TryParse(args[2], NumberStyles.Integer, inv, out int principal);
        int.TryParse(args[3], NumberStyles.Integer, inv, out int totalPaid);
        int.TryParse(args[4], NumberStyles.Integer, inv, out int loanFloor);
        TaskHelper.RunSafely(LoanService.ApplyActiveLoan(issuingPlayer, borrowed, principal, totalPaid, loanFloor));
        return new CmdResult(success: true, $"dl_sync active b={borrowed} p={principal} paid={totalPaid} floor={loanFloor}.");
    }
}

/// <summary>Enqueues <see cref="DebtLoanNetCmd"/> onto the run's synchronized action stream so it replays
/// on every peer. Shop-only events, never in combat, so <c>inCombat</c> is false.</summary>
internal static class DebtLoanNet
{
    internal static void BroadcastLoan(Player owner, int borrowed, int principal, int totalPaid, int loanFloor)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} active {borrowed} {principal} {totalPaid} {loanFloor}");

    internal static void BroadcastRepay(Player owner)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} repaid 0 0 0");

    private static void Dispatch(Player owner, string synced)
    {
        var sync = RunManager.Instance?.ActionQueueSynchronizer;
        if (sync == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] no ActionQueueSynchronizer — '{synced}' dropped."); return; }
        sync.RequestEnqueue(new ConsoleCmdGameAction(owner, synced, inCombat: false));
    }
}
