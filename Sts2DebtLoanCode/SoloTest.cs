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
            var recNd = LoanService.For(player)!;
            recNd.LoanFloor = baseFloor;        int cnt0  = LoanService.DebtCardCountFor(player);   // rooms 0  → 1
            recNd.LoanFloor = baseFloor - 12;   int cnt12 = LoanService.DebtCardCountFor(player);   // rooms 12 → 1 (below 13)
            recNd.LoanFloor = baseFloor - 13;   int cnt13 = LoanService.DebtCardCountFor(player);   // rooms 13 → 2
            recNd.LoanFloor = baseFloor - 16;   int cnt16 = LoanService.DebtCardCountFor(player);   // rooms 16 → 2 (below 17)
            recNd.LoanFloor = baseFloor - 17;   int cnt17 = LoanService.DebtCardCountFor(player);   // rooms 17 → 3
            recNd.LoanFloor = baseFloor - 21;   int cnt21 = LoanService.DebtCardCountFor(player);   // rooms 21 → 3 (below 22)
            recNd.LoanFloor = baseFloor - 22;   int cnt22 = LoanService.DebtCardCountFor(player);   // rooms 22 → 4
            recNd.LoanFloor = baseFloor - 30;   int cnt30 = LoanService.DebtCardCountFor(player);   // rooms 30 → 4 (cap)
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
                try { shopNode?.Open(); } catch { }
                await Task.Delay(500);
                tG = shopNode != null;
                W($"  assert shop-open: shopNode={(shopNode != null)} -> {tG} (원금 상환 버튼은 빚 상점 패널로 이동 → 3c 스샷 확인)");
                await Shot("3_shop");

                // Debt-shop ENTRY: verify our "외상 구매" button attached in the REAL shop, screenshot both buttons,
                // then open the panel over the live shop and screenshot it (the actual in-shop entry flow).
                var debtBtn = FindNode<NDebtCardShopButton>(stree.Root);
                var recG = LoanService.For(player); if (recG != null) recG.DebtShopVisits = 3;   // reveal all 6 offers
                await Task.Delay(800);   // let the button's _Process position + show it
                W($"  assert debt-shop-button: attached={(debtBtn != null)} visible={debtBtn?.Visible}");
                await Shot("3b_shop_buttons");                       // real shop: 외상 구매 button (원금 상환은 빚 상점 패널로 이동)

                // ★대출 기회 = 네이티브 호버툴팁에 붙는 한 줄. 실제 마우스를 대출 가능 슬롯 위로 옮겨(WarpMouse)
                // 게임의 CreateHoverTip 경로를 그대로 태운 뒤, 툴팁이 뜬 상태를 찍는다.
                try
                {
                    if ((int)player.Gold > 40) await PlayerCmd.LoseGold((int)player.Gold - 40, player);   // 부족분을 만들어 대출 가능 상태로
                    await Task.Delay(400);
                    var slots = new List<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot>();
                    void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot s) slots.Add(s); Walk(c); } }
                    if (shopNode != null) Walk(shopNode);
                    MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot? target = null;
                    foreach (var s in slots)
                    {
                        var e = s.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                 .Where(f => typeof(MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry).IsAssignableFrom(f.FieldType))
                                 .Select(f => f.GetValue(s) as MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry).FirstOrDefault(x => x != null);
                        if (e != null && s.Visible && LoanService.CanLoanCover(e, player)) { target = s; break; }
                    }
                    W($"  loan-chance tip: slots={slots.Count} loanable={(target != null)} gold={(int)player.Gold} draws={LoanService.DrawsLeftFor(player)}");
                    if (target != null)
                    {
                        var r = target.GetGlobalRect();
                        // ★마우스 워프로는 재현 불가(실측 2회: hoverTipSet=False). 자동화 환경에선 MouseEntered가
                        // 안 나서 네이티브 호버가 발동하지 않는다. 그래서 패치 대상 메서드를 직접 호출한다 —
                        // CreateAndShow가 곧 검증 대상이고, 프리픽스가 여기서 hoverTips에 우리 줄을 붙인다.
                        MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(target);   // 중복 owner 등록은 throw
                        await Task.Delay(120);
                        var baseTips = new List<MegaCrit.Sts2.Core.HoverTips.IHoverTip>
                        {
                            new MegaCrit.Sts2.Core.HoverTips.HoverTip { Title = "TEST", Description = "슬롯 자체 툴팁 자리", Id = "sp_probe" },
                        };
                        var set = MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.CreateAndShow(
                            target, baseTips, MegaCrit.Sts2.Core.HoverTips.HoverTipAlignment.Right);
                        await Task.Delay(900);
                        bool tipShown = set != null && set.Visible;
                        W($"  loan-chance tip: set={(set != null)} visible={tipShown} over slot {r.Position}+{r.Size} (원본 1줄 + 대출 기회 1줄이 보여야 함)");
                        all &= tipShown;
                        await Shot("3d_loan_chance_tip");
                        MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(target);
                    }
                    else W("  loan-chance tip: 대출 가능한 슬롯이 없어 호버 스샷 생략");
                }
                catch (Exception e) { W("  loan-chance tip shot failed: " + e.Message); }
                if (shopNode != null)
                {
                    // [diag] the shop's own CanvasLayer depth vs the debt-shop panel's — they should MATCH now, so
                    // tooltips/menus/fly-in (higher layers) draw over the panel instead of being hidden (issue: depth).
                    int shopCl = -999; for (Node? n = shopNode; n != null; n = n.GetParent()) if (n is CanvasLayer cl) { shopCl = cl.Layer; break; }
                    NDebtCardShopPanel.Show(shopNode, player);
                    int panelCl = -999; if (Engine.GetMainLoop() is SceneTree pst) { var p = FindNode<NDebtCardShopPanel>(pst.Root); for (Node? n = p; n != null; n = n.GetParent()) if (n is CanvasLayer cl) { panelCl = cl.Layer; break; } }
                    W($"  [diag] canvas depth: shop={shopCl} debtPanel={panelCl} (should match)");
                    await Shot("3c0_slidein");                       // MID-slide: loan canvas scrolling in from the right (Shot's own ~120ms lands mid-tween)
                    await Task.Delay(1000);
                    await Shot("3c_shop_panel");                     // the debt-card screen fully opened FROM the real shop
                    NDebtCardShopPanel.CloseOpen();                  // don't let it linger into the next room
                    await Task.Delay(200);
                }
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

                    // ── 강화 시 {card} 명칭 검증 (this session's fix): 지급 카드가 '+' 형태로 표시되는가 ──
                    // 언어 무관: 지급되는 카드의 로컬 이름 + "+" 가 face 에 들어있는지 확인.
                    string LocName(string key) => new MegaCrit.Sts2.Core.Localization.LocString("cards", key).GetFormattedText();
                    string wages = LocName("WAGES_CARD.title");
                    string diligent = LocName("DILIGENT_PAYMENT_CARD.title");
                    string payment = LocName("DEBT_CURSE_CARD.title");

                    var jpU = player.RunState.CreateCard<JobPlacementCard>(player);
                    jpU.UpgradeInternal(); jpU.FinalizeUpgradeInternal();
                    string jpUFace = jpU.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    bool jpOk = jpUFace.Contains(wages + "+");
                    W($"  [강화명칭] 취업알선+ FACE='{jpUFace}' -> '{wages}+' {(jpOk ? "OK" : "MISSING")}");

                    var rfU = player.RunState.CreateCard<RefundCard>(player);
                    rfU.UpgradeInternal(); rfU.FinalizeUpgradeInternal();
                    string rfUFace = rfU.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    bool rfOk = rfUFace.Contains(diligent + "+");
                    W($"  [강화명칭] 환급+ FACE='{rfUFace}' -> '{diligent}+' {(rfOk ? "OK" : "MISSING")}");

                    bool dlOk = dlUFace.Contains(payment + "+");
                    W($"  [강화명칭] 정기 납부+ -> '{payment}+' {(dlOk ? "OK" : "MISSING")}");

                    // 차환+: 되돌려주는 카드가 납부+ 로 표기되고(‘{card}’ 인자), 카드 자체 비용은 1 그대로여야 한다
                    // (강화 = 돌려받는 카드의 품질이지 스왑 가격이 아님).
                    var rfnBase = player.RunState.CreateCard<RefinanceCard>(player);
                    var rfnU = player.RunState.CreateCard<RefinanceCard>(player);
                    rfnU.UpgradeInternal(); rfnU.FinalizeUpgradeInternal();
                    string rfnUFace = rfnU.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    bool rfnNameOk = rfnUFace.Contains(payment + "+");
                    bool rfnCostOk = rfnU.EnergyCost.GetResolved() == rfnBase.EnergyCost.GetResolved() && rfnU.EnergyCost.GetResolved() == 1;
                    W($"  [강화명칭] 차환+ FACE='{rfnUFace}' -> '{payment}+' {(rfnNameOk ? "OK" : "MISSING")} / cost {rfnBase.EnergyCost.GetResolved()}->{rfnU.EnergyCost.GetResolved()} {(rfnCostOk ? "OK" : "CHANGED")}");

                    // 추심(집행 지급 파워): FACE 가 지급 토큰(집행)을 참조하는지 + 집행 등록 확인. 추심+는 1코(에너지).
                    string shakedown = LocName("SHAKEDOWN_CARD.title");
                    string coFace = player.RunState.CreateCard<CollectionCard>(player).GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    bool coOk = coFace.Contains(shakedown);
                    W($"  [추심] FACE='{coFace}' -> 지급 토큰 '{shakedown}' {(coOk ? "OK" : "MISSING")}");

                    // 개행 검증: 대출 강타 / 저당 = 효과+빚 두 줄 (\n 존재)
                    var lsNl = player.RunState.CreateCard<LoanStrikeCard>(player).GetDescriptionForPile(PileType.Hand).Contains("\n");
                    var mgNl = player.RunState.CreateCard<MortgageCard>(player).GetDescriptionForPile(PileType.Hand).Contains("\n");
                    W($"  [개행] 대출강타 2줄={(lsNl ? "OK" : "NO")} 저당 2줄={(mgNl ? "OK" : "NO")}");
                    // New payment-set cards: registration + loc resolve.
                    foreach (var t in new[] { typeof(WagesCard), typeof(JobPlacementCard), typeof(PaymentBenefitCard),
                                              typeof(RefundCard), typeof(DiligentPaymentCard), typeof(SettlementCard),
                                              typeof(InvoiceCard), typeof(BloodPaymentCard),
                                              typeof(CollectionCard), typeof(ShakedownCard) })
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

                // N2) NEW dedicated card art — render 대납/추심/집행 side by side so we can eyeball that the
                //     new portraits load (no "?" fallback). These three shipped placeholder art until now.
                if (Engine.GetMainLoop() is SceneTree t3)
                {
                    var gallery = new List<NCard>();
                    int gx = 320;
                    foreach (var t in new[] { typeof(BailoutCard), typeof(CollectionCard), typeof(ShakedownCard) })
                    {
                        var gm = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(t));
                        if (gm == null) { W($"  [newart] {t.Name}: NOT REGISTERED"); continue; }
                        var gc = NCard.Create(player.RunState.CreateCard(gm, player));
                        if (gc == null) continue;
                        t3.Root.AddChild(gc);
                        gc.Position = new Vector2(gx, 300);
                        gc.Scale = new Vector2(1.5f, 1.5f);
                        gallery.Add(gc);
                        gx += 430;
                    }
                    await Task.Delay(500);
                    await Shot("9_newart");
                    W($"  rendered {gallery.Count} new-art cards (대납/추심/집행 portraits)");
                    foreach (var gc in gallery) gc.QueueFree();
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

                // 추심(집행): 매 턴 집행 토큰 지급(0코, 1영수증→활력3). 인게임 실측은 추심 플레이 후 턴 시작 훅 필요 —
                // 여기선 영수증 카운트만 로깅(집행 지급=CollectionPower.AfterPlayerTurnStart, 정기 납부 훅과 동형).
                W($"  [추심 참고] 현재 영수증={LoanService.PaymentsThisCombat(player)} → 집행으로 1영수증당 활력3 (다음 공격 강화)");

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
                int expSettle = pays * 4;   // pure X-cost: 4 × 영수증 (no base)
                bool tP2 = settlePlayed && blkGain == expSettle && stackAfterSettle == 0;
                W($"  assert settlement scale+consume: blockGain={blkGain} (exp {pays}×4={expSettle}) tallyAfter={stackAfterSettle}(=0) -> {tP2}");

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

                // 취업알선 (Job Placement): a one-shot SKILL gated behind 영수증(2). Bank 2 payments → play SPENDS the
                // 2 영수증, adds Fee(20) onto OWED (no gold to player), and hands 3 품삯 (1 into hand + 2 into draw).
                // No power any more, and no per-turn generation → can't be stalled for gold.
                var recP = LoanService.For(player)!;
                if (!recP.Active || recP.Principal <= 0) { await LoanService.GrantLoanDirect(player, 200); recP = LoanService.For(player)!; }
                await LoanService.ConsumePaymentStack(player);                                         // tally → 0
                bool jobPlayable0 = cstate!.CreateCard<JobPlacementCard>(player).CanPlay();            // no 영수증 gate any more → playable at 0
                for (int i = 0; i < 2; i++) await LoanService.RecordPayment(player, pcc, 5);           // tally → 2 (JobPlacement must NOT spend these)
                await Task.Delay(120);
                int owed0 = recP.Principal;
                int jobGold0 = (int)player.Gold;                     // must NOT rise (fee is added to debt, not paid out)
                int tally0 = LoanService.PaymentsThisCombat(player); // 2
                int handPre = PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0;
                int drawPre = PileType.Draw.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0;
                var job = cstate!.CreateCard<JobPlacementCard>(player);
                bool jobGate2 = job.CanPlay();                       // 2 >= 2 → playable (informational; AutoPlay force-plays anyway)
                try { await CardCmd.AutoPlay(pcc, job, null); } catch (Exception e) { W("  job-placement play failed: " + e.Message); }
                await Task.Delay(150);
                int owedGain = recP.Principal - owed0;               // expect +20 (fee only; the play makes no payment)
                int jobGoldGain = (int)player.Gold - jobGold0;       // expect 0
                int tallyAfter = LoanService.PaymentsThisCombat(player);   // expect UNCHANGED (no receipt cost now)
                int handWages = (PileType.Hand.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0) - handPre;   // expect 1
                int drawWages = (PileType.Draw.GetPile(player)?.Cards.Count(c => c is WagesCard) ?? 0) - drawPre;   // expect 2
                bool tP4 = jobPlayable0 && owedGain == 20 && jobGoldGain == 0 && tallyAfter == tally0
                           && handWages >= 1 && drawWages >= 2;
                W($"  assert job-placement(skill): playable0={jobPlayable0}(no gate) owedGain={owedGain}(=20) goldGain={jobGoldGain}(=0) tally {tally0}->{tallyAfter}(unchanged) hand+{handWages}(>=1) draw+{drawWages}(>=2) -> {tP4}");

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

                // Receipt COUNTER hover tip (game convention, cf. STAR_COUNT): drive the counter's tooltip + shot it.
                try
                {
                    if (Engine.GetMainLoop() is SceneTree stt)
                    {
                        var counter = FindNode<NPaymentTallyCounter>(stt.Root);
                        if (counter != null)
                        {
                            var tip = new MegaCrit.Sts2.Core.HoverTips.HoverTip(
                                new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_RECEIPT_COUNT.title"),
                                new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_RECEIPT_COUNT.description"));
                            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.CreateAndShow(counter, tip, MegaCrit.Sts2.Core.HoverTips.HoverTipAlignment.Right);
                            await Task.Delay(450);
                            await Shot("6d2_receipt_tip");
                            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(counter);
                            W("  receipt counter tooltip rendered (see 6d2_receipt_tip)");
                        }
                        else W("  receipt counter node not found for tooltip shot");
                    }
                }
                catch (Exception e) { W("  receipt tip render failed: " + e.Message); }

                // tP7) DEBT-SHOP PURCHASE: 10-card pool, the shop shows a ROTATING 5 per visit. Buying adds the price
                //      to owed + drops the card in the deck; can't rebuy; and the bought card drops out next visit.
                bool tP7;
                {
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 100);
                    var shopRec = LoanService.For(player)!;
                    shopRec.DebtShopVisits = 1;
                    var offers1 = LoanService.RevealedPurchasable(shopRec);
                    bool count5 = offers1.Length == 5;                                // 10-card pool → 5 shown
                    var buyType = offers1[0];
                    int price = LoanService.ShopPriceFor(shopRec, buyType);   // tier ± variance, sale applied
                    int owedBefore = shopRec.Principal;
                    bool bought = await LoanService.BuyCardOnDebt(player, buyType);
                    bool owedUp = LoanService.For(player)!.Principal == owedBefore + price;
                    bool inDeck = PileType.Deck.GetPile(player)?.Cards?.Any(c => c.GetType() == buyType) ?? false;
                    bool noRebuy = !(await LoanService.BuyCardOnDebt(player, buyType));   // already bought → refused
                    shopRec.DebtShopVisits = 2;                                       // new visit → fresh selection
                    var offers2 = LoanService.RevealedPurchasable(shopRec);
                    bool count5b = offers2.Length == 5;
                    bool droppedBought = System.Array.IndexOf(offers2, buyType) < 0;  // bought card not re-offered
                    tP7 = count5 && count5b && bought && owedUp && inDeck && noRebuy && droppedBought;
                    W($"  assert debt-shop: offers v1/v2={offers1.Length}/{offers2.Length}(5/5) bought={bought} owed+{price}={owedUp} inDeck={inDeck} noRebuy={noRebuy} droppedBought={droppedBought} -> {tP7}");
                }

                // tP7b) DEBT-SHOP native-Debt penalty: every debt-shop VISIT (first buy) drops ONE native Debt into
                //       the deck — once per visit no matter how many you buy; the whole lot is swept on repay.
                bool tP7b;
                {
                    System.Func<int> deckDebt = () => PileType.Deck.GetPile(player)?.Cards?.Count(c => c is MegaCrit.Sts2.Core.Models.Cards.Debt) ?? 0;
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 100);
                    var recShop = LoanService.For(player)!;
                    recShop.DebtShopVisits = 3;                                  // reveal all offers
                    int savedCredit = DebtLoanConfig.ShopCreditLimit;
                    DebtLoanConfig.ShopCreditLimit = 9999;                       // isolate this test from the per-visit credit cap
                    var off = LoanService.RevealedPurchasable(recShop);
                    int d0 = deckDebt();
                    // ★Slot 0 is the FREE gift now, so the native-Debt curse is keyed to the first PAID buy, not the
                    // first buy: taking only the gift and walking out must cost nothing (see LoanService.ApplyBuyCard).
                    await LoanService.BuyCardOnDebt(player, off[0]); await Task.Delay(80); int dFree = deckDebt();  // FREE slot → NO Debt
                    await LoanService.BuyCardOnDebt(player, off[1]); await Task.Delay(80); int d1 = deckDebt();     // 1st PAID buy → +1 Debt
                    await LoanService.BuyCardOnDebt(player, off[2]); await Task.Delay(80); int d2 = deckDebt();     // same visit → no extra
                    recShop.LastDebtGrantFloor = -999; recShop.ShopSpentThisVisit = 0;   // simulate a NEW shop visit (fresh floor + credit)
                    await LoanService.BuyCardOnDebt(player, off[3]); await Task.Delay(80); int d3 = deckDebt();     // new visit → +1 Debt
                    DebtLoanConfig.ShopCreditLimit = savedCredit;               // restore
                    if ((int)player.Gold < recShop.Principal) await PlayerCmd.GainGold(recShop.Principal - (int)player.Gold, player, false);
                    await LoanService.Repay(player); await Task.Delay(120); int d4 = deckDebt();                  // repay → all swept
                    tP7b = dFree == d0 && d1 == d0 + 1 && d2 == d1 && d3 == d2 + 1 && d4 == 0;
                    W($"  assert debt-shop native-Debt: free={dFree}(={d0}, gift adds none) paid1={d1}(={d0}+1) paid2/sameVisit={d2}(=paid1) paid3/newVisit={d3}(=+1) afterRepay={d4}(=0) -> {tP7b}");
                }

                // tP7c) DEBT-SHOP 강화판: exactly ONE of the visit's offers is stocked already upgraded, it's an
                //       upgradable card, it isn't the sale card (when there's a choice), it carries the +30% price
                //       premium, and BUYING it puts an UPGRADED copy in the deck.
                bool tP7c;
                {
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 100);
                    var recU = LoanService.For(player)!;
                    recU.DebtShopVisits = 1;
                    var offersU = LoanService.RevealedPurchasable(recU);
                    var upType = LoanService.UpgradedCardFor(recU);
                    var saleType = LoanService.SaleCardFor(recU);
                    bool picked = upType != null && System.Array.IndexOf(offersU, upType) >= 0;
                    // Deterministic: the same record must name the same 강화판 every time it's asked.
                    bool stable = LoanService.UpgradedCardFor(recU) == upType;
                    bool notSale = upType != saleType;
                    int basePrice = picked ? LoanService.ShopBasePrice(recU, upType!) : 0;
                    int shownPrice = picked ? LoanService.ShopPriceFor(recU, upType!) : 0;
                    bool premium = picked && shownPrice > basePrice;
                    int savedCap = DebtLoanConfig.ShopCreditLimit;
                    DebtLoanConfig.ShopCreditLimit = 9999;                      // the premium price must not be the thing under test
                    bool boughtU = picked && await LoanService.BuyCardOnDebt(player, upType!);
                    await Task.Delay(120);
                    DebtLoanConfig.ShopCreditLimit = savedCap;
                    var inDeckU = PileType.Deck.GetPile(player)?.Cards?.FirstOrDefault(c => c.GetType() == upType);
                    bool grantedUpgraded = inDeckU != null && inDeckU.IsUpgraded;
                    tP7c = picked && stable && notSale && premium && boughtU && grantedUpgraded;
                    W($"  assert debt-shop 강화판: pick={upType?.Name}(inOffers={picked} stable={stable} notSale={notSale}) price={basePrice}->{shownPrice}({premium}) bought={boughtU} deckUpgraded={grantedUpgraded} -> {tP7c}");
                }

                // tP7d) 빚 카드 판정 단일화: 차환·돌려막기가 먹는 대상은 native Debt "한 종류"뿐이어야 한다.
                //       티어 저주(연체/차압/신용불량/강제징수)·납부·게임의 다른 저주는 전부 제외 — 티어 저주는 매
                //       전투 재주입되므로 허용하면 돌려막기가 무한 골드 수도꼭지가 된다.
                bool tP7d;
                {
                    var nativeDebt = player.RunState.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Debt>(player);
                    bool takesNative = LoanService.IsDebtCurseCard(nativeDebt);
                    bool skipsTier =
                        !LoanService.IsDebtCurseCard(player.RunState.CreateCard<DelinquencyCard>(player)) &&
                        !LoanService.IsDebtCurseCard(player.RunState.CreateCard<SeizureCard>(player)) &&
                        !LoanService.IsDebtCurseCard(player.RunState.CreateCard<BadCreditCard>(player)) &&
                        !LoanService.IsDebtCurseCard(player.RunState.CreateCard<DebtorCard>(player));
                    bool skipsPayment = !LoanService.IsDebtCurseCard(player.RunState.CreateCard<DebtCurseCard>(player));
                    tP7d = takesNative && skipsTier && skipsPayment;
                    W($"  assert 빚카드 판정: native={takesNative} 티어저주제외={skipsTier} 납부제외={skipsPayment} -> {tP7d}");
                }

                // tP7e) 돌려막기: 0코 / 2영수증 / 골드 30→40 / 게이트(영수증+손의 빚 카드 둘 다 있어야 플레이 가능).
                bool tP7e;
                {
                    var kit = player.RunState.CreateCard<KitingCard>(player);
                    var kitU = player.RunState.CreateCard<KitingCard>(player);
                    kitU.UpgradeInternal(); kitU.FinalizeUpgradeInternal();
                    bool cost0 = kit.EnergyCost.GetResolved() == 0 && kitU.EnergyCost.GetResolved() == 0;
                    bool tally2 = kit.TallyCost == 2;
                    bool gold30 = kit.DynamicVars["gold"].IntValue == 30;
                    bool gold40 = kitU.DynamicVars["gold"].IntValue == 40;
                    string kitFace = kit.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    string kitUFace = kitU.GetDescriptionForPile(PileType.Hand).Replace("\n", " | ");
                    bool faceOk = kitFace.Contains("30") && kitUFace.Contains("40");
                    tP7e = cost0 && tally2 && gold30 && gold40 && faceOk;
                    W($"  assert 돌려막기: cost0={cost0} 영수증={kit.TallyCost}(2) gold {kit.DynamicVars["gold"].IntValue}->{kitU.DynamicVars["gold"].IntValue}(30->40) face={faceOk} -> {tP7e}");
                    W($"    FACE='{kitFace}'  FACE+='{kitUFace}'");
                }

                // tP8) BORROW cards (대출 강타 / 저당): upgrade DROPS Exhaust (repeatable), base keeps it.
                bool tP8;
                {
                    var lsBase = cstate!.CreateCard<LoanStrikeCard>(player);
                    bool baseHas = lsBase.Keywords.Contains(CardKeyword.Exhaust);
                    var lsUp = cstate!.CreateCard<LoanStrikeCard>(player);
                    lsUp.UpgradeInternal(); lsUp.FinalizeUpgradeInternal();
                    bool lsUpHas = lsUp.Keywords.Contains(CardKeyword.Exhaust);
                    var mgUp = cstate!.CreateCard<MortgageCard>(player);
                    mgUp.UpgradeInternal(); mgUp.FinalizeUpgradeInternal();
                    bool mgUpHas = mgUp.Keywords.Contains(CardKeyword.Exhaust);
                    tP8 = baseHas && !lsUpHas && !mgUpHas;
                    W($"  assert borrow-upgrade: base Exhaust={baseHas} 대출강타+Exhaust={lsUpHas} 저당+Exhaust={mgUpHas} -> {tP8}");
                }

                bool tP = tP1 && tP2 && tP3 && tP4 && tP5 && tP7 && tP7b && tP7c && tP7d && tP7e && tP8;
                W($"  == payment-set mechanics: {(tP ? "PASS" : "FAIL")} ==");
                all &= tP;

                // tP-visual) DEBT-SHOP PANEL: open the buy-on-credit panel (all 6 revealed) and screenshot it, then
                //            buy one and re-shot so we can eyeball the card renders, prices, and the 품절 grey-out.
                try
                {
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 100);
                    LoanService.For(player)!.DebtShopVisits = 3;   // reveal all 6
                    NDebtCardShopPanel.ShowForTest(player);
                    await Task.Delay(800);
                    await Shot("6e_debtshop");
                    var firstOffer = LoanService.RevealedPurchasable(LoanService.For(player)!).FirstOrDefault();
                    if (firstOffer != null) { await LoanService.BuyCardOnDebt(player, firstOffer); await Task.Delay(500); await Shot("6f_debtshop_bought"); }
                    W("  debt-shop panel rendered (see 6e_debtshop / 6f_debtshop_bought)");
                    // 영수증 (Receipt) tooltip: the loc must resolve (not raw key) — it's shown on hover over any
                    // receipt-spending card (Invoice/Settlement/Garnishment + Counterclaim/Statement/InterestSupport).
                    var rcptTitle = new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_RECEIPT.title").GetFormattedText();
                    var rcptDesc = new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_RECEIPT.description").GetFormattedText();
                    W($"  receipt-tooltip loc: title='{rcptTitle}' desc='{rcptDesc}'");
                }
                catch (Exception e) { W("  debt-shop panel render failed: " + e.Message); }
            }
            catch (Exception e) { W("  payment-set section failed: " + e); all = false; }

            // 파산 선언 (Declare Bankruptcy) — in a FRESH live combat (the payment-set fight's enemy is dead by now,
            // and powers/cards can't be applied once a combat is won): inject native Debt cards, then playing the card
            // must exhaust them ALL, grant Strength = how many were wiped, and apply 파산 (blocks gold gain this combat).
            Step("bankruptcy (파산 선언)");
            try
            {
                if (Engine.GetMainLoop() is SceneTree)
                {
                    // Always enter a FRESH combat (like the payment-set step does): the prior fight's enemy is dead
                    // but CombatManager can still read InProgress, and powers/cards no-op with no live enemy.
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);
                    var bcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var bstate = player.Creature?.CombatState;
                    System.Func<Player, int> countDebt = p =>
                    {
                        int c = 0;
                        foreach (var pt in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
                        {
                            var pile = pt.GetPile(p);
                            if (pile != null) foreach (var card in pile.Cards) if (card is MegaCrit.Sts2.Core.Models.Cards.Debt) c++;
                        }
                        return c;
                    };
                    if ((int)player.Gold < 50) await PlayerCmd.GainGold(50 - (int)player.Gold, player, false);

                    const int debtN = 3;
                    var injected = new List<CardModel>();
                    for (int i = 0; i < debtN; i++)
                        if (bstate!.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Debt>(player) is CardModel d) injected.Add(d);
                    if (injected.Count > 0)
                        await CardPileCmd.AddGeneratedCardsToCombat(injected, PileType.Draw, player, CardPilePosition.Random);
                    await Task.Delay(200);
                    int debtBefore = countDebt(player);
                    int strBefore = (int)(player.Creature!.GetPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()?.Amount ?? 0);

                    var bank = bstate!.CreateCard<BankruptcyCard>(player);
                    try { await CardCmd.AutoPlay(bcc, bank, null); } catch (Exception e) { W("  bankruptcy play failed: " + e.Message); }
                    await Task.Delay(250);

                    int debtAfter = countDebt(player);
                    int strAfter = (int)(player.Creature!.GetPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()?.Amount ?? 0);
                    bool bankPow = player.Creature!.GetPower<BankruptcyPower>() != null;
                    // [hover] verify the 파산 power's tooltip loc resolves to real text (not a raw key / empty) — the
                    // reported "no power tooltip" issue. If this prints the localized sentence, the loc is injected fine.
                    var bp = player.Creature!.GetPower<BankruptcyPower>();
                    if (bp != null)
                    {
                        string bpTitle = "", bpDesc = ""; bool bpEx = false;
                        try { bpEx = bp.Description.Exists(); } catch { }
                        try { bpTitle = bp.Title.GetFormattedText(); } catch { }
                        try { bpDesc = bp.Description.GetFormattedText(); } catch { }
                        W($"  [hover] Bankruptcy power: exists={bpEx} title='{bpTitle}' desc='{bpDesc}'");
                    }
                    int goldBefore = (int)player.Gold;
                    await PlayerCmd.GainGold(50, player, false);   // bankrupt → blocked (BankruptcyPower.ModifyGoldGained → 0)
                    await Task.Delay(100);
                    bool goldBlocked = (int)player.Gold == goldBefore;

                    bool tBank = debtBefore >= debtN && debtAfter == 0 && (strAfter - strBefore) == debtBefore && bankPow && goldBlocked;
                    W($"  assert bankruptcy: debt {debtBefore}->{debtAfter}(=0) str+{strAfter - strBefore}(={debtBefore}) power={bankPow} goldBlock {goldBefore}->{(int)player.Gold}(blocked={goldBlocked}) -> {tBank}");
                    all &= tBank;
                    await Shot("12_bankruptcy");
                    if (bankPow) await PowerCmd.Remove<BankruptcyPower>(player.Creature!);
                    // POST-COMBAT sim: the power is gone (removed above), but the 파산 FLAG must still block reward
                    // gold via BankruptGoldBlockPatch. Then clear the flag so later tests' GainGold setups still work.
                    int gpPost = (int)player.Gold;
                    await PlayerCmd.GainGold(50, player, false);
                    await Task.Delay(80);
                    bool postBlock = (int)player.Gold == gpPost;
                    W($"  assert bankruptcy post-combat gold: {gpPost}->{(int)player.Gold} blocked={postBlock}(flag, power gone) -> {postBlock}");
                    all &= postBlock;
                    await LoanService.ResetPaymentsThisCombat(player);   // clear 파산 flag (next-fight reset) so later tests can gain gold
                }
            }
            catch (Exception e) { W("  bankruptcy section failed: " + e); all = false; }

            // 연체 (Delinquency) DAMAGE MEASURE: drive the enemy's REAL attack path (DamageCmd.FromMonster → the same
            // ModifyDamage pipeline a real enemy turn uses) at the player, once WITHOUT the 연체 card in hand and once
            // WITH it. If the card's ModifyDamageMultiplicative(×1.5) actually fires, withCurse == baseline×1.5.
            // Diagnosis via decompile: RunState.IterateHookListeners only walks player.Deck.Cards, so a COMBAT-injected
            // card is never a damage-hook listener → we EXPECT withCurse == baseline (no boost). This proves it live.
            Step("delinquency damage measure (연체 실측)");
            try
            {
                if (Engine.GetMainLoop() is SceneTree)
                {
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);
                    var dcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var dstate = player.Creature?.CombatState;
                    var foe = dstate?.Enemies?.FirstOrDefault(e => e != null && e.IsAlive && e.Monster != null);
                    if (foe?.Monster != null && player.Creature != null)
                    {
                        // Fresh combat → Block 0, HP full. baseline: enemy hits player for 10, no 연체 in hand.
                        int hpA = player.Creature.CurrentHp;
                        await MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(10m).FromMonster(foe.Monster).Execute(dcc);
                        await Task.Delay(200);
                        int baseline = hpA - player.Creature.CurrentHp;
                        // Inject 연체 and simulate it being DRAWN into hand (InvokeDrawn = the draw event that fires
                        // ApplyVulnerableOnDraw). It must apply native Vulnerable, so the SAME enemy attack now hits ×1.5.
                        // Inject 연체 and simulate it being DRAWN (InvokeDrawn = the on-draw path the Harmony patch
                        // hooks). It must apply native Vulnerable via the clone-safe patch, so the SAME enemy attack ×1.5.
                        var del = dstate!.CreateCard<DelinquencyCard>(player) as DelinquencyCard;
                        if (del != null)
                        {
                            await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { del }, PileType.Hand, player, CardPilePosition.Random);
                            await Task.Delay(120);
                            del.InvokeDrawn();          // ← drawn into hand → DelinquencyDrawPatch applies Vulnerable 1
                            await Task.Delay(250);
                        }
                        int vuln = (int)(player.Creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>()?.Amount ?? 0);
                        int hpB = player.Creature.CurrentHp;
                        await MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(10m).FromMonster(foe.Monster).Execute(dcc);
                        await Task.Delay(200);
                        int withCurse = hpB - player.Creature.CurrentHp;
                        bool tDel = baseline == 10 && withCurse == 15 && vuln >= 1;   // Vulnerable applied ON DRAW → ×1.5 on the real attack
                        W($"  assert delinquency→vulnerable(on draw): baseline={baseline}(=10) vulnStacks={vuln}(>=1) withCurse={withCurse}(=15,×1.5) -> {tDel}");
                        all &= tDel;
                    }
                    else W("  delinquency measure: no live monster with a model — skipped");
                }
            }
            catch (Exception e) { W("  delinquency measure failed: " + e.Message); }

            // 성실 납부 (Diligent Payment) MEASURE: block == the number of 납부 (DebtCurseCard) CARDS played-and-
            // exhausted this combat (NOT every payment path). Give 환급, actually PLAY 3 납부 cards (each 20 gold →
            // exhausts + 환급 hands a 성실 납부), then play one 성실 납부 and read Block (expect == exhausted count 3).
            Step("diligent payment measure (성실납부 실측)");
            try
            {
                if (Engine.GetMainLoop() is SceneTree)
                {
                    if (!(MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false))
                    {
                        await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                        await Task.Delay(4000);
                    }
                    var rcc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var rstate = player.Creature?.CombatState;
                    if (rstate != null && player.Creature != null)
                    {
                        await LoanService.ResetPaymentsThisCombat(player);
                        if ((int)player.Gold < 80) await PlayerCmd.GainGold(80 - (int)player.Gold, player, false);   // fund 3× 20-gold plays
                        if (LoanService.For(player) is null || !(LoanService.For(player)?.Active ?? false)) await LoanService.GrantLoanDirect(player, 200);
                        await PowerCmd.Apply<RefundPower>(rcc, player.Creature, 1, player.Creature, null);   // 환급 → hands 성실 납부 per payment
                        await Task.Delay(120);
                        bool refundPow = player.Creature.GetPower<RefundPower>() != null;
                        int dpBefore = PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0;
                        // PLAY 3 real 납부 cards (each exhausts → RecordExhaustedPaymentCard + 환급 hands a 성실 납부)
                        for (int i = 0; i < 3; i++)
                        {
                            var pay = rstate.CreateCard<DebtCurseCard>(player);
                            await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { pay }, PileType.Hand, player, CardPilePosition.Top);
                            await Task.Delay(80);
                            try { await CardCmd.AutoPlay(rcc, pay, null); } catch (Exception e) { W("  납부 play failed: " + e.Message); }
                            await Task.Delay(120);
                        }
                        int exhausted = LoanService.PaymentCountThisCombat(player);                     // exhausted 납부 cards = 3
                        int dpAfter = PileType.Hand.GetPile(player)?.Cards.Count(c => c is DiligentPaymentCard) ?? 0;
                        int dpHanded = dpAfter - dpBefore;                                             // 환급 handed 3 성실 납부
                        // play ONE 성실 납부 and read the Block it grants (== exhausted count)
                        int blk0 = player.Creature.Block;
                        var dp = PileType.Hand.GetPile(player)?.Cards.FirstOrDefault(c => c is DiligentPaymentCard) as CardModel;
                        int blkGain = -1;
                        if (dp != null)
                        {
                            try { await CardCmd.AutoPlay(rcc, dp, null); } catch (Exception e) { W("  diligent play failed: " + e.Message); }
                            await Task.Delay(150);
                            blkGain = player.Creature.Block - blk0;
                        }
                        bool tDil = refundPow && exhausted == 3 && dpHanded >= 3 && blkGain == exhausted && dp != null;
                        W($"  assert diligent payment: refundPow={refundPow} exhaustedPaymentCards={exhausted}(=3) cardsHanded={dpHanded}(>=3) blockGained={blkGain}(={exhausted}) played={dp != null} -> {tDil}");
                        all &= tDil;
                    }
                }
            }
            catch (Exception e) { W("  diligent payment measure failed: " + e.Message); }

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
                        await PowerCmd.Apply<BadCreditPower>(scc, cr, 1, cr, null);         // 신용 불량
                        await PowerCmd.Apply<CounterclaimPower>(scc, cr, 1, cr, null);      // 자본 타격 (Money Attack)
                        await PowerCmd.Apply<StatementPower>(scc, cr, 1, cr, null);         // 명세서 (Statement)
                        await PowerCmd.Apply<InterestSupportPower>(scc, cr, 1, cr, null);   // 이자 지원 (Interest Support)
                        await Task.Delay(600);
                        int active = 0;
                        if (cr.GetPower<DunningLetterPower>() != null) active++;
                        if (cr.GetPower<PaymentBenefitPower>() != null) active++;
                        if (cr.GetPower<RefundPower>() != null) active++;
                        if (cr.GetPower<BadCreditPower>() != null) active++;
                        if (cr.GetPower<CounterclaimPower>() != null) active++;
                        if (cr.GetPower<StatementPower>() != null) active++;
                        if (cr.GetPower<InterestSupportPower>() != null) active++;
                        W($"  power-icon gallery: {active}/7 custom powers active (see 9_power_icons.png)");

                        // ── HOVER TEXT: the character-hover tooltip shows each power's Title + Description
                        // (PowerModel.Description = LocString "powers/<ENTRY>.description"). Verify every custom
                        // power's description resolves to real localized text — not a raw loc key, not empty —
                        // and log it so we can eyeball what the tooltip will read.
                        var hoverPowers = new MegaCrit.Sts2.Core.Models.PowerModel?[]
                        {
                            cr.GetPower<DunningLetterPower>(), cr.GetPower<PaymentBenefitPower>(), cr.GetPower<RefundPower>(),
                            cr.GetPower<BadCreditPower>(), cr.GetPower<CounterclaimPower>(),
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
                        bool tHover = descTotal == 7 && descOk == 7;   // 취업알선 became a skill (no power) → 7 custom powers
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

            // U) CARD GALLERY (user request): render EVERY card this mod adds in a clean grid and screenshot it
            //    page by page, so the full card list can be shown for a playtest intro. Display-only, not PASS/FAIL.
            //    Uses the debt-shop's proven render recipe (NCard.Create(model) + UpdateVisuals) which shows real
            //    localized titles/descriptions (a bare NCard.Create without UpdateVisuals mangles the face).
            Step("card gallery");
            try
            {
                if (Engine.GetMainLoop() is SceneTree gtree)
                {
                    NDebtCardShopPanel.CloseOpen();   // in case a panel lingers
                    var galleryTypes = new System.Type[]
                    {
                        // acquirable powers/skills (payment engine)
                        typeof(DunningLetterCard), typeof(JobPlacementCard), typeof(PaymentBenefitCard), typeof(RefundCard),
                        typeof(StatementCard), typeof(InterestSupportCard), typeof(CounterclaimCard), typeof(CollectionCard),
                        // 영수증-spending attacks/skills
                        typeof(SettlementCard), typeof(InvoiceCard), typeof(GarnishmentCard), typeof(BloodPaymentCard),
                        // borrow + reward
                        typeof(LoanStrikeCard), typeof(MortgageCard), typeof(CreditRestoredCard),
                        // generated tokens + co-op
                        typeof(WagesCard), typeof(DiligentPaymentCard), typeof(ShakedownCard), typeof(BailoutCard),
                        // curses (ForcedCollectionCard omitted: orphan/미스폰 — never granted, unlocalized; 강제 징수 = DebtorCard)
                        typeof(DebtCurseCard), typeof(DelinquencyCard), typeof(SeizureCard), typeof(BadCreditCard),
                        typeof(DebtorCard),
                    };
                    const int cols = 5, rowsPerPage = 3, perPage = cols * rowsPerPage;
                    const float gscale = 0.58f, colPitch = 340f, rowPitch = 352f, gx0 = 195f, gy0 = 210f;
                    var vp = gtree.Root.GetVisibleRect().Size;
                    int pages = (galleryTypes.Length + perPage - 1) / perPage;
                    for (int page = 0; page < pages; page++)
                    {
                        var layer = new CanvasLayer { Layer = 200 };
                        gtree.Root.AddChild(layer);
                        var bg = new ColorRect { Color = new Color(0.10f, 0.09f, 0.13f), Position = Vector2.Zero, Size = vp };
                        layer.AddChild(bg);
                        int start = page * perPage, end = Math.Min(start + perPage, galleryTypes.Length), rendered = 0;
                        for (int i = start; i < end; i++)
                        {
                            var model = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(galleryTypes[i]));
                            if (model == null) { W($"  gallery: model missing for {galleryTypes[i].Name}"); continue; }
                            NCard? nc = null;
                            try { nc = NCard.Create(model); } catch (Exception e) { W($"  gallery card failed {galleryTypes[i].Name}: {e.Message}"); }
                            if (nc == null) continue;
                            int slot = i - start, col = slot % cols, row = slot / cols;
                            layer.AddChild(nc);
                            nc.Position = new Vector2(gx0 + col * colPitch, gy0 + row * rowPitch);
                            nc.Scale = new Vector2(gscale, gscale);
                            try { nc.UpdateVisuals(PileType.None, CardPreviewMode.Normal); } catch { }
                            rendered++;
                        }
                        await Task.Delay(600);
                        await Shot($"11_gallery_p{page + 1}");
                        W($"  card gallery page {page + 1}/{pages}: {rendered} cards (see 11_gallery_p{page + 1}.png)");
                        layer.QueueFree();
                        await Task.Delay(150);
                    }
                }
            }
            catch (Exception e) { W("  card gallery failed: " + e.Message); }

            // ── 도파민 3카드: 어음(에너지를 빚으로 산다) / 레버리지(원금이 곧 피해) / 채무 조정(런당 1회 탕감) ──────
            // Fresh combat, because a combat whose enemies are already dead makes PowerCmd/CardCmd no-ops (the
            // bankruptcy section's hard-won lesson) and 레버리지's CalculatedDamage only evaluates while
            // CombatManager.IsInProgress.
            Step("도파민 3카드 (어음/레버리지/채무 조정)");
            try
            {
                if (Engine.GetMainLoop() is SceneTree)
                {
                    // Earlier sections repay the loan, and a settle calls ResetFor(player) → the record is GONE.
                    // Every card here reads the ledger, so grant a fresh loan before entering combat.
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 200);
                    await Task.Delay(400);
                    await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
                    await Task.Delay(4000);
                    var ncc = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var nstate = player.Creature?.CombatState;
                    var nfoe = nstate?.HittableEnemies?.FirstOrDefault(e => e != null && e.IsAlive)
                               ?? nstate?.Enemies?.FirstOrDefault(e => e != null && e.IsAlive);
                    var nrec = LoanService.For(player);
                    if (nstate != null && nrec != null && player.Creature != null)
                    {
                        nrec.Active = true;
                        nrec.RestructuringUsed = false;

                        // ① 어음 — 0코 소멸: 에너지 +2, 원금 +100. AutoPlay pays the (zero) cost, OnPlay grants the energy.
                        nrec.Principal = 400; LoanService.SyncToRelic(player);
                        int e0 = player.PlayerCombatState.Energy, pn0 = nrec.Principal;
                        var note = nstate.CreateCard<PromissoryNoteCard>(player);
                        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { note }, PileType.Hand, player, CardPilePosition.Random);
                        await Task.Delay(150);
                        int handBeforeNote = PileType.Hand.GetPile(player)?.Cards.Count ?? 0;
                        try { await CardCmd.AutoPlay(ncc, note, null); } catch (Exception e) { W("  어음 play failed: " + e.Message); }
                        await Task.Delay(300);
                        int eGain = player.PlayerCombatState.Energy - e0;
                        int pGain = (LoanService.For(player)?.Principal ?? 0) - pn0;
                        int handAfterNote = PileType.Hand.GetPile(player)?.Cards.Count ?? 0;
                        bool tN1 = eGain == 2 && pGain == 100;
                        W($"  assert 어음: energy {e0}→{player.PlayerCombatState.Energy} (Δ{eGain}, exp 2) / owed {pn0}→{LoanService.For(player)?.Principal} (Δ{pGain}, exp 100) -> {tN1}");
                        all &= tN1;

                        // 어음+ : same deal, plus draw 2. Base play cost the hand 1 net card (played+exhausted);
                        // the upgraded one must come out AHEAD of that by the 2 it draws.
                        int baseHandDelta = handAfterNote - handBeforeNote;               // ≈ −1
                        var noteU = nstate.CreateCard<PromissoryNoteCard>(player);
                        noteU.UpgradeInternal(); noteU.FinalizeUpgradeInternal();
                        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { noteU }, PileType.Hand, player, CardPilePosition.Random);
                        await Task.Delay(150);
                        int handBeforeU = PileType.Hand.GetPile(player)?.Cards.Count ?? 0;
                        try { await CardCmd.AutoPlay(ncc, noteU, null); } catch (Exception e) { W("  어음+ play failed: " + e.Message); }
                        await Task.Delay(400);
                        int upgHandDelta = (PileType.Hand.GetPile(player)?.Cards.Count ?? 0) - handBeforeU;
                        bool tN2 = upgHandDelta - baseHandDelta >= 1;   // ≥1 extra card vs the base form (draw fired)
                        W($"  assert 어음+ draw: handΔ base={baseHandDelta} upgraded={upgHandDelta} (upgraded must exceed base) -> {tN2}");
                        all &= tN2;

                        // ② 레버리지 — damage = 남은 원금 ÷ 30 (강화 ÷22), live off the ledger. THE regression that
                        // matters: CalculatedDamageVar reads DynamicVars.ExtraDamage (NOT CalculationExtra), so a
                        // wrong var pairing silently resolves to 0 here.
                        var lrec = LoanService.For(player)!;
                        lrec.Principal = 600; LoanService.SyncToRelic(player);
                        var lev = nstate.CreateCard<LeverageCard>(player);
                        var levU = nstate.CreateCard<LeverageCard>(player);
                        levU.UpgradeInternal(); levU.FinalizeUpgradeInternal();
                        // ★Both must be IN A PILE before Calculate(): CardModel.CombatState is DERIVED FROM Pile, and
                        // CalculatedVar.Calculate skips the multiplier entirely when CombatState is null — a loose
                        // card always reports 0 damage no matter how the vars are wired.
                        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { lev, levU }, PileType.Hand, player, CardPilePosition.Random);
                        await Task.Delay(200);
                        int dmgBase = (int)lev.DynamicVars.CalculatedDamage.Calculate(nfoe);
                        int dmgUpg = (int)levU.DynamicVars.CalculatedDamage.Calculate(nfoe);
                        bool tL1 = dmgBase == 20 && dmgUpg == 27;      // 600/30, 600/22
                        W($"  assert 레버리지 scaling @owed 600: base={dmgBase}(exp 20) upgraded={dmgUpg}(exp 27) -> {tL1}");
                        all &= tL1;
                        // …and it tracks the ledger LIVE: paying the debt down must shrink the number on the face.
                        lrec.Principal = 300; LoanService.SyncToRelic(player);
                        int dmgAfter = (int)lev.DynamicVars.CalculatedDamage.Calculate(nfoe);
                        bool tL2 = dmgAfter == 10;                     // 300/30
                        W($"  assert 레버리지 live-tracking: owed 600→300 ⇒ dmg {dmgBase}→{dmgAfter}(exp 10) -> {tL2}");
                        all &= tL2;
                        // Actually swing it so the damage pipeline (not just the preview var) is proven.
                        if (nfoe != null)
                        {
                            lrec.Principal = 600; LoanService.SyncToRelic(player);
                            int foeHp0 = nfoe.CurrentHp;
                            try { await CardCmd.AutoPlay(ncc, lev, nfoe); } catch (Exception e) { W("  레버리지 play failed: " + e.Message); }
                            await Task.Delay(300);
                            int drop = foeHp0 - nfoe.CurrentHp;
                            bool tL3 = drop >= 10;                     // ~20 unblocked; allow enemy Block
                            W($"  assert 레버리지 real hit: enemy HP Δ{drop} (exp ~20, ≥10 with block) -> {tL3}");
                            all &= tL3;
                        }

                        // ③ 채무 조정 — 250 written off, ONCE per loan, and the card deletes itself from the DECK
                        // (Exhaust alone is combat-only). Also seed a DECK copy to prove the sweep.
                        var resRec = LoanService.For(player)!;
                        resRec.Principal = 600; resRec.RestructuringUsed = false; LoanService.SyncToRelic(player);
                        await DebtLoanGrants.GrantCard(player, typeof(RestructuringCard), preview: false);
                        await Task.Delay(300);
                        int deckRes0 = player.Deck.Cards.Count(c => c is RestructuringCard);
                        int rp0 = resRec.Principal, paid0 = resRec.TotalPaid;
                        var res = nstate.CreateCard<RestructuringCard>(player);
                        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { res }, PileType.Hand, player, CardPilePosition.Random);
                        await Task.Delay(150);
                        try { await CardCmd.AutoPlay(ncc, res, null); } catch (Exception e) { W("  채무 조정 play failed: " + e.Message); }
                        await Task.Delay(500);
                        var rrec2 = LoanService.For(player);
                        int rp1 = rrec2?.Principal ?? 0, paid1 = rrec2?.TotalPaid ?? paid0;
                        int deckRes1 = player.Deck.Cards.Count(c => c is RestructuringCard);
                        bool used = rrec2 == null || rrec2.RestructuringUsed;
                        bool tR1 = (rp0 - rp1) == 250 && used && deckRes0 >= 1 && deckRes1 == 0 && paid1 == paid0;
                        W($"  assert 채무 조정: owed {rp0}→{rp1} (Δ{rp0 - rp1}, exp 250) used={used} deckCopies {deckRes0}→{deckRes1}(exp 0) totalPaid {paid0}→{paid1}(must NOT move: 탕감≠납부) -> {tR1}");
                        all &= tR1;
                        bool tR2 = !LoanService.CanRestructure(player)
                                   && (rrec2 == null || !LoanService.IsPurchasable(rrec2, typeof(RestructuringCard)));
                        W($"  assert 채무 조정 run-once gate: canRestructure={LoanService.CanRestructure(player)}(exp False) purchasable=False -> {tR2}");
                        all &= tR2;
                        await Shot("13_dopamine_cards");
                    }
                    else W("  도파민 3카드: no combat state / loan record — skipped");
                }
            }
            catch (Exception e) { W("  도파민 3카드 section failed: " + e); all = false; }

            // ── 빚 상점 무료 슬롯 (슬롯 0) ────────────────────────────────────────────────────────────────────
            // The leftmost offer is a GIFT: price 0, no principal, no card debt, no credit-line spend, and — the one
            // that is easy to get wrong — no native Debt curse. A PAID buy right after must still bring the curse.
            Step("빚 상점 무료 슬롯 (슬롯 0)");
            try
            {
                if (LoanService.For(player) == null)   // a preceding settle may have wiped the record
                {
                    LoanService.ResetFor(player);
                    await LoanService.GrantLoanDirect(player, 200);
                    await Task.Delay(400);
                }
                var frec = LoanService.For(player);
                if (frec != null)
                {
                    frec.Active = true;
                    frec.DebtShopVisits = 3;                                         // reveal the full offer row
                    if (frec.Principal <= 0) frec.Principal = 300;
                    frec.RestructuringUsed = false;
                    frec.ShopSpentThisVisit = 0;
                    frec.PurchasedCards.Clear();
                    frec.CurrentOffers = null; frec.OfferVisit = -1;                 // force a fresh roll
                    frec.LastDebtGrantFloor = -1;                                    // so a PAID buy is eligible for the curse
                    LoanService.SyncToRelic(player);

                    var offers = LoanService.RevealedPurchasable(frec);
                    if (offers.Length >= 2)
                    {
                        var free = offers[0];
                        bool tF0 = LoanService.IsFreeOffer(frec, free)
                                   && LoanService.ShopPriceFor(frec, free) == 0
                                   && free != typeof(RestructuringCard)               // never the run-once write-off
                                   && LoanService.SaleCardFor(frec) != free           // sale tag is a paid-slot perk
                                   && LoanService.CanAffordCredit(frec, free);        // never blocked by the credit line
                        W($"  assert slot0 shape: card={free.Name} price={LoanService.ShopPriceFor(frec, free)}(exp 0) notRestructuring={free != typeof(RestructuringCard)} notSale={LoanService.SaleCardFor(frec) != free} -> {tF0}");
                        all &= tF0;

                        // ★가격/한도 불변식: 유료는 한 방문에 1장, 할인 카드를 집으면 2장. Checked against the LIVE
                        // prices this visit rolled, so a later tweak to the band / sale depth / credit line that
                        // breaks the intent fails here instead of in someone's run.
                        var saleT = LoanService.SaleCardFor(frec);
                        int salePrice = saleT != null ? LoanService.ShopPriceFor(frec, saleT) : -1;
                        // ★The "two at full price" check must EXCLUDE the sale card — that discounted offer is exactly
                        // the thing that is supposed to let a second card through, so counting it here would assert
                        // the opposite of the design.
                        var fullPrices = new List<int>();
                        for (int i = 1; i < offers.Length; i++)
                            if (offers[i] != saleT) fullPrices.Add(LoanService.ShopPriceFor(frec, offers[i]));
                        fullPrices.Sort();
                        int cheapestNonSale = fullPrices.Count > 0 ? fullPrices[0] : -1;
                        int lim = DebtLoanConfig.ShopCreditLimit;
                        bool twoFullPriceBlocked = fullPrices.Count < 2 || fullPrices[0] + fullPrices[1] > lim;
                        bool saleLetsYouBuyTwo = salePrice < 0 || cheapestNonSale < 0 || salePrice + cheapestNonSale <= lim;
                        int upgMax = 0;
                        var upgT = LoanService.UpgradedCardFor(frec);
                        if (upgT != null && !LoanService.IsFreeOffer(frec, upgT)) upgMax = LoanService.ShopPriceFor(frec, upgT);
                        bool upgReachable = upgMax <= lim;
                        bool tFP = twoFullPriceBlocked && saleLetsYouBuyTwo && upgReachable;
                        W($"  assert 가격/한도 불변식 (limit {lim}): 정가={string.Join(",", fullPrices)} 세일={salePrice} | 정가2장={(fullPrices.Count >= 2 ? fullPrices[0] + fullPrices[1] : -1)}(must be >{lim}) 세일+최저정가={(salePrice >= 0 && cheapestNonSale >= 0 ? salePrice + cheapestNonSale : -1)}(must be <={lim}) 강화판={upgMax}(<={lim}) -> {tFP}");
                        all &= tFP;

                        int fp0 = frec.Principal, fc0 = frec.CardDebt, fs0 = frec.ShopSpentThisVisit;
                        int nd0 = player.Deck.Cards.Count(c => c is MegaCrit.Sts2.Core.Models.Cards.Debt);
                        int fd0 = player.Deck.Cards.Count;
                        await LoanService.BuyCardOnDebt(player, free);
                        await Task.Delay(600);
                        var f2 = LoanService.For(player)!;
                        int nd1 = player.Deck.Cards.Count(c => c is MegaCrit.Sts2.Core.Models.Cards.Debt);
                        bool gotCard = player.Deck.Cards.Count > fd0;
                        bool tF1 = f2.Principal == fp0 && f2.CardDebt == fc0 && f2.ShopSpentThisVisit == fs0
                                   && nd1 == nd0 && gotCard;
                        W($"  assert FREE take: owed {fp0}→{f2.Principal} cardDebt {fc0}→{f2.CardDebt} visitSpend {fs0}→{f2.ShopSpentThisVisit} nativeDebt {nd0}→{nd1} cardAdded={gotCard} -> {tF1}");
                        all &= tF1;

                        // …and the PAID slot right after still charges debt AND drops the native Debt curse (the
                        // free take must not have consumed the per-visit curse stamp).
                        var paid = offers[1];
                        int price = LoanService.ShopPriceFor(f2, paid);
                        int pp0 = f2.Principal;
                        await LoanService.BuyCardOnDebt(player, paid);
                        await Task.Delay(600);
                        var f3 = LoanService.For(player)!;
                        int nd2 = player.Deck.Cards.Count(c => c is MegaCrit.Sts2.Core.Models.Cards.Debt);
                        bool tF2 = f3.Principal == pp0 + price && f3.ShopSpentThisVisit == price && nd2 == nd1 + 1;
                        W($"  assert PAID buy after free: card={paid.Name} price={price} owed {pp0}→{f3.Principal} visitSpend={f3.ShopSpentThisVisit} nativeDebt {nd1}→{nd2}(exp +1) -> {tF2}");
                        all &= tF2;
                    }
                    else W($"  무료 슬롯: only {offers.Length} offer(s) revealed — skipped");
                }
                else W("  무료 슬롯: no loan record — skipped");
            }
            catch (Exception e) { W("  무료 슬롯 section failed: " + e); all = false; }

            // ── 대출 인출 횟수 제한 (MaxLoanDraws) ─────────────────────────────────────────────────────────
            // 금액 상한만 있던 시절엔 한 상점을 소액 대출로 쓸어담을 수 있었다. 이제 인출 "횟수"가 희소 자원.
            Step("대출 인출 횟수 제한 (3회)");
            try
            {
                int savedMax = DebtLoanConfig.MaxLoan;
                DebtLoanConfig.MaxLoan = 0;                    // 기본값 = 금액 상한 없음 (횟수가 유일한 제약)
                LoanService.ResetFor(player);                  // 완납 후처럼 깨끗한 상태에서 시작
                int free0 = LoanService.DrawsLeftFor(player);  // 대출 전 = 만땅
                await LoanService.GrantLoanDirect(player, 50); await Task.Delay(250);
                int d1 = LoanService.DrawsLeftFor(player);
                await LoanService.GrantLoanDirect(player, 50); await Task.Delay(250);
                int d2 = LoanService.DrawsLeftFor(player);
                await LoanService.GrantLoanDirect(player, 50); await Task.Delay(250);
                int d3 = LoanService.DrawsLeftFor(player);
                var drec = LoanService.For(player)!;
                bool tD1 = free0 == 3 && d1 == 2 && d2 == 1 && d3 == 0 && drec.LoanDraws == 3;
                W($"  assert draws countdown: before={free0}(3) →{d1}(2) →{d2}(1) →{d3}(0) rec.LoanDraws={drec.LoanDraws}(3) -> {tD1}");
                all &= tD1;

                // 소진되면 CanLoanCover가 막아야 한다 — 금액 여유가 충분해도.
                int roomLeft = LoanService.RemainingRoom(player);
                bool tD2 = roomLeft == int.MaxValue && LoanService.DrawsLeft(drec) == 0;
                W($"  assert gate: remainingRoom={(roomLeft == int.MaxValue ? "무제한" : roomLeft.ToString())} (금액 상한 없음) drawsLeft=0 → 대출 차단 -> {tD2}");
                all &= tD2;

                // ★금액 상한 없음: 3회로 유물 3개(275×3=825)를 이론상 빌릴 수 있어야 한다.
                LoanService.ResetFor(player);
                for (int i = 0; i < 3; i++) { await LoanService.GrantLoanDirect(player, 275); await Task.Delay(200); }
                var brec = LoanService.For(player)!;
                bool tD5 = brec.Borrowed == 825 && LoanService.DrawsLeft(brec) == 0;
                W($"  assert no gold cap: borrowed={brec.Borrowed}(825 = 275×3) drawsLeft=0 -> {tD5}");
                all &= tD5;
                LoanService.ResetFor(player);
                await LoanService.GrantLoanDirect(player, 50); await Task.Delay(200);
                drec = LoanService.For(player)!;

                // 영속화: 유물에 실려 리로드를 견뎌야 한다(안 그러면 재접속으로 인출 횟수가 부활).
                drec.LoanDraws = 3;                            // 영속화 확인용으로 소진 상태를 만든다
                LoanService.SyncToRelic(player);
                int onRelic = LoanService.LedgerRelicOf(player)?.LoanDraws ?? -1;
                drec.LoanDraws = 0;                            // 레코드만 지우고 유물에서 복원
                LoanService.RestoreFromRelic(player);
                int restored = LoanService.For(player)?.LoanDraws ?? -1;
                bool tD3 = onRelic == 3 && restored == 3;
                W($"  assert persistence: relic.LoanDraws={onRelic}(3) restored={restored}(3) -> {tD3}");
                all &= tD3;

                // 완납하면 다시 3회 — 신용 회복의 의미.
                if ((int)player.Gold < LoanService.For(player)!.Principal)
                    await PlayerCmd.GainGold(LoanService.For(player)!.Principal - (int)player.Gold, player, false);
                await LoanService.Repay(player); await Task.Delay(300);
                int drawsAfterRepay = LoanService.DrawsLeftFor(player);
                bool tD4 = drawsAfterRepay == 3;
                W($"  assert repay resets draws: drawsLeft={drawsAfterRepay}(3, 완납=신용 회복) -> {tD4}");
                all &= tD4;
                DebtLoanConfig.MaxLoan = savedMax;
            }
            catch (Exception e) { W("  대출 인출 횟수 section failed: " + e); all = false; }

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
