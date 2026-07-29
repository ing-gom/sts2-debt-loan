using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;      // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Gold;       // GoldLossType
using MegaCrit.Sts2.Core.Entities.Merchant;   // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;              // TaskHelper
using MegaCrit.Sts2.Core.GameActions.Multiplayer;   // PlayerChoiceContext
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;               // RoomType
using MegaCrit.Sts2.Core.Runs;

namespace Sts2DebtLoan;

/// <summary>One player's outstanding loan for the run. The Debt cards are NOT stored here — they are
/// injected fresh into each combat's draw pile before the opening hand (see
/// <see cref="BeforeHandDrawInjectPatch"/>), so this only tracks the numeric state, which is persisted
/// onto the relic as [SavedProperty] fields.</summary>
internal sealed class LoanRecord
{
    /// <summary>Total gold ever borrowed this run (fixed once taken; rises on a top-up). Shown in the
    /// hover as "borrowed X" — does NOT shrink as the loan amortizes (that's <see cref="Principal"/>).</summary>
    internal int Borrowed;

    /// <summary>Gold still owed = the shop repay cost + the relic's badge. Starts equal to
    /// <see cref="Borrowed"/> and shrinks as each Debt-card payment retires a share of it (amortization).</summary>
    internal int Principal;

    /// <summary>Cumulative gold the Debt cards have drained = total paid so far (interest + amortized principal).</summary>
    internal int TotalPaid;

    /// <summary>How many rooms of node-interest have already been baked into <see cref="Principal"/> (0..
    /// MaxNodeInterestRooms). Tracked so room-entry accrual is idempotent and survives save/load.</summary>
    /// <summary>Total node-interest percent (of Borrowed) already baked into Principal, so it isn't re-charged on
    /// reload or re-fire. Replaces the old rooms counter now that the per-room rate scales with borrower count.</summary>
    internal int InterestPctApplied;

    /// <summary>How much of the charged interest has been paid off so far. Payments retire INTEREST FIRST; once
    /// InterestPaid reaches the total interest charged (origination + node), further payments eat into the borrowed
    /// principal. Drives the "remaining interest vs remaining principal" split on the ledger hover. Persisted.</summary>
    internal int InterestPaid;

    /// <summary>Card/shop debt taken on credit (debt-shop buys + card fees). Counts as PRINCIPAL and joins the node
    /// interest base (Borrowed + CardDebt), but does NOT get the origination fee (that's loans only). Persisted.</summary>
    internal int CardDebt;

    /// <summary>Absolute node interest accrued so far, in gold (base = Borrowed + CardDebt, capped at the node % cap
    /// AND the absolute <see cref="DebtLoanConfig.InterestGoldCap"/>). Tracked as gold (not a %) so a growing base
    /// from card debt doesn't retroactively rescale it. Persisted.</summary>
    internal int NodeInterestGold;

    /// <summary>TotalFloor of the shop where the loan was taken. Top-ups are allowed only at THAT shop.
    /// Rooms-since-loan (which drives the Debt-card count) is computed as TotalFloor − LoanFloor.</summary>
    internal int LoanFloor = -1;

    /// <summary>False once settled (repaid in full).</summary>
    internal bool Active = true;

    internal bool RelicGranted;

    /// <summary>Whether the 정기 납부 (Standing Order) leverage card has been handed to the deck this loan (once,
    /// on the first visit to a shop other than the loan shop). Persisted on the relic so a reload keeps it.</summary>
    internal bool DunningLetterGranted;

    /// <summary>How many times gold has actually been DRAWN on this loan (the first borrow + every top-up).
    /// Capped by <see cref="DebtLoanConfig.MaxLoanDraws"/>: the gold cap alone let a debtor nibble a shop clean in
    /// unlimited small loans, so the draws themselves are the scarce thing now — 300 gold split across 3 decisions.
    /// Counts MERCHANT-ITEM loans only; debt-shop card buys have their own per-visit credit line. Dies with the
    /// record on full repay, so clearing the debt restores a fresh set of draws (that's what 신용 회복 means).
    /// Persisted on the relic.</summary>
    internal int LoanDraws;

    /// <summary>Whether this loan's ONE 채무 조정 (Restructuring) write-off has been spent. Once true the card can no
    /// longer be played and the debt shop stops stocking it — without that gate it would be re-buyable at every shop
    /// (the sold-set clears per visit) for far less debt than it forgives, i.e. an infinite principal deleter. Dies
    /// with the record on full repay, so a LATER loan is a new agreement and gets its own. Persisted on the relic.</summary>
    internal bool RestructuringUsed;

    /// <summary>지금까지 받은 보상 단계의 <b>개수</b>(= 다음으로 받을 단계의 인덱스).
    /// <para>★수령이 <b>순차</b>이므로 받은 집합은 항상 접두사다 → 개수 하나로 완전히 표현된다.
    /// 비트마스크를 쓰다가 되돌렸다 — 보너스 단계가 <b>무한</b>이라 32비트로는 애초에 못 담는다.</para>
    /// 누적 상환액은 청산해도 리셋되지 않으므로 이 값이 없으면 청산할 때마다 같은 보상을 다시 받는다. 유물에 영속화.</summary>
    internal int CreditRewardsTaken;

    /// <summary>How many of the SHOP power cards have been handed out (one per shop-revisit). Drives the fixed
    /// order — 1st = 정기 납부, the rest a per-run shuffle of the power cards. Persisted.</summary>
    internal int EventGrantCount;

    /// <summary>Total 납부 (Payment) made while this loan was active (a run-wide stat; kept for reference/telemetry).
    /// Persisted.</summary>
    internal int LifetimePayments;

    /// <summary>How many DISTINCT shops (other than the loan shop) the debtor has visited this loan — the debt shop
    /// re-rolls its rotating offer selection on each new visit (see RevealedPurchasable). Persisted.</summary>
    internal int DebtShopVisits;

    /// <summary>The last TotalFloor at which <see cref="DebtShopVisits"/> was incremented — guards against
    /// double-counting the same shop on re-entry/reload. Persisted.</summary>
    internal int LastShopVisitFloor = -1;

    /// <summary>Gold of debt taken on CARD PURCHASES at the debt shop THIS visit. Gated against
    /// <see cref="DebtLoanConfig.ShopCreditLimit"/> so you can't sweep the whole offer; resets when you enter a new
    /// shop (see the visit-tracking in NoteShopVisit). Persisted so a reload mid-shop keeps the spent total.</summary>
    internal int ShopSpentThisVisit;

    /// <summary>이번 상점 방문에서 <b>빚으로 카드를 이미 제거했는가</b>. ★예산(외상 한도)만으로 제약하면
    /// 한도 슬라이더를 올린 플레이어는 한 방문에 3장까지 지울 수 있어서, "상인 제거 1회에 <b>더해</b> 한 번"
    /// 이라는 설계가 무너진다 → 방문당 하드 1회로 고정한다. 유물에 영속화(리로드로 되살아나면 안 된다).</summary>
    internal bool PurgedThisVisit;

    /// <summary>신용도로 인정된 상환액. <see cref="TotalPaid"/>(정직한 총 상환액)와 <b>일부러 다르다</b> —
    /// 목돈 상환은 <see cref="DebtLoanConfig.LumpSumCreditCap"/> 까지만 신용도로 쳐주고, 그 위로는 <b>납부로만</b>
    /// 오른다.
    /// <para>★이유: 인출 3회 + 금액 상한 없음이라 유물 3개를 한 번에 지르면 갚을 돈이 900을 넘고, 그 한 번의
    /// 청산으로 고정 사다리를 통째로 건너뛴다(실측: 825 빌리면 이자 포함 925 → 신용도 9). 사이클을 도는 쪽이
    /// 손해가 되어 "청산은 사이클의 마디"라는 설계와 정반대가 됐다. 초반 목돈은 인정하되(6까지) 그 뒤는 빚을
    /// 안고 굴린 시간 — 즉 납부 — 만 신용이 되게 한다.</para></summary>
    internal int CreditPaid;

    /// <summary>강제 청산(강제 징수·가압류)으로 계약이 닫혔는데 아직 뒷정리를 못 한 상태.
    /// <see cref="LoanService.ForceRepayPrincipal"/> 이 <b>동기</b> 메서드라 그 자리에서 await 를 못 하기 때문.</summary>
    internal bool PendingSettleCleanup;

    /// <summary>Type-names of the cards BOUGHT on debt at the shop this loan (so they drop out of the offer pool and
    /// show sold if still displayed). Persisted as a CSV on the relic. Cleared on repay/reset.</summary>
    internal readonly HashSet<string> PurchasedCards = new();

    /// <summary>Transient (not persisted) cache of the debt shop's current rotating offer selection + which
    /// DebtShopVisits it was rolled for, so the shown cards stay STABLE while you shop and re-roll only on a new
    /// visit. Rebuilt deterministically from (LoanFloor, DebtShopVisits) so it's fine that it resets on reload.</summary>
    internal int OfferVisit = -1;
    internal System.Type[]? CurrentOffers;

    /// <summary>PER-COMBAT transient: the 신용 불량 (Bad Credit) collection level 0..3. Reset to 0 at each
    /// combat start (by the injector) and ratcheted up by BadCreditCard every turn it sits in hand. Not
    /// persisted (it's a within-combat spiral, and it's deterministic from lockstep turn starts).</summary>
    internal int CollectionLevel;

    /// <summary>The last TotalFloor at which a debt-shop purchase dropped a native Debt curse into the deck — so
    /// the "every visit leaves a Debt" grant fires only ONCE per shop visit (per floor), no matter how many cards
    /// you buy that visit. Transient (a reload could re-grant on a re-buy in the same shop — a negligible edge);
    /// set identically on both co-op peers off the networked buy replay.</summary>
    internal int LastDebtGrantFloor = -1;
}

/// <summary>
/// The loan mechanic's brain. The Harmony patches are thin — they call into here. Gold mutations follow
/// the co-op host-authoritative pattern (LOCAL player + RewardSynchronizer); loans are single-player
/// gated for now (see <see cref="CanLoanCover"/>).
/// </summary>
internal static class LoanService
{
    private static readonly ConditionalWeakTable<Player, LoanRecord> Records = new();

    internal static LoanRecord? For(Player? player)
        => player != null && Records.TryGetValue(player, out var r) ? r : null;

    private static LoanRecord GetOrCreate(Player player)
        => Records.GetValue(player, _ => new LoanRecord());

    /// <summary>Debt cards from one player's loan = the schedule count for rooms-since-loan, COMPUTED as
    /// TotalFloor − LoanFloor. Deriving it from shared game state (not a stored counter) makes it identical
    /// on every co-op peer automatically — no per-room broadcast needed.</summary>
    internal static int DebtCardCountFor(Player? p)
    {
        var rec = For(p);
        if (rec == null || !rec.Active || rec.Principal <= 0 || p?.RunState == null) return 0;
        return DebtLoanConfig.TargetDebtCards(p.RunState.TotalFloor - rec.LoanFloor);   // 1 / 2 / 3 by rooms
    }

    /// <summary>Kept for the shop surcharge + relic tooltip: the highest per-combat curse-tier across all
    /// active loans (1/2/3). The injection itself composes per-loan (see <see cref="InjectAllDebtsForCombat"/>).</summary>
    internal static int RunWideDebtTotal(IRunState run)
    {
        if (run?.Players == null) return 0;
        int total = 0;
        foreach (var p in run.Players) total += DebtCardCountFor(p);
        return total;
    }

    /// <summary>Inject the DISTINCT Debt curse cards for EVERY active loan in the run into one player's draw
    /// pile — the run-wide contagion (a partner's loan seeps into your combat too; multiple loans stack).
    /// Each loan contributes an escalating SET by rooms-since-loan: 빚 독촉 (Dunning, upgraded to '+' once
    /// that loan is over the soft cap) always; +연체 (Delinquency) at 10 rooms; +차압 (Seizure) at 20. The
    /// cards are SHUFFLED into the draw pile (random positions) BEFORE the opening hand is dealt (this runs at
    /// BeforeHandDraw), so the normal draw pulls them in naturally from turn 1 — sometimes several land in the
    /// opening hand, sometimes they trickle in over the next turns, but they're never all forced onto turn 1.
    /// Temporary — gone at combat end.</summary>
    /// <summary>전투 시작에 섞어 넣을 <see cref="SeizedGoodsCard"/> 장수 — <b>남은 빚</b> 계단이다
    /// (250/500/750 → 1/2/3). 최소 대출(100)로는 0장: 압류품은 감수한 사람에게만 나온다.
    /// <para>★<see cref="LoanRecord.Borrowed"/>(이번 사이클에 빌린 총액)가 아니라 <b>Principal(지금 남은 빚)</b>
    /// 을 읽는다. Borrowed 로 하면 빌리고 곧바로 갚아도 압류품은 그대로라, 갚기 빌드가 빚 빌드의 보상까지
    /// 공짜로 챙기는 구멍이 생긴다. 카드 4종(담보·부도 위기·부실채권·레버리지)이 읽는 값과도 같아야
    /// 플레이어가 숫자 하나만 보면 된다.</para></summary>
    internal static int SeizedGoodsFor(Player? player)
    {
        int owed = PrincipalOf(player);
        if (owed >= 750) return 3;
        if (owed >= 500) return 2;
        if (owed >= 250) return 1;
        return 0;
    }

    internal static async Task InjectAllDebtsForCombat(Player injectee, IRunState run)
    {
        var combat = injectee?.Creature?.CombatState;
        if (combat == null || run?.Players == null) return;

        await ResetPaymentsThisCombat(injectee!);   // fresh 영수증 each combat (drives 정산/청구서 scaling)

        var cards = new List<CardModel>();
        foreach (var owner in run.Players)
        {
            var rec = For(owner);
            if (rec == null || !rec.Active || rec.Principal <= 0 || owner.RunState == null) continue;
            int tier = DebtLoanConfig.TargetDebtCards(owner.RunState.TotalFloor - rec.LoanFloor);   // 1/2/3

            // Tier 1 (rooms 0-12) injects NOTHING — a loan you're paying ON TIME isn't cursed. It only costs
            // interest (which accrues) + shop-price inflation; the 정기 납부 (Standing Order) power still feeds
            // 납부 cards to work it down. The penalty escalation only starts once you fall BEHIND:
            if (tier >= 4)
            {
                // Tier 4: 신용 불량 (Bad Credit) ALONE — it spawns a 강제 징수 (Forced Collection) every turn (the
                // escalating gold/HP drain that IS the tier-4 pressure), so nothing else is injected.
                rec.CollectionLevel = 0;   // fresh spiral each combat; BadCredit ratchets it up per turn
                var c = combat.CreateCard<BadCreditCard>(injectee); if (c != null) cards.Add(c);
            }
            else if (tier >= 2)
            {
                // Tier 2: 연체 (Delinquency, "you're late"). Tier 3: + 차압 (Seizure). Cumulative.
                var c = combat.CreateCard<DelinquencyCard>(injectee); if (c != null) cards.Add(c);
                if (tier >= 3) { var s = combat.CreateCard<SeizureCard>(injectee); if (s != null) cards.Add(s); }
            }
        }
        // ★압류품은 **자기 빚에만** 반응한다 — 위 저주 루프가 run.Players 전체를 도는 건 co-op 전염(동료의
        // 빚이 내 전투에도 스며든다)이라 그렇지만, 보상까지 전염시키면 빌리지도 않은 동료가 공짜 방어도를
        // 받는다. 그래서 injectee 본인의 기록만 본다.
        int seized = SeizedGoodsFor(injectee);
        for (int i = 0; i < seized; i++)
        {
            var g = combat.CreateCard<SeizedGoodsCard>(injectee);
            if (g != null) cards.Add(g);
        }

        if (cards.Count == 0) return;

        // Random positions → shuffled into the draw pile before the opening deal, so how many land in the
        // opening hand varies (not always all of them). The reveal shows them seeping into the pile. Random
        // uses the lockstep combat RNG → deterministic across co-op peers.
        var results = await CardPileCmd.AddGeneratedCardsToCombat(cards, PileType.Draw, injectee, CardPilePosition.Random);
        CardCmd.PreviewCardPileAdd(results);
        MainFile.Logger.Info($"[{MainFile.ModId}] shuffled {cards.Count} card(s) into the draw pile "
                             + $"({cards.Count - seized} debt curse, {seized} 압류품).");
    }

    /// <summary>True if the player already carries the Merchant's Ledger relic.</summary>
    internal static bool PlayerHasLedger(Player player)
    {
        try
        {
            string entry = ModelDb.GetId(typeof(DebtLoanRelic)).Entry;
            foreach (var r in player.Relics)
                if (r.Id.Entry == entry) return true;
        }
        catch { /* models not ready */ }
        return false;
    }

    /// <summary>Test-only (solo-verify): drop the run's loan record for a fresh scenario.</summary>
    internal static void ResetFor(Player player) => Records.Remove(player);

    internal static DebtLoanRelic? LedgerRelicOf(Player player)
    {
        if (player?.Relics == null) return null;
        foreach (var r in player.Relics)
            if (r is DebtLoanRelic dl) return dl;
        return null;
    }

    // ── Persistence (the relic carries [SavedProperty] fields; rebuilt on load) ─────────────────────

    internal static void SyncToRelic(Player player)
    {
        var rec = For(player);
        var relic = LedgerRelicOf(player);
        if (rec == null || relic == null) return;
        try
        {
            relic.Borrowed = rec.Borrowed;
            relic.Principal = rec.Principal;
            relic.TotalPaid = rec.TotalPaid;
            relic.InterestPctApplied = rec.InterestPctApplied;
            relic.InterestPaid = rec.InterestPaid;
            relic.CardDebt = rec.CardDebt;
            relic.NodeInterestGold = rec.NodeInterestGold;
            relic.LoanFloor = rec.LoanFloor;
            relic.Active = rec.Active;
            relic.DunningLetterGranted = rec.DunningLetterGranted;
            relic.RestructuringUsed = rec.RestructuringUsed;
            relic.LoanDraws = rec.LoanDraws;
            relic.CreditRewardsTaken = rec.CreditRewardsTaken;
            relic.EventGrantCount = rec.EventGrantCount;
            relic.LifetimePayments = rec.LifetimePayments;
            relic.DebtShopVisits = rec.DebtShopVisits;
            relic.LastShopVisitFloor = rec.LastShopVisitFloor;
            relic.ShopSpentThisVisit = rec.ShopSpentThisVisit;
            relic.PurgedThisVisit = rec.PurgedThisVisit;
            relic.CreditPaid = rec.CreditPaid;
            relic.PendingSettleCleanup = rec.PendingSettleCleanup;
            relic.PurchasedCardsCsv = string.Join(",", rec.PurchasedCards);
            relic.RefreshVars(DebtCardCountFor(player));   // borrowed/paid/cards into the relic's own DynamicVars (per-relic hover)
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic sync failed: {e.Message}"); }
    }

    // The hover text is no longer built here: it's a per-language STATIC template ("Borrowed {borrowed} …
    // Paid {paid} …") injected once by LocInjectionPatch, with the numbers filled from each relic's OWN
    // DynamicVars (see DebtLoanRelic.CanonicalVars / RefreshVars). That makes the hover per-relic, which is
    // co-op-safe — the old global loc-table overwrite showed the last-synced player's status on both relics.

    /// <summary>Rebuild the transient record from the relic on load. A repaid loan removed the relic, so
    /// no relic ⇒ no record ⇒ free to borrow again.</summary>
    internal static void RestoreFromRelic(Player player)
    {
        var relic = LedgerRelicOf(player);
        if (relic == null) return;
        var rec = GetOrCreate(player);
        rec.Borrowed = relic.Borrowed;
        rec.Principal = relic.Principal;
        rec.TotalPaid = relic.TotalPaid;
        rec.InterestPctApplied = relic.InterestPctApplied;
        rec.InterestPaid = relic.InterestPaid;
        rec.CardDebt = relic.CardDebt;
        rec.NodeInterestGold = relic.NodeInterestGold;
        // Migration: pre-v0.9.16 saves have InterestPctApplied but no NodeInterestGold. Reconstruct it so their
        // interest doesn't reset to just origination on load.
        if (rec.NodeInterestGold == 0 && rec.InterestPctApplied > 0)
            rec.NodeInterestGold = (int)Math.Round((rec.Borrowed + rec.CardDebt) * (rec.InterestPctApplied / 100.0));
        rec.LoanFloor = relic.LoanFloor;
        rec.Active = relic.Active;
        rec.DunningLetterGranted = relic.DunningLetterGranted;
        rec.RestructuringUsed = relic.RestructuringUsed;
        rec.LoanDraws = relic.LoanDraws;
        rec.CreditRewardsTaken = relic.CreditRewardsTaken;
        rec.EventGrantCount = relic.EventGrantCount;
        rec.LifetimePayments = relic.LifetimePayments;
        rec.DebtShopVisits = relic.DebtShopVisits;
        rec.LastShopVisitFloor = relic.LastShopVisitFloor;
        rec.ShopSpentThisVisit = relic.ShopSpentThisVisit;
        rec.PurgedThisVisit = relic.PurgedThisVisit;
        rec.CreditPaid = relic.CreditPaid;
        rec.PendingSettleCleanup = relic.PendingSettleCleanup;
        rec.PurchasedCards.Clear();
        foreach (var s in (relic.PurchasedCardsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) rec.PurchasedCards.Add(s);
        rec.RelicGranted = true;
        EnsureRoomWatch();   // resubscribe the shop-revisit grant watcher after a load (grants power cards + counts shop visits)
        relic.RefreshVars(DebtCardCountFor(player));
        MainFile.Logger.Info($"[{MainFile.ModId}] restored loan: borrowed {rec.Borrowed}, owed {rec.Principal}, paid {rec.TotalPaid}, loanFloor {rec.LoanFloor}, active {rec.Active}.");
    }

    /// <summary>DEBUG (dl_tier console cmd): force the loan to a given rooms-since-loan so the Ledger shows a
    /// target escalation tier — grants a starter loan if none exists, back-dates LoanFloor to hit the tier,
    /// then refreshes badge + hover + the evolving-icon overlay. SP/preview only; not for real play.</summary>
    internal static async Task DebugSetTier(Player player, int rooms)
    {
        if (player?.RunState == null) return;
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0) { await GrantLoanDirect(player, 200); rec = For(player); }
        if (rec == null) return;
        rec.LoanFloor = player.RunState.TotalFloor - rooms;   // rooms-since-loan = TotalFloor − LoanFloor
        if (rec.Principal <= 0) rec.Principal = 200;
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        LedgerOverlay.Refresh();
    }

    /// <summary>Display-only: push the current tier count into the relic's DynamicVars so the hover's per-tier
    /// text keeps pace (the badge is already computed live). No networked/SavedProperty mutation → safe to run
    /// per-client (e.g. at combat start). Also self-heals via SyncToRelic whenever a Debt card drains gold.</summary>
    internal static void RefreshRelicDisplay(Player? player)
    {
        if (player == null) return;
        var relic = LedgerRelicOf(player);
        if (relic != null) relic.RefreshVars(DebtCardCountFor(player));
    }

    /// <summary>TEST 전용: 레코드 → 유물 반영을 외부(networked 테스트 커맨드)에서 호출하기 위한 창구.</summary>
    internal static void SyncToRelicForTest(Player player) => SyncToRelic(player);

    // ── Eligibility ──────────────────────────────────────────────────────────

    /// <summary>★막 제한 폐지 — 언제든 빌릴 수 있다. 예전엔 1막 전용이었는데, 청산이 유물을 없애지 않고
    /// 신용도가 런 내내 쌓이는 구조가 되면서 "대출은 런 초반의 일회성 결정"이라는 전제 자체가 사라졌다.
    /// 대출 관계는 이제 런 전체에 걸친 순환이라 후반 막에서도 빌릴 수 있어야 성립한다.
    /// (호출부와 시그니처는 유지 — 나중에 막 제한을 되살릴 여지를 남긴다.)</summary>
    internal static bool ActAllowsLoan(Player player) => true;

    internal static int RemainingRoom(Player player)
    {
        // ★MaxLoan ≤ 0 = NO gold cap: the DRAW COUNT is the only limit, so three draws can theoretically finance
        // three relics. The old 300/400 cap made the draw limit nearly redundant — MinLoan is 100, so the hard cap
        // already allowed at most 4 draws and "3" only shaved one off. Now the question is purely "is this item
        // worth one of my three?", which is the decision the limit was added to create.
        if (DebtLoanConfig.MaxLoan <= 0) return int.MaxValue;
        var rec = For(player);
        int used = rec?.Borrowed ?? 0;      // cap is on lifetime borrowed, not the amortized outstanding
        return Math.Max(0, DebtLoanConfig.HardCap - used);   // may overshoot the soft cap up to the hard cap
    }

    /// <summary>Can this merchant item be bought on loan now? First loan: any Act-1 shop. Top-up: only at
    /// the same shop (until the borrow cap is reached).</summary>
    internal static bool CanLoanCover(MerchantEntry entry, Player player)
    {
        if (entry == null || player == null) return false;
        // Co-op: loans replicate via the networked dl_sync command (relic + record on both peers), and the
        // gold rides the reward-sync. Only the LOCAL player may take a loan (others' shops are theirs).
        bool sp = RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;
        if (!ActAllowsLoan(player)) return false;

        var rec = For(player);

        int cost = entry.Cost;
        int shortfall = cost - (int)player.Gold;
        if (shortfall <= 0) return false;                    // can already afford → not a loan
        if (RemainingRoom(player) < shortfall) return false; // over the cap

        if (rec == null || !rec.RelicGranted) return true;   // FIRST loan ever
        // ★청산한 계약(Active=false)은 '없는 것과 같다' — 유물과 결제 카드가 남아 record 가 살아 있을 뿐이므로
        // 다음 대출은 첫 대출과 동일하게 아무 상점에서나 시작할 수 있다. 여기서 false 를 돌려주던 예전 코드는
        // record 가 완납 시 삭제된다는 전제였고, 재설계로 그 전제가 깨지면서 청산 후 영구 대출 불가가 됐었다.
        if (!rec.Active) return true;
        if (DrawsLeft(rec) <= 0) return false;               // spent all draws on this loan → the merchant is done
        return player.RunState.TotalFloor == rec.LoanFloor;  // top-up ONLY at the same shop
    }

    /// <summary>How much a loan advances for this item: at least <see cref="DebtLoanConfig.MinLoan"/> (so a
    /// tiny shortfall still borrows a meaningful amount), never below the actual shortfall, and capped by the
    /// remaining borrow room. The extra over the shortfall lands in the player's pocket as change.</summary>
    internal static int LoanAmountFor(MerchantEntry entry, Player player)
    {
        int shortfall = entry.Cost - (int)player.Gold;
        if (shortfall <= 0) return 0;
        int want = Math.Max(shortfall, DebtLoanConfig.MinLoan);
        return Math.Max(0, Math.Min(want, RemainingRoom(player)));
    }

    /// <summary>Price multiplier the merchant applies to a player carrying debt at a DIFFERENT shop than
    /// the one they borrowed at: +10% (1 card) / +15% (2) / +20% (3). 1.0 = no change.</summary>
    internal static double DebtPriceMultiplier(Player player)
    {
        var rec = For(player);
        // Principal 까지 봐야 한다 — 청산 뒤에도 record 는 남으므로, 빚이 0인 사람에게 할증이 붙으면 안 된다.
        if (rec == null || !rec.Active || rec.Principal <= 0) return 1.0;
        if (player.RunState.TotalFloor == rec.LoanFloor) return 1.0;   // no surcharge at your own shop
        int tier = DebtCardCountFor(player);                            // 1..4
        return 1.0 + Math.Min(20, 5 + 5 * tier) / 100.0;                // 10% / 15% / 20% (capped — tier 4 bites via HP)
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    internal static Task GrantLoanFor(MerchantEntry entry, Player player)
        => GrantLoanDirect(player, LoanAmountFor(entry, player));

    /// <summary>Credit the loan gold, record the debt, and grant the Ledger relic on the first loan. The
    /// gold is a LOCAL mutation + reward-sync (so it shows on the partner too); the relic + loan record are
    /// applied LOCALLY in SP, or dispatched to BOTH peers via the networked <c>dl_sync</c> command in co-op
    /// (RelicCmd.Obtain is a local mutation — see <see cref="ApplyActiveLoan"/> — so running dl_sync on each
    /// peer grants exactly one relic per peer, no doubling).</summary>
    internal static async Task GrantLoanDirect(Player player, int amount)
    {
        if (amount <= 0) return;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return;

        // The loan disbursement itself must NOT be garnished (that would short the borrowed amount on a top-up when
        // interest has already accrued). The guard is read synchronously by the GainGold garnish prefix.
        _grantingFor = player;
        try { await PlayerCmd.GainGold(amount, player, false); }
        finally { _grantingFor = null; }
        run?.RewardSynchronizer?.SyncLocalObtainedGold(amount);

        var existing = For(player);
        // ★청산 후의 record 는 '살아 있는 계약'이 아니다(Active=false) → 이어붙이지 말고 새 사이클로 시작한다.
        // 이걸 놓치면 새 대출이 옛 LoanFloor 를 물려받아 **첫 방부터 최고 등급**으로 시작하고(TotalFloor −
        // LoanFloor 가 이미 크다), Borrowed 누적 때문에 이자 표시(Borrowed×20% + 노드이자)도 부풀려진다.
        bool topUp = existing != null && existing.Active;
        int oldBorrowed = topUp ? existing!.Borrowed : 0;
        int borrowed  = oldBorrowed + amount;                  // lifetime borrowed (drives the cap + hover)
        // Repayable > borrowed: you owe the gold you took PLUS interest. 20% ORIGINATION is added right now on
        // this amount; the rest accrues per-room (see AccrueNodeInterest). Borrowed is what you received (drives
        // the cap); Principal is what you must repay (shop cost + badge), amortized 1:1 by payments.
        int origination = (int)Math.Round(amount * (DebtLoanConfig.BorrowOriginationPct / 100.0));
        int principal = (topUp ? existing!.Principal : 0) + amount + origination;
        int totalPaid = existing?.TotalPaid ?? 0;              // ★신용도는 런 단위 — 사이클이 바뀌어도 이어진다
        int loanFloor = topUp
                        ? existing!.LoanFloor                  // top-up keeps the original shop floor
                        : player.RunState.TotalFloor;          // 첫 대출 / 청산 후 새 대출: rooms = TotalFloor − here (0 → 1 card)

        if (sp) await ApplyActiveLoan(player, borrowed, principal, totalPaid, loanFloor);
        else    DebtLoanNet.BroadcastLoan(player, borrowed, principal, totalPaid, loanFloor);

        MainFile.Logger.Info($"[{MainFile.ModId}] loan +{amount}g (borrowed {borrowed}/{DebtLoanConfig.MaxLoan}, owed {principal}=+30%).");

        // (No merchant bark on the loan itself any more — the merchant now speaks when he HANDS you a payoff card,
        //  see the 정기 납부 loan-time grant + TryGrantDunningLetter, hinting another card comes next visit.)
    }

    /// <summary>Apply an ACTIVE loan state locally: set the record, grant the Ledger relic if the player
    /// doesn't have it, and write the state through to the relic. Runs on EACH peer — directly in SP, or
    /// once per peer via the networked <c>dl_sync</c> replay in co-op (idempotent: re-grants only if missing).</summary>
    internal static async Task ApplyActiveLoan(Player player, int borrowed, int principal, int totalPaid, int loanFloor)
    {
        var rec = GetOrCreate(player);
        // ★계약이 닫힌 상태에서 들어왔다 = 새 대출 사이클. 전선에 실리지 않는 LOCAL 이자 회계를 여기서 리셋한다.
        // ApplyRepay 도 같은 리셋을 하지만 이 자리가 필요한 이유 — 강제 징수/가압류(ForceRepayPrincipal)는
        // 동기 메서드라 ApplyRepay 를 못 부르고 Active=false 만 세운다. 그 경로로 닫힌 계약 뒤에 빌리면
        // 옛 이자가 새 대출에 그대로 얹힌다. co-op: Active 는 양 피어가 같은 재생 경로로 갱신 → 판정도 수렴.
        if (!rec.Active)
        {
            rec.CardDebt = 0;
            rec.InterestPaid = 0;
            rec.NodeInterestGold = 0;
            rec.InterestPctApplied = 0;
            rec.LoanDraws = 0;                 // 아래에서 ++ → 새 사이클의 첫 인출 = 1
            rec.RestructuringUsed = false;     // 채무 조정은 '대출당 1회'
        }
        rec.Borrowed     = borrowed;
        rec.Principal    = principal;
        rec.TotalPaid    = totalPaid;
        rec.LoanFloor    = loanFloor;
        rec.Active       = true;
        rec.RelicGranted = true;
        // One call = one DRAW (the first borrow and every top-up both land here). Counted on the APPLIED path, which
        // SP runs directly and co-op replays on BOTH peers via dl_sync — so the count converges without widening the
        // wire (adding a 5th broadcast arg would break version parity, see coop-guard).
        rec.LoanDraws++;
        if (!PlayerHasLedger(player))
            await DebtLoanGrants.GrantRelic(player);
        // Hand the 정기 납부 (Standing Order) card AT LOAN TIME (not the first shop revisit) so you have immediate
        // counterplay to the injected 빚 (Debt) curse — play it and the power feeds 납부 cards to work the debt
        // down. Consumes sequence slot 0 (EventGrantCount → 1); the remaining payoff cards still come at shop
        // revisits. Once per loan (DunningLetterGranted guards top-ups). Local per-peer, like the revisit grant.
        if (!rec.DunningLetterGranted)
        {
            rec.DunningLetterGranted = true;
            rec.EventGrantCount = System.Math.Max(rec.EventGrantCount, 1);
            _ = DebtLoanGrants.GrantDunningLetter(player);
            MerchantBark.SayGrant(NextEventCardHintKey(rec));   // hand the 정기 납부 + hint the SPECIFIC next card
        }
        EnsureRoomWatch();   // watch shop revisits: grant the REMAINING power cards (slots 1-6) + count debt-shop visits
        SyncToRelic(player);
    }

    // ── 정기 납부 (Standing Order) shop-revisit grant ─────────────────────────────
    private static bool _roomWatchSubscribed;

    /// <summary>Subscribe (once) to room changes so we can hand the 정기 납부 leverage card to a debtor the first
    /// time they shop somewhere OTHER than where they borrowed. Fires per-peer (like the ledger overlay's own
    /// RoomEntered hook); the grant is flag-guarded + deterministic from synced loan state → converges in co-op.
    /// ⚠️ co-op: verify with coop-verify before release (local deck mutation off a per-peer event).</summary>
    internal static void EnsureRoomWatch()
    {
        if (_roomWatchSubscribed) return;
        var rm = RunManager.Instance;
        if (rm == null) return;
        rm.RoomEntered += OnRoomEntered;
        _roomWatchSubscribed = true;
    }

    // ── 외상 카드 구매 (buy the non-power cards on debt at the shop) ──────────────────────────────
    // The non-power cards are no longer earned in combat — the debtor BUYS them at a shop, taking on debt.
    // The offer list grows per shop visit (see RevealedPurchasable); price scales with card strength.

    /// <summary>Debt price of a purchasable card, by strength tier. owed is a SOFT cost (it only raises the repay
    /// total — not shop inflation, node interest, or curse tiers), so the real limiter is the per-visit reveal
    /// count × how many shops a run has; the price is the relative signal + a repay-build tax.</summary>
    // Price band (before sale). ★60/95 → 45/95: 방문당 유료를 1장에서 **합이 한도 이하면 2장**으로 연다.
    // 가격이 그대로 규칙이 되는 게 핵심 — 플레이어가 두 값을 더해 120 이하인지 화면에서 바로 읽는다
    // (예전엔 "왜 한 장밖에 못 사지?"가 숨은 규칙이었다). 티어는 45/55/65/75/85/95 등간이라
    //   · 45+75 = 120, 55+65 = 120 → 두 장
    //   · 65+65 = 130, 85+45 = 130, 95+45 = 140 → 한 장
    // 즉 매 방문이 "고급 1장이냐, 중저가 2장이냐"의 선택이 된다. 3장은 어떤 조합으로도 안 들어가고
    // (최저 3장 45+45+55 = 145 > 120), 세일 카드가 그 3장째를 사준다.
    private const int PriceMin = 45, PriceMax = 95;   // shop price band (before sale)

    /// <summary>Base tier price (centre of the band; the shown base is this ± variance, clamped to [40,70]).
    /// Tiers lowered ~10 so card debt piles up more slowly (paired with the tighter per-shop credit limit).</summary>
    internal static int CardDebtPrice(System.Type t)
    {
        // ★티어 재편(2026-07-28). 예전엔 21장 중 15장(71%)이 85 아니면 75에 몰려 있었다 — 파워 6종을
        // 벌리며 고쳤던 "값이 같으면 상위만 팔린다" 문제가 풀 전체 규모로 재발한 상태였다. 45~95 를
        // 10 간격 6단으로 벌려 최대 버킷을 8장(38%) → 6장(29%)으로 낮췄다. 서열 근거는 BALANCE_AUDIT.md
        // (게임 본편 548장 분포 대조: 1코 피해 중앙 8/Q3 10/max 30, 1코 방어도 중앙 6/Q3 8/max 15 —
        //  ★방어도가 피해보다 희소해서 같은 숫자면 더 비싼 값이다).

        // ── 95: 전투를 통째로 바꾸는 엔진 ────────────────────────────────────────────────────
        // 차입=턴당 에너지(세트 최대 효과) · 명세서=납부마다 드로우 · 납부 혜택=판금 순 +2/턴 무한 누적
        if (t == typeof(BorrowingCard) || t == typeof(StatementCard) || t == typeof(PaymentBenefitCard)) return 95;

        // ── 85: 한 방문을 통째로 쓸 값어치 ───────────────────────────────────────────────────
        // 채무 조정=한 대출당 1회 250 원금 탕감 · 경비 처리=나머지 영수증 카드를 전부 싸게 만드는 인에이블러
        // 어음=0코 순 +2 에너지(세트 최고 템포) · 레버리지=원금÷30, 2코 max 28 을 넘는 상한 + **비소멸 반복**
        if (t == typeof(RestructuringCard) || t == typeof(ExpensingCard)
            || t == typeof(PromissoryNoteCard) || t == typeof(LeverageCard)
            || t == typeof(DefaultRiskCard)) return 85;   // 부도 위기: 빚 빌드의 곱셈 축(레버리지·부실채권이 힘을 탄다)

        // ── 75: 영수증을 전량 태우는 X 스케일러 + 광역 ────────────────────────────────────────
        // 청구서·정산은 같은 4×X 쌍이라 **같은 값**이어야 한다(예전엔 85/75 로 갈려 있었다 — BALANCE_AUDIT
        // 이 "균형, 조정 불필요"로 판정한 쌍인데 가격만 어긋나 있던 모순). 영수증 8이면 32 로 1코 상한 초과.
        if (t == typeof(InvoiceCard) || t == typeof(SettlementCard) || t == typeof(GarnishmentCard)) return 75;

        // ── 65: 즉시 효과가 확실한 기본기 ────────────────────────────────────────────────────
        // 대출 강타 14(1코 중앙의 1.75배) / 저당 방어도 12(중앙의 2.0배) — 감사가 "오차 범위"라 판정한
        // 쌍이므로 같은 칸에 둔다. 환급·자본 타격·추심=납부 트리거 엔진, 취업알선=순 55골드.
        if (t == typeof(LoanStrikeCard) || t == typeof(MortgageCard) || t == typeof(RefundCard)
            || t == typeof(CounterclaimCard) || t == typeof(CollectionCard) || t == typeof(JobPlacementCard)
            || t == typeof(CollateralCard)) return 65;   // 담보: 저당과 같은 즉시 방어 기본기(이쪽은 원금 스케일)

        // ── 55: 조건부이거나 전투 기여가 없는 것 ─────────────────────────────────────────────
        // 파산·차환·돌려막기는 **손에 native Debt** 를 요구하는데 native Debt 는 유료 구매 1회당 1장뿐 →
        // 발동 빈도가 세트 최하. 이자 지원은 전투 효과가 0(골드만 준다).
        // ⚠️감사의 "이자 지원을 60으로" 는 구조상 불가능하다 — 45 미만으로는 못 가고, 45 자리는 한 장뿐이다.
        //   이 카드는 값이 아니라 효과 이중화로 풀어야 한다.
        if (t == typeof(BankruptcyCard) || t == typeof(RefinanceCard)
            || t == typeof(KitingCard) || t == typeof(InterestSupportCard)
            // 부실채권을 65 가 아니라 55 에 둔 이유: 65 버킷이 이미 6장이라 8장이 되면 방금 고친
            // '한 티어에 몰려 값이 신호가 아니게 되는' 문제로 되돌아간다. 55 는 5장이 된다.
            || t == typeof(BadDebtCard)) return 55;

        // ── 45: 밴드 최저 — 여기 앉을 수 있는 건 한 장뿐이다 ─────────────────────────────────
        // 45 가 둘이면 45+45+45 = 135 는 넘지만 두 장이 90 이라 세일까지 겹칠 때 3장이 들어온다. 혈납은
        // 전투 효과가 0인 부팅용 유틸이라 이 자리의 주인이다.
        if (t == typeof(BloodPaymentCard)) return 45;

        return 65;
    }

    /// <summary>The pre-sale shown price: tier base ± a deterministic variance (−10..+10 in 5s), clamped to the
    /// [50,80] band. Deterministic per (LoanFloor, visit, card). This is the "original" price struck through on a
    /// sale card.</summary>
    internal static int ShopBasePrice(LoanRecord rec, System.Type t)
    {
        int idx = System.Array.IndexOf(PurchasablePool, t);
        var rng = new System.Random(unchecked(rec.LoanFloor * 911 + rec.DebtShopVisits * 277 + idx * 53 + 7));
        // ★분산 ±10 → ±5: 티어 간격이 10이라 ±10 이면 인접 티어가 통째로 겹쳐(65 티어가 75까지 올라간다)
        // "합 120 이하면 두 장" 규칙이 값마다 뒤집힌다. ±5 여야 티어 경계가 유지된다.
        int variance = rng.Next(-1, 2) * 5;   // −5, 0, +5
        return Math.Clamp(CardDebtPrice(t) + variance, PriceMin, PriceMax);
    }

    /// <summary>Is this a 빚 (Debt) card — the ONE card type 차환 / 돌려막기 may consume? ONLY the game's native
    /// <see cref="MegaCrit.Sts2.Core.Models.Cards.Debt"/>, matching what 파산 선언 already eats. Deliberately
    /// EXCLUDES:
    /// <list type="bullet">
    /// <item>our tier curses (연체 / 차압 / 신용 불량 / 강제 징수) — those are the loan's escalating pressure and
    /// must not be cashable away; they are re-injected every combat anyway, which would make them an infinite
    /// fuel supply for 돌려막기.</item>
    /// <item>납부 (<see cref="DebtCurseCard"/>) — the repayment card, not a debt.</item>
    /// <item>every OTHER curse in the game (후회, 부상, …).</item>
    /// </list>
    /// Single source of truth so the consumers can never drift apart. Native Debt enters the deck only from a
    /// debt-shop visit or from 차환 itself, so this is a CLOSED loop: 차환 mints the fuel that 차환/돌려막기 burn.</summary>
    internal static bool IsDebtCurseCard(CardModel c) => c is MegaCrit.Sts2.Core.Models.Cards.Debt;

    // ── The FREE offer (leftmost slot) ────────────────────────────────────────────────────────────────────
    // Slot 0 of every visit's row is a GIFT: no debt added, no credit-line spend, and — crucially — no native Debt
    // curse. The debt shop stops being a pure "pay to get deeper" screen and always has one thing worth walking in
    // for, while the loan pressure still lives entirely on the paid slots to its right. Which card lands there is
    // the same deterministic shuffle as the rest, so it varies per visit (a guaranteed-floor variable reward) and
    // both co-op peers agree without any extra wire.

    /// <summary>Cards that must never occupy the free slot: anything that rewrites the LOAN RECORD itself. 채무 조정
    /// forgives 250 principal — free, that's a −250 gift you could just wait for, so it stays a paid offer.
    /// (Everything else is safe: the payment-set cards only pay off while you're in debt, and 돌려막기's native-Debt
    /// fuel is minted by PAID purchases only, so a free copy can't bootstrap anything.)</summary>
    private static bool FreeSlotIneligible(System.Type t) => t == typeof(RestructuringCard);

    /// <summary>Is this the visit's FREE offer (slot 0)? Single source of truth for the price, the credit gate,
    /// the sale/upgrade exclusions and the panel's "FREE" tag.</summary>
    internal static bool IsFreeOffer(LoanRecord rec, System.Type t)
    {
        var offers = RevealedPurchasable(rec);
        return offers.Length > 0 && offers[0] == t;
    }

    /// <summary>Which of this visit's offers is ON SALE — a deterministic pick from the revealed set (per LoanFloor
    /// + visit), like the merchant's discounted card. Its <see cref="ShopPriceFor"/> is knocked down ~30%.
    /// Slot 0 is excluded: it's already free, and a discount tag on a free card reads as a bug.</summary>
    internal static System.Type? SaleCardFor(LoanRecord rec)
    {
        var offers = RevealedPurchasable(rec);
        if (offers.Length <= 1) return null;   // only the free slot exists → nothing to discount
        var rng = new System.Random(unchecked(rec.LoanFloor * 333 + rec.DebtShopVisits * 97 + 3));
        return offers[1 + rng.Next(offers.Length - 1)];
    }

    /// <summary>Which of this visit's offers is stocked ALREADY UPGRADED (강화판) — a deterministic pick (per
    /// LoanFloor + visit) from the revealed set, restricted to cards that can actually be upgraded and, when there
    /// is a choice, never the sale card so the two shop perks land on different offers. Its
    /// <see cref="ShopPriceFor"/> carries a <see cref="UpgradePremiumPct"/> surcharge. Deterministic → co-op peers
    /// and a reload agree on which offer is the enhanced one.</summary>
    internal static System.Type? UpgradedCardFor(LoanRecord rec)
    {
        var offers = RevealedPurchasable(rec);
        if (offers.Length == 0) return null;
        // ★Slot 0 IS eligible: landing the 강화판 on the free gift is the shop's jackpot (free AND upgraded), and it
        // costs nothing to allow — ShopPriceFor returns 0 for slot 0 before the premium is ever applied, so the
        // surcharge simply doesn't exist there. (The SALE tag stays paid-only: ~30% off a free card is nonsense.)
        var sale = SaleCardFor(rec);
        var pool = new List<System.Type>();
        foreach (var t in offers) if (t != sale && IsUpgradable(t)) pool.Add(t);
        if (pool.Count == 0)   // the sale card is the only upgradable offer → let it be both
            foreach (var t in offers) if (IsUpgradable(t)) pool.Add(t);
        if (pool.Count == 0) return null;
        var rng = new System.Random(unchecked(rec.LoanFloor * 617 + rec.DebtShopVisits * 149 + 11));
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>Surcharge on the visit's upgraded offer (percent of its base price, rounded to 5). The band tops
    /// out at <see cref="PriceMax"/> 95, so +20% (114) stays under the per-visit credit line (120). ★Do not raise
    /// this back to 30%: 95 × 1.3 = 124 would put the visit's 강화판 offer permanently out of reach.</summary>
    private const int UpgradePremiumPct = 20;

    /// <summary>Can this card type be upgraded at all? (Curse-ish / fixed cards have MaxUpgradeLevel 0 and must
    /// never be picked as the visit's 강화판 — the buy would silently grant a normal copy at a premium price.)</summary>
    private static bool IsUpgradable(System.Type t)
    {
        var m = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(t));
        return m != null && m.MaxUpgradeLevel > 0;
    }

    /// <summary>The actual debt price of a card at the shop THIS visit: its tier base ± a deterministic variance
    /// (−10%..+15%, rounded to 5), +30% if it's the visit's UPGRADED offer, then ~30% off if it's the visit's sale
    /// card. Deterministic per (LoanFloor, visit, card) → co-op peers + a reload agree; the shown price == the
    /// charged price (BuyCardOnDebt uses this).</summary>
    internal static int ShopPriceFor(LoanRecord rec, System.Type t)
    {
        if (IsFreeOffer(rec, t)) return 0;   // slot 0 is the gift — charged nothing, and ApplyBuyCard skips the whole debt path
        int price = ShopBasePrice(rec, t);
        if (UpgradedCardFor(rec) == t) price = (int)Math.Round(price * (100 + UpgradePremiumPct) / 500.0) * 5;   // 강화판 프리미엄
        if (SaleCardFor(rec) == t) price = Math.Max(5, (int)Math.Round(price * 0.55 / 5.0) * 5);   // sale card ~45% off (deep enough that sale + one more fits the credit line)
        return price;
    }

    /// <summary>The cards on offer at the shop THIS visit — a rotating <see cref="ShopOfferCount"/>-card selection
    /// drawn from the not-yet-bought pool, deterministic per (LoanFloor, DebtShopVisits) so both co-op peers + a
    /// reload agree, and re-rolled only when you enter a NEW shop. Cached on the record so the selection is STABLE
    /// while you shop (buying one card doesn't reshuffle the rest); a bought card drops out on the next visit.</summary>
    internal static System.Type[] RevealedPurchasable(LoanRecord rec)
    {
        if (rec.CurrentOffers != null && rec.OfferVisit == rec.DebtShopVisits) return rec.CurrentOffers;
        var available = new List<System.Type>();
        // 채무 조정 leaves the shelf for good once its once-per-loan use is spent — the sold-set clears every visit,
        // so without this it would be re-stocked and re-buyable for far less debt than it forgives.
        foreach (var t in PurchasablePool)
            if (!rec.PurchasedCards.Contains(t.Name) && !(t == typeof(RestructuringCard) && rec.RestructuringUsed))
                available.Add(t);
        System.Type[] offers;
        if (available.Count <= ShopOfferCount) offers = available.ToArray();
        else
        {
            var rng = new System.Random(unchecked(rec.LoanFloor * 31 + rec.DebtShopVisits * 101 + 13));
            for (int i = available.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (available[i], available[j]) = (available[j], available[i]); }
            offers = available.GetRange(0, ShopOfferCount).ToArray();
        }
        // Slot 0 is the FREE gift, so it must not land on a free-slot-ineligible card (see FreeSlotIneligible).
        // Swap it rightward with the first eligible offer instead of dropping the gift — deterministic, so co-op
        // peers and a reload land on the same arrangement.
        if (offers.Length > 1 && FreeSlotIneligible(offers[0]))
            for (int i = 1; i < offers.Length; i++)
                if (!FreeSlotIneligible(offers[i])) { (offers[0], offers[i]) = (offers[i], offers[0]); break; }
        // ★Publish the memo BEFORE the affordability pass. That pass asks for prices, and ShopPriceFor →
        // SaleCardFor/UpgradedCardFor call back into THIS method — without the memo already set that is infinite
        // recursion (it killed the whole run, not just the shop). With it set, the callbacks hit the cache and
        // return; the pass then mutates `offers` in place, which IS rec.CurrentOffers, so both stay consistent.
        rec.CurrentOffers = offers;
        rec.OfferVisit = rec.DebtShopVisits;
        EnsureAffordablePair(rec, offers, available);
        return offers;
    }

    /// <summary>Guarantee the shop's core promise: <b>one paid card per visit — unless you take the sale card, then
    /// two.</b> That only holds if some PAID offer is cheap enough that sale + it fits the visit's credit line.
    /// <para>★Why this exists: the roll is a uniform shuffle of the pool, so a visit can legitimately deal five
    /// expensive cards and silently make the sale tag worthless. It was rare while the pool skewed cheap; adding
    /// 경비 처리(90)/차입(95) made it common enough that solo-verify's price invariant caught it. Rather than widen
    /// the credit line (which changes every other price relationship), swap the priciest paid offer for the cheapest
    /// card left in the pool whenever the pair doesn't fit.</para>
    /// Deterministic (reads only the already-shuffled list + the fixed price table) → co-op peers and a reload land
    /// on the same row. Mutates <paramref name="offers"/> in place; slot 0 (the free gift) is never touched.
    /// <para>⚠️★co-op 제약: 이 메서드는 <see cref="DebtLoanConfig.ShopCreditLimit"/> 을 읽는데 그건 <b>클라이언트별
    /// ModConfig 슬라이더</b>다. 즉 오퍼 목록은 더 이상 (LoanFloor, DebtShopVisits) 만의 함수가 아니고, 슬라이더가
    /// 다른 두 피어는 <b>다른 목록</b>을 만들 수 있다. 지금은 안전한데, 이유는 딱 하나다 — 구매가 와이어에 카드
    /// 타입과 가격을 실어 보내고(<c>dl_sync buy</c>) 재생 경로 <see cref="ApplyBuyCard"/> 가 오퍼 목록을
    /// <b>게이트로 쓰지 않기 때문</b>이다. 그래서 갈라지는 건 화면뿐이고 결과는 수렴한다.
    /// <b>오퍼 목록(RevealedPurchasable / IsPurchasable)을 네트워크 재생 경로의 조건으로 쓰는 순간 진짜 desync가
    /// 된다.</b> 그렇게 바꿔야 한다면 먼저 ShopCreditLimit 을 호스트 권위로 브로드캐스트하라(RelicForge rf_config
    /// 패턴). 근거: coop-guard 클래스3.</para></summary>
    private static void EnsureAffordablePair(LoanRecord rec, System.Type[] offers, List<System.Type> pool)
    {
        if (offers.Length < 2) return;
        var sale = SaleCardFor(rec);
        int limit = DebtLoanConfig.ShopCreditLimit;

        int cheapestPaid = int.MaxValue, priciestIdx = -1, priciest = -1;
        for (int i = 1; i < offers.Length; i++)
        {
            if (offers[i] == sale) continue;                       // the sale card is the OTHER half of the pair
            int p = ShopPriceFor(rec, offers[i]);
            if (p < cheapestPaid) cheapestPaid = p;
            if (p > priciest) { priciest = p; priciestIdx = i; }
        }
        if (priciestIdx < 0) return;                               // nothing but the sale card among the paid slots

        int salePrice = sale != null ? ShopPriceFor(rec, sale) : 0;
        if (salePrice + cheapestPaid <= limit) return;             // the promise already holds this visit

        // Find the cheapest card NOT already on the row and swap it in for the priciest one.
        System.Type? best = null; int bestPrice = int.MaxValue;
        foreach (var t in pool)
        {
            if (System.Array.IndexOf(offers, t) >= 0) continue;
            int p = ShopPriceFor(rec, t);
            if (p < bestPrice) { bestPrice = p; best = t; }
        }
        if (best != null && salePrice + bestPrice <= limit) offers[priciestIdx] = best;
    }

    /// <summary>An offered card is buyable if the loan is active, it's been revealed, and it hasn't been bought yet.</summary>
    internal static bool IsPurchasable(LoanRecord rec, System.Type t)
        => rec.Active && !rec.PurchasedCards.Contains(t.Name)
           && !(t == typeof(RestructuringCard) && rec.RestructuringUsed)   // spent its once-per-loan use → off the shelf
           && System.Array.IndexOf(RevealedPurchasable(rec), t) >= 0;

    /// <summary>Gold of debt-shop credit still available THIS visit (limit minus what's already been spent on cards
    /// this visit). Separate from the initial loan's HardCap. Never negative.</summary>
    internal static int RemainingShopCredit(LoanRecord rec)
        => System.Math.Max(0, DebtLoanConfig.ShopCreditLimit - rec.ShopSpentThisVisit);

    /// <summary>Can this offer be bought right now given the per-visit credit line? (Its price fits the remaining
    /// credit.) The panel greys offers that fail this, and BuyCardOnDebt refuses them.</summary>
    internal static bool CanAffordCredit(LoanRecord rec, System.Type t)
        => IsFreeOffer(rec, t) || ShopPriceFor(rec, t) <= RemainingShopCredit(rec);   // the gift never touches the credit line

    /// <summary>Buy a revealed non-power card on debt: adds its price onto what you owe and drops the card into the
    /// deck (like every other debt card — removed on full repay). Marks it sold so the shop won't re-sell it.
    /// internal so the self-test can invoke it directly (what clicking a shop offer does).</summary>
    internal static async Task<bool> BuyCardOnDebt(Player player, System.Type type)
    {
        var rec = For(player);
        if (rec == null || !IsPurchasable(rec, type)) return false;
        if (!CanAffordCredit(rec, type)) return false;   // over this shop's credit line → refuse (panel already greys it)
        int price = ShopPriceFor(rec, type);          // the shown price (tier ± variance, upgrade premium / sale applied)
        bool upgraded = UpgradedCardFor(rec) == type; // this visit's 강화판 offer → grant the card already upgraded

        // Only the shopper's OWN peer initiates the buy (the panel is local to the player who opened it). Then:
        // SP → apply here; co-op → broadcast so the deck-add + owed-increase replay identically on BOTH peers
        // (a local-only purchase would leave the partner's replica of this player's owed/deck/sold-set diverged
        // → checksum drop). Mirrors GrantLoanDirect / Repay. The price rides the wire so it can't drift.
        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        // The upgraded flag rides the wire next to the price for the same reason: the remote peer's offer cache may
        // not have been built (it never opened the panel), so it must NOT re-derive which offer was the 강화판.
        if (sp) await ApplyBuyCard(player, type.Name, price, upgraded);
        else    DebtLoanNet.BroadcastBuy(player, type.Name, price, upgraded);
        MainFile.Logger.Info($"[{MainFile.ModId}] buy {type.Name}{(upgraded ? "+" : "")} on debt for {price} ({(sp ? "SP local" : "co-op broadcast")}).");
        return true;
    }

    /// <summary>Apply a debt-shop purchase on THIS peer: add the price onto what's owed, mark it sold, and drop
    /// the card into the deck. Runs directly in SP, or once per peer via the networked <c>dl_sync buy</c> replay
    /// in co-op. Idempotent — the sold-mark guards a double-apply on the initiator's own replay / any re-delivery,
    /// so each peer charges the price and grants the card exactly once.</summary>
    internal static async Task ApplyBuyCard(Player player, string typeName, int price, bool upgraded = false)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return;
        if (rec.PurchasedCards.Contains(typeName)) return;      // already bought → no-op (idempotent)
        var type = System.Array.Find(PurchasablePool, t => t.Name == typeName);
        if (type == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] dl_sync buy: unknown card '{typeName}'."); return; }

        // price 0 = the visit's FREE offer (slot 0). It is a pure gift: no principal, no card debt, no credit-line
        // spend — and, below, no native Debt curse either. The price rides the wire (dl_sync buy … 0 …) rather than
        // being re-derived here, because a remote peer never opened the panel and has no CurrentOffers cache.
        bool free = price <= 0;
        rec.Principal += price;                                 // owed goes up; no gold gained (bought on credit)
        rec.CardDebt += price;                                  // card debt = principal that also accrues node interest
        rec.ShopSpentThisVisit += price;                        // count against this shop's per-visit credit line
        rec.PurchasedCards.Add(typeName);
        await DebtLoanGrants.GrantCard(player, type, upgraded: upgraded);   // fly-in shows again now the panel sits at the shop's layer depth
        // Every debt-shop VISIT you actually BUY ON CREDIT leaves a native Debt curse in your deck — the price of
        // leaning on the credit line. Once per floor (= per shop visit), no matter how many cards you buy that visit;
        // swept on repay. ★The free offer is exempt: taking only the gift and walking out must cost nothing, so the
        // per-floor guard is not even stamped on a free take (a later PAID buy that same visit still gets the curse).
        // Runs in the same per-peer networked buy replay as the card grant, and reads shared floor state → co-op consistent.
        if (!free && player.RunState != null && rec.LastDebtGrantFloor != player.RunState.TotalFloor)
        {
            rec.LastDebtGrantFloor = player.RunState.TotalFloor;
            await DebtLoanGrants.GrantNativeDebt(player);
        }
        SyncToRelic(player);
        // No refresh event needed: NDebtCardShopPanel polls its refreshers every frame in _Process, so the sold
        // state greys out on the next frame once the (possibly deferred co-op replay) purchase lands here.
    }

    /// <summary>Players in the run currently carrying an ACTIVE loan (Principal &gt; 0). Read from shared run state
    /// so it's identical on every co-op peer — this is the ONLY input to the MP interest scaling, which keeps that
    /// scaling lockstep-deterministic (never read a local/UI value here).</summary>
    internal static int BorrowerCount(IRunState? run)
    {
        if (run?.Players == null) return 0;
        int n = 0;
        foreach (var p in run.Players) { var r = For(p); if (r != null && r.Active && r.Principal > 0) n++; }
        return n;
    }

    /// <summary>Accrue per-room interest into the owed Principal as a percentage of Borrowed, tracked as the total
    /// percent baked so far (<see cref="LoanRecord.InterestPctApplied"/>) — idempotent across re-fires and reloads.
    /// <para>Both the RATE and the CAP scale with how many players carry a loan (N = <see cref="BorrowerCount"/>),
    /// so debt shared by more people is harsher:</para>
    /// <list type="bullet">
    /// <item>rate  = NodeInterestPct × N per room (5% solo → 20% at 4 debtors: accrues faster)</item>
    /// <item>cap   = base (NodeInterestPct × MaxNodeInterestRooms = 40%) + min(MpExtraCapMax, MpExtraCapPerBorrower × (N−1))</item>
    /// </list>
    /// SP is exactly the old behaviour (N=1 → 5%/room, 40% cap). N comes only from shared run state, so both co-op
    /// peers compute the same target; if a partner repays and N drops, we never REFUND already-accrued interest.</summary>
    internal static void AccrueNodeInterest(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || player?.RunState == null) return;
        int roomsCarried = Math.Max(0, player.RunState.TotalFloor - rec.LoanFloor);
        if (roomsCarried <= 0) return;

        int n = Math.Max(1, BorrowerCount(player.RunState));              // this player is active → at least 1
        int perRoomPct = DebtLoanConfig.NodeInterestPct * n;             // 5% × N — faster accrual with more debtors
        int capPct = NodeInterestCapPct(player);                         // 40% SP (grows with borrower count in co-op)
        int targetPct = Math.Min(capPct, perRoomPct * roomsCarried);     // node-interest % that should be baked by now

        // Node interest is charged as GOLD on the whole interest base (loan + card debt), capped two ways: by the
        // node % AND by the absolute InterestGoldCap (total interest = origination + node never exceeds it). Tracked
        // as absolute gold so growing card debt doesn't retroactively rescale already-accrued interest.
        int baseAmt = rec.Borrowed + rec.CardDebt;
        int origination = (int)Math.Round(rec.Borrowed * (DebtLoanConfig.BorrowOriginationPct / 100.0));
        int maxNodeGold = Math.Max(0, DebtLoanConfig.InterestGoldCap - origination);   // absolute ceiling minus origination
        int targetNodeGold = Math.Min(maxNodeGold, (int)Math.Round(baseAmt * (targetPct / 100.0)));
        if (targetNodeGold <= rec.NodeInterestGold) { rec.InterestPctApplied = Math.Max(rec.InterestPctApplied, targetPct); return; }
        int add = targetNodeGold - rec.NodeInterestGold;
        rec.Principal += add;
        rec.NodeInterestGold = targetNodeGold;
        rec.InterestPctApplied = targetPct;   // kept for the garnishment "interest maxed" check
        SyncToRelic(player);
    }

    private static void OnRoomEntered()
    {
        try
        {
            var run = RunManager.Instance?.State;
            if (run?.Players == null) return;
            // Every room: refresh each ledger's badge (rooms-until-next-tier) so it visibly counts DOWN as you
            // walk the map — TotalFloor changed, so the live DisplayAmount must be re-pushed to the widget.
            foreach (var p in run.Players) { AccrueNodeInterest(p); RefreshRelicDisplay(p); }
            // 가압류로 닫힌 계약의 뒷정리(강제 징수 카드는 자기 자리에서 즉시 처리한다).
            foreach (var p in run.Players) TaskHelper.RunSafely(FinishForcedSettle(p));
            if (run.CurrentRoom?.RoomType != RoomType.Shop) return;
            foreach (var p in run.Players) { CountShopVisit(p); TryGrantDunningLetter(p); }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] room-watch grant failed: {e.Message}"); }
    }

    /// <summary>Count a DISTINCT shop visit (other than the loan shop) — drives how many cards the debt-card shop
    /// reveals (visit 1 → 3, 2 → 5, 3+ → all). Guarded by LastShopVisitFloor so re-entry/reload doesn't double-count.</summary>
    private static void CountShopVisit(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || player.RunState == null) return;
        int floor = player.RunState.TotalFloor;
        if (floor == rec.LoanFloor || floor == rec.LastShopVisitFloor) return;
        rec.LastShopVisitFloor = floor;
        rec.DebtShopVisits++;
        rec.ShopSpentThisVisit = 0;   // fresh credit line at each new shop (see DebtLoanConfig.ShopCreditLimit)
        rec.PurgedThisVisit = false; // 제거 기회도 상점마다 새로 한 번
        rec.PurchasedCards.Clear();   // "sold" is per-VISIT now → cards can be re-bought next shop (dupes allowed), just not twice in one visit
        SyncToRelic(player);
    }

    /// <summary>Grant the 정기 납부 once per loan, when the debtor enters a shop that isn't the one they borrowed
    /// at (TotalFloor != LoanFloor). Deck mutation is local + deterministic → the same card lands on each peer.</summary>
    // SHOP AUTO-GRANT channel = ONLY 정기 납부 (the repay engine) handed out free, once, at LOAN TIME. Everything
    // else — 취업알선(income), 납부혜택, and the rest — is BOUGHT at the debt shop (see PurchasablePool) and can be
    // re-bought on later visits for duplicates (just not twice in one visit).
    private static readonly System.Type[] FixedOrder =
    {
        typeof(DunningLetterCard),    // slot 0 — granted at loan time (정기 납부, the repay engine); the ONLY free card
    };
    private static readonly System.Type[] RemainderPool = System.Array.Empty<System.Type>();   // 취업알선 moved to the shop
    private const int TotalEventCards = 1;   // FixedOrder(1) only — 정기 납부 is the single free starter card

    // PURCHASABLE pool = everything NOT auto-granted: the 4 remaining POWER engines + the 6 non-power cards. The
    // debtor BUYS these on debt at the shop (see BuyCardOnDebt); removed on repay like every other debt card. The
    // shop shows a rotating ShopOfferCount-card selection per visit (see RevealedPurchasable), like a real merchant.
    private static readonly System.Type[] PurchasablePool =
    {
        typeof(RefundCard), typeof(CounterclaimCard), typeof(StatementCard), typeof(InterestSupportCard),  // power engines
        typeof(PaymentBenefitCard),                                                                         // 납부혜택: payment → block (moved from free grants)
        typeof(CollectionCard),                                                                             // 추심: 공격판 환급 (scaling attack gen)
        typeof(SettlementCard), typeof(InvoiceCard), typeof(GarnishmentCard), typeof(KitingCard),          // receipt spenders (돌려막기: 빚 저주 → 골드)
        typeof(LoanStrikeCard), typeof(MortgageCard), typeof(BloodPaymentCard),                            // borrow / HP
        typeof(PromissoryNoteCard),                                                                        // 어음: borrow for ENERGY (the third borrow currency)
        typeof(LeverageCard),                                                                              // 레버리지: damage scaled by the principal you still owe
        typeof(RestructuringCard),                                                                         // 채무 조정: once-per-loan principal write-off
        typeof(ExpensingCard),                                                                             // 경비 처리: 영수증 비용 -1 (지출 병목 완화)
        typeof(BorrowingCard),                                                                             // 차입: 턴당 에너지 (영수증 4 = 엔진 초반 산출 전부)
        typeof(JobPlacementCard),                                                                          // 취업알선: income skill (moved from free grants)
        typeof(BankruptcyCard), typeof(RefinanceCard),                                                     // debt payoff: Bankruptcy(→Strength) / Refinance(→Payment cards)
        // ★원금 스케일링 축(2026-07-28). 이 세트는 오랫동안 **갚기에만** 보상을 걸고 있었다 — 영수증에
        // 비례해 강해지는 카드가 10장인데 원금에 비례하는 건 레버리지 하나뿐이라, 저주 티어·상점 할증·
        // HP 압박이라는 대가를 다 치르고도 '빚을 안고 간다'가 전략이 아니라 그냥 손해였다. 이 3장이
        // 그 반대편을 만든다: 담보=생존, 부도 위기=곱셈(힘), 부실채권=막힌 손패를 탄약으로.
        typeof(CollateralCard), typeof(DefaultRiskCard), typeof(BadDebtCard),
    };
    private const int ShopOfferCount = 5;   // cards displayed per shop visit (rotating), like the merchant's card row

    /// <summary>Deterministic per-run shuffle of the remainder pool (seeded from the loan floor → same order on both
    /// co-op peers with no networking).</summary>
    private static System.Type[] ShuffledRemainder(int seed)
    {
        var arr = (System.Type[])RemainderPool.Clone();
        var rng = new System.Random(seed);
        for (int i = arr.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (arr[i], arr[j]) = (arr[j], arr[i]); }
        return arr;
    }

    /// <summary>The INDIRECT-hint key for the NEXT event card the merchant will hand out (the merchant alludes to
    /// its effect without naming it), or null if the sequence is finished. Deterministic (same LoanFloor → same
    /// order + persists across save/load), so the hint always matches what actually arrives next. Call AFTER
    /// EventGrantCount has advanced past the current card.</summary>
    private static string? NextEventCardHintKey(LoanRecord rec)
    {
        int pos = rec.EventGrantCount;   // the NEXT slot
        if (pos >= TotalEventCards) return null;
        var type = pos < FixedOrder.Length ? FixedOrder[pos] : ShuffledRemainder(rec.LoanFloor)[pos - FixedOrder.Length];
        if (type == typeof(SettlementCard))       return "SETTLEMENT";
        if (type == typeof(InvoiceCard))          return "INVOICE";
        if (type == typeof(InterestSupportCard))  return "INTEREST_SUPPORT";
        if (type == typeof(JobPlacementCard))     return "JOB_PLACEMENT";
        if (type == typeof(PaymentBenefitCard))   return "PAYMENT_BENEFIT";
        if (type == typeof(RefundCard))           return "REFUND";
        if (type == typeof(BloodPaymentCard))     return "BLOOD_PAYMENT";
        if (type == typeof(CounterclaimCard))     return "COUNTERCLAIM";
        if (type == typeof(StatementCard))        return "STATEMENT";
        return null;   // 정기 납부 (never "next") or unmapped → generic bark
    }

    private static void TryGrantDunningLetter(Player player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || player.RunState == null) return;
        if (player.RunState.TotalFloor == rec.LoanFloor) return;   // still the loan shop → not a "revisit"
        if (rec.EventGrantCount >= TotalEventCards) return;         // all event cards handed out

        int pos = rec.EventGrantCount;   // 0-based sequence index
        System.Type cardType = pos < FixedOrder.Length
            ? FixedOrder[pos]
            : ShuffledRemainder(rec.LoanFloor)[pos - FixedOrder.Length];

        rec.EventGrantCount++;
        if (cardType == typeof(DunningLetterCard)) rec.DunningLetterGranted = true;   // repay-vanish still keys on this
        _ = DebtLoanGrants.GrantCard(player, cardType);
        MerchantBark.SayGrant(NextEventCardHintKey(rec));   // hand a payoff card + hint the SPECIFIC next one
        SyncToRelic(player);
    }

    /// <summary>Total interest CHARGED on the loan so far = origination (20% of the borrowed loan) + the accrued node
    /// interest gold (on loan + card debt, absolute-capped). Grows as node interest accrues; independent of payments.</summary>
    internal static int InterestChargedNow(LoanRecord rec)
        => (int)Math.Round(rec.Borrowed * (DebtLoanConfig.BorrowOriginationPct / 100.0)) + rec.NodeInterestGold;

    /// <summary>Interest still owed = charged − already paid (never below 0). Payments clear this before principal.</summary>
    internal static int InterestRemaining(LoanRecord rec) => Math.Max(0, InterestChargedNow(rec) - rec.InterestPaid);

    /// <summary>A payment drained gold toward the loan. INTEREST FIRST: the payment retires outstanding interest
    /// before it eats into the borrowed principal (tracked via <see cref="LoanRecord.InterestPaid"/>); the total owed
    /// (<see cref="LoanRecord.Principal"/> = shop repay cost) always drops by the full amount. Pure record math; runs
    /// deterministically on both co-op peers in the lockstep combat. (principalShareOverride is now ignored — kept
    /// for signature compatibility with existing callers.)</summary>
    internal static async Task AccrueInterest(Player player, int drained, double? principalShareOverride = null)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || drained <= 0) return;
        rec.TotalPaid += drained;
        rec.CreditPaid += drained;   // ★납부는 언제나 신용이 된다(상한 없음)
        int toInterest = Math.Min(drained, InterestRemaining(rec));   // interest first
        rec.InterestPaid += toInterest;                               // the rest (drained − toInterest) is principal
        rec.Principal = Math.Max(0, rec.Principal - drained);         // total owed drops by the full payment
        SyncToRelic(player);
        await Task.CompletedTask;
    }

    /// <summary>취업알선 (Job Placement) placement fee: add <paramref name="amount"/> gold straight onto what you
    /// OWE (the shop repay cost / relic badge). You do NOT receive the gold — it's a fee, not a loan, so no gold
    /// enters your pocket and there is no surcharge. The payoff is the lump of 품삯 (Wages) the skill hands you on
    /// play. Needs an active loan. Pure record math off a lockstep card play → co-op safe.</summary>
    internal static void AddCombatDebt(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        rec.Principal += amount;   // owed goes up; the player gains no gold (it's a fee, not a loan)
        rec.CardDebt += amount;    // card-fee debt = principal that also accrues node interest
        SyncToRelic(player);
    }

    // ── 납부 (Payment) resource system ─────────────────────────────────────────
    // 영수증 (payment tally) is a CUSTOM combat resource, shown on its own HUD counter near the energy orb (like
    // Regent's Stars but our own, so no collision). It is NOT a power buff — it lives here as a per-combat value and
    // raises TallyChanged so the custom NPaymentTallyCounter node updates. Cards read it and CONSUME it. The value is
    // computed the same way on every peer (payments are lockstep), so the display stays in sync; the counter is local.
    private static readonly ConditionalWeakTable<Player, int[]> _tally = new();

    /// <summary>PER-COMBAT monotonic count of 납부 (Payment) cards played this combat — bumped on every
    /// <see cref="RecordPayment"/> and, UNLIKE the spendable 영수증 tally, NEVER consumed. 성실 납부
    /// (Diligent Payment) scales its Block off this so 정산/청구서 draining the tally doesn't shrink it. Reset
    /// each combat by <see cref="ResetPaymentsThisCombat"/>. Deterministic (payments are lockstep) → co-op safe.</summary>
    private static readonly ConditionalWeakTable<Player, int[]> _paymentCount = new();

    /// <summary>How many 납부 (DebtCurseCard) cards have been PLAYED-and-exhausted this combat. 성실 납부 (Diligent
    /// Payment) scales its Block off this — one Block per Payment card you actually spent (bailouts/other RecordPayment
    /// paths do NOT count; only a real 납부 card leaving via play). Monotonic, never consumed; reset each combat by
    /// <see cref="ResetPaymentsThisCombat"/>. Bumped by <see cref="RecordExhaustedPaymentCard"/> from DebtCurseCard.OnPlay.
    /// Deterministic (card plays are lockstep) → co-op safe.</summary>
    internal static int PaymentCountThisCombat(Player? p)
        => p != null && _paymentCount.TryGetValue(p, out var a) ? a[0] : 0;

    /// <summary>Count one 납부 (DebtCurseCard) card played-and-exhausted this combat (drives 성실 납부's Block).
    /// Called from DebtCurseCard.OnPlay so ONLY spent Payment cards count — not bailouts or other payment paths.</summary>
    internal static void RecordExhaustedPaymentCard(Player? p)
    {
        if (p != null) _paymentCount.GetValue(p, _ => new int[1])[0]++;
    }

    /// <summary>Fired whenever a player's 영수증 changes → the HUD counter re-renders. (player, newValue).</summary>
    internal static event Action<Player, int>? TallyChanged;

    /// <summary>Banked 납부 (Payment) count this combat. Read by 정산 (block × tally) and 청구서 (damage × tally),
    /// which then spend it via <see cref="ConsumePaymentStack"/>.</summary>
    internal static int PaymentsThisCombat(Player? p)
        => p != null && _tally.TryGetValue(p, out var a) ? a[0] : 0;

    private static void SetTally(Player p, int value)
    {
        var cell = _tally.GetValue(p, _ => new int[1]);
        if (cell[0] == value) return;
        cell[0] = value < 0 ? 0 : value;
        TallyChanged?.Invoke(p, cell[0]);
    }

    /// <summary>Bank <paramref name="n"/> extra 영수증 (receipts). 납부 calls this for the BONUS receipt when you
    /// actually paid gold — the base receipt already came from <see cref="RecordPayment"/>. HUD updates via SetTally.</summary>
    internal static void GrantReceipt(Player? p, int n = 1)
    {
        if (p != null && n != 0) SetTally(p, PaymentsThisCombat(p) + n);
    }

    /// <summary>What a 영수증-costing card ACTUALLY costs right now, after 경비 처리 (ExpensingPower) discounts.
    /// ★Single source of truth — the playable gate, the consume call and the cost badge must all read this, or the
    /// card greys out at a price different from the one printed on it.
    /// <para>X-cards (<see cref="IUsesPaymentTally.TallyCost"/> &lt; 0 = 청구서/정산, "spend the whole tally") are
    /// returned unchanged: there is no fixed price to discount, and clamping their sentinel to 0 would turn them into
    /// free no-ops.</para></summary>
    internal static int EffectiveTallyCost(int rawCost, Player? owner)
    {
        if (rawCost < 0) return rawCost;                                     // X = spend-all sentinel, not a price
        int cut = (int)(owner?.Creature?.GetPower<ExpensingPower>()?.Amount ?? 0m);
        return Math.Max(0, rawCost - cut);
    }

    /// <summary>Convenience overload for the cards themselves (they know their own raw cost and owner).</summary>
    internal static int EffectiveTallyCost(IUsesPaymentTally card, Player? owner)
        => EffectiveTallyCost(card.TallyCost, owner);

    /// <summary>Spend the WHOLE 영수증 tally (called by 청구서/정산 after they pay out). No-op if none.</summary>
    internal static Task ConsumePaymentStack(Player? p)
    {
        if (p != null) SetTally(p, 0);
        return Task.CompletedTask;
    }

    /// <summary>Spend a FIXED amount of 영수증 — used by power cards that cost N to install. Clamps at 0.</summary>
    internal static Task SpendTally(Player? p, int n)
    {
        if (p != null && n > 0) SetTally(p, PaymentsThisCombat(p) - n);
        return Task.CompletedTask;
    }

    // ── 파산 (Bankruptcy) — blocks ALL gold gain (in-combat AND the post-combat reward) until the next fight ──────
    private static readonly ConditionalWeakTable<Player, bool[]> _bankrupt = new();

    /// <summary>True while 파산 선언 (Declare Bankruptcy) has locked this player out of gold. The power's in-combat
    /// ModifyGoldGained covers the fight; this flag is what BankruptGoldBlockPatch reads to ALSO block the
    /// post-combat reward gold (the power is gone by then). Cleared at the next combat start (below).</summary>
    internal static bool IsBankrupt(Player? p) => p != null && _bankrupt.TryGetValue(p, out var a) && a[0];

    /// <summary>Set by 파산 선언 on play — no gold this fight, including the victory reward, until the next combat.</summary>
    internal static void SetBankrupt(Player? p) { if (p != null) _bankrupt.GetValue(p, _ => new bool[1])[0] = true; }

    /// <summary>Clear the tally at combat start (fresh each fight).</summary>
    internal static Task ResetPaymentsThisCombat(Player p)
    {
        SetTally(p, 0);
        if (_paymentCount.TryGetValue(p, out var a)) a[0] = 0;   // reset the monotonic 납부 count too
        if (_bankrupt.TryGetValue(p, out var bk)) bk[0] = false;  // 파산 clears at the next fight (reward of the PAST fight was already blocked)
        return Task.CompletedTask;
    }

    /// <summary>The unified 납부 (Payment) entry: pay the loan's PRINCIPAL down 1:1 (the whole payment goes to
    /// principal — the interest is the up-front 50% surcharge baked in at loan time, not a per-payment cut),
    /// bump the per-combat payment counter, then fire the payment-reactive powers (납부 혜택 → Plating, 환급 → a
    /// 성실 납부 card). Called by the Debt cards after the gold is taken (or, for the HP-payment card, after the
    /// HP loss). The AccrueInterest math is deterministic on both peers; the power effects are self-appliers →
    /// co-op safe.</summary>
    internal static async Task RecordPayment(Player player, PlayerChoiceContext cc, int amount)
    {
        var rec0 = For(player);
        bool wasOwing = rec0 != null && rec0.Active && rec0.Principal > 0;   // did this payment have a debt to clear?
        await AccrueInterest(player, amount, principalShareOverride: 1.0);   // 100% to principal (interest = the surcharge)
        if (player?.Creature == null) return;
        SetTally(player, PaymentsThisCombat(player) + 1);   // 영수증 +1 → HUD counter updates
        // NOTE: 성실 납부's block counter (_paymentCount) is NOT bumped here — it counts only 납부 CARDS actually
        // played-and-exhausted (RecordExhaustedPaymentCard from DebtCurseCard.OnPlay), not every payment path.
        if (rec0 != null && rec0.Active) { rec0.LifetimePayments++; SyncToRelic(player); }   // milestone counter (combat cards)
        var benefit = player.Creature.GetPower<PaymentBenefitPower>();
        if (benefit != null) await benefit.OnPayment(cc, player);
        var refund = player.Creature.GetPower<RefundPower>();
        if (refund != null) await refund.OnPayment(cc, player);
        var counterclaim = player.Creature.GetPower<CounterclaimPower>();
        if (counterclaim != null) await counterclaim.OnPayment(cc, player);
        var statement = player.Creature.GetPower<StatementPower>();
        if (statement != null) await statement.OnPayment(cc, player);
        var interestSupport = player.Creature.GetPower<InterestSupportPower>();
        if (interestSupport != null) await interestSupport.OnPayment(cc, player, amount);   // refunds half the payment
        // NOTE: 추심 (CollectionPower) no longer triggers on payment — it grants Vigor at each turn start (see
        // CollectionPower.AfterPlayerTurnStart), scaling off the 영수증 tally, so nothing is wired here.

        // Paid the loan off mid-combat? Lift the whole debt right now (see SettleLoanInCombat).
        if (wasOwing)
        {
            var rec = For(player);
            if (rec != null && rec.Active && rec.Principal <= 0) await SettleLoanInCombat(player);
        }
    }

    /// <summary>A payment drove the principal to 0 DURING combat — settle the loan immediately instead of waiting
    /// for a shop. Strips the injected Debt curses from combat (so 강제 징수 stops collecting the moment you're
    /// square), removes the 신용 불량 spawner power, then runs the normal repay settle (remove the Ledger relic +
    /// clear the record so credit is restored). The satisfying "debt cleared, curse lifted" beat — and it fixes
    /// collections continuing after you no longer owe anything. Runs in the lockstep payment path (principal hits
    /// 0 identically on both peers; relic/card removals are local per-peer). ⚠️ co-op contagion: this clears ALL
    /// Debt curses in your combat, including any seeped from a partner's still-active loan — verify coop-verify.</summary>
    internal static async Task SettleLoanInCombat(Player player)
    {
        MainFile.Logger.Info($"[{MainFile.ModId}] loan paid off in combat — lifting the debt immediately.");
        await DebtLoanGrants.RemoveDebtCardsFromCombat(player);      // stop the injected curses taxing/debuffing NOW
        if (player.Creature != null && player.Creature.GetPower<BadCreditPower>() != null)
            await PowerCmd.Remove<BadCreditPower>(player.Creature);  // kill the 강제 징수 spawner so its icon clears too
        await ApplyRepay(player);                                   // Active=false + 원금 0 + 네이티브 Debt sweep (유물·결제 카드는 유지)
    }

    /// <summary>강제 청산(강제 징수·가압류)으로 닫힌 계약의 뒷정리. ★이게 없으면 <b>청산 경로에 따라 결과가
    /// 달라진다</b> — 납부/채무 조정으로 갚으면 네이티브 Debt 저주가 쓸려나가는데, HP·골드를 뜯겨 청산된
    /// 경우에만 그 저주가 덱에 남았다(사용 불가 + 손에 있으면 턴당 −10골드인 순수 하방). 가장 가혹한 경로가
    /// 가장 큰 벌을 남기는 셈이라 명백한 비일관이었다.
    /// <para><see cref="ForceRepayPrincipal"/> 이 동기라 그 자리에서 못 하므로 플래그를 세워 두고, 강제 징수
    /// 카드(async)와 방 진입 훅 두 곳에서 처리한다. 양 피어가 같은 lockstep 지점에서 플래그를 세우고 각자
    /// 로컬 덱을 정리하므로 수렴한다.</para></summary>
    internal static async Task FinishForcedSettle(Player? player)
    {
        var rec = For(player);
        if (rec == null || player == null || !rec.PendingSettleCleanup) return;
        rec.PendingSettleCleanup = false;
        await DebtLoanGrants.RemoveDebtCardsFromCombat(player);
        await DebtLoanGrants.RemoveNativeDebtCards(player);
        if (player.RunState != null) rec.LoanFloor = player.RunState.TotalFloor;
        rec.CardDebt = 0; rec.Borrowed = 0; rec.InterestPaid = 0; rec.NodeInterestGold = 0;
        rec.InterestPctApplied = 0; rec.RestructuringUsed = false; rec.LoanDraws = 0;
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        MainFile.Logger.Info($"[{MainFile.ModId}] forced settle cleaned up (native Debt swept, cycle reset).");
    }

    /// <summary>Draws still available on this loan (∞ when <see cref="DebtLoanConfig.MaxLoanDraws"/> ≤ 0).
    /// Single source of truth for the <see cref="CanLoanCover"/> gate and the shop chip.</summary>
    internal static int DrawsLeft(LoanRecord? rec)
    {
        int max = DebtLoanConfig.MaxLoanDraws;
        if (max <= 0) return int.MaxValue;               // 0 = unlimited (ModConfig)
        return Math.Max(0, max - (rec?.LoanDraws ?? 0));
    }

    /// <summary>Draws left for this player, for UI. No loan yet ⇒ the full allowance.</summary>
    internal static int DrawsLeftFor(Player? p)
    {
        var rec = For(p);
        return DrawsLeft(rec != null && rec.Active ? rec : null);
    }

    /// <summary>Principal still owed on this player's ACTIVE loan (0 if there is no live loan). The read-only view
    /// 레버리지 scales its damage off and 어음 gates on. Never negative.</summary>
    internal static int PrincipalOf(Player? player)
    {
        var rec = For(player);
        return rec != null && rec.Active ? Math.Max(0, rec.Principal) : 0;
    }

    /// <summary>Is this player's once-per-loan 채무 조정 (Restructuring) still available? Needs a live loan with
    /// principal left AND an unspent use. Single source of truth for the card's IsPlayable, the shop's offer list
    /// and <see cref="IsPurchasable"/>, so the three can't drift apart.</summary>
    internal static bool CanRestructure(Player? player)
    {
        var rec = For(player);
        return rec != null && rec.Active && rec.Principal > 0 && !rec.RestructuringUsed;
    }

    /// <summary>Burn this loan's single 채무 조정 use. Called BEFORE the write-off, because a write-off that clears
    /// the loan closes the contract (SettleLoanInCombat → ApplyRepay → Active=false + RestructuringUsed 리셋),
    /// and setting the flag afterwards would arm it against the NEXT loan instead of this one.</summary>
    internal static void MarkRestructuringUsed(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active) return;
        rec.RestructuringUsed = true;
        SyncToRelic(player!);
    }

    /// <summary>Write principal off the ledger as FORGIVENESS — no gold and no HP changed hands (채무 조정). Retires
    /// outstanding interest first, exactly like a payment, so the relic hover's "interest vs principal" split stays
    /// coherent. ★It deliberately does NOT touch <see cref="LoanRecord.TotalPaid"/>: that is the "gold you actually
    /// paid" line the 신용 회복 reward ladder gates on (DebtLoanConfig.CreditReward*), so escaping via restructuring clears the
    /// debt but forfeits the medal for having worked it off. Clearing the principal settles the loan mid-combat (the
    /// curses lift right there) instead of leaving it for the next shop. Pure record math off a lockstep card play →
    /// co-op safe.</summary>
    internal static async Task ForgivePrincipal(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        int cut = Math.Min(rec.Principal, amount);
        rec.InterestPaid += Math.Min(cut, InterestRemaining(rec));   // interest first (same order as a payment)
        rec.Principal = Math.Max(0, rec.Principal - cut);
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        MainFile.Logger.Info($"[{MainFile.ModId}] forgave {cut} principal (owed now {rec.Principal}).");
        if (rec.Principal <= 0) await SettleLoanInCombat(player);
    }

    /// <summary>The 강제 징수 (Forced Collection) writes principal off DIRECTLY — no gold, it's paid in HP. So
    /// the whole amount retires principal (all "principal", no interest split), counts toward TotalPaid, and
    /// once principal hits 0 the loan is settled (record only — the relic drops at the next shop). Pure record
    /// math off the lockstep turn-end, identical on both peers.</summary>
    internal static void ForceRepayPrincipal(Player player, int amount)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || amount <= 0) return;
        int cut = Math.Min(rec.Principal, amount);
        rec.InterestPaid += Math.Min(cut, InterestRemaining(rec));   // interest first (same order as a normal payment)
        rec.Principal = Math.Max(0, rec.Principal - cut);
        rec.TotalPaid += cut;
        rec.CreditPaid += cut;       // 강제 징수·가압류도 '빚을 안고 굴린 대가' → 납부와 동급
        // 원금이 0이면 계약을 닫는다. ★이 경로는 동기 메서드(가압류는 GainGold 프리픽스에서 호출)라
        // await ApplyRepay 를 못 부른다 → 청산 보상·네이티브 Debt 정리는 여기서 일어나지 않는다.
        // 대신 다음 대출이 ApplyActiveLoan 에서 사이클 회계를 리셋하므로 상태는 어긋나지 않는다.
        if (rec.Principal <= 0) { rec.Active = false; rec.PendingSettleCleanup = true; }   // spiral self-terminates
        SyncToRelic(player);
    }

    // Set only while the loan disbursement's GainGold runs, so garnishment doesn't skim the borrowed gold itself.
    private static Player? _grantingFor;

    /// <summary>The node-interest ceiling (%) for this player — base 40% (NodeInterestPct × MaxNodeInterestRooms),
    /// grown by borrower count in co-op. When <see cref="LoanRecord.InterestPctApplied"/> reaches this, interest is
    /// MAXED. Shared by the interest accrual and the garnishment trigger so they can't drift.</summary>
    internal static int NodeInterestCapPct(Player? player)
    {
        int n = Math.Max(1, player?.RunState != null ? BorrowerCount(player.RunState) : 1);
        int baseCap = DebtLoanConfig.NodeInterestPct * DebtLoanConfig.MaxNodeInterestRooms;   // 40% SP
        return baseCap + Math.Min(DebtLoanConfig.MpInterestExtraCapMaxPct,
                                  DebtLoanConfig.MpInterestExtraCapPerBorrowerPct * (n - 1));
    }

    /// <summary>Creditor garnishment: ONLY once the loan's interest has hit its MAXIMUM (node interest at the cap)
    /// does the creditor start withholding a share (<see cref="DebtLoanConfig.GarnishMaxPct"/>) of GOLD INCOME and
    /// applying it straight to the debt as forced repayment. Below max interest → nothing. Returns the gold garnished
    /// (≤ income, ≤ remaining principal); the caller hands the player income − garnished. Deterministic per-player
    /// record math (ForceRepayPrincipal) → co-op mirrors it as each peer replays the gold gain.</summary>
    internal static int GarnishIncome(Player player, int income)
    {
        if (ReferenceEquals(_grantingFor, player)) return 0;   // don't garnish the loan disbursement itself
        var rec = For(player);
        if (rec == null || !rec.Active || rec.Principal <= 0 || income <= 0) return 0;
        if (rec.InterestPctApplied < NodeInterestCapPct(player)) return 0;   // only when interest is MAXED
        int ratePct = DebtLoanConfig.GarnishMaxPct;
        if (ratePct <= 0) return 0;
        int garnish = Math.Min(rec.Principal, (int)Math.Floor(income * (ratePct / 100.0)));
        if (garnish <= 0) return 0;
        ForceRepayPrincipal(player, garnish);
        return garnish;
    }

    // ── MP 대납 (Bailout) — help a teammate pay down their debt ────────────────────────────────────────
    /// <summary>Co-op 대납 (Bailout): the <paramref name="payer"/> spends <paramref name="amount"/> gold to make a
    /// 납부 (Payment) on the <paramref name="debtor"/>'s behalf. It routes through <see cref="RecordPayment"/> so it
    /// is a REAL payment FOR THE DEBTOR — their 영수증 (payment tally) accumulates, their payment powers fire, and it
    /// settles their loan mid-combat if it clears (lifts their curses). Only the GOLD comes from the payer; the
    /// principal write-down + tally are the debtor's. Runs INSIDE the lockstep card play (the debtor's CombatId rides
    /// the play action), so both peers resolve the same debtor and apply identically — no broadcast, like any combat
    /// card. The payer covers what they can afford. Returns the gold actually applied (0 if the target owes nothing
    /// or the payer is broke).</summary>
    internal static async Task<int> ApplyBailout(PlayerChoiceContext cc, Player payer, Player debtor, int amount)
    {
        var rec = For(debtor);
        if (rec == null || !rec.Active || rec.Principal <= 0 || amount <= 0) return 0;
        int cut = Math.Min(Math.Min(rec.Principal, amount), (int)payer.Gold);   // pay what you can afford, capped at the debt
        if (cut <= 0) return 0;

        await PlayerCmd.LoseGold(cut, payer, GoldLossType.Spent);   // payer foots the bill (lockstep card play → no RewardSync)
        await RecordPayment(debtor, cc, cut);                       // a real 납부 FOR THE DEBTOR: tally(영수증)++, powers fire, settle-on-zero
        MainFile.Logger.Info($"[{MainFile.ModId}] bailout: {payer.NetId} paid {cut}g toward {debtor.NetId}'s debt (owed now {For(debtor)?.Principal ?? 0}).");
        return cut;
    }

    /// <summary>MP 대납 (Bailout) on a MISSED payment: a 납부 (Payment) card left unplayed is about to Ethereal-exhaust
    /// for nothing, so hand the RICHEST teammate who can afford it (gold ≥ <see cref="BailoutCard.BailoutGold"/>) a
    /// 대납 card — Ethereal+Exhaust, upgraded to match a 빚 독촉+ — a fleeting chance to cover the debtor this turn. If
    /// NO teammate can afford one, nothing is injected. Runs in the lockstep turn-end-in-hand path over shared state
    /// (players + gold), so both peers pick the same recipient and deal the same card — co-op-safe, no broadcast.</summary>
    internal static async Task GrantBailoutForMissedPayment(Player debtor, bool upgraded)
    {
        if (RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true) return;   // co-op only
        var combat = debtor?.Creature?.CombatState;
        var run = RunManager.Instance?.State;
        if (combat == null || run?.Players == null || debtor == null) return;

        // Richest OTHER player who can actually pay a bailout. Deterministic: reads shared gold, strict > keeps the
        // FIRST in the (identical-on-both-peers) player order on a tie.
        Player? recipient = null;
        foreach (var p in run.Players)
        {
            if (p == debtor || p.Creature == null) continue;
            if ((int)p.Gold < BailoutCard.BailoutGold) continue;
            if (recipient == null || p.Gold > recipient.Gold) recipient = p;
        }
        if (recipient == null) return;   // nobody can afford it → no bailout injected

        var card = combat.CreateCard<BailoutCard>(recipient);
        if (card == null) return;
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }   // 빚 독촉+ missed → 대납+ (0-cost)
        await CardPileCmd.AddGeneratedCardsToCombat(new List<CardModel> { card }, PileType.Hand, recipient, CardPilePosition.Top);
        MainFile.Logger.Info($"[{MainFile.ModId}] missed payment by {debtor.NetId} → bailout{(upgraded ? "+" : "")} to {recipient.NetId} (gold {(int)recipient.Gold}).");
    }

    /// <summary>Repay the outstanding principal at a shop → 원금 0 · 등급 1로 리셋(유물과 결제 카드는 남는다).
    /// <para>★<b>빌린 그 상점에서는 갚을 수 없다</b>(<see cref="CanRepayHere"/>). 청산이 인출 횟수를 되돌려주는
    /// 구조라, 같은 상점에서 즉시 되갚기가 가능하면 "100 빌리고 120 갚기"를 무한 반복해 순 골드 200으로
    /// 최고 보상(누적 1200)까지 갈 수 있다. 다음 상점까지 걸어가게 만들면 그 사이 노드 이자(5%/방)가 붙고
    /// 방 수도 소모돼 사이클이 스스로 비싸진다.</para></summary>
    internal static async Task<bool> Repay(Player player)
    {
        var rec = For(player);
        // ★Principal > 0 까지 확인해야 한다: 재설계로 청산 뒤에도 record 가 살아남으므로, 원금 0 인 상태로
        // 다시 들어오면 0골드를 "상환"하고 ApplyRepay 를 한 번 더 돌려(보상 재판정·LoanFloor 재리셋) 버린다.
        if (rec == null || !rec.Active || rec.Principal <= 0) return false;
        if (!CanRepayHere(player)) return false;
        if ((int)player.Gold < rec.Principal) return false;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        int tier = DebtCardCountFor(player);   // how deep in debt they were → tier-specific repay bark
        int owed = rec.Principal;
        // ★목돈 상환도 '갚은 금액'이다 — 신용도(누적 상환액)에 반드시 더해야 한다. 예전엔 TotalPaid 가
        // 납부 카드(1회 20골드)만 셌는데, 보상 문턱이 300/600/900/1200 인 지금 그 산식으로는 문턱 하나를
        // 넘는 데 납부 15회가 필요해 사실상 도달 불가다. 청산이 신용을 쌓는 주된 경로가 되어야 한다.
        // ★★단 여기서 더하지 않는다 — 이 메서드는 **구매자 피어에서만** 돈다. 로컬에서 TotalPaid 를 올리면
        // 원격 피어는 그 증가분을 영영 못 받는다(예전엔 곧이어 새 대출이 totalPaid 를 전선에 실어 우연히
        // 덮어써 줘서 가려져 있었다 — 청산 후 재대출을 안 하면 그대로 갈라진다). 누적 상환액은 이제 보상
        // 사다리의 축이고 CreditRewardsTaken 은 체크섬을 타는 SavedProperty 라, 갈라지면 진짜 desync 다.
        // → 증가는 양 피어가 재생하는 ApplyRepay 안에서 한다(금액을 와이어에 싣는다).
        await PlayerCmd.LoseGold(owed, player, GoldLossType.Spent);
        run?.RewardSynchronizer?.SyncLocalGoldLost(owed);
        MainFile.Logger.Info($"[{MainFile.ModId}] repaid principal {owed}g — credit restored.");

        if (sp) await ApplyRepay(player, owed);
        else    DebtLoanNet.BroadcastRepay(player, owed);
        MerchantBark.SayRepay(tier);           // merchant reacts to being paid off (varies by how deep you were)
        return true;
    }

    /// <summary>Apply the repay settle locally: 원금을 0 으로 지우고 계약을 닫는다(<see cref="LoanRecord.Active"/>
    /// = false). 유물·결제 카드·누적 상환액은 남는다. Runs on EACH peer — directly in SP, or once per peer
    /// via the networked <c>dl_sync repaid</c> replay in co-op (deck mutations are local per-peer).</summary>
    /// <param name="paidAdd">이번 목돈 상환액(신용도에 더할 금액). ★여기서 더하는 이유 = 이 메서드만이
    /// 양 피어가 모두 재생하는 지점이다. 전투 중 납부로 원금이 0 이 된 경우(SettleLoanInCombat)는
    /// 이미 RecordPayment 가 낙스텝으로 TotalPaid 를 올렸으므로 0 을 넘긴다.</param>
    internal static async Task ApplyRepay(Player player, int paidAdd = 0)
    {
        var rec = For(player);
        if (rec == null || player?.RunState == null) return;
        if (!rec.Active) return;   // 이미 닫힌 계약 — 재진입 시 보상 재판정·리셋을 두 번 하지 않는다
        if (paidAdd > 0)
        {
            rec.TotalPaid += paidAdd;                      // 양 피어가 같은 값을 더한다(수치는 와이어로 온다)
            // ★목돈 상환은 신용도 6까지만 인정된다. 그 위로는 납부(AccrueInterest/강제 징수)만 신용을 올린다.
            //   두 피어가 같은 CreditPaid 에서 같은 클램프를 계산하므로 수렴한다.
            int room = Math.Max(0, DebtLoanConfig.LumpSumCreditCap - rec.CreditPaid);
            rec.CreditPaid += Math.Min(paidAdd, room);
        }

        // ★★청산의 의미가 바뀌었다: 유물도 카드도 사라지지 않는다.
        // 예전엔 유물 제거 + 결제 카드셋 전부 sweep + 기록 삭제였는데, 그러면 빚에서 벗어나려고 쌓아 올린
        // 엔진이 벗어나는 순간 같이 죽었다(모드에서 가장 컸던 구조적 결함). 이제 청산은 "원금이 0이 되고
        // 신용 등급이 1로 돌아간다"만 뜻한다 — 유물·결제 카드·누적 상환액은 그대로 남아 다음 대출로 이어진다.
        // 이자와 저주 주입은 이미 원금 0에서 자동으로 멈춘다(AccrueNodeInterest / DebtCardCountFor)이라
        // "돈을 빌렸을 때만 발동"은 별도 게이트 없이 성립한다.
        int credit = CreditScore(rec);                     // 청산 시점의 신용도(누적 상환액 기준) = 보상 등급

        // ★★`Active = false` 가 이 재설계의 핵심 불변식이다. 유물과 카드가 남는다고 해서 계약까지 열어두면
        // 안 된다 — 코드 전역이 `rec.Active` 를 "빚이 있다"로 읽는다(가격 할증·빚 상점·상환 버튼·전투 중
        // 청산 판정·톱업 게이트). 열어둔 채로 두면 그 판정이 전부 반대로 가서, 특히 CanLoanCover 의
        // "톱업은 빌린 상점에서만" 규칙이 영구히 적용돼 **다음 상점부터 영영 대출을 못 받는다**.
        // 남기는 것(유물·결제 카드·TotalPaid·CreditRewardsTaken)과 닫는 것(계약)은 별개다.
        rec.Active = false;
        rec.Principal = 0;
        rec.CardDebt = 0;
        rec.Borrowed = 0;                                  // 이자·한도 회계는 대출 사이클 단위 (TotalPaid·CreditPaid 만 런 단위로 누적)
        rec.InterestPaid = 0;                              // 다음 대출은 이자 회계를 새로 시작
        rec.NodeInterestGold = 0;
        rec.InterestPctApplied = 0;
        rec.LoanFloor = player.RunState.TotalFloor;        // 등급 1로 리셋(= 여기서부터 다시 센다)
        rec.RestructuringUsed = false;                     // 채무 조정의 "대출당 1회"도 새 대출 기준으로
        rec.LoanDraws = 0;                                 // 청산했으니 인출 3회 회복 = 신용을 되찾았다는 뜻
        // ★파밍 가드는 여기가 아니라 상환 지점에 있다 — 빌린 상점에서는 갚을 수 없으므로(CanRepayHere)
        // 사이클마다 다음 상점까지 걸어가야 하고, 그 사이 노드 이자가 붙어 루프가 스스로 비싸진다.
        SyncToRelic(player);

        await DebtLoanGrants.RemoveDebtCardsFromCombat(player);   // 전투 중 청산이면 주입된 저주 즉시 정리
        // ★네이티브 Debt 저주는 계속 쓸어낸다: 결제 카드셋(=엔진, 남긴다)과 달리 이건 '나쁜 빚'의 벌점이고
        // 사용 불가 + 손에 있으면 턴당 -10골드인 순수 하방이라, 남겨두면 청산이 보상이 아니라 처벌이 된다.
        await DebtLoanGrants.RemoveNativeDebtCards(player);
        // ★보상은 여기서 지급하지 않는다 — 청산은 문턱을 '열어줄' 뿐이고, 실제 수령은 빚 상점 헤더의
        // 수령 버튼(ClaimCreditReward)이 한다. 자동 지급이 아니라 버튼으로 바뀐 이유: ①900/1200 은 카드
        // 선택 화면이 필요해 청산 흐름 한가운데서 열면 곤란하고, ②수령 시점이 플레이어 손에 있어야
        // co-op 재생 경로가 단순해지며, ③사다리가 화면에 보이는 목표로 남는다.
        MainFile.Logger.Info($"[{MainFile.ModId}] settled: credit={credit} paid={rec.TotalPaid} "
                           + $"pendingRewards={PendingRewardCount(rec)}.");
    }

    /// <summary>신용도 = 누적 상환액 ÷ <see cref="DebtLoanConfig.GoldPerCreditPoint"/>. 청산해도 리셋되지
    /// 않는 런 단위 진행 트랙이라 여러 대출 사이클이 하나의 신용 이력으로 쌓인다. 빚 상점 헤더에 표시된다.</summary>
    internal static bool CanRepayHere(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.Active || player?.RunState == null) return true;
        return player.RunState.TotalFloor != rec.LoanFloor;   // 빌린 그 상점에서는 갚을 수 없다
    }

    internal static int CreditScore(LoanRecord? rec)
        => rec == null ? 0 : rec.CreditPaid / Math.Max(1, DebtLoanConfig.GoldPerCreditPoint);

    /// <summary>Convenience overload for UI.</summary>
    internal static int CreditScoreOf(Player? player) => CreditScore(For(player));

    // ── 신용 보상 사다리 (순차 해금 + 무한 보너스) ────────────────────────────────────────────────
    /// <summary>고정 4단계의 문턱(누적 상환액). 그 뒤로는 <see cref="RewardTierAt"/> 가 무한히 이어간다.</summary>
    internal static int[] CreditRewardTiers => new[]
    {
        DebtLoanConfig.CreditRewardCard, DebtLoanConfig.CreditRewardUpgraded,
        DebtLoanConfig.CreditRewardUpgradeAny, DebtLoanConfig.CreditRewardRemoveAny,
    };

    /// <summary>보너스 단계의 간격(골드). 신용도 2 = 200 골드.</summary>
    internal static int BonusRewardStep => DebtLoanConfig.BonusRewardCredits * DebtLoanConfig.GoldPerCreditPoint;

    /// <summary><paramref name="index"/> 번째 단계의 문턱(누적 상환액). 0..3 은 고정 사다리,
    /// 4 부터는 <b>끝없는 보너스</b> — 마지막 고정 문턱에서 <see cref="BonusRewardStep"/> 씩 올라간다.
    /// <para>★사다리에 끝이 없어야 하는 이유(유저 설계): 신용도는 청산해도 리셋되지 않는 런 단위 트랙이라
    /// 후반에 계속 쌓이는데, 12 에서 멈추면 그 뒤의 상환이 아무 의미가 없어진다.</para></summary>
    internal static int RewardTierAt(int index)
    {
        var fixedTiers = CreditRewardTiers;
        if (index < 0) return 0;
        if (index < fixedTiers.Length) return fixedTiers[index];
        return fixedTiers[fixedTiers.Length - 1] + (index - fixedTiers.Length + 1) * BonusRewardStep;
    }

    /// <summary>이 단계가 <b>보너스</b>(고정 4단계 이후의 무한 구간)인가.</summary>
    internal static bool IsBonusReward(int index) => index >= CreditRewardTiers.Length;

    /// <summary>보너스 단계의 보상 종류: <b>제거 → 강화 → 제거 …</b> 로 교대한다(유저 설계).
    /// <para>★고르게 하지 않고 교대로 바꾼 이유: 택1은 매번 "당연히 제거"로 굳어져 선택이 아니었고, 칩도 두
    /// 개로 갈라져 화면이 번잡했다. 교대는 다음에 뭐가 올지 미리 보이므로 <b>언제 청산할지</b>를 계획하게
    /// 만든다 — 이 모드가 원하는 결정과 같은 종류의 결정이다.</para></summary>
    internal static bool BonusIsRemoval(int index) => ((index - CreditRewardTiers.Length) % 2) == 0;

    // ★상태 = 두 값이 전부다. 도달 = TotalPaid, 수령 = CreditRewardsTaken(받은 단계 수).
    //   ★★비트마스크에서 '개수'로 되돌린 이유 = 수령이 **순차**가 됐기 때문이다(앞 단계를 받아야 다음이
    //   열린다) → 받은 집합은 항상 접두사라 개수 하나로 완전히 표현된다. 게다가 보너스가 무한이라
    //   32비트 마스크로는 애초에 표현할 수 없다.
    /// <summary>지금 받을 차례인 단계의 인덱스(= 이미 받은 개수).</summary>
    internal static int NextRewardIndex(LoanRecord? rec) => Math.Max(0, rec?.CreditRewardsTaken ?? 0);

    /// <summary>지금 받을 차례인 단계의 문턱.</summary>
    internal static int NextRewardTier(LoanRecord? rec) => RewardTierAt(NextRewardIndex(rec));

    /// <summary>이 인덱스를 이미 받았는가. 순차라서 "인덱스 &lt; 받은 개수"가 곧 수령 여부다.</summary>
    internal static bool IsRewardClaimedAt(LoanRecord? rec, int index) => index < NextRewardIndex(rec);

    /// <summary>지금 누를 수 있는가 — <b>다음 차례</b>이면서 문턱에 도달했을 때만. ★앞 단계를 건너뛸 수 없다:
    /// 사다리가 한 칸씩 열려야 600(= 300 에서 받은 카드를 강화)이 300 을 전제로 성립하고, 화면에도
    /// "지금 할 일" 하나만 남는다.</summary>
    internal static bool CanClaimNextReward(LoanRecord? rec)
        => rec != null && rec.CreditPaid >= NextRewardTier(rec);

    /// <summary>지금 수령 가능한 문턱(없으면 0).</summary>
    internal static int NextClaimableReward(LoanRecord? rec)
        => CanClaimNextReward(rec) ? NextRewardTier(rec) : 0;

    /// <summary>밀린 보상 개수 — 목돈 상환 한 번에 여러 단계를 넘길 수 있다(순차라 하나씩 눌러야 한다).</summary>
    internal static int PendingRewardCount(LoanRecord? rec)
    {
        if (rec == null) return 0;
        int n = 0, i = NextRewardIndex(rec);
        while (rec.CreditPaid >= RewardTierAt(i) && n < 64) { n++; i++; }   // 64 = 폭주 방지 상한
        return n;
    }

    /// <summary>아직 도달하지 못한 다음 문턱 — 호버의 "앞으로 N 골드" 용. 사다리가 무한하므로 항상 값이 있다.</summary>
    internal static int NextUnreachedTier(LoanRecord? rec)
    {
        int i = NextRewardIndex(rec);
        while (rec != null && rec.CreditPaid >= RewardTierAt(i) && i < 4096) i++;
        return RewardTierAt(i);
    }

    internal static bool HasUnclaimedCreditReward(Player? player) => CanClaimNextReward(For(player));

    /// <summary>보상 수령(플레이어가 칩을 눌렀다). 로컬 게이트만 여기서 보고, 실제 적용은 co-op 이면
    /// <c>dl_sync claim</c> 재생으로 <b>양 피어가 함께</b> 돈다.
    /// <para>★★900/1200/보너스는 카드 선택 화면을 여는데, 그 선택의 co-op 동기화는 <b>엔진이 이미 한다</b> —
    /// <c>CardSelectCmd.FromDeckForUpgrade/ForRemoval</c> 이 <c>ReserveChoiceId</c> → 한쪽은
    /// <c>SyncLocalChoice</c>, 다른 쪽은 <c>WaitForRemoteChoice</c> 로 맞춘다(실 DLL 확인). 그래서 고른
    /// 카드를 직접 와이어에 실을 필요가 없다. <b>단 전제가 하나</b>: 양 피어가 같은 큐 위치에서 그 핸드셰이크에
    /// 들어가야 하므로 재생이 <b>detached 면 안 된다</b> → <c>dl_sync claim</c> 은 Task 를 CmdResult 에 실어
    /// 액션이 await 하게 한다(dl_testcard 와 동형).
    /// ⚠️테스트 하네스가 <c>CardSelectCmd.PushSelector</c> 로 셀렉터를 밀어 넣으면 그 동기화 블록이 통째로
    /// 건너뛰어진다(각 피어가 독립 선택) — 하네스 한정 함정이지 실플 경로가 아니다.</para></summary>
    /// <param name="removeChoice">보너스 단계에서만 의미: true = 카드 제거, false = 카드 강화.
    /// ★이 선택은 <b>와이어에 싣는다</b> — 어느 쪽 화면을 열지는 두 피어가 반드시 같아야 한다.</param>
    internal static async Task<bool> ClaimCreditReward(Player player)
    {
        var rec = For(player);
        if (rec == null || !CanClaimNextReward(rec)) return false;

        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;   // 남의 장부는 못 건드린다

        int index = NextRewardIndex(rec);
        if (sp) await ApplyClaimReward(player, index);
        else    DebtLoanNet.BroadcastClaim(player, index);
        return true;
    }

    /// <summary>보상 수령을 실제로 적용한다 — SP 는 직접, co-op 은 <c>dl_sync claim</c> 재생으로 각 피어가 1회.
    /// 덱 변경은 피어별 로컬이고 카드 선택은 엔진이 동기화한다.</summary>
    internal static async Task ApplyClaimReward(Player player, int index)
    {
        var rec = For(player);
        if (rec == null) return;
        // 순차 + 멱등: 지금 차례가 아닌 인덱스는 전부 무시한다(재전달·자기 재생·순서 뒤바뀜 모두 무해).
        if (index != NextRewardIndex(rec) || rec.CreditPaid < RewardTierAt(index)) return;
        rec.CreditRewardsTaken = index + 1;   // ★선(先)기록: 선택 화면에서 취소해도 단계는 소비된다
        SyncToRelic(player);

        if (IsBonusReward(index))
        {
            // 보너스: 제거 → 강화 → 제거 … 교대. 인덱스에서 결정되므로 와이어에 선택을 실을 필요가 없다.
            if (BonusIsRemoval(index)) await DebtLoanGrants.RemoveChosenDeckCard(player);
            else                       await DebtLoanGrants.UpgradeChosenDeckCard(player);
        }
        else if (index == 0) await DebtLoanGrants.GrantRewardCard(player, upgraded: false);
        else if (index == 1) await DebtLoanGrants.UpgradeOrGrantRewardCard(player);
        else if (index == 2) await DebtLoanGrants.UpgradeChosenDeckCard(player);
        else if (index == 3) await DebtLoanGrants.RemoveChosenDeckCard(player);

        MainFile.Logger.Info($"[{MainFile.ModId}] credit reward #{index} claimed at {RewardTierAt(index)} "
                           + $"(credit-paid {rec.CreditPaid}{(IsBonusReward(index) ? (BonusIsRemoval(index) ? ", bonus:remove" : ", bonus:upgrade") : "")}).");
    }

    // ── 빚으로 카드 제거 (상인 제거 1회에 '더해' 추가 제거 · 방문당 1회 · 외상 한도와 무관) ──────────────────────────────────────────
    /// <summary>이 상점에서 카드 1장을 제거하는 값. ★상인의 제거 슬롯과 <b>같은 공식</b>을 쓴다
    /// (<c>MerchantCardRemovalEntry.CalcCost</c>: 기본 75 + 25×이미 쓴 횟수, Inflation 승천이면 100 + 50×).
    /// 같은 카운터(<c>ExtraFields.CardShopRemovalsUsed</c>)를 읽고 <b>올리기도</b> 하므로, 빚으로 한 제거가
    /// 상인의 다음 제거값도 올린다 — "제거를 한 번 더 살 수 있다"이지 "싸게 산다"가 아니다.</summary>
    internal static int PurgePrice(Player? player)
    {
        int used = player?.ExtraFields?.CardShopRemovalsUsed ?? 0;
        int baseCost = MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry.PriceIncrease >= 50 ? 100 : 75;
        return baseCost + MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry.PriceIncrease * used;
    }

    /// <summary>빚으로 제거를 살 수 있는가 — 유물 보유 + 이번 방문에 아직 안 썼을 것 + 제거할 카드가 있을 것.
    /// <para>★<b>방문당 외상 한도(ShopCreditLimit)와는 무관</b>하다(유저 결정). 제거는 카드 구매와
    /// 예산을 다투지 않고, 한도를 깎아먹지도 않는다 — 제약은 오직 <b>방문당 1회</b>와 오르는 가격뿐.</para></summary>
    internal static bool CanPurgeOnDebt(Player? player)
    {
        var rec = For(player);
        if (rec == null || !rec.RelicGranted || player?.RunState == null) return false;
        if (rec.PurgedThisVisit) return false;                             // ★방문당 하드 1회
        var deck = PileType.Deck.GetPile(player)?.Cards;
        return deck != null && deck.Any(c => c.IsRemovable);
    }

    /// <summary>구매자 로컬 게이트 → co-op 이면 <c>dl_sync purge</c> 로 양 피어가 함께 적용.
    /// 가격은 <b>와이어에 싣는다</b>: 원격 피어의 <c>CardShopRemovalsUsed</c> 가 먼저 증가해 값이 갈릴 여지를
    /// 없앤다(<c>dl_sync buy</c> 가 가격을 싣는 것과 같은 이유).</summary>
    internal static async Task<bool> PurgeCardOnDebt(Player player)
    {
        if (!CanPurgeOnDebt(player)) return false;
        var run = RunManager.Instance;
        bool sp = run?.IsSingleplayerOrFakeMultiplayer ?? true;
        if (!(sp || LocalContext.IsMe(player))) return false;

        int price = PurgePrice(player);
        if (sp) await ApplyPurgeCard(player, price);
        else    DebtLoanNet.BroadcastPurge(player, price);
        return true;
    }

    /// <summary>제거를 적용한다: 빚을 먼저 지우고(취소해도 카드값은 안 물린다 — 아래 참고) 선택 화면을 연다.</summary>
    internal static async Task ApplyPurgeCard(Player player, int price)
    {
        var rec = For(player);
        if (rec == null) return;
        // ★카드를 실제로 고른 뒤에 과금한다 — 취소 가능한 화면이라 먼저 물리면 "돈만 내고 아무것도 못 지움"이 된다.
        var removed = await DebtLoanGrants.RemoveChosenDeckCard(player, cancelable: true);
        if (removed == null) return;                       // 취소 → 빚도 카운터도 그대로

        rec.CardDebt += price;
        rec.Principal += price;
        rec.PurgedThisVisit = true;                        // 이번 방문의 제거 기회를 썼다(양 피어가 함께 기록)
        if (player.ExtraFields != null) player.ExtraFields.CardShopRemovalsUsed++;   // 다음 제거값이 오른다(상인과 공유)
        SyncToRelic(player);
        RefreshRelicDisplay(player);
        MainFile.Logger.Info($"[{MainFile.ModId}] purged '{removed.Id.Entry}' on debt for {price} (owed now {rec.Principal}).");
    }
}
