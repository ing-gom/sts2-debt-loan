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
                var type = offers[0];
                int price = LoanService.ShopPriceFor(rec, type);
                int before = rec.Principal;
                W($"HOST: buying {type.Name} for {price} (owed {before} → expect {before + price})");
                await LoanService.BuyCardOnDebt(me, type);
                await Task.Delay(5000);      // let dl_sync buy replay on BOTH peers
                W($"HOST: after buy, owed={LedgerPrincipalOf(me)} purchased={PurchasedCount(me)} deckHasBought={DeckHasAnyPurchased(me)}");
            }
            else W("HOST: no debt-shop offers to buy (skipping purchase step)");
            await Shot("03_bought");

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
            W($"HOST: FINAL owed={LedgerPrincipalOf(host)} purchased={PurchasedCount(host)} deckHasBought={DeckHasAnyPurchased(host)} joinBailoutMax={joinBailoutMax}");
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
            for (int i = 0; i < 90 && !File.Exists(hostTxt); i++)
            {
                Step($"JOIN waiting for host (t+{i * 2}s)");
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

        static string Of(Player h) => $"owed(host)={LedgerPrincipalOf(h)} purchased(host)={PurchasedCount(h)} deckHasBought={DeckHasAnyPurchased(h)}";
    }

    /// <summary>Principal recorded on a player's Ledger relic, or -1 if they don't carry one. Both peers
    /// read the HOST player through this; equal non-(-1) values = the relic + its SavedProperty replicated.</summary>
    private static int LedgerPrincipalOf(Player p)
    {
        var relic = LoanService.LedgerRelicOf(p);
        return relic?.Principal ?? -1;
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

#if DEBTLOAN_SELFTEST
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
#endif
