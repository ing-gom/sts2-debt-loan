#if DEBTLOAN_SELFTEST
// solo-verify harness for Sts2DebtLoan. Armed only when `selftest.sp.flag` sits next to the DLL.
// Drives the loan cycle through LoanService directly (the merchant-purchase + repay-button UI paths
// are exercised separately; here we verify the state machine end to end on a live SP run).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;                        // PlayerCmd, CardSelectCmd
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives; // CardRewardAlternative
using MegaCrit.Sts2.Core.Entities.Cards;                  // CardCreationResult
using MegaCrit.Sts2.Core.Entities.Players;                // Player
using MegaCrit.Sts2.Core.Helpers;                         // TaskHelper
using MegaCrit.Sts2.Core.Models;                          // ModelDb, ActModel, ModifierModel
using MegaCrit.Sts2.Core.Nodes;                           // NGame
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;          // NMainMenu (run-start readiness gate)
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;          // NOverlayStack
using MegaCrit.Sts2.Core.Random;                          // Rng
using MegaCrit.Sts2.Core.Runs;                            // RunManager, GameMode
using MegaCrit.Sts2.Core.TestSupport;                     // ICardSelector
using MegaCrit.Sts2.Core.Entities.Merchant;               // MerchantInventory, MerchantRelicEntry
using MegaCrit.Sts2.Core.Entities.Relics;                 // RelicRarity
using MegaCrit.Sts2.Core.Entities.Gold;                   // GoldLossType
using MegaCrit.Sts2.Core.Rooms;                           // RoomType
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;             // NMerchantInventory

namespace Sts2DebtLoan;

internal static class SoloTest
{
    private const string Tag = "Sts2DebtLoan";
    private const double StepTimeoutSec = 90;

    private static readonly StringBuilder _out = new();
    private static bool _started, _done;
    private static string _step = "(not started)";
    private static DateTime _stepAt = DateTime.UtcNow;

    private static string ModDir() => Path.GetDirectoryName(typeof(SoloTest).Assembly.Location) ?? ".";

    public static void ArmIfRequested()
    {
        try
        {
            if (!File.Exists(Path.Combine(ModDir(), "selftest.sp.flag"))) return;
            W("solo selftest armed");
            Poll();
        }
        catch (Exception e) { Log($"solo arm failed: {e.Message}"); }
    }

    private static void Poll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || _done) return;
        try { Tick(tree); } catch (Exception e) { W("tick exception: " + e.Message); }
        if (!_done) tree.CreateTimer(2.0).Timeout += Poll;
    }

    private static void Tick(SceneTree tree)
    {
        var run = RunManager.Instance;
        if (!_started && (run == null || !run.IsInProgress))
        {
            if (NGame.Instance == null) { W("waiting for NGame…"); return; }
            if (ModelCount() == 0) { W("waiting for ModelDb to populate…"); return; }
            // ★ Wait for the MAIN MENU to actually be up before starting a run. In a heavy modded env
            // ModelDb populates long before the menu finishes loading, so gating only on ModelDb fired
            // StartNewSingleplayerRun too early — the run never entered (259 log lines before the menu
            // was 'loaded (complete)'). NMainMenu present = the game is ready to start a run.
            if (FindNode<NMainMenu>(tree.Root) == null) { W("waiting for main menu…"); return; }
            _started = true;
            Step("starting single-player run");
            TaskHelper.RunSafely(StartRunThenTest());
            return;
        }

        if (_started && !_done && (DateTime.UtcNow - _stepAt).TotalSeconds > StepTimeoutSec)
        {
            W($"WATCHDOG: no progress for {StepTimeoutSec:F0}s at step '{_step}' — flushing partial result.");
            W($"WATCHDOG: overlay on top = {TopScreenName()}.");
            Flush(false);
        }
    }

    private static void Step(string name) { _step = name; _stepAt = DateTime.UtcNow; W($"— {name}"); }

    private static int ModelCount()
    {
        try
        {
            var f = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
            return (f?.GetValue(null) as System.Collections.IDictionary)?.Count ?? 0;
        }
        catch { return 0; }
    }

    private static async Task StartRunThenTest()
    {
        try
        {
            // ★ Use vanilla IRONCLAD explicitly, not AllCharacters.First(): in a heavy modded environment
            // First() can resolve to a custom character whose run-start hangs — that (not this mod) is why
            // the modded automated test stalled at 'starting single-player run'.
            var character = ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry == "IRONCLAD")
                            ?? ModelDb.AllCharacters.First();
            W($"picked character: {character.Id.Entry} (of {ModelDb.AllCharacters.Count()}; First()={ModelDb.AllCharacters.First().Id.Entry})");
            var acts = ActModel.GetDefaultList().ToList();
            await NGame.Instance.StartNewSingleplayerRun(character, shouldSave: false, acts,
                Array.Empty<ModifierModel>(), "SOLOTEST", GameMode.Standard, 0);
            await Task.Delay(3000);

            var run = RunManager.Instance;
            if (run?.IsInProgress != true || (run.State?.Players?.Count ?? 0) == 0)
            { W("run did not start"); Flush(false); return; }
            var player = run.State!.Players.First();
            W($"run started: {player.Character?.Id.Entry}, floor {run.State.TotalFloor}, gold {(int)player.Gold}");

            StartAutomation();
            await Shot("1_run");

            // Deterministic config for the scenario.
            DebtLoanConfig.MaxLoan = 300;
            DebtLoanConfig.InterestPerDraw = 10;
            DebtLoanConfig.PrincipalRepayShare = 0.2;
            DebtLoanConfig.MaxLoanActIndex = 2;   // allow loans in every act for the test

            bool all = true;

            // Custom curse-card portraits: each must load from the mod pck at the exact res:// path the card's
            // PortraitPath override returns (renderer: _portrait.Texture = Model.Portrait => Load(PortraitPath)).
            Step("curse-card portraits");
            {
                bool tArt = true;
                foreach (var n in new[] { "debt_dunning", "debt_dunning_plus", "overdue", "seizure", "bad_credit", "forced_levy" })
                {
                    var tex = ResourceLoader.Load<Texture2D>($"res://Sts2DebtLoan/card_art/{n}.png", null, ResourceLoader.CacheMode.Reuse);
                    var sz = tex?.GetSize() ?? Vector2.Zero;
                    bool ok = tex != null && (int)sz.X == 1000 && (int)sz.Y == 760;
                    W($"  art {n}: {(tex != null ? "loaded" : "NULL")} {(int)sz.X}x{(int)sz.Y} -> {ok}");
                    tArt &= ok;
                }
                W($"  assert portraits: all 6 load @1000x760 -> {tArt}");
                all &= tArt;
            }

            DebtLoanConfig.MaxLoan = 9999;   // let any test loan through the cap
            var mkEntry = new Func<MerchantRelicEntry>(() => new MerchantRelicEntry(RelicRarity.Shop, player));

            // A) Loan grant → relic + immediate 1 Debt card (rooms 0).
            Step("loan grant");
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 100);
            await Task.Delay(300);
            var rec = LoanService.For(player);
            bool tA = LoanService.PlayerHasLedger(player) && rec != null && rec.Borrowed == 100 && rec.Principal == 100
                      && rec.Active && LoanService.DebtCardCountFor(player) == 1;
            W($"  assert loan: ledger={LoanService.PlayerHasLedger(player)} borrowed={rec?.Borrowed} owed={rec?.Principal} active={rec?.Active} cards@0={LoanService.DebtCardCountFor(player)} -> {tA}");
            all &= tA;

            // B) Debt-curse TIER schedule: accelerating gaps 0/10/17/22 (each tier unlocks a new curse),
            //    capped at 4. Rooms are COMPUTED as TotalFloor − LoanFloor, so we simulate by back-dating.
            //    Check the exact boundaries incl. just-below (16→2, 21→3) to prove the thresholds.
            Step("debt-curse tier schedule (10/17/22)");
            int baseFloor = player.RunState.TotalFloor;
            var recB = LoanService.For(player)!;
            recB.LoanFloor = baseFloor;        int cnt0  = LoanService.DebtCardCountFor(player);   // rooms 0  → 1
            recB.LoanFloor = baseFloor - 10;   int cnt10 = LoanService.DebtCardCountFor(player);   // rooms 10 → 2
            recB.LoanFloor = baseFloor - 16;   int cnt16 = LoanService.DebtCardCountFor(player);   // rooms 16 → 2 (below 17)
            recB.LoanFloor = baseFloor - 17;   int cnt17 = LoanService.DebtCardCountFor(player);   // rooms 17 → 3
            recB.LoanFloor = baseFloor - 21;   int cnt21 = LoanService.DebtCardCountFor(player);   // rooms 21 → 3 (below 22)
            recB.LoanFloor = baseFloor - 22;   int cnt22 = LoanService.DebtCardCountFor(player);   // rooms 22 → 4
            recB.LoanFloor = baseFloor - 30;   int cnt30 = LoanService.DebtCardCountFor(player);   // rooms 30 → 4 (cap)
            LoanService.SyncToRelic(player);         // persist LoanFloor=baseFloor-30 onto the relic for the C round-trip
            bool tB = cnt0 == 1 && cnt10 == 2 && cnt16 == 2 && cnt17 == 3 && cnt21 == 3 && cnt22 == 4 && cnt30 == 4;
            W($"  assert tier: r0={cnt0}(1) r10={cnt10}(2) r16={cnt16}(2) r17={cnt17}(3) r21={cnt21}(3) r22={cnt22}(4) r30={cnt30}(4) -> {tB}");
            all &= tB;
            // Badge countdown = rooms until the NEXT escalation (0 at the top tier → counter hidden).
            int b0 = DebtLoanConfig.RoomsUntilNextTier(0), b10 = DebtLoanConfig.RoomsUntilNextTier(10),
                b17 = DebtLoanConfig.RoomsUntilNextTier(17), b22 = DebtLoanConfig.RoomsUntilNextTier(22);
            bool tBadge = b0 == 10 && b10 == 7 && b17 == 5 && b22 == 0;
            W($"  assert badge: r0={b0}(10) r10={b10}(7) r17={b17}(5) r22={b22}(0/max) -> {tBadge}");
            all &= tBadge;
            // Per-relic hover: DynamicDescription fills {borrowed}/{paid} + the choose() per-tier curse name.
            // Verify the choose() actually resolved (no leftover "{cards"/"choose(" token in the rendered text).
            string hoverT4 = "";
            try { hoverT4 = LoanService.LedgerRelicOf(player)?.DynamicDescription.GetFormattedText() ?? "";
                  W($"  ledger hover (tier 4): {hoverT4}"); }
            catch (Exception e) { W("  hover read failed: " + e.Message); }
            bool tChoose = hoverT4.Length > 0 && !hoverT4.Contains("{cards") && !hoverT4.Contains("choose(");
            W($"  assert choose render (per-tier name resolved): {tChoose}");
            all &= tChoose;

            // C) Persistence round-trip (numeric state on the relic).
            Step("save/load persistence");
            var save = RunManager.Instance.ToSave(null);
            var reloaded = RunState.FromSerializable(save);
            var rp = reloaded.Players.First();
            var rrelic = LoanService.LedgerRelicOf(rp);
            bool tC = rrelic != null && rrelic.Borrowed == 100 && rrelic.Principal == 100 && rrelic.LoanFloor == baseFloor - 30 && rrelic.Active;
            LoanService.RestoreFromRelic(rp);
            var rrec = LoanService.For(rp);
            // rooms-since-loan (30 → tier 4) is re-derived from the restored LoanFloor, not stored.
            bool tC2 = rrec != null && rrec.Borrowed == 100 && rrec.Principal == 100 && rrec.LoanFloor == baseFloor - 30 && rrec.Active
                       && LoanService.DebtCardCountFor(rp) == 4;
            W($"  assert persist: relic borrowed={rrelic?.Borrowed} owed={rrelic?.Principal} loanFloor={rrelic?.LoanFloor} -> {tC}; restore owed={rrec?.Principal} cards={LoanService.DebtCardCountFor(rp)}(4) -> {tC2}");
            all &= tC && tC2;

            // D) Debt price surcharge at OTHER shops (rooms 30 = 3 cards → +20%); none at your own shop.
            Step("debt price surcharge");
            var rd = LoanService.For(player)!;
            int df = rd.LoanFloor;
            rd.LoanFloor = player.RunState.TotalFloor;                            // same shop → rooms 0, no surcharge
            double sameMult = LoanService.DebtPriceMultiplier(player);           // 1.0
            rd.LoanFloor = player.RunState.TotalFloor - 30;                       // different shop, rooms 30 → 3 cards
            double otherMult = LoanService.DebtPriceMultiplier(player);          // 1.20
            rd.LoanFloor = df;
            bool tD = Math.Abs(sameMult - 1.0) < 0.001 && Math.Abs(otherMult - 1.20) < 0.001;
            W($"  assert surcharge: sameShop={sameMult}(1.0) otherShop={otherMult}(1.2) -> {tD}");
            all &= tD;

            // E) Same-shop top-up rule.
            Step("same-shop top-up");
            if ((int)player.Gold > 0) await PlayerCmd.LoseGold((int)player.Gold, player, GoldLossType.Spent);
            var re = LoanService.For(player)!;
            re.LoanFloor = player.RunState.TotalFloor;   // undo B's room back-dating: we're back at the borrow shop
            var entryE = mkEntry();
            bool sameOk = LoanService.CanLoanCover(entryE, player);
            int sf = re.LoanFloor; re.LoanFloor = sf - 999;
            bool otherDenied = !LoanService.CanLoanCover(entryE, player);
            re.LoanFloor = sf;
            bool tE = sameOk && otherDenied;
            W($"  assert same-shop: sameOk={sameOk} otherDenied={otherDenied} -> {tE}");
            all &= tE;

            // F) Repay → relic REMOVED + record reset → can borrow again (fresh first loan allowed).
            Step("repay → re-borrow");
            if ((int)player.Gold < re.Principal) await PlayerCmd.GainGold(re.Principal - (int)player.Gold, player, false);
            await Task.Delay(150);
            bool repaid = await LoanService.Repay(player);
            await Task.Delay(250);
            bool relicGone = !LoanService.PlayerHasLedger(player);
            bool recGone = LoanService.For(player) == null;
            if ((int)player.Gold > 0) await PlayerCmd.LoseGold((int)player.Gold, player, GoldLossType.Spent);
            bool canReborrow = LoanService.CanLoanCover(mkEntry(), player);
            bool tF = repaid && relicGone && recGone && canReborrow;
            W($"  assert repay-reborrow: repaid={repaid} relicGone={relicGone} recGone={recGone} canReborrow={canReborrow} -> {tF}");
            all &= tF;

            // G) Shop UI: take a loan at a REAL shop (repay button + green tags apply here).
            Step("shop repay button");
            bool tG = false;
            if (Engine.GetMainLoop() is SceneTree stree)
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
                await Task.Delay(3000);
                DebtLoanConfig.MaxLoan = 300;
                if ((int)player.Gold < 150) await PlayerCmd.GainGold(150 - (int)player.Gold, player, false);
                await LoanService.GrantLoanDirect(player, 120);
                await Task.Delay(500);
                var shopNode = FindNode<NMerchantInventory>(stree.Root);
                var repayBtn = FindNode<NMerchantRepayButton>(stree.Root);
                try { shopNode?.Open(); } catch { }
                await Task.Delay(500);
                tG = shopNode != null && repayBtn != null;
                W($"  assert repay-button: shopNode={(shopNode != null)} attached={(repayBtn != null)} -> {tG}");
                await Shot("3_shop");
            }
            all &= tG;

            // H) Amortization: each Debt-card payment splits 20% principal / 80% interest, so the owed
            //    amount drops and the total-paid rises. 5 drains of 10 → owed 100-5×2=90, paid 50.
            Step("amortization (20% to principal)");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(150);
            DebtLoanConfig.PrincipalRepayShare = 0.2;
            await LoanService.GrantLoanDirect(player, 100);
            await Task.Delay(150);
            for (int i = 0; i < 5; i++) await LoanService.AccrueInterest(player, 10);   // 5 × (2 principal, 8 interest)
            await Task.Delay(200);
            var rh = LoanService.For(player);
            var hRelic = LoanService.LedgerRelicOf(player);
            // owed 90, paid 50; relic KEPT + still active (no default mechanic anymore); hover reflects it.
            bool tH = rh != null && rh.Active && rh.Borrowed == 100 && rh.Principal == 90 && rh.TotalPaid == 50
                      && hRelic != null && hRelic.Principal == 90 && hRelic.TotalPaid == 50;
            string hover = "";
            try { hover = hRelic?.DynamicDescription.GetFormattedText() ?? ""; } catch { }
            W($"  assert amortize: borrowed={rh?.Borrowed}(100) owed={rh?.Principal}(90) paid={rh?.TotalPaid}(50) relicOwed={hRelic?.Principal} -> {tH}");
            W($"  amortized hover: {hover}");
            all &= tH;

            // I) Combat-start injection: a fresh loan (count 1) → entering a Monster room fires
            //    the first player turn (AfterPlayerTurnStart), which must put the Debt card(s) into the piles.
            Step("combat-start injection");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(150);
            await LoanService.GrantLoanDirect(player, 60);          // count 1 at rooms 0
            await Task.Delay(150);
            bool tI = false;
            if (Engine.GetMainLoop() is SceneTree)
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                await Task.Delay(4000);                            // combat setup + first turn start (injection)
                int debtInCombat = 0;
                foreach (var pt in new[] { PileType.Draw, PileType.Hand, PileType.Discard })
                {
                    var pile = pt.GetPile(player);
                    if (pile != null) debtInCombat += pile.Cards.Count(c => c is DebtCurseCard);
                }
                bool inCombat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false;
                tI = inCombat && debtInCombat >= 1;
                W($"  assert combat inject: inCombat={inCombat} debtCardsInCombat={debtInCombat}(>=1) -> {tI}");
                await Shot("4_combat");
            }
            all &= tI;

            // J) Min-loan floor: a 1-gold shortfall still borrows at least MinLoan (100), not 1.
            Step("min-loan floor");
            DebtLoanConfig.MinLoan = 100;
            DebtLoanConfig.MaxLoan = 9999;
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            var jEntry = mkEntry();
            int jCost = jEntry.Cost;
            int jGold = (int)player.Gold, jTarget = Math.Max(0, jCost - 1);   // shortfall = 1
            if (jGold > jTarget) await PlayerCmd.LoseGold(jGold - jTarget, player, GoldLossType.Spent);
            else if (jGold < jTarget) await PlayerCmd.GainGold(jTarget - jGold, player, false);
            await Task.Delay(120);
            int jAmt = LoanService.LoanAmountFor(jEntry, player);
            bool tJ = jAmt == 100;
            W($"  assert min-loan: cost={jCost} gold={(int)player.Gold} shortfall={jCost-(int)player.Gold} -> amount={jAmt}(100) -> {tJ}");
            all &= tJ;

            // K) Over-soft-cap: borrowing past MaxLoan up to HardCap is allowed; over the soft cap the Dunning
            //    card is injected UPGRADED (빚 독촉+), but the card COUNT stays tier-by-rooms (over-cap no longer
            //    adds a card). We verify the cap math + the over-cap flag here; the '+' upgrade is exercised in
            //    the combat-inject path / real play.
            Step("over-cap borrowing (soft 300 / hard 400)");
            DebtLoanConfig.MaxLoan = 300; DebtLoanConfig.OverCapAllowance = 100;   // soft 300 / hard 400
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            await LoanService.GrantLoanDirect(player, 200);          // borrowed 200 (under soft cap)
            await Task.Delay(120);
            int roomUnder  = LoanService.RemainingRoom(player);      // 400-200 = 200
            int cardsUnder = LoanService.DebtCardCountFor(player);   // rooms 0 → tier 1
            await LoanService.GrantLoanDirect(player, 120);          // → borrowed 320 (over soft, under hard)
            await Task.Delay(120);
            var recK = LoanService.For(player)!;
            int borrowedK = recK.Borrowed;
            bool overCapK = recK.Borrowed > DebtLoanConfig.MaxLoan;  // 320 > 300 → true
            int roomAfter = LoanService.RemainingRoom(player);       // 400-320 = 80
            bool tK = roomUnder == 200 && cardsUnder == 1 && borrowedK == 320 && overCapK && roomAfter == 80;
            W($"  assert over-cap: room@200={roomUnder}(200) tier={cardsUnder}(1) borrowed={borrowedK}(320) overCap={overCapK}(true) roomAfter={roomAfter}(80) -> {tK}");
            all &= tK;
            // Tooltip: the relic's hover tips include the Debt-card preview (must not throw).
            try { int ht = 0; foreach (var _ in LoanService.LedgerRelicOf(player)!.HoverTips) ht++;
                  W($"  ledger hovertips: {ht} (incl. Debt card preview)"); }
            catch (Exception e) { W("  hovertips failed: " + e.Message); }

            // L) 강제 징수 (Forced Collection) payload = ForceRepayPrincipal writes off principal DIRECTLY (no
            //    interest split), counts toward paid, and settles the loan (Active=false) when principal hits 0.
            //    Mirrors the L0..L3 collection amounts 5/10/30/80 the spiral applies over a fight.
            Step("forced collection → principal writeoff + self-terminate");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            DebtLoanConfig.MaxLoan = 300;
            await LoanService.GrantLoanDirect(player, 125);          // principal 125 = exactly 5+10+30+80
            await Task.Delay(120);
            var recL = LoanService.For(player)!;
            int p0 = recL.Principal;                                  // 125
            LoanService.ForceRepayPrincipal(player, 5);   int p1 = recL.Principal;   // L0 → 120
            LoanService.ForceRepayPrincipal(player, 10);  int p2 = recL.Principal;   // L1 → 110
            LoanService.ForceRepayPrincipal(player, 30);  int p3 = recL.Principal;   // L2 → 80
            LoanService.ForceRepayPrincipal(player, 80);  int p4 = recL.Principal;   // L3 → 0 → settle
            LoanService.ForceRepayPrincipal(player, 80);  int p5 = recL.Principal;   // already settled → no-op (stays 0)
            bool settledL = !recL.Active && recL.TotalPaid == 125;
            bool tL = p0 == 125 && p1 == 120 && p2 == 110 && p3 == 80 && p4 == 0 && p5 == 0 && settledL;
            W($"  assert forced: 125→{p1}(120)→{p2}(110)→{p3}(80)→{p4}(0) paid={recL.TotalPaid}(125) active={recL.Active}(false) -> {tL}");
            all &= tL;

            await Shot("2_final");
            W($"=== solo test done: {(all ? "ALL PASS" : "FAIL")} ===");
            Flush(all);
        }
        catch (Exception e) { W("test exception: " + e); Flush(false); }
    }

    #region selection automation (safety net; this test triggers no prompts but keep it robust)
    private static readonly HashSet<string> _pumpIgnore = new();
    private const int PumpGraceMs = 4000;
    private static IDisposable? _selectorScope;
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

    private static void EnsureSelector()
    {
        try
        {
            if (CardSelectCmd.Selector != null) return;
            _selectorScope = CardSelectCmd.PushSelector(new AutoSelector());
        }
        catch (Exception e) { W("selector push failed: " + e.Message); }
    }

    private sealed class AutoSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var list = options.ToList();
            int n = Math.Min(maxSelect, list.Count);
            if (n < minSelect) n = Math.Min(minSelect, list.Count);
            W($"  [selector] auto-picked {n}/{list.Count}");
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
                if (attempts >= 3) continue;
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
        { W($"  [pump] no AutoSlay handler for {screen.GetType().Name}"); return; }
        var ht = handler.GetType();
        var timeout = ht.GetProperty("Timeout")?.GetValue(handler) as TimeSpan? ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(timeout);
        var task = ht.GetMethod("HandleAsync")?.Invoke(handler, new object[] { rng, cts.Token }) as Task;
        if (task == null) return;
        await task;
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
            if (iface == null) return _screenHandlers = map;
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

    /// <summary>Depth-first search the scene tree for the first node of type T (RelicForge idiom).</summary>
    private static T? FindNode<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
        {
            var r = FindNode<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    private static async Task Shot(string name)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            await Task.Delay(120);
            var img = tree.Root.GetTexture()?.GetImage();
            if (img == null) { W($"shot {name}: null image"); return; }
            string p = Path.Combine(ModDir(), $"selftest.sp.{name}.png");
            var err = img.SavePng(p);
            W($"shot {name}: {(err == Error.Ok ? $"saved {img.GetWidth()}x{img.GetHeight()}" : "err " + err)}");
        }
        catch (Exception e) { W($"shot {name} failed: {e.Message}"); }
    }

    private static void W(string line) { _out.AppendLine(line); Log(line); }
    private static void Log(string s) { try { MainFile.Logger.Info($"[{Tag}] SOLO | {s}"); } catch { } }

    private static void Flush(bool ok)
    {
        if (_done) return;
        _done = true;
        _selectorScope?.Dispose();
        _selectorScope = null;
        _out.Insert(0, (ok ? "RESULT: OK\n" : "RESULT: FAIL\n"));
        try { File.WriteAllText(Path.Combine(ModDir(), "selftest.sp.txt"), _out.ToString()); } catch { }
    }
}
#endif
