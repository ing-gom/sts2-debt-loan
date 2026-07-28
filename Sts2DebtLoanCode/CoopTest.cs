#if DEBTLOAN_SELFTEST
// LOCAL TEST ONLY — dormant unless `selftest.coop.flag` is next to the mod DLL, and only compiled when
// DEBTLOAN_SELFTEST is defined. Drives the co-op lobby, then on the HOST's local player runs the mod's real
// out-of-combat paths: takes a loan (GrantLoanDirect → networked dl_sync) and BUYS A CARD ON DEBT
// (BuyCardOnDebt → networked dl_sync buy). Both peers then record what they SEE of the host's replicated loan:
// owed principal, how many cards were bought on debt, and whether a bought card is really in the host's deck
// on this peer. Convergence = both peers agree on all three AND no session drop across a room boundary (the
// checksum boundary that a local-only purchase would fail). See the coop-verify skill.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;                         // CombatManager
using MegaCrit.Sts2.Core.Commands;                        // CardSelectCmd
using MegaCrit.Sts2.Core.Context;                         // LocalContext
using MegaCrit.Sts2.Core.DevConsole;                      // ConsoleCmdGameAction
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives; // CardRewardAlternative
using MegaCrit.Sts2.Core.Entities.Cards;                  // CardCreationResult, PileType
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;                         // TaskHelper
using MegaCrit.Sts2.Core.Models;                          // RelicModel
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;          // NOverlayStack
using MegaCrit.Sts2.Core.Random;                          // Rng
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;                     // ICardSelector

namespace Sts2DebtLoan;

internal static class CoopTest
{
    /// <summary>Seconds without a Step() call before the watchdog declares this peer wedged. The host
    /// script drives a loan + a full combat + a room jump (~40s of awaits), so keep this well above that.</summary>
    private const double StepTimeoutSec = 150;

    private static readonly StringBuilder _out = new();
    private static bool _isHost, _readySent, _launched, _done;
    private static string _role = "?";
    private static string _step = "(not started)";
    private static DateTime _stepAt = DateTime.UtcNow;

    private static string ModDir() => Path.GetDirectoryName(typeof(CoopTest).Assembly.Location) ?? ".";

    /// <summary>Name the phase you're entering. Resets the watchdog and timestamps the log.</summary>
    private static void Step(string name)
    {
        _step = name;
        _stepAt = DateTime.UtcNow;
        W($"— {name}");
    }

    public static void ArmIfRequested()
    {
        try
        {
            if (!File.Exists(Path.Combine(ModDir(), "selftest.coop.flag"))) return;
            var fm = System.Environment.GetCommandLineArgs().FirstOrDefault(a => a.Contains("fastmp"));
            _isHost = fm != null && fm.Contains("host");
            _role = fm == null ? "nofastmp" : (_isHost ? "host" : "join");
            W($"coop selftest armed (role={_role})");
            Poll();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] coop arm failed: {e.Message}"); }
    }

    /// <summary>진행 폴링 루프. ★두 번 데였다:
    /// <para>① 트리가 아직 없으면 예전엔 <b>재예약 없이 return</b> 해서 한 번만 어긋나도 폴링이 영영 멈췄다
    /// — 호스트가 COOP 로그 2줄만 찍고 멈추고(=폴링 사망), 조인은 상대가 준비되기를 무한 대기해
    /// <b>결과 파일 0개</b>가 된다. 이 증상은 '조인 실패'와 구별되지 않는다 → 이제 <c>CallDeferred</c> 로 다시 잡는다.</para>
    /// <para>② 이걸 <c>async void</c> + <c>Task.Delay</c> 로 바꿔 봤다가 <b>더 나빠졌다</b> — 모드 초기화 시점엔
    /// 동기화 컨텍스트가 없어 continuation 이 메인 스레드로 안 돌아온다(호스트에서 재현). <c>CreateTimer</c> 가 정답.</para></summary>
    private static void Poll()
    {
        if (_done) return;
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            try { Tick(tree); } catch (Exception e) { W("tick exception: " + e.Message); }
            if (!_done) tree.CreateTimer(2.0).Timeout += Poll;
            return;
        }
        // ★트리가 아직 없어도 루프를 죽이지 않는다 — 예전 코드는 여기서 재예약 없이 return 해서 한 번만
        // 어긋나도 폴링이 영영 멈췄다. (Task.Delay 로 갈아타는 것도 시도했으나, 모드 초기화 시점엔 동기화
        // 컨텍스트가 없어 continuation 이 메인 스레드로 안 돌아와 호스트에서 더 나빠졌다 — 실측.)
        Callable.From(Poll).CallDeferred();
    }

    private static void Tick(SceneTree tree)
    {
        var run = RunManager.Instance;
        if (!_launched && run != null && run.IsInProgress && (run.State?.Players?.Count ?? 0) >= 2)
        {
            _launched = true;
            W($"COOP RUN IN PROGRESS — players={run.State!.Players.Count}");
            // Answer selection prompts from here on. MUST come after the run starts: RunManager.CleanUp
            // calls CardSelectCmd.Reset(), which drops every pushed selector.
            StartAutomation();
            Step(_isHost ? "host phase" : "join phase");
            TaskHelper.RunSafely(_isHost ? HostPhase(run) : JoinPhase(run));
            return;
        }

        // Watchdog: a wedged peer writes NO file (_out only reaches disk in Flush()) — and "no result
        // file" is exactly what a failed join looks like too, so a partial FAIL naming the step is what
        // tells the two apart.
        if (!_done && _launched && (DateTime.UtcNow - _stepAt).TotalSeconds > StepTimeoutSec)
        {
            W($"WATCHDOG: no progress for {StepTimeoutSec:F0}s at step '{_step}' — flushing partial result.");
            W($"WATCHDOG: overlay on top = {TopScreenName()} (a selection screen here = an unanswered prompt " +
              "on THIS peer, which also stalls the other peer in WaitForRemoteChoice).");
            Flush(false);
            return;
        }

        if (!_readySent)
        {
            var screen = FindScreen(tree.Root);
            if (screen == null) { W("waiting for character-select lobby…"); return; }
            var lobby = LobbyOf(screen);
            if (lobby == null) { W("lobby null"); return; }
            try { lobby.SetReady(true); _readySent = true; Step("SetReady(true) sent"); }
            catch (Exception e) { W("SetReady failed: " + e.Message); }
        }
    }

    // ── The DebtLoan co-op scenario (v2 — debt-shop purchase replication) ──────────────────────────────
    // The shop model changed: cards are now BOUGHT ON DEBT at the debt shop, a LOCAL deck mutation on the
    // shopper's peer. If that isn't networked, the partner's replica of the shopper's owed / deck / sold-set
    // diverges → the next room's checksum drops the client. So the HOST: (1) takes a loan (Ledger replicates
    // via dl_sync), (2) BUYS A CARD ON DEBT — which must broadcast `dl_sync buy` so BOTH peers add the price to
    // the host's owed, drop the card into the host's deck, and mark it sold — then (3) fights (run-wide debt
    // injection still fires) and (4) jumps rooms to force the checksum. The JOIN reads the HOST's REPLICATED
    // owed + purchased-count + deck: they must match and the session must NOT drop. (MP interest scaling + the
    // missed-payment 대납 grant are lockstep-deterministic combat paths — static-analysis + solo verified; the
    // debt injection is logged here for observation.)

    private static async Task HostPhase(RunManager run)
    {
        try
        {
            await Task.Delay(2000);
            await Shot("01_run");   // ★mandatory visual evidence: this instance really entered the co-op run
            var me = LocalPlayerOf(run);
            if (me == null) { W("HOST: no local player"); Flush(false); return; }

            DebtLoanConfig.MaxLoan = 9999;   // don't let the cap block the test loan

            // 1) Take a loan → dl_sync replicates the Ledger + record to BOTH peers.
            Step("HOST take loan");
            await LoanService.GrantLoanDirect(me, 100);
            await Task.Delay(5000);          // let dl_sync replicate to the client
            W($"HOST: after loan, owed={LedgerPrincipalOf(me)} purchased={PurchasedCount(me)}");
            await Shot("02_loan");

            // 2) ★ Buy a card on debt → dl_sync buy must replicate owed += price + the deck card + the sold-mark.
            Step("HOST buy card on debt");
            var rec = LoanService.For(me);
            var offers = rec != null ? LoanService.RevealedPurchasable(rec) : System.Array.Empty<System.Type>();
            if (rec != null && offers.Length > 0)
            {
                // ★Slot 0 is the FREE gift (price 0 rides the dl_sync wire): owed and the native-Debt count must NOT
                // move, but the card must still land in the deck on BOTH peers.
                var freeType = offers[0];
                int freePrice = LoanService.ShopPriceFor(rec, freeType);
                int owedPreFree = rec.Principal, ndPreFree = NativeDebtInDeck(me);
                W($"HOST: FREE slot0 = {freeType.Name} price={freePrice} (expect 0) — owed {owedPreFree} must stay, nativeDebt {ndPreFree} must stay");
                await LoanService.BuyCardOnDebt(me, freeType);
                await Task.Delay(5000);      // let dl_sync buy replay on BOTH peers
                W($"HOST: after FREE take, owed={LedgerPrincipalOf(me)} nativeDebt={NativeDebtInDeck(me)} purchased={PurchasedCount(me)} deckHasBought={DeckHasAnyPurchased(me)}");

                // …and a PAID slot right after must charge debt AND drop the native Debt curse.
                if (offers.Length > 1)
                {
                    var type = offers[1];
                    int price = LoanService.ShopPriceFor(rec, type);
                    int before = rec.Principal, ndBefore = NativeDebtInDeck(me);
                    W($"HOST: PAID buying {type.Name} for {price} (owed {before} → expect {before + price}, nativeDebt {ndBefore} → expect +1)");
                    await LoanService.BuyCardOnDebt(me, type);
                    await Task.Delay(5000);
                    W($"HOST: after PAID buy, owed={LedgerPrincipalOf(me)} nativeDebt={NativeDebtInDeck(me)} purchased={PurchasedCount(me)} deckHasBought={DeckHasAnyPurchased(me)}");
                }
            }
            else W("HOST: no debt-shop offers to buy (skipping purchase step)");
            await Shot("03_bought");

            // ⑥ 청산 → 청산 후 재대출 — 재설계로 **통째로 바뀐 경로**라 이번 co-op 재실측의 핵심이다.
            //   Repay → BroadcastRepay(`dl_sync repaid`) → 양 피어가 ApplyRepay 를 재생: Active=false, 원금·
            //   Borrowed·이자 회계 0, LoanFloor/LoanDraws 리셋, 네이티브 Debt sweep. 이어서 새 대출이 **톱업이
            //   아니라 새 사이클**로 잡히는지(Borrowed 가 합산되지 않고 100, draws=1)까지 본다.
            //   ★★반드시 **전투 밖**에서 한다: `Repay` 는 `RewardSynchronizer.SyncLocalGoldLost` 를 부르는데
            //   엔진이 전투 중 골드 손실 sync 를 금지한다("Tried to sync losing N gold during combat!" →
            //   InvalidOperationException, 실측으로 2회 확인). 실제 플레이도 동일 — 상환 버튼은 상점 패널에만
            //   있고, 전투 중 청산은 골드가 오가지 않는 `SettleLoanInCombat → ApplyRepay` 로 따로 간다.
            //   여기(상점 단계, 전투 진입 전)가 실제 플레이와 같은 자리다. 바로 아래 `room monster` 전환이
            //   checksum 경계 역할을 하므로, 청산/재대출 상태가 갈라졌다면 JOIN 이 거기서 드롭된다.
            Step("HOST repay → settle → re-borrow");
            // 톱업 1회 — 청산 목돈을 신용 보상 첫 문턱(300) 위로 올려 ⑦ 수령 단계가 실제로 발화하게 한다.
            // (겸사겸사 '같은 상점에서의 추가 인출'도 양 피어에서 재생되는지 본다: draws 가 2 가 돼야 한다.)
            await LoanService.GrantLoanDirect(me, 150);
            await Task.Delay(3000);
            W($"HOST: ⑥ top-up → owed={LedgerPrincipalOf(me)} draws={LoanService.For(me)?.LoanDraws}(2)");

            // ★★먼저 **다음 상점으로 이동**해야 한다 — 빌린 그 상점에서는 갚을 수 없다(CanRepayHere: 신용도
            //   파밍 가드). 이 이동 없이 부르면 `Repay` 가 조용히 false 를 돌려주고 시나리오가 아무것도
            //   검증하지 못한 채 "수렴 PASS" 로 보인다(실측으로 한 번 그렇게 나왔다 — repay=False, active=True,
            //   그리고 뒤이은 재대출이 새 사이클이 아니라 톱업으로 잡혀 borrowed=200/draws=2 가 됐다).
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "room shop", inCombat: false));
            await Task.Delay(8000);
            if (run.State == null || run.State.Players.Count == 0) { W("HOST: SESSION DROPPED at pre-repay room jump"); Flush(false); return; }
            var recPre = LoanService.For(me);
            W($"HOST: ⑥ moved to next shop — floor={me.RunState?.TotalFloor} loanFloor={recPre?.LoanFloor} "
              + $"canRepayHere={LoanService.CanRepayHere(me)}(True)");

            int owedPreRepay = LedgerPrincipalOf(me);
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "gold 999", inCombat: false));
            await Task.Delay(3000);
            bool repaidOk = await LoanService.Repay(me);
            await Task.Delay(4000);
            var recSettled = LoanService.For(me);
            W($"HOST: ⑥ repay={repaidOk} owed {owedPreRepay}→{LedgerPrincipalOf(me)}(0) "
              + $"active={(recSettled != null ? recSettled.Active.ToString() : "n/a")}(False) paid={recSettled?.TotalPaid} "
              + $"nativeDebt={NativeDebtInDeck(me)}(0, 청산이 쓸어냄)");
            await Shot("03b_settled");

            // ⑦ 신용 보상 수령 — 청산으로 누적 상환액이 문턱을 넘었으면 대기 상태가 되고, 수령은 버튼(=여기)이
            //   한다. `dl_sync claim` 은 **awaited** 재생이라 양 피어가 같은 큐 위치에서 처리한다.
            //   ⚠️900/1200 은 카드 선택 화면을 여는데, 이 하네스는 `CardSelectCmd.PushSelector` 로 셀렉터를
            //   밀어 넣으므로 **엔진의 choice 동기화 블록이 통째로 건너뛰어진다**(각 피어가 독립 선택).
            //   그래서 여기서 증명되는 것은 '와이어 + 상태 수렴'이지 엔진 핸드셰이크가 아니다. 하네스 셀렉터가
            //   결정적(list.Take(n))이고 덱 순서가 양 피어 동일이라 선택 자체는 일치한다.
            var recClaim = LoanService.For(me);
            int pendingBefore = LoanService.PendingRewardCount(recClaim);
            int claimedBefore = recClaim?.CreditRewardsTaken ?? 0;
            if (pendingBefore > 0)
            {
                await LoanService.ClaimCreditReward(me);
                await Task.Delay(5000);
            }
            W($"HOST: ⑦ claim pending {pendingBefore}→{LoanService.PendingRewardCount(LoanService.For(me))} "
              + $"claimed {claimedBefore}→{LoanService.For(me)?.CreditRewardsTaken} paid={LoanService.For(me)?.TotalPaid}");

            // ⑦-b ★★엔진 choice 핸드셰이크 실측 — 지금까지 유일하게 남아 있던 미검증 구간.
            //   하네스 셀렉터를 양 피어에서 끄고(dl_testsel 0), 신용 상태를 900/taken=2 로 맞춘 뒤
            //   (dl_testcredit) 수령하면 엔진이 실제 덱 강화 화면을 띄운다. 호스트가 그 화면을 확정하면
            //   SyncLocalChoice → 조인의 WaitForRemoteChoice 가 풀린다. 양 피어가 **같은 카드**를 강화했는지가
            //   FINAL 의 upg= 서명으로 드러난다.
            Step("HOST 엔진 choice 핸드셰이크");
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "dl_testsel 0", inCombat: false));
            await Task.Delay(2500);
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "dl_testcredit 900 2", inCombat: false));
            await Task.Delay(2500);
            var recHs = LoanService.For(me);
            W($"HOST: ⑦b before — creditPaid={recHs?.CreditPaid} taken={recHs?.CreditRewardsTaken} "
              + $"nextTier={LoanService.NextRewardTier(recHs)}(900) claimable={LoanService.CanClaimNextReward(recHs)}");
            TaskHelper.RunSafely(LoanService.ClaimCreditReward(me));   // 화면이 뜰 때까지 기다려야 하므로 detach
            string picked = "(none)";
            for (int i = 0; i < 20 && picked == "(none)"; i++)
            {
                await Task.Delay(1000);
                string r = DriveCardSelectScreen();
                if (!r.StartsWith("(")) picked = r;
            }
            await Task.Delay(5000);   // SyncLocalChoice → 상대 WaitForRemoteChoice 해소까지
            W($"HOST: ⑦b engine-handshake picked={picked} taken={LoanService.For(me)?.CreditRewardsTaken}(3)");
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "dl_testsel 1", inCombat: false));
            await Task.Delay(2500);
            await Shot("03d_handshake");

            await LoanService.GrantLoanDirect(me, 100);
            await Task.Delay(4000);
            var recNew = LoanService.For(me);
            W($"HOST: ⑥ re-borrow borrowed={recNew?.Borrowed}(100, 옛 사이클과 합산 아님) owed={LedgerPrincipalOf(me)}(120) "
              + $"draws={recNew?.LoanDraws}(1) active={recNew?.Active}(True)");

            // ⑧ 빚으로 카드 제거 — 제거 화면 + 빚 증가 + 제거 카운터를 양 피어가 함께 재생해야 한다.
            Step("HOST purge a card on debt");
            int deckBeforePurge = PileType.Deck.GetPile(me)?.Cards?.Count ?? -1;
            int owedBeforePurge = LedgerPrincipalOf(me);
            int purgePrice = LoanService.PurgePrice(me);
            bool purgeOk = await LoanService.PurgeCardOnDebt(me);
            await Task.Delay(5000);
            W($"HOST: ⑧ purge={purgeOk} price={purgePrice} deck {deckBeforePurge}→{PileType.Deck.GetPile(me)?.Cards?.Count} "
              + $"owed {owedBeforePurge}→{LedgerPrincipalOf(me)} removalsUsed={me.ExtraFields?.CardShopRemovalsUsed}");
            await Shot("03c_purged");

            // 3) Combat → run-wide debt injection still fires; 4) room jump → checksum boundary.
            Step("HOST enter combat");
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "room monster", inCombat: false));
            await Task.Delay(9000);
            int joinBailoutMax = 0;
            var cm = CombatManager.Instance;
            if (cm != null && cm.IsInProgress)
            {
                W($"HOST: in combat, my Debt cards = {CombatDebtCards(me)}");
                await Shot("04_combat");

                Step("HOST play 어음 / 레버리지 / 채무 조정");
                // ★★ Cards MUST reach both peers through a networked route and be played through the lockstep
                // action queue. The solo idiom (CombatState.CreateCard + AddGeneratedCardsToCombat + CardCmd.AutoPlay)
                // is HOST-LOCAL on every count — CreateCard fabricates a card only this peer knows about, and
                // CardCmd.AutoPlay executes directly instead of enqueueing — so it desyncs the run by construction
                // (measured: StateDivergence checksum 4, client dropped). Use the built-in `card` console command
                // (IsNetworked = true → spawns into BOTH peers' hands) and TryManualPlay (→ RequestEnqueue(
                // PlayCardAction), the same lockstep path the JOIN's 대납 already uses).
                var cstate = me.Creature?.CombatState;
                if (cstate != null)
                {
                    async Task<CardModel?> NetSpawn(System.Type type)
                    {
                        string id = ModelDb.GetId(type).Entry;
                        int hand = PileType.Hand.GetPile(me)?.Cards?.Count ?? -1;
                        W($"HOST: spawning {id} via dl_testcard (hand {hand}/10)");
                        run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, $"dl_testcard {type.Name}", inCombat: true));
                        await Task.Delay(4000);   // networked spawn lands on both peers (action queue may be busy)
                        var c = PileType.Hand.GetPile(me)?.Cards?.FirstOrDefault(x => x.GetType() == type);
                        if (c == null) W($"HOST: '{id}' not in hand after networked spawn (hand full?)");
                        return c;
                    }

                    // 어음: 에너지 +2, 원금 +100
                    int owedPreNote = LedgerPrincipalOf(me);
                    var note = await NetSpawn(typeof(PromissoryNoteCard));
                    if (note != null) { try { note.TryManualPlay(null); } catch (Exception e) { W("HOST 어음: " + e.Message); } }
                    await Task.Delay(3500);
                    W($"HOST: 어음 played — owed {owedPreNote}→{LedgerPrincipalOf(me)} (expect +100)");

                    // 레버리지: 원금에서 파생된 피해가 실제로 적에게 들어가는지
                    var lev = await NetSpawn(typeof(LeverageCard));
                    var foe = cstate.HittableEnemies?.FirstOrDefault(e => e != null && e.IsAlive)
                              ?? cstate.Enemies?.FirstOrDefault(e => e != null && e.IsAlive);
                    int levDmg = lev != null ? (int)lev.DynamicVars.CalculatedDamage.Calculate(foe) : -1;
                    int foeHp0 = foe?.CurrentHp ?? -1;
                    if (lev != null && foe != null) { try { lev.TryManualPlay(foe); } catch (Exception e) { W("HOST 레버리지: " + e.Message); } }
                    await Task.Delay(3500);
                    W($"HOST: 레버리지 dmg={levDmg} (owed {LedgerPrincipalOf(me)} ÷ 30) enemyHP {foeHp0}→{foe?.CurrentHp ?? -1}");

                    // 채무 조정: 원금 250 탕감 + 런당 1회 플래그
                    int owedPreRes = LedgerPrincipalOf(me);
                    var res = await NetSpawn(typeof(RestructuringCard));
                    if (res != null) { try { res.TryManualPlay(null); } catch (Exception e) { W("HOST 채무 조정: " + e.Message); } }
                    await Task.Delay(3500);
                    W($"HOST: 채무 조정 played — owed {owedPreRes}→{LedgerPrincipalOf(me)} (expect −250, floored at 0) used={LoanService.For(me)?.RestructuringUsed}");
                    await Shot("04d_new_cards");
                }


                var joinP = run.State!.Players.OrderBy(p => p.NetId).Last();   // non-host player (NetId 1000)
                W($"HOST: JOIN gold={(int)joinP.Gold} (>=20 → eligible for 대납)");

                // ④ Fire the missed-payment grant (networked → both peers, exactly as a missed 납부's OnTurnEndInHand
                // does) so the wealthy JOIN receives a 대납. We do NOT advance the turn, so the Ethereal 대납 lingers
                // in the JOIN's hand for it to PLAY (③ below). The turn-end TRIGGER is analysed + solo-verified; the
                // co-op-critical parts are this GRANT's cross-peer convergence and the 대납's USE.
                Step("HOST fire missed-payment bailout grant");
                run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "dl_testmiss", inCombat: true));
                int owedPreBailout = LedgerPrincipalOf(me);
                W($"HOST: owed before bailout={owedPreBailout}");

                Step("HOST await 대납 granted to JOIN");
                for (int probe = 0; probe < 12 && joinBailoutMax == 0; probe++)
                {
                    await Task.Delay(1500);
                    joinBailoutMax = Math.Max(joinBailoutMax, BailoutCardsInHand(joinP));
                    if (probe % 3 == 0 || joinBailoutMax > 0) W($"HOST: probe {probe} JOIN대납={BailoutCardsInHand(joinP)}(max={joinBailoutMax})");
                }
                W($"HOST: ④ JOIN received 대납 max={joinBailoutMax} (expect >=1)");
                await Shot("04b_bailout");

                // ③ The JOIN now PLAYS the 대납 targeting the HOST (its own loop drives the play). STOP ending turns
                // (leave the JOIN a stable turn to play in) and watch the HOST's owed drop by the bailout (no interest
                // accrues mid-combat, so any drop is the 대납 paying down the debt via RecordPayment).
                Step("HOST wait for JOIN to play 대납 → owed drops");
                for (int probe = 0; probe < 20; probe++)
                {
                    await Task.Delay(1500);
                    int owedNow = LedgerPrincipalOf(me);
                    if (owedNow >= 0 && owedNow < owedPreBailout)
                    {
                        W($"HOST: ③ owed dropped {owedPreBailout}→{owedNow} (JOIN played 대납 on my debt)");
                        break;
                    }
                    if (probe % 4 == 0) W($"HOST: waiting for 대납 play — owed={owedNow} (pre={owedPreBailout})");
                }
                int owedPostBailout = LedgerPrincipalOf(me);
                W($"HOST: ③ owed {owedPreBailout}→{owedPostBailout} (expect drop ~20 once JOIN plays the 대납)");
                await Shot("04c_bailout_used");

                // Exit combat → checksum boundary (a divergent owed / deck / sold-set drops the JOIN here).
                run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "room rest", inCombat: true));
                await Task.Delay(8000);
            }
            else W("HOST: combat did not start (room monster jump failed)");

            if (run.State == null || run.State.Players.Count == 0) { W("HOST: SESSION DROPPED"); Flush(false); return; }

            var host = run.State.Players.OrderBy(p => p.NetId).First();
            W($"HOST: FINAL {Descriptor(host)} joinBailoutMax={joinBailoutMax}");
            await Shot("05_final");
            W("=== coop host done ===");
            Flush(true);
        }
        catch (Exception e) { W("HOST exception: " + e); Flush(false); }
    }

    private static async Task JoinPhase(RunManager run)
    {
        try
        {
            await Task.Delay(2000);
            await Shot("01_run");            // ★mandatory: the JOIN side also entered the run

            // Wait for the HOST to finish its whole script (the two instances share one mods folder, so the
            // host's result FILE is a reliable done-signal). While waiting, auto-ready in combat (so the host's
            // fight can progress) and record what the client SEES of the HOST's replicated loan: owed, how many
            // cards the host bought on debt, and whether a bought card is actually in the host's deck on THIS
            // peer. If `dl_sync buy` didn't replicate, purchased=0 / owed is stale here → and the room jump drops us.
            string hostTxt = Path.Combine(ModDir(), "selftest.coop.host.txt");
            string lastLine = "";
            int bailoutMax = 0;
            bool playedBailout = false;
            for (int i = 0; i < 220 && !File.Exists(hostTxt); i++)   // 엔진 핸드셰이크 단계가 추가돼 또 길어졌다   // 청산+재대출+room 전환이 붙어 호스트 스크립트가 길어졌다
            {
                Step($"JOIN waiting for host (t+{i * 2}s)");
                if (run.State == null || run.State.Players.Count == 0) break;   // dropped mid-loop → fall through to the report
                var meJ = LocalPlayerOf(run);
                if (meJ != null)
                {
                    int myBail = BailoutCardsInHand(meJ);
                    bailoutMax = Math.Max(bailoutMax, myBail);   // ④ did I receive a 대납?
                    var cm = CombatManager.Instance;
                    // ③ Once I hold a 대납, PLAY it targeting the HOST (pays down its debt). Networked PlayCardAction via
                    // TryManualPlay → the target's CombatId rides the wire (lockstep, same path as any AnyEnemy card).
                    // We don't end the turn, so the Ethereal 대납 stays in hand until this fires.
                    if (myBail > 0 && !playedBailout && cm?.IsInProgress == true)
                    {
                        var hostPlayer = run.State?.Players.OrderBy(p => p.NetId).First();
                        var bailout = FindBailoutInHand(meJ);
                        if (bailout != null && hostPlayer?.Creature != null)
                        {
                            bool ok = false;
                            try { ok = bailout.TryManualPlay(hostPlayer.Creature); } catch (Exception e) { W("JOIN play 대납: " + e.Message); }
                            W($"JOIN: playing 대납 at host (NetId {hostPlayer.NetId}) → enqueued={ok}");
                            if (ok) playedBailout = true;
                        }
                    }
                }

                string line = (run.State != null && run.State.Players.Count > 0)
                    ? $"{Of(run.State.Players.OrderBy(p => p.NetId).First())} myBailout={bailoutMax} played={playedBailout}"
                    : "(state null — session dropped?)";
                if (line != lastLine) { W($"JOIN t+{i * 2}s: {line}"); lastLine = line; }
                await Task.Delay(2000);
            }
            await Task.Delay(1500);          // let the last replicated actions settle

            if (run.State == null || run.State.Players.Count == 0)
            {
                W("JOIN: SESSION DROPPED (run state gone — the host disconnected us, e.g. state divergence)");
                Flush(false);
                return;
            }
            var host = run.State.Players.OrderBy(p => p.NetId).First();
            W($"JOIN: FINAL {Of(host)} myBailout={bailoutMax} played={playedBailout}");
            await Shot("02_final");          // ★mandatory: what the client actually SEES after replication
            W("=== coop join done ===");
            Flush(true);
        }
        catch (Exception e) { W("JOIN exception: " + e); Flush(false); }

        static string Of(Player h) => Descriptor(h);
    }

    /// <summary>Principal recorded on a player's Ledger relic, or -1 if they don't carry one. Both peers
    /// read the HOST player through this; equal non-(-1) values = the relic + its SavedProperty replicated.</summary>
    private static int LedgerPrincipalOf(Player p)
    {
        var relic = LoanService.LedgerRelicOf(p);
        return relic?.Principal ?? -1;
    }

    /// <summary>Native Debt curses sitting in a player's DECK. The debt shop drops one per PAID visit; the FREE
    /// slot-0 gift must not. Replicated through the `dl_sync buy` replay → both peers must agree.</summary>
    private static int NativeDebtInDeck(Player p)
        => PileType.Deck.GetPile(p)?.Cards?.Count(c => c is MegaCrit.Sts2.Core.Models.Cards.Debt) ?? -1;

    /// <summary>THE convergence descriptor — both peers build this for the HOST player and the two strings must be
    /// byte-identical. `lev` is 레버리지's damage DERIVED from the replicated principal (÷30, the base card's rate):
    /// 레버리지 is the first card in this mod whose DAMAGE is the ledger, so any principal drift that the older
    /// fixed-damage borrow cards used to hide shows up here as a different number.</summary>
    private static string Descriptor(Player h)
    {
        int owed = LedgerPrincipalOf(h);
        var rec = LoanService.For(h);
        return $"owed(host)={owed} purchased(host)={PurchasedCount(h)} deckHasBought={DeckHasAnyPurchased(h)} "
             + $"nativeDebt={NativeDebtInDeck(h)} restructured={(rec != null ? rec.RestructuringUsed.ToString() : "n/a")} "
             + $"lev={(owed >= 0 ? (owed / 30).ToString() : "-1")} "
             + $"draws={(rec != null ? rec.LoanDraws.ToString() : "n/a")} "   // relic SavedProperty → rides the checksum
             // ★재설계 3종. active=계약 개폐(청산해도 record 는 남으므로 이게 유일한 "빚이 있다" 신호),
             //   paid=누적 상환액(청산해도 리셋되지 않는 런 단위 트랙 = 보상 사다리의 축),
             //   borrowed=사이클 단위 원금(청산 후 새 대출이 옛 값과 합산되면 여기서 즉시 드러난다).
             + $"active={(rec != null ? rec.Active.ToString() : "n/a")} "
             + $"paid={(rec != null ? rec.TotalPaid.ToString() : "n/a")} "
             + $"borrowed={(rec != null ? rec.Borrowed.ToString() : "n/a")} "
             // ★신규: 수령한 보상 문턱(유물 SavedProperty → 체크섬)과 덱 장수(빚 제거·보상 카드가 다 여기 드러난다).
             + $"claimed={(rec != null ? rec.CreditRewardsTaken.ToString() : "n/a")} "
             + $"deck={PileType.Deck.GetPile(h)?.Cards?.Count ?? -1} "
             + $"purges={h.ExtraFields?.CardShopRemovalsUsed ?? -1} "
             // ★엔진 choice 핸드셰이크의 산출물 — 두 피어가 **같은 카드**를 강화했는지가 핵심.
             + $"upg={UpgradedSignature(h)}";
    }

    /// <summary>덱에서 강화된 카드의 서명(개수 + 첫 카드 id). 엔진이 동기화한 선택이 양 피어에서 같은
    /// 카드였는지를 이 문자열 하나로 비교한다.</summary>
    private static string UpgradedSignature(Player p)
    {
        // ★전투 중엔 PileType.Deck.GetPile 이 전투 파일을 가리켜 마스터 덱의 강화가 안 보인다 —
        // solo 가 쓰는 player.Deck(마스터 덱)을 그대로 써야 두 피어를 같은 기준으로 비교할 수 있다.
        var up = p.Deck?.Cards?.Where(c => c.IsUpgraded).Select(c => c.Id.Entry).OrderBy(x => x).ToList();
        if (up == null || up.Count == 0) return "0:-";
        return $"{up.Count}:{up[0]}";
    }

    /// <summary>How many cards this player has bought on debt (their sold-set size), or -1 if they carry no loan.
    /// Replicated via `dl_sync buy` → both peers must agree.</summary>
    private static int PurchasedCount(Player p) => LoanService.For(p)?.PurchasedCards.Count ?? -1;

    /// <summary>True if the player's DECK actually holds a card they bought on debt — proves the purchase's deck
    /// mutation (not just the owed number) replicated to THIS peer.</summary>
    private static bool DeckHasAnyPurchased(Player p)
    {
        var rec = LoanService.For(p);
        var deck = PileType.Deck.GetPile(p);
        if (rec == null || deck == null || rec.PurchasedCards.Count == 0) return false;
        return deck.Cards.Any(c => rec.PurchasedCards.Contains(c.GetType().Name));
    }

    /// <summary>Debt cards currently in a player's combat piles (draw/hand/discard).</summary>
    private static int CombatDebtCards(Player p)
    {
        int n = 0;
        foreach (var pt in new[] { PileType.Draw, PileType.Hand, PileType.Discard })
        {
            var pile = pt.GetPile(p);
            if (pile != null) n += pile.Cards.Count(c => c is DebtCurseCard);
        }
        return n;
    }

    /// <summary>납부 (DebtCurseCard) cards in a player's HAND — confirms the test injection landed on this peer.
    /// Guarded: GetPile(Hand) throws OUT of combat, and this is polled before/after combat too.</summary>
    private static int PaymentCardsInHand(Player p)
    {
        try { return PileType.Hand.GetPile(p)?.Cards.Count(c => c is DebtCurseCard) ?? 0; } catch { return 0; }
    }

    /// <summary>대납 (BailoutCard) cards in a player's HAND — the missed-payment grant handed to a wealthy ally.
    /// Guarded the same way (polled out of combat).</summary>
    private static int BailoutCardsInHand(Player p)
    {
        try { return PileType.Hand.GetPile(p)?.Cards.Count(c => c is BailoutCard) ?? 0; } catch { return 0; }
    }

    /// <summary>The first 대납 (BailoutCard) in a player's hand, or null. Used by the JOIN to PLAY it at the host.</summary>
    private static CardModel? FindBailoutInHand(Player p)
    {
        try { return PileType.Hand.GetPile(p)?.Cards.FirstOrDefault(c => c is BailoutCard); } catch { return null; }
    }

    /// <summary>End a co-op turn the ONLY way that actually works: enqueue the NETWORKED <c>EndPlayerTurnAction</c>
    /// for this peer's local player. <c>SetReadyToEndTurn</c> only adds the caller to the LOCAL ready-set (1 of 2),
    /// so the turn never ends; the networked action replays on both peers, so each peer's set reaches 2 → the turn
    /// ends and end-of-turn hooks (OnTurnEndInHand) fire. Both peers must call this for the shared turn to advance.
    /// Guarded to the play phase + not-already-ready (a second enqueue would be a stale no-op / an Undo toggle).</summary>
    private static void TryEndTurn(Player p)
    {
        try
        {
            var cm = CombatManager.Instance;
            var sync = RunManager.Instance?.ActionQueueSynchronizer;
            if (cm == null || sync == null || !cm.IsInProgress) return;
            if (cm.IsPlayerReadyToEndTurn(p)) return;   // already ready → a re-enqueue would be a stale no-op
            // (No play-phase guard: EndPlayerTurnAction is round-stamped, so an enqueue during the enemy turn is
            //  ignored by its own round check — harmless.)
            int round = p.Creature!.CombatState!.RoundNumber;
            sync.RequestEnqueue(new MegaCrit.Sts2.Core.GameActions.EndPlayerTurnAction(p, round));
        }
        catch (Exception e) { W("TryEndTurn: " + e.Message); }
    }

    #region selection automation (auto-selector + screen pump)
    // Identical to Sts2RelicForge's CoopTest: every CardSelectCmd path is `if (Selector != null) auto-pick;
    // else await screen.CardsSelected()`, and an unanswered prompt on ONE co-op peer parks the OTHER in
    // WaitForRemoteChoice → both result files vanish. The selector + pump close that hole. The pick MUST be
    // deterministic (first N) because the co-op selector path skips SyncLocalChoice. NEVER TestMode.IsOn
    // (it disables ChecksumTracker, the very detector this test relies on). See the coop-verify skill.

    private static readonly HashSet<string> _pumpIgnore = new();
    private const int PumpGraceMs = 4000;
    private static IDisposable? _selectorScope;
    /// <summary>엔진 핸드쉐이크 실측 중엔 펌프가 셀렉터를 다시 밀지 못하게 막는다 —
    /// PumpLoop 가 매 반복 EnsureSelector() 를 부르므로 이 게 없으면 끄자마자 부활한다(실측으로 확인).</summary>
    private static bool _selectorSuspended;
    private static bool _pumpRunning;

    private static void StartAutomation()
    {
        EnsureSelector();
        if (_pumpRunning) return;
        _pumpRunning = true;
        int handlers = ScreenHandlers().Count;
        TaskHelper.RunSafely(PumpLoop());
        W($"selection automation on (selector + {handlers} screen handler(s), grace {PumpGraceMs}ms)");
    }

    /// <summary>하네스 셀렉터를 끈다/켠다. ★엔진 핸드셰이크를 실측하려면 반드시 꺼야 한다 —
    /// <c>CardSelectCmd.Selector</c> 가 살아 있으면 엔진이 ReserveChoiceId 블록을 통째로 건너뛴다.
    /// 두 피어가 <b>동시에</b> 꺼져 있어야 하므로 networked 커맨드로 토글한다.</summary>
    internal static void SetSelector(bool on)
    {
        try
        {
            if (on) { _selectorSuspended = false; EnsureSelector(); W("  [selector] ON (하네스 자동 선택)"); }
            else    { _selectorSuspended = true; _selectorScope?.Dispose(); _selectorScope = null; CardSelectCmd.Reset();
                      W($"  [selector] OFF (엔진 실경로 사용) selector={(CardSelectCmd.Selector == null ? "null" : "STILL SET")}"); }
        }
        catch (Exception e) { W("selector toggle failed: " + e.Message); }
    }

    /// <summary>열려 있는 카드 선택 화면을 찾아 첫 카드로 확정한다(= 마우스 클릭 대신).
    /// 화면의 <c>_completionSource</c> 를 채우면 <c>await screen.CardsSelected()</c> 가 풀리고, 엔진이
    /// 이어서 <c>SyncLocalChoice</c> 를 호출한다 — 그게 상대 피어의 WaitForRemoteChoice 를 푼다.</summary>
    internal static string DriveCardSelectScreen()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return "(no tree)";
        var screen = FindSelectScreen(tree.Root);
        if (screen == null) return "(no screen)";
        try
        {
            var t = screen.GetType();
            FieldInfo? cardsF = null, srcF = null;
            for (var ty = t; ty != null && (cardsF == null || srcF == null); ty = ty.BaseType)
            {
                cardsF ??= ty.GetField("_cards", BindingFlags.NonPublic | BindingFlags.Instance);
                srcF   ??= ty.GetField("_completionSource", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (cardsF?.GetValue(screen) is not IReadOnlyList<CardModel> cards || cards.Count == 0) return "(no cards)";
            var pick = cards[0];   // ★결정적: 항상 첫 카드
            var src = srcF?.GetValue(screen);
            var trySet = src?.GetType().GetMethod("TrySetResult");
            trySet?.Invoke(src, new object[] { new List<CardModel> { pick } });
            return pick.Id.Entry;
        }
        catch (Exception e) { return "(drive failed: " + e.Message + ")"; }
    }

    private static Node? FindSelectScreen(Node root)
    {
        if (root.GetType().Name.Contains("SelectScreen") || root.GetType().Name.Contains("SelectionScreen"))
        {
            for (var ty = root.GetType(); ty != null; ty = ty.BaseType)
                if (ty.Name == "NCardGridSelectionScreen") return root;
        }
        foreach (var c in root.GetChildren()) { var hit = FindSelectScreen(c); if (hit != null) return hit; }
        return null;
    }

    private static void EnsureSelector()
    {
        if (_selectorSuspended) return;   // ★핸드쉐이크 실측 구간에선 절대 되살리지 않는다
        try { if (CardSelectCmd.Selector == null) _selectorScope = CardSelectCmd.PushSelector(new AutoSelector()); }
        catch (Exception e) { W("selector push failed: " + e.Message); }
    }

    /// <summary>First-N deterministic picker — random is forbidden in co-op (selector path skips sync).</summary>
    private sealed class AutoSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var list = options.ToList();
            int n = Math.Min(maxSelect, list.Count);
            if (n < minSelect) n = Math.Min(minSelect, list.Count);
            W($"  [selector] auto-picked {n}/{list.Count}: [{string.Join(", ", list.Take(n).Select(c => c.Id.Entry))}]");
            return Task.FromResult<IEnumerable<CardModel>>(list.Take(n).ToList());
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            var pick = options.FirstOrDefault()?.Card;
            W($"  [selector] auto-picked card reward: {pick?.Id.Entry ?? "(none)"}");
            return new CardRewardSelection { card = pick, alternative = null };
        }
    }

    private static async Task PumpLoop()
    {
        var rng = new Rng(1u);
        object? seen = null;
        var seenAt = DateTime.UtcNow;
        int attempts = 0;
        while (!_done)
        {
            await Task.Delay(500);
            try
            {
                EnsureSelector();
                object? top = NOverlayStack.Instance?.Peek();
                if (top == null) { seen = null; attempts = 0; continue; }
                if (!ReferenceEquals(top, seen)) { seen = top; seenAt = DateTime.UtcNow; attempts = 0; continue; }
                if ((DateTime.UtcNow - seenAt).TotalMilliseconds < PumpGraceMs) continue;

                string name = top.GetType().Name;
                if (_pumpIgnore.Contains(name)) continue;
                if (attempts >= 3)
                {
                    if (attempts == 3) { attempts++; W($"  [pump] {name} will not close after 3 attempts — leaving it (watchdog will name the step)"); }
                    continue;
                }
                attempts++;
                W($"  [pump] auto-handling unattended screen: {name} (attempt {attempts})");
                await HandleScreen(top, rng);
                seenAt = DateTime.UtcNow;
            }
            catch (Exception e) { W("  [pump] " + e.Message); }
        }
    }

    private static async Task HandleScreen(object screen, Rng rng)
    {
        if (!ScreenHandlers().TryGetValue(screen.GetType(), out var handler))
        {
            W($"  [pump] no AutoSlay handler for {screen.GetType().Name} — cannot auto-dismiss; drive it from the test or avoid it.");
            return;
        }
        var ht = handler.GetType();
        var timeout = ht.GetProperty("Timeout")?.GetValue(handler) as TimeSpan? ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(timeout);
        var task = ht.GetMethod("HandleAsync")?.Invoke(handler, new object[] { rng, cts.Token }) as Task;
        if (task == null) { W($"  [pump] {ht.Name}.HandleAsync not invokable"); return; }
        await task;
        W($"  [pump] handled {screen.GetType().Name}");
    }

    private static Dictionary<Type, object>? _screenHandlers;

    private static Dictionary<Type, object> ScreenHandlers()
    {
        if (_screenHandlers != null) return _screenHandlers;
        var map = new Dictionary<Type, object>();
        try
        {
            var asm = typeof(CardSelectCmd).Assembly;
            var iface = asm.GetType("MegaCrit.Sts2.Core.AutoSlay.Handlers.IScreenHandler");
            if (iface == null) { W("  [pump] AutoSlay handlers not found in this game build — pump limited to logging"); return _screenHandlers = map; }
            Type?[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || t.IsInterface || !iface.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                var h = Activator.CreateInstance(t);
                if (h != null && t.GetProperty("ScreenType")?.GetValue(h) is Type st) map[st] = h;
            }
        }
        catch (Exception e) { W("  [pump] handler discovery failed: " + e.Message); }
        return _screenHandlers = map;
    }

    private static string TopScreenName()
    {
        try { return NOverlayStack.Instance?.Peek()?.GetType().Name ?? "(none)"; } catch { return "(unavailable)"; }
    }
    #endregion

    /// <summary>Save the root viewport to selftest.coop.&lt;role&gt;.&lt;name&gt;.png, retrying while the frame is
    /// still BLACK (post-entry frames are often loading/transition). See coop-verify.</summary>
    private static async Task Shot(string name, int tries = 6)
    {
        try
        {
            for (int i = 0; i < tries; i++)
            {
                if (Engine.GetMainLoop() is not SceneTree tree) return;
                var img = tree.Root.GetTexture()?.GetImage();
                if (img != null && !IsBlank(img))
                {
                    var err = img.SavePng(Path.Combine(ModDir(), $"selftest.coop.{_role}.{name}.png"));
                    W($"shot {name}: {err} (try {i + 1})");
                    return;
                }
                await Task.Delay(2000);
            }
            if (Engine.GetMainLoop() is SceneTree t2)
                t2.Root.GetTexture()?.GetImage()?.SavePng(Path.Combine(ModDir(), $"selftest.coop.{_role}.{name}.png"));
            W($"shot {name}: still black after {tries} tries (saved anyway)");
        }
        catch (Exception e) { W($"shot {name} failed: {e.Message}"); }
    }

    private static bool IsBlank(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        if (w == 0 || h == 0) return true;
        for (int x = w / 10; x < w; x += System.Math.Max(1, w / 10))
            for (int y = h / 10; y < h; y += System.Math.Max(1, h / 10))
            {
                var c = img.GetPixel(x, y);
                if (c.R + c.G + c.B > 0.05f) return false;
            }
        return true;
    }

    private static NCharacterSelectScreen? FindScreen(Node n)
    {
        if (n is NCharacterSelectScreen s) return s;
        foreach (var c in n.GetChildren()) { var r = FindScreen(c); if (r != null) return r; }
        return null;
    }

    private static StartRunLobby? LobbyOf(NCharacterSelectScreen screen)
    {
        try { return typeof(NCharacterSelectScreen).GetField("_lobby", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(screen) as StartRunLobby; }
        catch { return null; }
    }

    private static Player? LocalPlayerOf(RunManager run)
    {
        var players = run.State!.Players;
        try { var me = LocalContext.GetMe(players); if (me != null) return me; } catch { }
        ulong id; try { id = run.NetService.NetId; } catch { id = 1uL; }
        return players.FirstOrDefault(p => p.NetId == id) ?? players.FirstOrDefault();
    }

    private static void W(string line) { _out.AppendLine(line); MainFile.Logger.Info($"[{MainFile.ModId}] COOP[{_role}] | {line}"); }

    private static void Flush(bool ok)
    {
        if (_done) return;
        _done = true;
        _selectorScope?.Dispose();
        _selectorScope = null;
        _out.Insert(0, (ok ? "RESULT: OK\n" : "RESULT: FAIL\n") + "role=" + _role + "\n");
        try { File.WriteAllText(Path.Combine(ModDir(), $"selftest.coop.{_role}.txt"), _out.ToString()); } catch { }
    }
}

/// <summary>TEST-ONLY networked command (Debug builds only — stripped from Release): inject a 납부 (DebtCurseCard)
/// into the issuing player's combat HAND on BOTH peers, so the co-op self-test can MISS it (end the turn without
/// playing it) and drive the REAL trigger — the 납부's OnTurnEndInHand → GrantBailoutForMissedPayment. Networked
/// (like dl_sync) so both peers put the card in the same hand lockstep, exactly as the 정기 납부 power feeds it.</summary>
public sealed class DebtLoanTestMissCmd : MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.AbstractConsoleCmd
{
    public override string CmdName => "dl_testmiss";
    public override string Args => "";
    public override string Description => "TEST: inject a 납부 card into your combat hand (both peers).";
    public override bool IsNetworked => true;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer?.Creature?.CombatState == null)
            return new CmdResult(success: false, "dl_testmiss: not in combat.");
        // Fire the SAME grant a missed 납부's OnTurnEndInHand fires — networked, so it replays on BOTH peers exactly
        // as the real hook does (fires on both peers, lockstep). We drive it directly rather than through a real
        // turn-end because the end-of-turn-in-hand snapshot is taken at TURN START, so a card provisioned during the
        // test (never present at a turn start) can't reach that hook in the harness. The turn-end path itself is
        // solo-verified; here we verify the co-op-critical GRANT + its cross-peer convergence + the 대납's USE.
        TaskHelper.RunSafely(LoanService.GrantBailoutForMissedPayment(issuingPlayer, upgraded: false));
        return new CmdResult(success: true, "dl_testmiss: fired missed-payment bailout grant.");
    }
}

/// <summary>TEST-ONLY networked command (Debug builds only): put one of THIS MOD's cards into the issuing player's
/// combat hand on BOTH peers, so the co-op self-test can then play it through the lockstep PlayCardAction.
///
/// Why not the built-in `card &lt;ID&gt;`? It is IsNetworked and its action DOES execute on both peers, but the card
/// never reached the hand in this harness (measured twice, hand 5/10 so it wasn't the full-hand refusal) — so the
/// test silently exercised nothing. This command mirrors <see cref="DebtLoanTestMissCmd"/>, which is the injection
/// path already proven to converge in this very scenario (the 대납 grant), and it uses the mod's own
/// AddGeneratedCardsToCombat rather than CardPileCmd.Add.
///
/// The created Task rides the CmdResult (like the built-in CardConsoleCmd) so the action AWAITS it — no detached
/// RunSafely inside a mid-command hook (coop-guard class 1). Position is Top, not Random, so no RNG is consumed.</summary>
/// <summary>TEST(networked): 신용 상태를 양 피어에 동일하게 세팅한다. ★로컬로 rec 를 만지면 유물
/// SavedProperty 가 갈라져 그 자체로 desync 다 — 반드시 두 피어가 함께 적용해야 한다.</summary>
public sealed class DebtLoanTestCreditCmd : MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.AbstractConsoleCmd
{
    public override string CmdName => "dl_testcredit";
    public override string Args => "<creditPaid> <taken>";
    public override string Description => "TEST: set credit-paid + claimed-count on both peers.";
    public override bool IsNetworked => true;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var rec = issuingPlayer != null ? LoanService.For(issuingPlayer) : null;
        if (rec == null) return new CmdResult(success: false, "dl_testcredit: no loan record.");
        if (args.Length < 2) return new CmdResult(success: false, "dl_testcredit: <creditPaid> <taken>");
        int.TryParse(args[0], out int paid);
        int.TryParse(args[1], out int taken);
        rec.CreditPaid = paid;
        rec.CreditRewardsTaken = taken;
        LoanService.SyncToRelicForTest(issuingPlayer!);
        return new CmdResult(success: true, $"dl_testcredit paid={paid} taken={taken}.");
    }
}

/// <summary>TEST(networked): 하네스 셀렉터를 양 피어에서 동시에 끄고/켠다. 엔진의 choice 핸드셰이크를
/// 실측하려면 두 피어 모두 셀렉터가 없어야 한다(있으면 엔진이 동기화 블록을 건너뛴다).</summary>
public sealed class DebtLoanTestSelectorCmd : MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.AbstractConsoleCmd
{
    public override string CmdName => "dl_testsel";
    public override string Args => "<0|1>";
    public override string Description => "TEST: toggle the harness card-selector on both peers.";
    public override bool IsNetworked => true;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        bool on = args.Length > 0 && args[0] == "1";
        CoopTest.SetSelector(on);
        return new CmdResult(success: true, $"dl_testsel {(on ? "on" : "off")}.");
    }
}

public sealed class DebtLoanTestCardCmd : MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.AbstractConsoleCmd
{
    public override string CmdName => "dl_testcard";
    public override string Args => "<ClassName>";
    public override string Description => "TEST: put a DebtLoan card into your combat hand (both peers).";
    public override bool IsNetworked => true;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var cstate = issuingPlayer?.Creature?.CombatState;
        if (cstate == null) return new CmdResult(success: false, "dl_testcard: not in combat.");
        if (args.Length == 0) return new CmdResult(success: false, "dl_testcard: no card class given.");
        var type = typeof(DebtLoanTestCardCmd).Assembly.GetType("Sts2DebtLoan." + args[0]);
        if (type == null) return new CmdResult(success: false, $"dl_testcard: unknown card '{args[0]}'.");
        var card = cstate.CreateCard(ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(type))!, issuingPlayer!);
        var task = CardPileCmd.AddGeneratedCardsToCombat(
            new List<CardModel> { card }, PileType.Hand, issuingPlayer!, CardPilePosition.Top);
        return new CmdResult(task, success: true, $"dl_testcard: added {args[0]} to hand.");
    }
}
#endif
