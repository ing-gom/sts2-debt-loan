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
            var character = ModelDb.AllCharacters.First();
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
            DebtLoanConfig.InterestCapMultiplier = 2.0;
            DebtLoanConfig.AllowLoansOutsideAct1 = true;

            bool all = true;

            // 1) Loan grant → relic + principal.
            Step("loan grant");
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 100);
            await Task.Delay(300);
            var rec = LoanService.For(player);
            bool t1 = LoanService.PlayerHasLedger(player) && rec != null && rec.Principal == 100 && rec.Active;
            W($"  assert loan: ledger={LoanService.PlayerHasLedger(player)} principal={rec?.Principal} active={rec?.Active} -> {t1}");
            all &= t1;

            // 2) Room escalation at 14/17/20 → 1/3/5 Debt cards.
            Step("room escalation");
            for (int i = 0; i < 13; i++) await LoanService.OnRoomEntered(player);
            int c13 = LoanService.For(player)!.DebtCards.Count;
            await LoanService.OnRoomEntered(player);                       // 14th
            int c14 = LoanService.For(player)!.DebtCards.Count;
            for (int i = 0; i < 3; i++) await LoanService.OnRoomEntered(player); // 17th
            int c17 = LoanService.For(player)!.DebtCards.Count;
            for (int i = 0; i < 3; i++) await LoanService.OnRoomEntered(player); // 20th
            int c20 = LoanService.For(player)!.DebtCards.Count;
            bool t2 = c13 == 0 && c14 == 1 && c17 == 3 && c20 == 5;
            W($"  assert escalation: r13={c13}(0) r14={c14}(1) r17={c17}(3) r20={c20}(5) -> {t2}");
            all &= t2;

            // 2b) Persistence: state (principal 100, rooms 20, 5 cards, active) must survive a
            // save→load round-trip via the relic's [SavedProperty] fields + deck rescan.
            Step("save/load persistence");
            var save = RunManager.Instance.ToSave(null);
            var reloaded = RunState.FromSerializable(save);
            var rp = reloaded.Players.First();
            var rrelic = LoanService.LedgerRelicOf(rp);
            bool tP = rrelic != null && rrelic.Principal == 100 && rrelic.RoomsSinceLoan == 20 && rrelic.Active;
            LoanService.RestoreFromRelic(rp);
            var rrec = LoanService.For(rp);
            bool tR = rrec != null && rrec.Principal == 100 && rrec.RoomsSinceLoan == 20 && rrec.Active && rrec.DebtCards.Count == 5;
            W($"  assert persist: relic P={rrelic?.Principal} rooms={rrelic?.RoomsSinceLoan} active={rrelic?.Active} -> {tP}; " +
              $"restore rec P={rrec?.Principal} cards={rrec?.DebtCards.Count} -> {tR}");
            all &= tP && tR;

            // 3) Repay principal → retire + clear cards.
            Step("repay");
            if ((int)player.Gold < 100) await PlayerCmd.GainGold(100 - (int)player.Gold, player, false);
            await Task.Delay(200);
            bool repaid = await LoanService.Repay(player);
            await Task.Delay(200);
            var recR = LoanService.For(player);
            bool t3 = repaid && recR != null && !recR.Active && recR.DebtCards.Count == 0;
            W($"  assert repay: repaid={repaid} active={recR?.Active} cards={recR?.DebtCards.Count} -> {t3}");
            all &= t3;

            // 4) Interest hits 200% of principal → retire + clear cards.
            Step("interest cap");
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 50);                 // principal 50, cap 100
            await Task.Delay(200);
            for (int i = 0; i < 20; i++) await LoanService.OnRoomEntered(player);   // 5 cards
            int beforeCards = LoanService.For(player)!.DebtCards.Count;
            for (int i = 0; i < 10; i++) await LoanService.AccrueInterest(player, 10); // 100 >= cap 100
            await Task.Delay(200);
            var recC = LoanService.For(player);
            bool t4 = beforeCards == 5 && recC != null && !recC.Active && recC.DebtCards.Count == 0
                      && recC.InterestPaid >= recC.InterestCap;
            W($"  assert cap: beforeCards={beforeCards}(5) active={recC?.Active} cards={recC?.DebtCards.Count} interest={recC?.InterestPaid}/{recC?.InterestCap} -> {t4}");
            all &= t4;

            // 5) Shop loan purchase: buy an unaffordable relic → the OnTryPurchaseWrapper prefix credits a
            // loan, grants the Ledger (already owned here, so no dup), and re-runs the buy.
            Step("shop loan purchase");
            LoanService.ResetFor(player);
            DebtLoanConfig.MaxLoan = 9999;   // ensure any shop-relic cost is coverable in the test
            var inv = MerchantInventory.CreateForNormalMerchant(player);
            var entry = new MerchantRelicEntry(RelicRarity.Shop, player);
            int cost = entry.Cost;
            string? boughtRelicId = entry.Model?.Id.Entry;   // capture BEFORE buy — entry nulls Model after purchase
            if ((int)player.Gold > 0) await PlayerCmd.LoseGold((int)player.Gold, player, GoldLossType.Spent);
            bool bought = await entry.OnTryPurchaseWrapper(inv, false);
            await Task.Delay(200);
            var srec = LoanService.For(player);
            bool boughtRelic = boughtRelicId != null && player.Relics.Any(r => r.Id.Entry == boughtRelicId);
            bool t5 = bought && srec != null && srec.Active && srec.Principal == cost
                      && LoanService.PlayerHasLedger(player) && boughtRelic;
            W($"  assert shop-loan: cost={cost} bought={bought} principal={srec?.Principal} active={srec?.Active} boughtRelic={boughtRelic} -> {t5}");
            all &= t5;

            // 6) Repay button attaches to the real shop node (EnterRoomDebug builds NMerchantInventory),
            //    and green price tags appear on loan-coverable items. Set a realistic 300 cap + middling
            //    gold so the shop shows a mix: affordable (cream) / loanable (green) / too dear (red).
            Step("shop repay button + green tags");
            bool t6 = false;
            if (Engine.GetMainLoop() is SceneTree stree)
            {
                DebtLoanConfig.MaxLoan = 300;
                int want = 150;
                if ((int)player.Gold < want) await PlayerCmd.GainGold(want - (int)player.Gold, player, false);
                await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
                await Task.Delay(4000);
                var shopNode = FindNode<NMerchantInventory>(stree.Root);
                var repayBtn = FindNode<NMerchantRepayButton>(stree.Root);
                try { shopNode?.Open(); } catch { /* open is only to surface the button for the shot */ }
                await Task.Delay(500);
                t6 = shopNode != null && repayBtn != null;
                W($"  assert repay-button: shopNode={(shopNode != null)} attached={(repayBtn != null)} -> {t6}");
                await Shot("3_shop");
            }
            else W("  no scene tree for shop test");
            all &= t6;

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
