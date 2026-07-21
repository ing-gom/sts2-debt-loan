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

            DebtLoanConfig.MaxLoan = 9999;   // let any test loan through the cap
            var mkEntry = new Func<MerchantRelicEntry>(() => new MerchantRelicEntry(RelicRarity.Shop, player));

            // A) Loan grant → relic + immediate 1 Debt card (rooms 0).
            Step("loan grant");
            LoanService.ResetFor(player);
            await LoanService.GrantLoanDirect(player, 100);
            await Task.Delay(300);
            var rec = LoanService.For(player);
            bool tA = LoanService.PlayerHasLedger(player) && rec != null && rec.Principal == 100 && rec.Active
                      && LoanService.DebtCardCountFor(player) == 1;
            W($"  assert loan: ledger={LoanService.PlayerHasLedger(player)} P={rec?.Principal} active={rec?.Active} cards@0={LoanService.DebtCardCountFor(player)} -> {tA}");
            all &= tA;

            // B) Debt-card count schedule: 1 / 2 / 3 at rooms 0 / 10 / 20, capped at 3. Rooms are now COMPUTED
            //    as TotalFloor − LoanFloor, so we simulate room progress by back-dating LoanFloor.
            Step("debt-card count schedule");
            int baseFloor = player.RunState.TotalFloor;
            var recB = LoanService.For(player)!;
            recB.LoanFloor = baseFloor;        int cnt0  = LoanService.DebtCardCountFor(player);   // rooms 0
            recB.LoanFloor = baseFloor - 10;   int cnt10 = LoanService.DebtCardCountFor(player);   // rooms 10
            recB.LoanFloor = baseFloor - 20;   int cnt20 = LoanService.DebtCardCountFor(player);   // rooms 20
            recB.LoanFloor = baseFloor - 30;   int cnt30 = LoanService.DebtCardCountFor(player);   // rooms 30 (cap)
            LoanService.SyncToRelic(player);         // persist LoanFloor=baseFloor-30 onto the relic for the C round-trip
            bool tB = cnt0 == 1 && cnt10 == 2 && cnt20 == 3 && cnt30 == 3;
            W($"  assert count: r0={cnt0}(1) r10={cnt10}(2) r20={cnt20}(3) r30={cnt30}(3 max) -> {tB}");
            all &= tB;
            try { W($"  ledger hover: {new MegaCrit.Sts2.Core.Localization.LocString("relics", "DEBT_LOAN_RELIC.description").GetFormattedText()}"); }
            catch (Exception e) { W("  hover read failed: " + e.Message); }

            // C) Persistence round-trip (numeric state on the relic).
            Step("save/load persistence");
            var save = RunManager.Instance.ToSave(null);
            var reloaded = RunState.FromSerializable(save);
            var rp = reloaded.Players.First();
            var rrelic = LoanService.LedgerRelicOf(rp);
            bool tC = rrelic != null && rrelic.Principal == 100 && rrelic.LoanFloor == baseFloor - 30 && rrelic.Active;
            LoanService.RestoreFromRelic(rp);
            var rrec = LoanService.For(rp);
            // rooms-since-loan (30 → 3 cards) is re-derived from the restored LoanFloor, not stored.
            bool tC2 = rrec != null && rrec.Principal == 100 && rrec.LoanFloor == baseFloor - 30 && rrec.Active
                       && LoanService.DebtCardCountFor(rp) == 3;
            W($"  assert persist: relic P={rrelic?.Principal} loanFloor={rrelic?.LoanFloor} -> {tC}; restore P={rrec?.Principal} cards={LoanService.DebtCardCountFor(rp)}(3) -> {tC2}");
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

            // H) Default (200%) → frozen: relic disabled (kept), no re-borrow the rest of the run.
            Step("default → frozen");
            LoanService.ResetFor(player);
            await DebtLoanGrants.RemoveRelic(player);          // clear the shop-test loan's relic for a clean default
            await Task.Delay(150);
            await LoanService.GrantLoanDirect(player, 50);     // cap 100
            await Task.Delay(150);
            for (int i = 0; i < 10; i++) await LoanService.AccrueInterest(player, 10);   // 100 >= cap
            await Task.Delay(200);
            var rh = LoanService.For(player);
            var hRelic = LoanService.LedgerRelicOf(player);
            if ((int)player.Gold > 0) await PlayerCmd.LoseGold((int)player.Gold, player, GoldLossType.Spent);
            bool lockedOut = !LoanService.CanLoanCover(mkEntry(), player);
            bool tH = rh != null && !rh.Active && rh.Defaulted && hRelic != null && lockedOut;
            W($"  assert default: active={rh?.Active} defaulted={rh?.Defaulted} relicKept={(hRelic != null)} status={hRelic?.Status} lockedOut={lockedOut} -> {tH}");
            all &= tH;

            // I) Combat-start injection: a fresh loan (count 1) → entering a Monster room fires
            //    BeforeCombatStart, which must put the Debt card(s) into the combat piles.
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
                await Task.Delay(4000);                            // combat setup + BeforeCombatStart
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
