// LOCAL TEST ONLY — dormant unless `selftest.coop.flag` is next to the mod DLL, and only compiled when
// DEBTLOAN_SELFTEST is defined. Drives the co-op lobby, takes a loan on the HOST's local player through
// the mod's real path (GrantLoanDirect → the networked dl_sync command), then both peers record what they
// see: the host's Ledger relic principal, the run-wide Debt total, and how many Debt cards were injected
// into THIS peer's own combat. Convergence = both peers agree AND the client (who borrowed nothing) still
// got a Debt card (the "shared debt" contagion), with no session drop across a room boundary. See the
// coop-verify skill.
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

    private static void Poll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || _done) return;
        try { Tick(tree); } catch (Exception e) { W("tick exception: " + e.Message); }
        if (!_done) tree.CreateTimer(2.0).Timeout += Poll;
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

    // ── The DebtLoan scenario ────────────────────────────────────────────────────────────────────────
    // HOST borrows on its local player. That fires GrantLoanDirect → the networked dl_sync command, which
    // must (a) grant the Ledger relic on BOTH peers with the same principal, and (b) make RunWideDebtTotal
    // = 1 on both. Then a combat: the run-wide Debt total injects into EVERY player's draw pile, so the
    // JOIN player — who borrowed nothing — must still receive a Debt card (the shared-debt contagion). A
    // room jump out of combat forces the replica checksum; a divergent relic principal or debt count drops
    // the JOIN there.

    private static async Task HostPhase(RunManager run)
    {
        try
        {
            await Task.Delay(2000);
            await Shot("01_run");   // ★mandatory visual evidence: this instance really entered the co-op run
            var me = LocalPlayerOf(run);
            if (me == null) { W("HOST: no local player"); Flush(false); return; }

            DebtLoanConfig.MaxLoan = 9999;   // don't let the cap block the test loan

            // 1) Take a loan on the host's local player → dl_sync replicates the Ledger + record to BOTH peers.
            Step("HOST take loan");
            await LoanService.GrantLoanDirect(me, 100);
            await Task.Delay(5000);          // let dl_sync replicate to the client
            var host = run.State!.Players.OrderBy(p => p.NetId).First();
            W($"HOST: after loan, ledgerPrincipal(host)={LedgerPrincipalOf(host)} debtTotal={LoanService.RunWideDebtTotal(run.State)}");
            await Shot("02_loan");

            // 2) Enter combat → the run-wide Debt total injects into every player's draw pile (contagion).
            Step("HOST enter combat");
            run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "room monster", inCombat: false));
            await Task.Delay(9000);
            int myDebt = 0;
            var cm = CombatManager.Instance;
            if (cm != null && cm.IsInProgress)
            {
                myDebt = CombatDebtCards(me);
                W($"HOST: in combat, my Debt cards = {myDebt}, debtTotal={LoanService.RunWideDebtTotal(run.State)}");
                await Shot("03_combat");
                for (int t = 0; t < 2; t++)
                {
                    Step($"HOST combat turn {t + 1}");
                    cm.SetReadyToEndTurn(me, canBackOut: false);
                    await Task.Delay(8000);
                    myDebt = Math.Max(myDebt, CombatDebtCards(me));   // catch cards that reach hand mid-fight
                }
                // Exit combat → checksum boundary (a divergent relic/debt count drops the JOIN here).
                run.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, "room rest", inCombat: true));
                await Task.Delay(8000);
            }
            else W("HOST: combat did not start (room monster jump failed)");

            if (run.State == null || run.State.Players.Count == 0) { W("HOST: SESSION DROPPED"); Flush(false); return; }
            var host2 = run.State.Players.OrderBy(p => p.NetId).First();
            W($"HOST: FINAL ledgerPrincipal={LedgerPrincipalOf(host2)} debtTotal={LoanService.RunWideDebtTotal(run.State)} myCombatDebt={myDebt}");
            await Shot("04_final");
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
            // host's result FILE is a reliable done-signal). While waiting, auto-ready in combat (so the
            // host's fight can progress) and record the MAX Debt cards that land in MY OWN combat piles —
            // the client borrowed nothing, so any Debt card here proves the shared-debt contagion.
            string hostTxt = Path.Combine(ModDir(), "selftest.coop.host.txt");
            int myDebtMax = 0;
            string lastLine = "";
            for (int i = 0; i < 90 && !File.Exists(hostTxt); i++)
            {
                Step($"JOIN waiting for host (t+{i * 2}s)");
                try
                {
                    var cm = CombatManager.Instance;
                    var meJ = LocalPlayerOf(run);
                    if (cm?.IsInProgress == true && meJ != null)
                    {
                        cm.SetReadyToEndTurn(meJ, canBackOut: false);
                        myDebtMax = Math.Max(myDebtMax, CombatDebtCards(meJ));
                    }
                }
                catch { /* combat participation is best-effort */ }

                string line = (run.State != null && run.State.Players.Count > 0)
                    ? $"ledgerPrincipal(host)={LedgerPrincipalOf(run.State.Players.OrderBy(p => p.NetId).First())} " +
                      $"debtTotal={LoanService.RunWideDebtTotal(run.State)} myCombatDebt={myDebtMax}"
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
            W($"JOIN: FINAL ledgerPrincipal={LedgerPrincipalOf(host)} debtTotal={LoanService.RunWideDebtTotal(run.State)} myCombatDebt={myDebtMax}");
            await Shot("02_final");          // ★mandatory: what the client actually SEES after replication
            W("=== coop join done ===");
            Flush(true);
        }
        catch (Exception e) { W("JOIN exception: " + e); Flush(false); }
    }

    /// <summary>Principal recorded on a player's Ledger relic, or -1 if they don't carry one. Both peers
    /// read the HOST player through this; equal non-(-1) values = the relic + its SavedProperty replicated.</summary>
    private static int LedgerPrincipalOf(Player p)
    {
        var relic = LoanService.LedgerRelicOf(p);
        return relic?.Principal ?? -1;
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

    #region selection automation (auto-selector + screen pump)
    // Identical to Sts2RelicForge's CoopTest: every CardSelectCmd path is `if (Selector != null) auto-pick;
    // else await screen.CardsSelected()`, and an unanswered prompt on ONE co-op peer parks the OTHER in
    // WaitForRemoteChoice → both result files vanish. The selector + pump close that hole. The pick MUST be
    // deterministic (first N) because the co-op selector path skips SyncLocalChoice. NEVER TestMode.IsOn
    // (it disables ChecksumTracker, the very detector this test relies on). See the coop-verify skill.

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
