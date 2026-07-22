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
            bool tA = LoanService.PlayerHasLedger(player) && rec != null && rec.Borrowed == 100 && rec.Principal == 150 // 100 + 50% surcharge
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
            bool tC = rrelic != null && rrelic.Borrowed == 100 && rrelic.Principal == 150 && rrelic.LoanFloor == baseFloor - 30 && rrelic.Active;
            LoanService.RestoreFromRelic(rp);
            var rrec = LoanService.For(rp);
            // rooms-since-loan (30 → tier 4) is re-derived from the restored LoanFloor, not stored.
            bool tC2 = rrec != null && rrec.Borrowed == 100 && rrec.Principal == 150 && rrec.LoanFloor == baseFloor - 30 && rrec.Active
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

            // H) Amortization: borrow 100 → owe 150 (100 + 50% surcharge). Each Payment now goes 100% to
            //    principal (interest = the up-front surcharge), so the owed drops by the full amount paid.
            //    5 drains of 10 → 150 − 50 = 100, paid 50.
            Step("amortization (100% to principal, on 150 owed)");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(150);
            await LoanService.GrantLoanDirect(player, 100);   // borrowed 100 → principal 150
            await Task.Delay(150);
            for (int i = 0; i < 5; i++) await LoanService.AccrueInterest(player, 10, principalShareOverride: 1.0);   // 5 × 10 principal
            await Task.Delay(200);
            var rh = LoanService.For(player);
            var hRelic = LoanService.LedgerRelicOf(player);
            // owed 100, paid 50; relic KEPT + still active (only a shop repay removes it); hover reflects it.
            bool tH = rh != null && rh.Active && rh.Borrowed == 100 && rh.Principal == 100 && rh.TotalPaid == 50
                      && hRelic != null && hRelic.Principal == 100 && hRelic.TotalPaid == 50;
            string hover = "";
            try { hover = hRelic?.DynamicDescription.GetFormattedText() ?? ""; } catch { }
            W($"  assert amortize: borrowed={rh?.Borrowed}(100) owed={rh?.Principal}(100) paid={rh?.TotalPaid}(50) relicOwed={hRelic?.Principal} -> {tH}");
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
            await LoanService.GrantLoanDirect(player, 125);          // (grants a loan; surcharge would make it 163)
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
            Step("dunning letter grant + repay-vanish");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveDunningLetter(player);
            await DebtLoanGrants.RemoveRelic(player);
            await Task.Delay(120);
            bool dlModel = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(typeof(DunningLetterCard))) != null;
            await LoanService.GrantLoanDirect(player, 150);
            await Task.Delay(120);
            var recM = LoanService.For(player)!;
            recM.LoanFloor = -50;                                     // pretend we borrowed elsewhere → this shop is a "revisit"
            int deckBefore = player.Deck.Cards.Count(c => c is DunningLetterCard);
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop);  // fires RoomEntered → the grant watcher
            await Task.Delay(500);
            int afterGrant = player.Deck.Cards.Count(c => c is DunningLetterCard);
            bool granted = afterGrant == 1 && recM.DunningLetterGranted;
            if ((int)player.Gold < recM.Principal) await PlayerCmd.GainGold(recM.Principal - (int)player.Gold, player, false);
            await LoanService.Repay(player);                          // repay → card evaporates with the debt
            await Task.Delay(200);
            int afterRepay = player.Deck.Cards.Count(c => c is DunningLetterCard);
            bool tM = dlModel && deckBefore == 0 && granted && afterRepay == 0;
            W($"  assert dunning-letter: model={dlModel} before={deckBefore} afterGrant={afterGrant}(1) flag={recM.DunningLetterGranted} afterRepay={afterRepay}(0) -> {tM}");
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

                // Reactive powers: 납부 혜택 → Plating each payment, 환급 → a 성실 납부 card each payment.
                await PowerCmd.Apply<PaymentBenefitPower>(pcc, player.Creature!, 1, player.Creature, null);
                await PowerCmd.Apply<RefundPower>(pcc, player.Creature!, 1, player.Creature, null);
                await Task.Delay(120);

                LoanService.ResetPaymentsThisCombat(player);
                int dp0 = PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0;
                for (int i = 0; i < 3; i++) await LoanService.RecordPayment(player, pcc, 10);   // 납부 시퀀스 ×3
                await Task.Delay(200);

                int pays = LoanService.PaymentsThisCombat(player);                               // 3
                var plating = player.Creature!.GetPower<MegaCrit.Sts2.Core.Models.Powers.PlatingPower>();
                int platingAmt = plating != null ? (int)plating.Amount : 0;                     // 3 × 3 = 9 (if it stacks)
                int dpGain = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0) - dp0;
                bool tP1 = pays == 3 && platingAmt >= 3 && dpGain >= 3;   // count + both reactive powers fired 3×
                W($"  assert payment-trigger: payments={pays}(3) plating={platingAmt}(>=3, exp 9) diligentCardsAdded={dpGain}(>=3) -> {tP1}");

                // 정산 (Settlement): block gained = payments × 4.
                int blk0 = player.Creature.Block;
                var settle = cstate!.CreateCard<SettlementCard>(player);
                bool settlePlayed = true;
                try { await CardCmd.AutoPlay(pcc, settle, null); } catch (Exception e) { settlePlayed = false; W("  settlement play failed: " + e.Message); }
                await Task.Delay(150);
                int blkGain = player.Creature.Block - blk0;
                bool tP2 = settlePlayed && blkGain == pays * 4;
                W($"  assert settlement scale: blockGain={blkGain} (exp {pays}×4={pays * 4}) -> {tP2}");

                // 청구서 (Invoice): damage to the enemy = payments × 5 (exact when the enemy carries no Block).
                bool tP3 = true;
                if (enemy != null)
                {
                    int ehp0 = enemy.CurrentHp, eblk = enemy.Block;
                    var inv = cstate!.CreateCard<InvoiceCard>(player);
                    try { await CardCmd.AutoPlay(pcc, inv, enemy); } catch (Exception e) { W("  invoice play failed: " + e.Message); }
                    await Task.Delay(150);
                    int dmg = ehp0 - enemy.CurrentHp;
                    tP3 = eblk == 0 ? dmg == pays * 4 : dmg >= 1;   // 청구서 = payments × 4 (was ×5)
                    W($"  assert invoice scale: enemyHp {ehp0}->{enemy.CurrentHp} dmg={dmg} (exp {pays * 4}, enemyBlock={eblk}) -> {tP3}");
                }
                else W("  invoice: no live enemy to target — skipped");

                // 취업알선 (Job Placement): play → borrow Fee(50, +surcharge) onto the loan + apply JobPlacementPower;
                // a turn-start then slips a 품삯(Wages) card into hand.
                var recP = LoanService.For(player)!;
                int owed0 = recP.Principal;
                var job = cstate!.CreateCard<JobPlacementCard>(player);
                try { await CardCmd.AutoPlay(pcc, job, null); } catch (Exception e) { W("  job-placement play failed: " + e.Message); }
                await Task.Delay(150);
                int owedGain = recP.Principal - owed0;
                var jobPow = player.Creature.GetPower<JobPlacementPower>();
                int w0 = PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0;
                if (jobPow != null) await jobPow.AfterPlayerTurnStart(pcc, player);
                await Task.Delay(150);
                int wagesGain = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0) - w0;
                bool tP4 = owedGain >= 50 && jobPow != null && wagesGain >= 1;
                W($"  assert job-placement: owedGain={owedGain}(>=50) power={(jobPow != null)} wagesAdded={wagesGain}(>=1) -> {tP4}");

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
                bool tP = tP1 && tP2 && tP3 && tP4 && tP5;
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
