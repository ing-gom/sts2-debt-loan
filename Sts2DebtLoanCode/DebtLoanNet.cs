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
    public override string Args => "<active <borrowed> <principal> <totalPaid> <loanFloor> | buy <cardType> <price> [upgraded] | repaid | claim <index> | purge <price>>";
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
            // args[1] = 이번 목돈 상환액. ★전에는 더미 0 이었고 구매자 피어가 TotalPaid 를 로컬로
            // 올렸는데, 그러면 원격 피어는 그 증가분을 못 받는다(청산 후 재대출을 안 하면 그대로 갈라졌다).
            int.TryParse(args.Length > 1 ? args[1] : "0", NumberStyles.Integer, inv, out int repaidAmount);
            TaskHelper.RunSafely(LoanService.ApplyRepay(issuingPlayer, repaidAmount));
            return new CmdResult(success: true, $"dl_sync repaid {repaidAmount}.");
        }

        if (state == "buy")
        {
            // Debt-shop purchase: replay the same deck-add + owed-increase on every peer. The applied state is
            // idempotent (ApplyBuyCard no-ops if the card is already marked sold), so the initiator's own replay
            // and any re-delivery are harmless — exactly one card + one price charge per peer.
            if (args.Length < 3) return new CmdResult(success: false, "dl_sync buy: expected <cardType> <price>.");
            string typeName = args[1];
            int.TryParse(args[2], NumberStyles.Integer, inv, out int buyPrice);
            // Optional 4th arg: 1 = this was the visit's 강화판 offer, so grant the card already upgraded. Sent
            // rather than re-derived, because the remote peer never opened the panel and may hold no offer cache.
            bool buyUpgraded = args.Length > 3 && args[3] == "1";
            TaskHelper.RunSafely(LoanService.ApplyBuyCard(issuingPlayer, typeName, buyPrice, buyUpgraded));
            return new CmdResult(success: true, $"dl_sync buy {typeName}{(buyUpgraded ? "+" : "")} price={buyPrice}.");
        }

        if (state == "claim")
        {
            // 신용 보상 수령. ★★여기서만 `TaskHelper.RunSafely`(detached)를 쓰지 않고 **Task 를 CmdResult 에
            // 실어 액션이 await 하게** 한다: 900/1200 은 카드 선택 화면을 여는데, 그 선택의 co-op 동기화를
            // 엔진이 `ReserveChoiceId → SyncLocalChoice / WaitForRemoteChoice` 로 처리하려면 **양 피어가 같은
            // 큐 위치에서** 그 핸드셰이크에 들어가야 하기 때문이다. detached 로 띄우면 두 피어의 진입 시점이
            // 엇갈려 choiceId 가 어긋날 수 있다. (dl_testcard 가 쓰는 것과 같은 관용구.)
            // args = <index>. ★인덱스를 싣는 이유 = 보너스 단계가 무한이라 문턱값으로는 단계를
            // 특정할 수 없고, 순차 검사(index == 다음 차례)가 그대로 멱등 가드가 된다. 보너스의 보상
            // 종류(제거/강화)는 인덱스에서 교대로 결정되므로 전선에 실을 필요가 없다.
            if (args.Length < 2) return new CmdResult(success: false, "dl_sync claim: expected <index>.");
            int.TryParse(args[1], NumberStyles.Integer, inv, out int rewardIndex);
            return new CmdResult(LoanService.ApplyClaimReward(issuingPlayer, rewardIndex), success: true,
                                 $"dl_sync claim #{rewardIndex}.");
        }

        if (state == "purge")
        {
            // 빚으로 카드 제거. claim 과 같은 이유로 awaited (제거도 선택 화면을 연다). 가격은 와이어에서
            // 받는다 — 각 피어가 자기 CardShopRemovalsUsed 로 다시 계산하면 값이 갈릴 수 있다.
            if (args.Length < 2) return new CmdResult(success: false, "dl_sync purge: expected <price>.");
            int.TryParse(args[1], NumberStyles.Integer, inv, out int purgePrice);
            return new CmdResult(LoanService.ApplyPurgeCard(issuingPlayer, purgePrice), success: true, $"dl_sync purge {purgePrice}.");
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

    internal static void BroadcastBuy(Player owner, string cardTypeName, int price, bool upgraded = false)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} buy {cardTypeName} {price} {(upgraded ? 1 : 0)}");

    internal static void BroadcastRepay(Player owner, int paidAdd)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} repaid {paidAdd}");

    internal static void BroadcastClaim(Player owner, int index)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} claim {index}");

    internal static void BroadcastPurge(Player owner, int price)
        => Dispatch(owner, $"{DebtLoanNetCmd.Verb} purge {price}");

    private static void Dispatch(Player owner, string synced)
    {
        var sync = RunManager.Instance?.ActionQueueSynchronizer;
        if (sync == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] no ActionQueueSynchronizer — '{synced}' dropped."); return; }
        sync.RequestEnqueue(new ConsoleCmdGameAction(owner, synced, inCombat: false));
    }
}
