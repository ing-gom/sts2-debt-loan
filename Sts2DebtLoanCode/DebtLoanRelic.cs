using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;            // CardSelectorPrefs (덤 선택 화면 프롬프트/옵션)
using MegaCrit.Sts2.Core.Commands;                // RelicCmd, CardPileCmd, CardSelectCmd
using MegaCrit.Sts2.Core.Entities.Cards;          // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;         // RelicRarity
using MegaCrit.Sts2.Core.HoverTips;               // HoverTipFactory, IHoverTip (Debt-card preview in tooltip)
using MegaCrit.Sts2.Core.Localization.DynamicVars; // DynamicVar (per-relic hover values)
using MegaCrit.Sts2.Core.Models;                  // RelicModel, ModelDb
using MegaCrit.Sts2.Core.Nodes.CommonUi;          // CardPreviewStyle (CardCmd.Upgrade)
using MegaCrit.Sts2.Core.Saves.Runs;              // SavedProperty, SerializationCondition

namespace Sts2DebtLoan;

/// <summary>
/// The "Merchant's Ledger" relic. Granted the instant you take a loan; it carries the whole loan state
/// (as [SavedProperty] fields) and, at the START of each combat, injects the current number of Debt
/// curse cards (1/2/3 by rooms since the loan) into the draw pile — temporary cards that vanish at
/// combat end rather than clogging the deck. Disabled (kept, greyed) if the loan defaults at 200%.
/// </summary>
public sealed class DebtLoanRelic : RelicModel
{
    // Event rarity = grant-only: reward/shop pools only roll Common/Uncommon/Rare/Shop, so it never drops.
    public override RelicRarity Rarity => RelicRarity.Event;

    public override string PackedIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";
    protected override string PackedIconOutlinePath => "res://Sts2DebtLoan/icons/debt_loan_relic_outline.png";
    protected override string BigIconPath => "res://Sts2DebtLoan/icons/debt_loan_relic.png";

    private int _borrowed, _principal, _totalPaid, _loanFloor, _interestPctApplied, _interestPaid, _cardDebt, _nodeInterestGold;
    private bool _active;
    private int _cards;   // transient (not saved): current per-combat Debt-card count, for the hover {cards}

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Borrowed { get => _borrowed; set { AssertMutable(); _borrowed = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Principal { get => _principal; set { AssertMutable(); _principal = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int TotalPaid { get => _totalPaid; set { AssertMutable(); _totalPaid = value; } }

    /// <summary>Total node-interest percent (of Borrowed) already baked into Principal (so it isn't re-charged on
    /// reload). Percent, not rooms, because the per-room rate now scales with the number of borrowers in co-op.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int InterestPctApplied { get => _interestPctApplied; set { AssertMutable(); _interestPctApplied = value; } }

    /// <summary>Interest paid off so far (payments retire interest before principal). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int InterestPaid { get => _interestPaid; set { AssertMutable(); _interestPaid = value; } }

    /// <summary>Card/shop debt (part of principal + node interest base). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CardDebt { get => _cardDebt; set { AssertMutable(); _cardDebt = value; } }

    /// <summary>Absolute node interest accrued (gold). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int NodeInterestGold { get => _nodeInterestGold; set { AssertMutable(); _nodeInterestGold = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LoanFloor { get => _loanFloor; set { AssertMutable(); _loanFloor = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool Active { get => _active; set { AssertMutable(); _active = value; InvokeDisplayAmountChanged(); } }

    private bool _dunningLetterGranted;
    /// <summary>Whether the 독촉장 (Dunning Letter) leverage card has already been handed to the deck this loan
    /// (granted once, on the first visit to a shop OTHER than the loan shop). Persisted so a reload doesn't
    /// re-grant it. Cleared with the loan on repay (the card is removed alongside the relic).</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool DunningLetterGranted { get => _dunningLetterGranted; set { AssertMutable(); _dunningLetterGranted = value; } }

    private bool _restructuringUsed;
    /// <summary>Whether this loan's ONE 채무 조정 (Restructuring) write-off has been spent. Persisted so a reload
    /// can't hand back the once-per-loan use (which would turn the card into a repeatable principal deleter — see
    /// <see cref="LoanRecord.RestructuringUsed"/>). Cleared with the loan on repay.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool RestructuringUsed { get => _restructuringUsed; set { AssertMutable(); _restructuringUsed = value; } }

    private int _loanDraws;
    /// <summary>Gold draws taken on this loan (first borrow + top-ups), capped by DebtLoanConfig.MaxLoanDraws.
    /// Persisted so a reload can't hand back spent draws. Cleared with the loan on repay.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LoanDraws { get => _loanDraws; set { AssertMutable(); _loanDraws = value; } }

    private int _creditRewardsTaken;
    /// <summary>지금까지 받은 보상 단계 수(순차 해금이라 개수 하나로 충분 + 보너스가 무한이라 마스크 불가). 누적 상환액은 청산해도 리셋되지 않으므로
    /// 이게 없으면 청산할 때마다 같은 보상을 다시 받는다. See <see cref="LoanRecord.CreditRewardsTaken"/>.
    /// <para>★영속으로 복구됨(2026-07-27c). 한동안 <c>[SavedProperty]</c> 를 뗀 채 뒀는데, 그건 "새 저장
    /// 프로퍼티가 크래시 원인"이라는 가설 때문이었다. 실제 원인은 테스트 헬퍼의 무한 재귀(스택 오버플로)로
    /// 밝혀져 그 가설이 기각됐다. 비영속으로 두면 리로드 때 0 으로 돌아가 같은 보상을 다시 받을 수 있다.</para></summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CreditRewardsTaken { get => _creditRewardsTaken; set { AssertMutable(); _creditRewardsTaken = value; } }

    private int _eventGrantCount;
    /// <summary>How many of the SHOP power cards have been handed out (one per shop-revisit). Persisted so the
    /// fixed order (1st=정기 납부) + per-run shuffle survives reloads.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int EventGrantCount { get => _eventGrantCount; set { AssertMutable(); _eventGrantCount = value; } }

    private int _lifetimePayments;
    /// <summary>Run-wide 납부 count while this loan is active — the milestone counter that earns the non-power
    /// combat cards (정산/청구서/혈납), one per 10. Persisted so the milestone survives reloads.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LifetimePayments { get => _lifetimePayments; set { AssertMutable(); _lifetimePayments = value; } }

    private int _debtShopVisits;
    /// <summary>How many DISTINCT shops (other than the loan shop) the debtor has visited this loan — drives how many
    /// non-power cards the debt-card shop reveals (visit 1 → 3, 2 → 5, 3+ → all). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int DebtShopVisits { get => _debtShopVisits; set { AssertMutable(); _debtShopVisits = value; } }

    private int _lastShopVisitFloor = -1;
    /// <summary>Last TotalFloor at which <see cref="DebtShopVisits"/> was incremented (double-count guard). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LastShopVisitFloor { get => _lastShopVisitFloor; set { AssertMutable(); _lastShopVisitFloor = value; } }

    private int _shopSpentThisVisit;
    /// <summary>Gold of debt spent on cards at the debt shop THIS visit (per-visit credit line). Persisted so a
    /// reload mid-shop keeps the spent total; reset on entering a new shop.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int ShopSpentThisVisit { get => _shopSpentThisVisit; set { AssertMutable(); _shopSpentThisVisit = value; } }

    private bool _purgedThisVisit;
    /// <summary>이번 방문에서 빚으로 카드를 제거했는지(방문당 1회 제한). 리로드로 기회가 되살아나지 않도록 영속화.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool PurgedThisVisit { get => _purgedThisVisit; set { AssertMutable(); _purgedThisVisit = value; } }

    private int _creditPaid;
    /// <summary>신용도로 인정된 상환액(목돈은 상한까지만). See <see cref="LoanRecord.CreditPaid"/>.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CreditPaid { get => _creditPaid; set { AssertMutable(); _creditPaid = value; } }

    private int _debtRoomGold;
    /// <summary>채무 적분 계측(골드×방). See <see cref="LoanRecord.DebtRoomGold"/>. ★청산해도 리셋 안 됨.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int DebtRoomGold { get => _debtRoomGold; set { AssertMutable(); _debtRoomGold = value; } }

    private int _lastLoadFloor = -1;
    /// <summary>적분을 마지막으로 적립한 층(멱등 앵커). See <see cref="LoanRecord.LastLoadFloor"/>.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int LastLoadFloor { get => _lastLoadFloor; set { AssertMutable(); _lastLoadFloor = value; } }

    private bool _pendingSettleCleanup;
    /// <summary>강제 청산 뒷정리 대기(리로드로 유실되면 저주가 영영 남는다). See <see cref="LoanRecord.PendingSettleCleanup"/>.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool PendingSettleCleanup { get => _pendingSettleCleanup; set { AssertMutable(); _pendingSettleCleanup = value; } }

    private string _purchasedCardsCsv = "";
    /// <summary>CSV of non-power card type-names BOUGHT on debt at the shop this loan (so it shows them sold-out and
    /// won't re-sell). Persisted; cleared on repay.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public string PurchasedCardsCsv { get => _purchasedCardsCsv; set { AssertMutable(); _purchasedCardsCsv = value ?? ""; } }

    /// <summary>Live badge: rooms remaining until the NEXT escalation ("N rooms until it gets worse"),
    /// computed live from the current floor so it ticks down as you walk the map. 0 once at the top tier
    /// (badge hidden — see ShowCounter). Owner is set while the relic is carried.</summary>
    public override int DisplayAmount
    {
        get
        {
            if (!_active || Owner?.RunState == null) return 0;
            return DebtLoanConfig.RoomsUntilNextTier(Owner.RunState.TotalFloor - _loanFloor);
        }
    }

    /// <summary>Show the countdown only while a loan is active AND there's a next escalation to count down to
    /// (hidden at the top tier, so it reads as "—" rather than a stuck 0).</summary>
    public override bool ShowCounter => _active && DisplayAmount > 0;

    /// <summary>Current escalation tier 1..4 (0 = no active loan) computed live from the floor — drives the
    /// evolving-ledger overlay (LedgerOverlay). Same live source as the badge.</summary>
    internal int CurrentTier
        => (_active && Owner?.RunState != null) ? DebtLoanConfig.TargetDebtCards(Owner.RunState.TotalFloor - _loanFloor) : 0;

    // Per-relic dynamic hover: the loc description is the static template "Owed [gold]{owed} Gold[/gold]…
    // Paid [gold]{paid} Gold[/gold]…". {owed} = the REMAINING repayable principal (borrowed + the 50% surcharge,
    // amortized down by payments), NOT the raw borrowed amount — this is what you'd pay at a shop right now.
    // RelicModel.DynamicDescription applies DynamicVars per-instance, so two players' Ledgers each show their own.
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[]
        {
            new DynamicVar("borrowed", _borrowed + _cardDebt),   // total principal taken (loan + card/shop debt)
            new DynamicVar("igold", InterestRemaining),   // interest STILL owed (payments clear this first → shrinks)
            new DynamicVar("prem", PrincipalRemaining),   // borrowed principal still owed (owed − remaining interest)
            new DynamicVar("owed", _principal),      // total you'd repay right now
            new DynamicVar("paid", _totalPaid),
            new DynamicVar("cards", _cards),
            // ── 신용 회복 예고 ────────────────────────────────────────────────────────────────────
            // ★이 세 값이 없으면 보상의 존재 자체가 인게임에서 비공개다. 조건(등급·누적 상환액)은 이미
            // 추적 중이고 누적 상환액은 바로 윗줄에 표시까지 되는데, 그게 '목표'라는 말이 어디에도 없어서
            // 정직하게 빨리 갚은 플레이어는 보상도 못 받고 결제 카드셋도 잃는다 — 유물 문구가 "오래 갚지
            // 않을수록 나빠진다"만 말하니 빨리 갚는 게 정답처럼 읽힌다. 목표를 적어주면 그 카운트다운이
            // 위협 표시에서 리스크/리워드 선택으로 바뀐다.
            // ★재설계: 문턱이 '등급(빚의 깊이)'에서 **누적 상환액 사다리**로 바뀌었다. 청산이 더는 유물을
            // 없애지 않으니 "한 번 깊게 갔다 오기"가 아니라 "신용을 쌓아 올리기"가 진행의 축이다.
            new DynamicVar("tier", _cards),                            // 현재 등급(=주입 저주 수)
            new DynamicVar("cr1", DebtLoanConfig.CreditRewardCard),      // 신용 회복 카드 문턱(누적 상환액)
            new DynamicVar("cr2", DebtLoanConfig.CreditRewardUpgraded),  // 강화판 문턱
        };

    /// <summary>Total interest CHARGED so far in gold = origination (20% of the loan) + accrued node interest gold.</summary>
    private int InterestCharged => (int)System.Math.Round(_borrowed * (DebtLoanConfig.BorrowOriginationPct / 100.0)) + _nodeInterestGold;
    /// <summary>Interest STILL owed (charged − paid; payments retire interest first). Shrinks as you pay.</summary>
    private int InterestRemaining => System.Math.Max(0, InterestCharged - _interestPaid);
    /// <summary>Borrowed principal still owed = total owed minus the remaining interest slice (never below 0).</summary>
    private int PrincipalRemaining => System.Math.Max(0, _principal - InterestRemaining);

    /// <summary>Show a preview of the Debt curse cards (plus their keyword tips) in the relic's hover tooltip
    /// — the same mechanism vanilla Soot uses. The set MATCHES the live escalation tier, so hovering the
    /// Ledger reveals EXACTLY which Debt cards will be injected next combat: 빚 독촉 always, +연체 at tier 2,
    /// +차압 at tier 3, +불량신용 at tier 4. A deepening debt visibly grows its preview (1 → 4 cards). Read off
    /// the SAME source as the injector (<see cref="LoanService.InjectAllDebtsForCombat"/>) so the two can't drift.</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            // Live tier 1..4. The hover previews ONLY the DEBT CURSES this ledger injects into combat — 연체(2) /
            // 차압(3) / 신용 불량(4). The 납부 (DebtCurseCard) is NOT shown: it's the voluntary repay card fed by the
            // 정기 납부 power, not something the ledger injects, so previewing it here was misleading (user request).
            int tier = CurrentTier;
            var tips = new List<IHoverTip>();
            if (tier >= 2) tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<DelinquencyCard>());
            if (tier >= 3) tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<SeizureCard>());
            if (tier >= 4) tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<BadCreditCard>());
            return tips;
        }
    }

    /// <summary>Push the current borrowed/paid values + per-combat Debt-card count into the cached DynamicVars
    /// so the hover shows live, per-relic numbers. Called by LoanService.SyncToRelic on every state change
    /// (<paramref name="cards"/> is the current injection count, computed from rooms-since-loan). DynamicVars
    /// is built lazily from CanonicalVars and then cached, so we update the vars in place.</summary>
    internal void RefreshVars(int cards)
    {
        _cards = cards;
        try
        {
            var vars = DynamicVars;
            if (vars.TryGetValue("borrowed", out var bo)) bo.BaseValue = _borrowed + _cardDebt;
            if (vars.TryGetValue("igold", out var ig)) ig.BaseValue = InterestRemaining;   // interest still owed
            if (vars.TryGetValue("prem", out var pr)) pr.BaseValue = PrincipalRemaining;   // principal still owed
            if (vars.TryGetValue("owed", out var b)) b.BaseValue = _principal;             // total owed
            if (vars.TryGetValue("paid", out var p)) p.BaseValue = _totalPaid;
            if (vars.TryGetValue("cards", out var c)) c.BaseValue = _cards;
            if (vars.TryGetValue("tier", out var t)) t.BaseValue = _cards;   // 신용 회복 예고의 "현재 등급"
            // The badge (DisplayAmount = rooms-until-next-tier) is computed live from TotalFloor, but the widget
            // only re-reads it when notified. Walking a node changes TotalFloor without a setter firing, so poke
            // it here → the badge counts DOWN as you move (this is called on every room via RefreshRelicDisplay).
            InvokeDisplayAmountChanged();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] ledger var refresh failed: {e.Message}"); }
    }
}

/// <summary>Grant/remove/disable helpers, kept out of the model so it stays a pure data type.</summary>
internal static class DebtLoanGrants
{
    internal static async Task GrantRelic(Player player)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(typeof(DebtLoanRelic)));
            if (model == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] DebtLoanRelic model not found."); return; }
            await RelicCmd.Obtain(model.ToMutable(), player);
            MainFile.Logger.Info($"[{MainFile.ModId}] granted Merchant's Ledger relic.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic grant failed: {e.Message}"); }
    }

    /// <summary>Repay path: remove the relic entirely (clean slate → can borrow again).</summary>
    internal static async Task RemoveRelic(Player player)
    {
        try
        {
            var relic = LoanService.LedgerRelicOf(player);
            if (relic != null) { await RelicCmd.Remove(relic); MainFile.Logger.Info($"[{MainFile.ModId}] removed Ledger relic (repaid)."); }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic remove failed: {e.Message}"); }
    }

    /// <summary>Add the 독촉장 (Dunning Letter) leverage card to the player's deck (shop-revisit reward). Uses
    /// the deck-pile command (CardPileCmd.Add) so the card actually lands in the Deck pile — raw RunState.AddCard
    /// only touches the master list, not the pile the game reads. Local mutation, applied per-peer.</summary>
    internal static async Task GrantDunningLetter(Player player)
    {
        try
        {
            var card = player.RunState.CreateCard<DunningLetterCard>(player);
            // PreviewCardPileAdd plays the "card flies into the deck" animation (vanilla card-reward feel) —
            // without it the card just silently appears in the deck. Local-gated → co-op safe.
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck));
            MainFile.Logger.Info($"[{MainFile.ModId}] granted 독촉장 (Dunning Letter) card to the deck.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] Dunning Letter grant failed: {e.Message}"); }
    }

    /// <summary>Add a debt event card to the deck by canonical type (품삯 / 납부 혜택 / 환급 / 정산 / 청구서 /
    /// 혈납). <paramref name="preview"/> plays the fly-in animation; pass false when granting from the debt-shop
    /// panel (its CanvasLayer sits ABOVE the fly-in, so the animation would render hidden under the rug — the buy is
    /// fed back by the offer greying to 품절 instead).</summary>
    internal static async Task GrantCard(Player player, System.Type cardType, bool preview = true, bool upgraded = false)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(cardType));
            if (model == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] card model not found: {cardType.Name}."); return; }
            var card = player.RunState.CreateCard(model, player);
            // 강화판 offer (the debt shop stocks one upgraded card per visit) → upgrade before it enters the deck.
            if (upgraded && card.MaxUpgradeLevel > 0) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            var results = await CardPileCmd.Add(card, PileType.Deck);
            if (preview) CardCmd.PreviewCardPileAdd(results);
            MainFile.Logger.Info($"[{MainFile.ModId}] granted {cardType.Name}{(upgraded ? "+" : "")} to the deck.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] card grant failed ({cardType.Name}): {e.Message}"); }
    }

    /// <summary>Every debt-shop VISIT leaves ONE native Debt curse in the deck (the price of leaning on the credit
    /// line) — dropped once per visit by <see cref="LoanService.ApplyBuyCard"/>. Uses the game's own Debt card, so
    /// it Unplayable-clogs the deck and bleeds 10 gold/turn if held (compounding the debt). Swept on repay by
    /// <see cref="RemoveNativeDebtCards"/>. Same deck-pile path as GrantCard.</summary>
    internal static async Task GrantNativeDebt(Player player, string reason = "debt-shop visit")
    {
        try
        {
            if (player?.RunState == null) return;
            var card = player.RunState.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Debt>(player);
            if (card != null) CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck));
            MainFile.Logger.Info($"[{MainFile.ModId}] {reason} → +1 native Debt to the deck.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] native Debt grant failed: {e.Message}"); }
    }

    /// <summary>Reward for clearing a tier-3+ loan: add the 신용 회복 (Credit Restored) card PERMANENTLY to the
    /// deck (upgraded at tier 4). If this happens mid-combat, also drop a temporary copy into hand so it helps
    /// THIS fight too. The deck copy is permanent — repay only sweeps native Debt (RemoveNativeDebtCards), so no
    /// later loan's settle can strip a reward you already earned.
    /// Local per-peer mutation, applied inside the settle path → co-op safe (⚠️ verify with coop-verify).</summary>
    internal static async Task GrantRewardCard(Player player, bool upgraded)
    {
        try
        {
            var deckCard = player.RunState.CreateCard<CreditRestoredCard>(player);
            if (upgraded) { deckCard.UpgradeInternal(); deckCard.FinalizeUpgradeInternal(); }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(deckCard, PileType.Deck));   // permanent keepsake
            // Mid-combat payoff? Give a temporary copy in hand so the reward is usable in the current fight too.
            var combat = player.Creature?.CombatState;
            if (combat != null && (MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false)
                && combat.CreateCard<CreditRestoredCard>(player) is CreditRestoredCard handCard)
            {
                if (upgraded) { handCard.UpgradeInternal(); handCard.FinalizeUpgradeInternal(); }
                await CardPileCmd.AddGeneratedCardToCombat(handCard, PileType.Hand, player, CardPilePosition.Bottom);
            }
            MainFile.Logger.Info($"[{MainFile.ModId}] granted 신용 회복 (Credit Restored{(upgraded ? "+" : "")}) reward card.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] reward-card grant failed: {e.Message}"); }
    }

    /// <summary>600 문턱: <b>300 에서 이미 받은 신용 회복을 강화</b>한다. 같은 카드를 한 장 더 주지 않는 이유 =
    /// 사다리가 '중복 지급'이 아니라 '성장'으로 읽혀야 하기 때문(유저 설계). 덱에 강화 안 된 신용 회복이 없으면
    /// (300 을 건너뛰었거나 그 카드를 제거했다면) 강화판을 새로 준다.</summary>
    internal static async Task UpgradeOrGrantRewardCard(Player player)
    {
        try
        {
            var deck = PileType.Deck.GetPile(player)?.Cards;
            var target = deck?.FirstOrDefault(c => c is CreditRestoredCard && !c.IsUpgraded);
            if (target != null)
            {
                // ★★덱 카드 강화는 반드시 CardCmd.Upgrade 로 — raw UpgradeInternal 은 강화를 **맵포인트
                // 히스토리에 기록하지 않는다**(CurrentMapPointHistoryEntry.UpgradedCards). 바닐라 모루
                // (SmithRestSiteOption)가 쓰는 정규 경로가 이것이고, 그 기록이 co-op 리플리카 재구축·세이브
                // 복원이 덱을 되짚을 때의 근거가 된다.
                CardCmd.Upgrade(target, CardPreviewStyle.None);
                MainFile.Logger.Info($"[{MainFile.ModId}] upgraded the existing 신용 회복 reward card in the deck.");
                return;
            }

            // ★★대상이 없을 때가 두 가지인데, 플레이어에게 이로운 쪽이 서로 다르다.
            //   ① 카드가 아예 없다(3단계를 건너뛰었거나 제거했다) → 강화판으로 '복구'해 주는 게 맞다.
            //   ② 카드는 있는데 **플레이어가 이미 스스로 강화**했다(모닥불 등) → 여기서 또 한 장을 주면
            //      쓸모없는 중복이 된다. 대신 '덱의 아무 카드 1장 강화'로 돌려준다 — 보상의 가치를 지키면서
            //      플레이어가 먼저 강화한 선택을 벌하지 않는다.
            //   두 피어가 같은 덱 상태를 보고 같은 분기를 타므로 co-op 에서도 갈라지지 않는다.
            bool hasAny = deck?.Any(c => c is CreditRestoredCard) ?? false;
            if (!hasAny) { await GrantRewardCard(player, upgraded: true); return; }
            MainFile.Logger.Info($"[{MainFile.ModId}] 신용 회복 already upgraded — falling back to 'upgrade any card'.");
            await UpgradeChosenDeckCard(player);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] reward upgrade failed: {e.Message}"); }
    }

    /// <summary>900 문턱: 덱의 아무 카드 1장을 강화한다. 선택은 엔진의 덱 강화 화면
    /// (<see cref="CardSelectCmd.FromDeckForUpgrade"/>)이 처리하고 <b>co-op 동기화도 엔진이 한다</b>.</summary>
    /// <summary>마지막 강화 보상 시도의 진단 문자열. co-op 은 **두 인스턴스의 로그가 같은 파일에 뒤섞여**
    /// 어느 쪽이 무엇을 했는지 로그만으로는 못 가른다 — 테스트가 이 값을 role 태그와 함께 찍어 가른다.</summary>
    internal static string LastUpgradeDiag = "(not attempted)";

    internal static async Task UpgradeChosenDeckCard(Player player)
    {
        try
        {
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1) { Cancelable = false };
            int cand = PileType.Deck.GetPile(player)?.Cards?.Count(c => c.IsUpgradable) ?? -1;
            bool hadSelector = CardSelectCmd.Selector != null;
            int upBefore = PileType.Deck.GetPile(player)?.Cards?.Count(c => c.IsUpgraded) ?? -1;
            LastUpgradeDiag = $"start(sel={hadSelector} cand={cand} up={upBefore})";
            var picked = (await CardSelectCmd.FromDeckForUpgrade(player, prefs)).FirstOrDefault();
            LastUpgradeDiag = $"picked={picked?.Id.Entry ?? "null"} sel={hadSelector} cand={cand} up={upBefore}";
            if (picked == null) { MainFile.Logger.Info($"[{MainFile.ModId}] upgrade reward: nothing upgradable."); return; }
            // ★★FromDeckForUpgrade 는 **고르기만 하고 강화는 하지 않는다**(실 DLL 확인: 선택을 돌려주고
            // 끝). 이걸 놓쳐서 신용도 9 보상과 보너스 강화가 **단계만 소모하고 아무 일도 안 했다**.
            // 제거 쪽은 CardPileCmd.RemoveFromDeck 을 명시적으로 불러 멀쩡했다.
            // ★★그리고 강화는 raw UpgradeInternal 이 아니라 **CardCmd.Upgrade** 여야 한다 — 바닐라 모루
            // (SmithRestSiteOption)가 쓰는 정규 경로로, UpgradeInternal 앞에 강화 사실을 맵포인트 히스토리
            // (UpgradedCards)에 남긴다. 그 기록이 co-op 리플리카 재구축·세이브 복원의 근거다.
            CardCmd.Upgrade(picked, CardPreviewStyle.None);
            int upAfter = PileType.Deck.GetPile(player)?.Cards?.Count(c => c.IsUpgraded) ?? -1;
            LastUpgradeDiag = $"upgraded={picked.Id.Entry} sel={hadSelector} cand={cand} up={upBefore}→{upAfter}";
            MainFile.Logger.Info($"[{MainFile.ModId}] credit reward upgraded '{picked.Id.Entry}'.");
        }
        catch (Exception e)
        {
            LastUpgradeDiag = "threw: " + e.Message;
            MainFile.Logger.Warn($"[{MainFile.ModId}] deck-upgrade reward failed: {e.Message}");
        }
    }

    /// <summary>1200 문턱 / 빚 제거: 덱의 카드 1장을 제거한다. 선택 화면 + co-op 동기화는 엔진이 한다.
    /// 실제로 제거된 카드를 돌려주므로(취소·후보 없음이면 null) 호출부가 과금 여부를 결정할 수 있다.</summary>
    internal static async Task<CardModel?> RemoveChosenDeckCard(Player player, bool cancelable = false)
    {
        try
        {
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1) { Cancelable = cancelable };
            var picked = (await CardSelectCmd.FromDeckForRemoval(player, prefs)).FirstOrDefault();
            if (picked == null) return null;
            await CardPileCmd.RemoveFromDeck(picked);
            MainFile.Logger.Info($"[{MainFile.ModId}] removed '{picked.Id.Entry}' from the deck.");
            return picked;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] deck-removal failed: {e.Message}"); return null; }
    }

    /// <summary>Repay path: strip every 독촉장 (base or +) from the deck — the leverage tool evaporates with
    /// the debt. Uses CardPileCmd.RemoveFromDeck so the Deck pile updates too. Local, applied per-peer.</summary>
    internal static async Task RemoveDunningLetter(Player player)
    {
        try
        {
            foreach (var card in new List<CardModel>(player.Deck.Cards))
                if (card is DunningLetterCard) await CardPileCmd.RemoveFromDeck(card);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] Dunning Letter remove failed: {e.Message}"); }
    }

    // ⚠️RemoveAllDebtLoanCards(결제셋까지 전부 제거)는 v0.14 재설계에서 아래 RemoveNativeDebtCards 로 갈라진 뒤
    // 호출부가 하나도 없는 사문으로 남아 있다가 제거됐다. 되살리지 말 것 — 아래 <para>가 그 이유다.
    // ★한 번 덱에 들어온 모드 카드는 청산해도 제거하지 않는다(빚 0이면 담보·레버리지처럼 효과가 0이 되는
    // 카드라도 그대로 남긴다). 재대출하면 다시 살아나므로 사이클 설계와 일관된다.

    /// <summary>청산 시 덱에서 <b>네이티브 Debt 저주만</b> 쓸어낸다 — 모드의 결제 카드셋은 남긴다.
    /// <para>★결제 카드를 남기는 이유: 청산이 더는 대출의 끝이 아니라 사이클의 마디가 됐다. 빚에서 벗어나려고
    /// 쌓아 올린 엔진이 벗어나는 순간 같이 죽는 게 이 모드의 가장 큰 구조적 결함이었으므로 결제 카드는 남긴다.
    /// 반대로 네이티브 Debt 는 '나쁜 빚'의 벌점이고 사용 불가 + 손에 있으면 턴당 -10골드인 순수 하방이라,
    /// 남겨두면 청산이 보상이 아니라 처벌이 된다.</para>
    /// <para>덱에 영구히 들어가는 저주는 네이티브 Debt <b>하나뿐</b>이다(빚 상점 유료 구매·차환). 티어 저주
    /// (연체/차압/신용 불량/강제 징수)는 전투 파일에만 주입되므로 <see cref="RemoveDebtCardsFromCombat"/>가
    /// 맡는다 — 둘을 합치면 "빚을 지는 동안 붙은 저주는 청산 시 전부 사라진다"가 성립한다.</para></summary>
    internal static async Task RemoveNativeDebtCards(Player player)
    {
        try
        {
            foreach (var card in new List<CardModel>(player.Deck.Cards))
                if (card is MegaCrit.Sts2.Core.Models.Cards.Debt) await CardPileCmd.RemoveFromDeck(card);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] native Debt sweep failed: {e.Message}"); }
    }

    /// <summary>Mid-combat settle: strip the TEMPORARY injected Debt curses (납부/연체/차압/신용 불량/강제 징수) from the
    /// player's COMBAT piles (hand/draw/discard) so they stop taxing and debuffing the instant the loan is paid
    /// off. <see cref="RemoveNativeDebtCards"/> only clears the DECK; these injected cards never join the deck,
    /// so they need this separate sweep. Local per-peer; runs inside the lockstep payment path.</summary>
    internal static async Task RemoveDebtCardsFromCombat(Player player)
    {
        try
        {
            foreach (var pt in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
            {
                var pile = pt.GetPile(player);
                if (pile == null) continue;
                foreach (var card in new List<CardModel>(pile.Cards))
                    if (card is DebtCurseCard or DelinquencyCard or SeizureCard or BadCreditCard or DebtorCard or ForcedCollectionCard)
                        await CardPileCmd.RemoveFromCombat(card);
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] combat Debt-card sweep failed: {e.Message}"); }
    }
}
