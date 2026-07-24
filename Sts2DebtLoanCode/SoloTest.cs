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
using MegaCrit.Sts2.Core.Nodes.Cards;                     // NCard (frame-recolor render check)
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
            bool tA = LoanService.PlayerHasLedger(player) && rec != null && rec.Borrowed == 100 && rec.Principal == 120 // 100 + 20% origination (fresh, 0 rooms)
                      && rec.Active && LoanService.DebtCardCountFor(player) == 1;
            W($"  assert loan: ledger={LoanService.PlayerHasLedger(player)} borrowed={rec?.Borrowed} owed={rec?.Principal} active={rec?.Active} cards@0={LoanService.DebtCardCountFor(player)} -> {tA}");
            all &= tA;

            // A2) Node interest: +5% of borrowed per room carried, up to 8 rooms (+40%); on top of the 20% origination
            //     that's a 60% ceiling. Idempotent per room (no double-charge on re-fire/reload).
            Step("node interest accrual");
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 100);         // owed 120 (20% origination), 0 rooms applied
            await Task.Delay(200);
            var recN = LoanService.For(player)!;
            int owedN0 = recN.Principal;                            // 120
            recN.LoanFloor = player.RunState.TotalFloor - 4;        // simulate carrying the debt 4 rooms
            LoanService.AccrueNodeInterest(player);                 // +100×5%×4 = +20 → 140
            int owedN4 = recN.Principal;
            LoanService.AccrueNodeInterest(player);                 // idempotent (same 4 rooms) → still 140
            int owedN4b = recN.Principal;
            recN.LoanFloor = player.RunState.TotalFloor - 20;       // 20 rooms → capped at 8 (max +40%)
            LoanService.AccrueNodeInterest(player);                 // +100×5%×(8−4)=+20 → 160 (60% total)
            int owedNMax = recN.Principal;
            bool tA2 = owedN0 == 120 && owedN4 == 140 && owedN4b == 140 && owedNMax == 160;
            W($"  assert node-interest: owed0={owedN0}(120) 4rooms={owedN4}(140) idempotent={owedN4b}(140) capped={owedNMax}(160) -> {tA2}");
            all &= tA2;
            // Restore a fresh owed-120 loan (0 rooms) for the sections below, which back-date LoanFloor and expect 120.
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 100);
            await Task.Delay(200);

            // B) Debt-curse TIER schedule: accelerating gaps 0/10/17/22 (each tier unlocks a new curse),
            //    capped at 4. Rooms are COMPUTED as TotalFloor − LoanFloor, so we simulate by back-dating.
            //    Check the exact boundaries incl. just-below (16→2, 21→3) to prove the thresholds.
            Step("debt-curse tier schedule (10/17/22)");
            int baseFloor = player.RunState.TotalFloor;
            var recB = LoanService.For(player)!;
            recB.LoanFloor = baseFloor;        int cnt0  = LoanService.DebtCardCountFor(player);   // rooms 0  → 1
            recB.LoanFloor = baseFloor - 12;   int cnt12 = LoanService.DebtCardCountFor(player);   // rooms 12 → 1 (below 13)
            recB.LoanFloor = baseFloor - 13;   int cnt13 = LoanService.DebtCardCountFor(player);   // rooms 13 → 2
            recB.LoanFloor = baseFloor - 16;   int cnt16 = LoanService.DebtCardCountFor(player);   // rooms 16 → 2 (below 17)
            recB.LoanFloor = baseFloor - 17;   int cnt17 = LoanService.DebtCardCountFor(player);   // rooms 17 → 3
            recB.LoanFloor = baseFloor - 21;   int cnt21 = LoanService.DebtCardCountFor(player);   // rooms 21 → 3 (below 22)
            recB.LoanFloor = baseFloor - 22;   int cnt22 = LoanService.DebtCardCountFor(player);   // rooms 22 → 4
            recB.LoanFloor = baseFloor - 30;   int cnt30 = LoanService.DebtCardCountFor(player);   // rooms 30 → 4 (cap)
            LoanService.SyncToRelic(player);         // persist LoanFloor=baseFloor-30 onto the relic for the C round-trip
            bool tB = cnt0 == 1 && cnt12 == 1 && cnt13 == 2 && cnt16 == 2 && cnt17 == 3 && cnt21 == 3 && cnt22 == 4 && cnt30 == 4;
            W($"  assert tier: r0={cnt0}(1) r12={cnt12}(1) r13={cnt13}(2) r16={cnt16}(2) r17={cnt17}(3) r21={cnt21}(3) r22={cnt22}(4) r30={cnt30}(4) -> {tB}");
            all &= tB;
            // Badge countdown = rooms until the NEXT escalation (0 at the top tier → counter hidden). Schedule 0/13/17/22.
            int b0 = DebtLoanConfig.RoomsUntilNextTier(0), b13 = DebtLoanConfig.RoomsUntilNextTier(13),
                b17 = DebtLoanConfig.RoomsUntilNextTier(17), b22 = DebtLoanConfig.RoomsUntilNextTier(22);
            bool tBadge = b0 == 13 && b13 == 4 && b17 == 5 && b22 == 0;
            W($"  assert badge: r0={b0}(13) r13={b13}(4) r17={b17}(5) r22={b22}(0/max) -> {tBadge}");
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
            bool tC = rrelic != null && rrelic.Borrowed == 100 && rrelic.Principal == 120 && rrelic.LoanFloor == baseFloor - 30 && rrelic.Active;
            LoanService.RestoreFromRelic(rp);
            var rrec = LoanService.For(rp);
            // rooms-since-loan (30 → tier 4) is re-derived from the restored LoanFloor, not stored.
            bool tC2 = rrec != null && rrec.Borrowed == 100 && rrec.Principal == 120 && rrec.LoanFloor == baseFloor - 30 && rrec.Active
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
                LoanService.ResetFor(player);                       // fresh loan → the loan-time 독촉장 grant fires the bark
                await LoanService.GrantLoanDirect(player, 120);
                await Task.Delay(2300);                             // deferred (0.6s) bark; wait for the grant-card display to
                                                                    // fade so the speech bubble (3s) is unobstructed
                await Shot("8_merchant_bark");                      // merchant hint naming the NEXT card (정산) when handing 독촉장
                var shopNode = FindNode<NMerchantInventory>(stree.Root);
                var repayBtn = FindNode<NMerchantRepayButton>(stree.Root);
                try { shopNode?.Open(); } catch { }
                await Task.Delay(500);
                tG = shopNode != null && repayBtn != null;
                W($"  assert repay-button: shopNode={(shopNode != null)} attached={(repayBtn != null)} -> {tG}");
                await Shot("3_shop");
            }
            all &= tG;

            // H) Amortization: borrow 100 → owe 120 (100 + 20% origination, fresh/0 rooms). Each Payment goes 100%
            //    to the owed, so it drops by the full amount paid. 5 drains of 10 → 120 − 50 = 70, paid 50.
            Step("amortization (100% to owed, on 120 owed)");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(150);
            await LoanService.GrantLoanDirect(player, 100);   // borrowed 100 → owed 120 (20% origination)
            await Task.Delay(150);
            for (int i = 0; i < 5; i++) await LoanService.AccrueInterest(player, 10, principalShareOverride: 1.0);   // 5 × 10 principal
            await Task.Delay(200);
            var rh = LoanService.For(player);
            var hRelic = LoanService.LedgerRelicOf(player);
            // owed 70, paid 50; relic KEPT + still active (only a shop repay removes it); hover reflects it.
            bool tH = rh != null && rh.Active && rh.Borrowed == 100 && rh.Principal == 70 && rh.TotalPaid == 50
                      && hRelic != null && hRelic.Principal == 70 && hRelic.TotalPaid == 50;
            string hover = "";
            try { hover = hRelic?.DynamicDescription.GetFormattedText() ?? ""; } catch { }
            W($"  assert amortize: borrowed={rh?.Borrowed}(100) owed={rh?.Principal}(100) paid={rh?.TotalPaid}(50) relicOwed={hRelic?.Principal} -> {tH}");
            W($"  amortized hover: {hover}");
            all &= tH;

            // I) Combat-start injection: tier 1 injects NOTHING (on-time grace), so we back-date the loan to tier 2
            //    (rooms 13) and check the 연체 (Delinquency) curse gets SHUFFLED into the draw pile at BeforeHandDraw
            //    (before the opening deal), drawn by the normal logic. We assert it's IN COMBAT; opening-hand is logged.
            Step("combat-start injection (tier 2 → 연체)");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(150);
            await LoanService.GrantLoanDirect(player, 60);
            var recI = LoanService.For(player); if (recI != null) recI.LoanFloor = player.RunState.TotalFloor - 13;   // tier 2 → 연체 injected
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
                    if (pile != null) debtInCombat += pile.Cards.Count(c => c is DelinquencyCard);
                }
                int debtInHand = PileType.Hand.GetPile(player)?.Cards.Count(c => c is DelinquencyCard) ?? 0;
                bool inCombat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false;
                tI = inCombat && debtInCombat >= 1;
                W($"  assert combat inject (tier2): inCombat={inCombat} delinquencyInCombat={debtInCombat}(>=1) inOpeningHand={debtInHand}(random, may be 0) -> {tI}");
                await Shot("4_combat");
            }
            all &= tI;

            // I2) Tier 4 injects ONLY 신용 불량 (Bad Credit) — NOT the cumulative 납부/연체/차압 (which would flood the
            //     hand). Bad Credit drives the 강제 징수 spiral instead. Assert none of the tier-1..3 curses appear.
            Step("tier4 = bad-credit only");
            bool tI2 = true;
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            DebtLoanConfig.MaxLoan = 9999;
            await LoanService.GrantLoanDirect(player, 200);
            await LoanService.DebugSetTier(player, 25);            // rooms-since-loan 25 → tier 4
            await Task.Delay(120);
            if (Engine.GetMainLoop() is SceneTree)
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                await Task.Delay(4000);                            // combat setup → tier-4 injection runs
                int cumulativeCurses = 0;
                foreach (var pt in new[] { PileType.Draw, PileType.Hand, PileType.Discard })
                {
                    var pile = pt.GetPile(player);
                    if (pile != null) cumulativeCurses += pile.Cards.Count(c => c is DelinquencyCard or SeizureCard);
                }
                tI2 = cumulativeCurses == 0;
                W($"  assert tier4=신용불량 only: 연체/차압 injected={cumulativeCurses}(=0) -> {tI2}");
            }
            all &= tI2;

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

            // K) Over-soft-cap: borrowing past MaxLoan up to HardCap is allowed; the card COUNT stays tier-by-
            //    rooms (over-cap no longer upgrades the injected Dunning — 빚 독촉+ is now exclusively the 독촉장+
            //    power's card). We verify the cap math + the over-cap flag here.
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
            await LoanService.GrantLoanDirect(player, 125);          // (grants a loan; 20% origination → owed 150)
            await Task.Delay(120);
            var recL = LoanService.For(player)!;
            recL.Principal = 125;                                     // pin to a clean value = exactly 5+10+30+80 for this focused test
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

            // M) 독촉장 (Dunning Letter): granted once when the debtor shops somewhere OTHER than the loan shop
            //    (RoomEntered watch), and removed from the deck when the loan is repaid. Registration + grant +
            //    vanish, all outside combat (deck mutations).
            Step("dunning letter grant-at-loan + repay-vanish");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveDunningLetter(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            bool dlModel = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(typeof(DunningLetterCard))) != null;
            int deckBefore = player.Deck.Cards.Count(c => c is DunningLetterCard);   // 0
            await LoanService.GrantLoanDirect(player, 150);          // now hands 정기 납부 at loan time
            await Task.Delay(400);                                    // let the fire-and-forget deck grant land
            var recMile = LoanService.For(player)!;
            int afterLoan = player.Deck.Cards.Count(c => c is DunningLetterCard);   // expect 1 (granted with the loan)
            bool granted = afterLoan == 1 && recMile.DunningLetterGranted;
            if ((int)player.Gold < recMile.Principal) await PlayerCmd.GainGold(recMile.Principal - (int)player.Gold, player, false);
            await LoanService.Repay(player);                          // repay → card evaporates with the debt
            await Task.Delay(200);
            int afterRepay = player.Deck.Cards.Count(c => c is DunningLetterCard);
            bool tM = dlModel && deckBefore == 0 && granted && afterRepay == 0;
            W($"  assert dunning-letter (grant-at-loan): model={dlModel} before={deckBefore} afterLoan={afterLoan}(1) flag={recMile.DunningLetterGranted} afterRepay={afterRepay}(0) -> {tM}");
            all &= tM;

            // N) Frame recolor: render an NCard for the 독촉장 and screenshot so the custom slate-lavender frame
            //    is visible (portrait may be blank until the pck ships the art — we're checking the FRAME here).
            Step("dunning letter frame render");
            try
            {
                var dlCard = player.RunState.CreateCard<DunningLetterCard>(player);
                // Probe: does the loc resolve for a fresh card (vs the "If you can read this" placeholder)?
                // And how many hover tips does it report (tooltips)? Same for 빚 독촉.
                try
                {
                    var dc = player.RunState.CreateCard<DebtCurseCard>(player);
                    // GetDescriptionForPile = what the card FACE renders (auto-prepends [gold]휘발성[/gold] etc).
                    string dlFace = dlCard.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    string dcFace = dc.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    W($"  [loc] 독촉장 FACE='{dlFace}' tips={dlCard.HoverTips.Count()}");
                    W($"  [loc] 빚독촉 FACE='{dcFace}' tips={dc.HoverTips.Count()}");
                    // Upgrade check: does 빚 독촉 → 빚 독촉+ (title '+' auto-appended) and cost 1 → 0?
                    var dcU = player.RunState.CreateCard<DebtCurseCard>(player);
                    dcU.UpgradeInternal(); dcU.FinalizeUpgradeInternal();
                    W($"  [upgrade] 빚독촉+ title='{dcU.Title}' upgraded={dcU.IsUpgraded}");
                    // Upgraded 독촉장+ face should now reference 빚 독촉+ ({card} arg).
                    var dlU = player.RunState.CreateCard<DunningLetterCard>(player);
                    dlU.UpgradeInternal(); dlU.FinalizeUpgradeInternal();
                    string dlUFace = dlU.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    W($"  [upgrade] 독촉장+ title='{dlU.Title}' FACE='{dlUFace}'");
                    // New payment-set cards: registration + loc resolve.
                    foreach (var t in new[] { typeof(WagesCard), typeof(JobPlacementCard), typeof(PaymentBenefitCard),
                                              typeof(RefundCard), typeof(DiligentPaymentCard), typeof(SettlementCard),
                                              typeof(InvoiceCard), typeof(BloodPaymentCard) })
                    {
                        var m = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(t));
                        if (m == null) { W($"  [newcard] {t.Name}: NOT REGISTERED"); continue; }
                        var c = player.RunState.CreateCard(m, player);
                        W($"  [newcard] {t.Name}: '{c.Title}' | {c.GetDescriptionForPile(PileType.Hand).Replace("\n", " / ")}");
                    }
                }
                catch (Exception e2) { W("  loc probe failed: " + e2.Message); }
                var nCard = NCard.Create(dlCard);
                if (Engine.GetMainLoop() is SceneTree t2 && nCard != null)
                {
                    t2.Root.AddChild(nCard);
                    nCard.Position = new Vector2(720, 200);
                    nCard.Scale = new Vector2(1.8f, 1.8f);
                    await Task.Delay(500);
                    await Shot("5_card");
                    W("  rendered 독촉장 NCard (frame-color check)");
                    nCard.QueueFree();
                }
            }
            catch (Exception e) { W("  card render failed: " + e.Message); }

            // O) Ledger tier overlay size: force tier 4 and screenshot the relic tray — the evolving overlay
            //    must FIT the relic icon (ExpandMode.IgnoreSize), not render at the texture's native size (huge).
            Step("ledger tier overlay size");
            try
            {
                await LoanService.DebugSetTier(player, 22);   // rooms-since-loan 22 → tier 4
                if (Engine.GetMainLoop() is SceneTree)
                {
                    await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
                    await Task.Delay(700);
                    await Shot("6_relic_t4");
                    W("  rendered relic at tier 4 (overlay size check)");
                }
            }
            catch (Exception e) { W("  overlay check failed: " + e.Message); }

            // P) NEW payment-set mechanics in a LIVE combat: 납부(Payment) trigger + sequence, 정산/청구서 scaling,
            //    취업알선(Job Placement) loan. Enter a fresh Monster room for a real enemy, apply the two payment-
            //    reactive powers, drive 3 payments, then PLAY the scaling/loan cards through the real pipeline
            //    (CardCmd.AutoPlay → OnPlay) and measure the effects (block gained / enemy HP / loan owed / hand).
            Step("payment-set mechanics (납부 trigger·시퀀스·정산/청구서·취업알선)");
            try
            {
                LoanService.ResetFor(player);
                await DebtLoanGrants.RemoveRelic(player);
                await Task.Delay(150);
                DebtLoanConfig.MaxLoan = 9999;
                await LoanService.GrantLoanDirect(player, 200);      // active loan (RecordPayment/AddCombatDebt need one)
                await Task.Delay(150);
                if (Engine.GetMainLoop() is SceneTree)
                {
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);                          // combat + first turn (injector resets the payment counter)
                }
                var pcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                var cstate = player.Creature?.CombatState;
                var enemy = cstate?.HittableEnemies?.FirstOrDefault(e => e != null && e.IsAlive)
                            ?? cstate?.Enemies?.FirstOrDefault(e => e != null && e.IsAlive);

                // Reactive powers: 납부 혜택 → Plating, 환급 → 성실 납부, + the 3 engine-expansion powers:
                // 자본 타격 → 5 dmg random enemy, 명세서 → draw a card, 이자 지원 → refund half the payment, each per payment.
                await PowerCmd.Apply<PaymentBenefitPower>(pcc, player.Creature!, 1, player.Creature, null);
                await PowerCmd.Apply<RefundPower>(pcc, player.Creature!, 1, player.Creature, null);
                await PowerCmd.Apply<CounterclaimPower>(pcc, player.Creature!, 1, player.Creature, null);
                await PowerCmd.Apply<StatementPower>(pcc, player.Creature!, 1, player.Creature, null);
                await PowerCmd.Apply<InterestSupportPower>(pcc, player.Creature!, 1, player.Creature, null);
                await Task.Delay(120);

                await LoanService.ResetPaymentsThisCombat(player);
                int dp0 = PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0;
                int ccHp0 = enemy?.CurrentHp ?? -1;                                              // 자본 타격 target HP before
                int isGold0 = (int)player.Gold;                                                  // 이자 지원 gold before
                for (int i = 0; i < 3; i++) await LoanService.RecordPayment(player, pcc, 10);   // 납부 시퀀스 ×3 (10 each)
                await Task.Delay(200);

                int pays = LoanService.PaymentsThisCombat(player);                               // 3 = 납부 실적 resource value
                await Shot("6b_tally");   // custom HUD tally counter should now read 3 near the energy orb
                var plating = player.Creature!.GetPower<MegaCrit.Sts2.Core.Models.Powers.PlatingPower>();
                int platingAmt = plating != null ? (int)plating.Amount : 0;                     // 3 × 3 = 9 (if it stacks)
                int dpGain = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0) - dp0;
                bool tP1 = pays == 3 && platingAmt >= 3 && dpGain >= 3;   // 납부 실적 counter + both reactive powers fired 3×
                W($"  assert payment-trigger: 납부실적={pays}(3) plating={platingAmt}(>=3, exp 9) diligentCardsAdded={dpGain}(>=3) -> {tP1}");

                // Engine-expansion powers fired 3× too: 자본 타격 dealt damage, 이자 지원 refunded half, 명세서 applied.
                int ccDrop = (ccHp0 >= 0 && enemy != null) ? ccHp0 - enemy.CurrentHp : -1;       // ~15 (3×5) if unblocked
                int subsidyGain = (int)player.Gold - isGold0;                                    // +15 (3 × 10/2)
                bool stmt = player.Creature!.GetPower<StatementPower>() != null;
                bool ccOK = enemy == null || ccDrop >= 3;   // some damage landed (allow enemy block)
                bool tP1b = ccOK && subsidyGain >= 15 && stmt;
                W($"  assert engine-expansion: moneyAttackDmg={ccDrop} interestSubsidyGold={subsidyGain}(>=15) statementApplied={stmt} -> {tP1b}");
                all &= tP1b;

                // ISOLATE the remaining sub-tests from the reactive powers just exercised. 명세서 (StatementPower)
                // draws a card on every payment — left active it keeps the hand full, starving 취업알선's 품삯 of a
                // hand slot (tP4). 이자 지원 (InterestSupportPower) refunds half of any payment — left active it
                // gives back 10 of the 빚 독촉 20-gold play cost below, masking the raw deduction (tP5). Both are
                // correct in real play; the sub-tests just need a clean slate. Remove all reactive powers + empty the hand.
                await PowerCmd.Remove<PaymentBenefitPower>(player.Creature!);
                await PowerCmd.Remove<RefundPower>(player.Creature!);
                await PowerCmd.Remove<CounterclaimPower>(player.Creature!);
                await PowerCmd.Remove<StatementPower>(player.Creature!);
                await PowerCmd.Remove<InterestSupportPower>(player.Creature!);
                var handClear = PileType.Hand.GetPile(player)?.Cards?.ToList();
                if (handClear != null && handClear.Count > 0) await CardPileCmd.RemoveFromCombat(handClear, skipVisuals: true);
                await Task.Delay(120);

                // Put 청구서/가압류/자본타격/이자지원 into the (now-empty) HAND so the screenshot shows the custom cost
                // badges (X / 2 / 2 / 1) AND the cards' REAL titles — a hand card renders its localized title
                // correctly, unlike a standalone NCard.Create (which mangles it to a headless-render artifact).
                try
                {
                    var handOld = PileType.Hand.GetPile(player)?.Cards?.ToList();
                    if (handOld != null && handOld.Count > 0) await CardPileCmd.RemoveFromCombat(handOld, skipVisuals: false);
                    await Task.Delay(300);
                    var badgeCards = new List<CardModel>
                    {
                        cstate!.CreateCard<LoanStrikeCard>(player), cstate!.CreateCard<MortgageCard>(player),
                        cstate!.CreateCard<GarnishmentCard>(player), cstate!.CreateCard<InvoiceCard>(player),
                    };
                    await CardPileCmd.AddGeneratedCardsToCombat(badgeCards, PileType.Hand, player, CardPilePosition.Top);
                    await Task.Delay(500);
                    var handNames = string.Join(" | ", PileType.Hand.GetPile(player)?.Cards?.Select(c => c.Title) ?? Enumerable.Empty<string>());
                    W($"  cards in hand (real titles): [{handNames}]");   // 가압류 등 실제 표시명 확인
                    await Shot("6c_badge");   // 손패: 청구서=X / 가압류=2(AoE) / 자본타격=2 / 이자지원=1, 실제 이름 표시
                }
                catch (Exception e) { W("  badge render failed: " + e.Message); }

                // 정산 (Settlement): block = 납부 실적 × 4, THEN it CONSUMES the whole tally (stack → 0).
                int blk0 = player.Creature.Block;
                var settle = cstate!.CreateCard<SettlementCard>(player);
                bool settlePlayed = true;
                try { await CardCmd.AutoPlay(pcc, settle, null); } catch (Exception e) { settlePlayed = false; W("  settlement play failed: " + e.Message); }
                await Task.Delay(150);
                int blkGain = player.Creature.Block - blk0;
                int stackAfterSettle = LoanService.PaymentsThisCombat(player);                    // consumed → 0
                int expSettle = (pays + 1) * 4;   // base 4 + 4 per 납부 실적
                bool tP2 = settlePlayed && blkGain == expSettle && stackAfterSettle == 0;
                W($"  assert settlement scale+consume: blockGain={blkGain} (exp ({pays}+1)×4={expSettle}) tallyAfter={stackAfterSettle}(=0) -> {tP2}");

                // 청구서 (Invoice): settlement just spent the tally, so REBUILD it (pay ×3), then Invoice deals
                // damage × 납부 실적 and CONSUMES it too (stack → 0).
                bool tP3 = true;
                if (enemy != null)
                {
                    for (int i = 0; i < 3; i++) await LoanService.RecordPayment(player, pcc, 10);   // rebuild tally to 3
                    await Task.Delay(120);
                    int paysInv = LoanService.PaymentsThisCombat(player);                           // 3
                    int ehp0 = enemy.CurrentHp, eblk = enemy.Block;
                    var inv = cstate!.CreateCard<InvoiceCard>(player);
                    try { await CardCmd.AutoPlay(pcc, inv, enemy); } catch (Exception e) { W("  invoice play failed: " + e.Message); }
                    await Task.Delay(150);
                    int dmg = ehp0 - enemy.CurrentHp;
                    int stackAfterInv = LoanService.PaymentsThisCombat(player);                     // consumed → 0
                    int expInv = (paysInv + 1) * 4;   // base 1 hit + 1 per 납부 실적, ×4 damage
                    bool dmgOk = eblk == 0 ? dmg == expInv : dmg >= 1;
                    tP3 = dmgOk && stackAfterInv == 0;
                    W($"  assert invoice scale+consume: enemyHp {ehp0}->{enemy.CurrentHp} dmg={dmg} (exp ({paysInv}+1)×4={expInv}, block={eblk}) tallyAfter={stackAfterInv}(=0) -> {tP3}");
                }
                else W("  invoice: no live enemy to target — skipped");

                // 취업알선 (Job Placement): play → add Fee(50) onto OWED (no gold to player) + apply JobPlacementPower
                // + hand ONE 품삯(Wages) IMMEDIATELY; a turn-start then slips ANOTHER 품삯 into hand.
                var recP = LoanService.For(player)!;
                int owed0 = recP.Principal;
                int jobGold0 = (int)player.Gold;                     // must NOT rise (no gold handed to the player)
                int wagesPre = PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0;
                var job = cstate!.CreateCard<JobPlacementCard>(player);
                try { await CardCmd.AutoPlay(pcc, job, null); } catch (Exception e) { W("  job-placement play failed: " + e.Message); }
                await Task.Delay(150);
                int owedGain = recP.Principal - owed0;
                int jobGoldGain = (int)player.Gold - jobGold0;       // expect 0 (the fee is added to debt, not paid out)
                int wagesImmediate = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0) - wagesPre;   // expect 1 on play
                var jobPow = player.Creature.GetPower<JobPlacementPower>();
                int w0 = PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0;
                if (jobPow != null) await jobPow.AfterPlayerTurnStart(pcc, player);
                await Task.Delay(150);
                int wagesTurn = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0) - w0;   // expect 1 per turn
                bool tP4 = owedGain == 50 && jobGoldGain == 0 && jobPow != null && wagesImmediate >= 1 && wagesTurn >= 1;
                W($"  assert job-placement: owedGain={owedGain}(=50) goldGain={jobGoldGain}(=0) power={(jobPow != null)} wagesOnPlay={wagesImmediate}(>=1) wagesPerTurn={wagesTurn}(>=1) -> {tP4}");

                // tP5) EMPIRICAL 골드 차감: play a real 빚 독촉 (Dunning) through the pipeline and assert the player's
                //      ACTUAL held gold drops by the 20-gold play cost (bug report: "납부했을 때 실제 보유 골드가 안 줄어듦").
                //      The earlier 납부 시퀀스 called RecordPayment directly, which only amortizes — it does NOT touch
                //      gold. Only the card's OnPlay → PlayerCmd.LoseGold deducts, so THIS is the true-path check.
                bool tP5 = true;
                {
                    int target = 100;
                    if ((int)player.Gold > target) await PlayerCmd.LoseGold((int)player.Gold - target, player, GoldLossType.Spent);
                    else if ((int)player.Gold < target) await PlayerCmd.GainGold(target - (int)player.Gold, player, false);
                    await Task.Delay(100);
                    int goldBefore = (int)player.Gold;                 // 100
                    var dunning = cstate!.CreateCard<DebtCurseCard>(player);
                    try { await CardCmd.AutoPlay(pcc, dunning, null); } catch (Exception e) { tP5 = false; W("  dunning play failed: " + e.Message); }
                    await Task.Delay(150);
                    int goldDrop = goldBefore - (int)player.Gold;
                    tP5 &= goldDrop == 20;                             // PlayCost = 20
                    W($"  assert gold-deduction: gold {goldBefore}->{(int)player.Gold} drop={goldDrop} (exp 20) -> {tP5}");
                }

                await Shot("7_payment_combat");

                // tP6) POWER-CARD 영수증 COST: 자본 타격 costs 2, 이자 지원 costs 1. Gate on receipts (CanPlay false
                //      when short) + spend the cost on play.
                {
                    await LoanService.ConsumePaymentStack(player);                                  // tally → 0
                    var ccCard = cstate!.CreateCard<CounterclaimCard>(player);
                    bool gate0 = !ccCard.CanPlay();                                                 // 0 < 2 → not playable
                    await LoanService.GrantLoanDirect(player, 200);
                    for (int i = 0; i < 3; i++) await LoanService.RecordPayment(player, pcc, 5);    // tally → 3
                    await Task.Delay(120);
                    int t0 = LoanService.PaymentsThisCombat(player);                                // 3
                    bool gate3 = ccCard.CanPlay();                                                  // 3 >= 2 → playable
                    try { await CardCmd.AutoPlay(pcc, ccCard, null); } catch (Exception e) { W("  자본타격 play failed: " + e.Message); }
                    await Task.Delay(120);
                    int tAfterCc = LoanService.PaymentsThisCombat(player);                          // 3 - 2 = 1
                    bool ccPow = player.Creature!.GetPower<CounterclaimPower>() != null;
                    var isCard = cstate!.CreateCard<InterestSupportCard>(player);
                    bool isGate = isCard.CanPlay();                                                 // 1 >= 1 → playable
                    try { await CardCmd.AutoPlay(pcc, isCard, null); } catch (Exception e) { W("  이자지원 play failed: " + e.Message); }
                    await Task.Delay(120);
                    int tAfterIs = LoanService.PaymentsThisCombat(player);                          // 1 - 1 = 0
                    bool isPow = player.Creature!.GetPower<InterestSupportPower>() != null;
                    bool tP6 = gate0 && gate3 && ccPow && tAfterCc == t0 - 2 && isGate && isPow && tAfterIs == 0;
                    W($"  assert power-card 영수증 cost: gate@0={gate0}(unplayable) gate@3={gate3} 자본타격→tally {tAfterCc}(=1,cost2) 이자지원→tally {tAfterIs}(=0,cost1) powers={ccPow}&{isPow} -> {tP6}");
                    all &= tP6;
                }

                // 2-digit check: drive the tally up to 12 and screenshot the HUD counter so we can see a two-digit
                // value render (asserts above are all done, so extra payments here are harmless).
                await LoanService.GrantLoanDirect(player, 200);
                for (int i = 0; i < 12; i++) await LoanService.RecordPayment(player, pcc, 5);
                await Task.Delay(250);
                W($"  2-digit tally check: 납부실적={LoanService.PaymentsThisCombat(player)} (see 6d_twodigit)");
                await Shot("6d_twodigit");

                // tP7) COMBAT MILESTONE: the 6 non-power cards are earned one per 10 payments at combat win — 정산
                //      always first (fixed head), then the other 5 in per-run SHUFFLED order (CombatSequence). Assert
                //      the COUNT crossed at each milestone (order is now seed-dependent, so we don't name types past 정산).
                bool tP7;
                {
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 100);
                    var mileRec = LoanService.For(player)!;
                    mileRec.LifetimePayments = 5; mileRec.CombatCardsGranted = 0;
                    LoanService.TryGrantCombatCards(player);
                    bool m5 = mileRec.CombatCardsGranted == 0;    // 5 < 10 → nothing yet
                    mileRec.LifetimePayments = 10;
                    LoanService.TryGrantCombatCards(player);
                    bool m10 = mileRec.CombatCardsGranted == 1;   // 10 → 정산 (fixed head)
                    // first granted card must be 정산 (the guaranteed early spender), regardless of the shuffle —
                    // GrantCard adds to the master Deck pile, so that's where the granted 정산 lands.
                    bool headIsSettlement = PileType.Deck.GetPile(player)?.Cards?.Any(c => c is SettlementCard) ?? false;
                    mileRec.LifetimePayments = 25;
                    LoanService.TryGrantCombatCards(player);
                    bool m25 = mileRec.CombatCardsGranted == 2;   // 25 → 2 milestones crossed
                    mileRec.LifetimePayments = 40;
                    LoanService.TryGrantCombatCards(player);
                    bool m40 = mileRec.CombatCardsGranted == 4;   // 40 → 4 crossed
                    mileRec.LifetimePayments = 60;
                    LoanService.TryGrantCombatCards(player);
                    bool m60 = mileRec.CombatCardsGranted == 6;   // 60 → all 6 (pool exhausted, no more)
                    tP7 = m5 && m10 && m25 && m40 && m60 && headIsSettlement;
                    W($"  assert combat-milestone: @5={m5}(0) @10={m10}(1) head정산={headIsSettlement} @25={m25}(2) @40={m40}(4) @60={m60}(6) -> {tP7}");
                }

                bool tP = tP1 && tP2 && tP3 && tP4 && tP5 && tP7;
                W($"  == payment-set mechanics: {(tP ? "PASS" : "FAIL")} ==");
                all &= tP;
            }
            catch (Exception e) { W("  payment-set section failed: " + e); all = false; }

            // Q) FRAME HUE SWEEP (item 6): render the 독촉장 NCard at a range of slate-lavender hues so the frame
            //    colour can be compared and the best h picked. Only the frame material's h changes per shot; the
            //    ship value is restored at the end. Not part of the PASS/FAIL — it's a visual artifact for tuning.
            Step("frame hue sweep");
            try
            {
                float ship = NCardFramePatch.TargetH;
                foreach (float h in new[] { 0.66f, 0.68f, 0.70f, 0.72f, 0.74f, 0.76f, 0.78f, 0.80f })
                {
                    NCardFramePatch.TargetH = h;
                    NCardFramePatch.ResetCacheForSweep();
                    var card = player.RunState.CreateCard<DunningLetterCard>(player);
                    var nc = NCard.Create(card);
                    if (Engine.GetMainLoop() is SceneTree ht && nc != null)
                    {
                        ht.Root.AddChild(nc);
                        nc.Position = new Vector2(690, 150);
                        nc.Scale = new Vector2(2.2f, 2.2f);
                        await Task.Delay(400);
                        await Shot($"hue_{(int)Math.Round(h * 100)}");
                        nc.QueueFree();
                        await Task.Delay(120);
                    }
                }
                NCardFramePatch.TargetH = ship;   // restore the ship hue
                NCardFramePatch.ResetCacheForSweep();
                W($"  hue sweep done (restored ship h={ship})");
            }
            catch (Exception e) { W("  hue sweep failed: " + e.Message); }

            // R) NEW 신용 불량 (Bad Credit) system: the 신용 불량 curse auto-applies BadCreditPower + exhausts; the
            //    power spawns an escalating 빚쟁이 (Debtor) each turn (level +1 every 3rd turn); 빚쟁이's gold/HP
            //    scale with level (20+10·L / 2+2·L).
            Step("bad-credit system (신용불량→파워→빚쟁이)");
            try
            {
                LoanService.ResetFor(player);
                await DebtLoanGrants.RemoveRelic(player);
                await Task.Delay(150);
                DebtLoanConfig.MaxLoan = 9999;
                await LoanService.GrantLoanDirect(player, 300);
                await Task.Delay(150);
                if (Engine.GetMainLoop() is SceneTree)
                {
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);
                }
                var cs = player.Creature?.CombatState;
                var bcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();

                // R1) 빚쟁이 gold/HP by escalation level.
                int[] gL = new int[3], hL = new int[3];
                for (int L = 0; L < 3; L++)
                    if (cs!.CreateCard<DebtorCard>(player) is DebtorCard dd) { dd.Level = L; gL[L] = (int)dd.DynamicVars["gold"].BaseValue; hL[L] = (int)dd.DynamicVars["hp"].BaseValue; }
                bool tR1 = gL[0] == 20 && gL[1] == 30 && gL[2] == 40 && hL[0] == 2 && hL[1] == 4 && hL[2] == 6;
                W($"  assert 빚쟁이 scale: gold {gL[0]}/{gL[1]}/{gL[2]}(20/30/40) hp {hL[0]}/{hL[1]}/{hL[2]}(2/4/6) -> {tR1}");

                // R2) 신용 불량 curse auto-applies the power + exhausts when in hand at turn start.
                bool hadPower0 = player.Creature!.GetPower<BadCreditPower>() != null;
                var bc = cs!.CreateCard<BadCreditCard>(player);
                await CardPileCmd.AddGeneratedCardToCombat(bc, PileType.Hand, player, CardPilePosition.Bottom);
                await Task.Delay(120);
                bool bcInHand = PileType.Hand.GetPile(player)?.Cards.Contains(bc) ?? false;
                await ((BadCreditCard)bc).AfterPlayerTurnStart(bcc, player);
                await Task.Delay(150);
                var power = player.Creature.GetPower<BadCreditPower>();
                bool bcGone = !(PileType.Hand.GetPile(player)?.Cards.Contains(bc) ?? true);
                bool tR2 = !hadPower0 && bcInHand && power != null && bcGone;
                W($"  assert 신용불량 auto-apply: inHand={bcInHand} powerApplied={power != null} exhausted={bcGone} -> {tR2}");

                // R3) Power spawns a 빚쟁이 each turn; level ratchets every 3rd (turn3 → L1).
                bool tR3 = false;
                if (power != null)
                {
                    LoanService.For(player)!.CollectionLevel = 0;
                    var seen = new HashSet<DebtorCard>(PileType.Hand.GetPile(player)?.Cards.OfType<DebtorCard>() ?? System.Linq.Enumerable.Empty<DebtorCard>());
                    var lv = new List<int>();
                    for (int t = 0; t < 4; t++)
                    {
                        await power.AfterPlayerTurnStart(bcc, player);
                        await Task.Delay(80);
                        var fresh = (PileType.Hand.GetPile(player)?.Cards.OfType<DebtorCard>() ?? System.Linq.Enumerable.Empty<DebtorCard>()).FirstOrDefault(d => !seen.Contains(d));
                        if (fresh != null) { lv.Add(fresh.Level); seen.Add(fresh); }
                    }
                    tR3 = lv.Count == 4 && lv[0] == 0 && lv[1] == 0 && lv[2] == 1 && lv[3] == 1;   // every 3rd turn → +1
                    W($"  assert 빚쟁이 spawn levels: [{string.Join(",", lv)}] exp [0,0,1,1] -> {tR3}");
                }

                await Shot("8_badcredit");
                bool tR = tR1 && tR2 && tR3;
                W($"  == bad-credit system: {(tR ? "PASS" : "FAIL")} ==");
                all &= tR;
            }
            catch (Exception e) { W("  bad-credit section failed: " + e); all = false; }

            // S) POWER-ICON GALLERY (user request): apply all 5 custom-PowerModel powers at once and screenshot the
            //    player's status bar, so the icons served by PowerIconPatch (res://Sts2DebtLoan/power_icons/*.png)
            //    can be eyeballed in-game. Display-only; not part of PASS/FAIL.
            Step("power-icon gallery");
            try
            {
                if (Engine.GetMainLoop() is SceneTree)
                {
                    if (!(MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false))
                    {
                        await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                        await Task.Delay(4000);
                    }
                    var scc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var cr = player.Creature;
                    if (cr != null)
                    {
                        await PowerCmd.Apply<DunningLetterPower>(scc, cr, 1, cr, null);     // 정기 납부 (Standing Order)
                        await PowerCmd.Apply<PaymentBenefitPower>(scc, cr, 1, cr, null);    // 납부 혜택
                        await PowerCmd.Apply<RefundPower>(scc, cr, 1, cr, null);            // 환급
                        await PowerCmd.Apply<JobPlacementPower>(scc, cr, 1, cr, null);      // 취업알선
                        await PowerCmd.Apply<BadCreditPower>(scc, cr, 1, cr, null);         // 신용 불량
                        await PowerCmd.Apply<CounterclaimPower>(scc, cr, 1, cr, null);      // 자본 타격 (Money Attack)
                        await PowerCmd.Apply<StatementPower>(scc, cr, 1, cr, null);         // 명세서 (Statement)
                        await PowerCmd.Apply<InterestSupportPower>(scc, cr, 1, cr, null);   // 이자 지원 (Interest Support)
                        await Task.Delay(600);
                        int active = 0;
                        if (cr.GetPower<DunningLetterPower>() != null) active++;
                        if (cr.GetPower<PaymentBenefitPower>() != null) active++;
                        if (cr.GetPower<RefundPower>() != null) active++;
                        if (cr.GetPower<JobPlacementPower>() != null) active++;
                        if (cr.GetPower<BadCreditPower>() != null) active++;
                        if (cr.GetPower<CounterclaimPower>() != null) active++;
                        if (cr.GetPower<StatementPower>() != null) active++;
                        if (cr.GetPower<InterestSupportPower>() != null) active++;
                        W($"  power-icon gallery: {active}/8 custom powers active (see 9_power_icons.png)");

                        // ── HOVER TEXT: the character-hover tooltip shows each power's Title + Description
                        // (PowerModel.Description = LocString "powers/<ENTRY>.description"). Verify every custom
                        // power's description resolves to real localized text — not a raw loc key, not empty —
                        // and log it so we can eyeball what the tooltip will read.
                        var hoverPowers = new MegaCrit.Sts2.Core.Models.PowerModel?[]
                        {
                            cr.GetPower<DunningLetterPower>(), cr.GetPower<PaymentBenefitPower>(), cr.GetPower<RefundPower>(),
                            cr.GetPower<JobPlacementPower>(), cr.GetPower<BadCreditPower>(), cr.GetPower<CounterclaimPower>(),
                            cr.GetPower<StatementPower>(), cr.GetPower<InterestSupportPower>(),
                        };
                        int descOk = 0, descTotal = 0;
                        foreach (var pw in hoverPowers)
                        {
                            if (pw == null) continue;
                            descTotal++;
                            string title = "", desc = ""; bool exists = false;
                            try { exists = pw.Description.Exists(); } catch { }
                            try { title = pw.Title.GetFormattedText(); } catch { }
                            try { desc = pw.Description.GetFormattedText(); } catch { }
                            bool ok = exists && !string.IsNullOrWhiteSpace(desc)
                                      && !desc.Contains("_POWER") && !desc.Contains(".description");
                            if (ok) descOk++;
                            W($"    [hover] {pw.GetType().Name}: title='{title}' | desc='{desc}' -> {(ok ? "OK" : "MISSING")}");
                        }
                        bool tHover = descTotal == 8 && descOk == 8;
                        W($"  power-hover descriptions: {descOk}/{descTotal} resolve to real text -> {tHover}");
                        all &= tHover;
                    }
                    await Shot("9_power_icons");
                }
            }
            catch (Exception e) { W("  power-icon gallery failed: " + e.Message); }

            // T) MID-COMBAT PAYOFF SETTLE (user request): paying the loan down to 0 DURING combat must lift the debt
            //    at once — relic removed, record reset (credit restored), and the injected Debt cards swept out of
            //    combat so 강제 징수 stops collecting the instant you're square.
            Step("mid-combat payoff settle");
            try
            {
                LoanService.ResetFor(player);
                await DebtLoanGrants.RemoveRelic(player);
                await Task.Delay(120);
                DebtLoanConfig.MaxLoan = 9999;
                await LoanService.GrantLoanDirect(player, 60);        // owe 72 (60 + 20% origination)
                await Task.Delay(120);
                if (!(MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false))
                {
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);
                }
                var tcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                var tcs = player.Creature?.CombatState;
                if (tcs != null && tcs.CreateCard<DebtorCard>(player) is DebtorCard dcard)   // a 강제 징수 that must get swept
                    await CardPileCmd.AddGeneratedCardToCombat(dcard, PileType.Hand, player, CardPilePosition.Bottom);
                await Task.Delay(120);
                int CountDebtInCombat() { int n = 0; foreach (var pt in new[] { PileType.Hand, PileType.Draw, PileType.Discard }) { var pl = pt.GetPile(player); if (pl != null) n += pl.Cards.Count(c => c is DebtCurseCard or DelinquencyCard or SeizureCard or BadCreditCard or DebtorCard); } return n; }
                bool stRelicBefore = LoanService.LedgerRelicOf(player) != null;
                int stDebtBefore = CountDebtInCombat();
                await LoanService.RecordPayment(player, tcc, 500);    // pay far more than owed → principal hits 0 → settle
                await Task.Delay(250);
                bool stRelicGone = LoanService.LedgerRelicOf(player) == null;
                bool stRecordReset = LoanService.For(player) == null;
                int stDebtAfter = CountDebtInCombat();
                bool tT = stRelicBefore && stDebtBefore >= 1 && stRelicGone && stRecordReset && stDebtAfter == 0;
                W($"  assert mid-combat settle: relicBefore={stRelicBefore} debtBefore={stDebtBefore}(>=1) -> relicGone={stRelicGone} recordReset={stRecordReset} debtAfter={stDebtAfter}(0) -> {tT}");
                all &= tT;

                // T2) Reward gate = tier 4 AND total PAID ≥ 400 (갚은 금액). A near-max loan (borrow 300 → owe 450)
                //     carried to tier 4 and fully paid off (TotalPaid 450 ≥ 400) drops a permanent 신용 회복+ into the
                //     deck — reachable from a big loan alone, no 취업알선 needed.
                LoanService.ResetFor(player);
                await DebtLoanGrants.RemoveRelic(player);
                await Task.Delay(120);
                await LoanService.GrantLoanDirect(player, 350);      // owe 420 (350 + 20% origination)
                await LoanService.DebugSetTier(player, 25);          // rooms-since-loan 25 → tier 4 (keeps owed 420)
                await Task.Delay(120);
                int reward0 = player.Deck.Cards.Count(c => c is CreditRestoredCard);
                int owedT2 = LoanService.For(player)?.Principal ?? 0;   // 420
                await LoanService.RecordPayment(player, tcc, owedT2); // pay exactly owed → TotalPaid 420 (≥400) → settle+reward
                await Task.Delay(200);
                var rewardCards = player.Deck.Cards.OfType<CreditRestoredCard>().ToList();
                int rewardGain = rewardCards.Count - reward0;
                var upCard = rewardCards.FirstOrDefault(c => c.IsUpgraded);
                int rewardPlate = upCard != null && upCard.DynamicVars.TryGetValue("plate", out var pv) ? (int)pv.IntValue : -1;
                bool tT2 = rewardGain == 1 && upCard != null && rewardPlate == 5;   // 신용 회복+ = 5 Plating
                W($"  assert reward (tier4 + paid>=400): owedPaid={owedT2} added={rewardGain}(=1) upgraded={upCard != null} plate={rewardPlate}(=5) -> {tT2}");
                all &= tT2;

                // T2b) NEGATIVE: tier 4 but only paid 150 (< 400) must NOT grant the reward (the 400-paid gate).
                LoanService.ResetFor(player);
                await DebtLoanGrants.RemoveRelic(player);
                await Task.Delay(120);
                await LoanService.GrantLoanDirect(player, 100);      // owe 120
                var smallRec = LoanService.For(player); if (smallRec != null) smallRec.LoanFloor = player.RunState.TotalFloor - 25;   // tier 4 by rooms
                await Task.Delay(120);
                int reward0b = player.Deck.Cards.Count(c => c is CreditRestoredCard);
                int owedT2b = LoanService.For(player)?.Principal ?? 0;   // 150
                await LoanService.RecordPayment(player, tcc, owedT2b); // pay exactly owed → TotalPaid 150 (<500) → no reward
                await Task.Delay(200);
                int rewardGainB = player.Deck.Cards.Count(c => c is CreditRestoredCard) - reward0b;
                bool tT2b = rewardGainB == 0;
                W($"  assert reward gate (tier4 + paid<400 → none): paid={owedT2b} added={rewardGainB}(=0) -> {tT2b}");
                all &= tT2b;
                await Shot("10_settled");
            }
            catch (Exception e) { W("  mid-combat settle failed: " + e); all = false; }

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
