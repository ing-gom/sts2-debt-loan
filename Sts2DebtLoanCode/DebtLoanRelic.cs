using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;                // RelicCmd, CardPileCmd
using MegaCrit.Sts2.Core.Entities.Cards;          // PileType, CardPilePosition
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;         // RelicRarity
using MegaCrit.Sts2.Core.HoverTips;               // HoverTipFactory, IHoverTip (Debt-card preview in tooltip)
using MegaCrit.Sts2.Core.Localization.DynamicVars; // DynamicVar (per-relic hover values)
using MegaCrit.Sts2.Core.Models;                  // RelicModel, ModelDb
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

    private int _borrowed, _principal, _totalPaid, _loanFloor, _interestRoomsApplied;
    private bool _active;
    private int _cards;   // transient (not saved): current per-combat Debt-card count, for the hover {cards}

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Borrowed { get => _borrowed; set { AssertMutable(); _borrowed = value; } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int Principal { get => _principal; set { AssertMutable(); _principal = value; InvokeDisplayAmountChanged(); } }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int TotalPaid { get => _totalPaid; set { AssertMutable(); _totalPaid = value; } }

    /// <summary>Rooms of node-interest already baked into Principal (so it isn't re-charged on reload).</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int InterestRoomsApplied { get => _interestRoomsApplied; set { AssertMutable(); _interestRoomsApplied = value; } }

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

    private int _combatCardsGranted;
    /// <summary>How many combat payoff cards have been earned so far (0..3). Persisted.</summary>
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CombatCardsGranted { get => _combatCardsGranted; set { AssertMutable(); _combatCardsGranted = value; } }

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
        new[] { new DynamicVar("owed", _principal), new DynamicVar("paid", _totalPaid), new DynamicVar("cards", _cards) };

    /// <summary>Show a preview of the Debt curse cards (plus their keyword tips) in the relic's hover tooltip
    /// — the same mechanism vanilla Soot uses. The set MATCHES the live escalation tier, so hovering the
    /// Ledger reveals EXACTLY which Debt cards will be injected next combat: 빚 독촉 always, +연체 at tier 2,
    /// +차압 at tier 3, +불량신용 at tier 4. A deepening debt visibly grows its preview (1 → 4 cards). Read off
    /// the SAME source as the injector (<see cref="LoanService.InjectAllDebtsForCombat"/>) so the two can't drift.</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            // Live tier 1..4 (0 only when Owner/RunState isn't wired yet — fall through to the base card so
            // the preview is never empty).
            int tier = CurrentTier;
            var tips = new List<IHoverTip>();
            tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<DebtCurseCard>());
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
            if (vars.TryGetValue("owed", out var b)) b.BaseValue = _principal;   // remaining repayable (principal)
            if (vars.TryGetValue("paid", out var p)) p.BaseValue = _totalPaid;
            if (vars.TryGetValue("cards", out var c)) c.BaseValue = _cards;
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
    /// 혈납), with the fly-in animation. Same deck-pile path as the 독촉장 grant.</summary>
    internal static async Task GrantCard(Player player, System.Type cardType)
    {
        try
        {
            var model = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(cardType));
            if (model == null) { MainFile.Logger.Warn($"[{MainFile.ModId}] card model not found: {cardType.Name}."); return; }
            var card = player.RunState.CreateCard(model, player);
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck));
            MainFile.Logger.Info($"[{MainFile.ModId}] granted {cardType.Name} to the deck.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] card grant failed ({cardType.Name}): {e.Message}"); }
    }

    /// <summary>Reward for clearing a tier-3+ loan: add the 신용 회복 (Credit Restored) card PERMANENTLY to the
    /// deck (upgraded at tier 4). If this happens mid-combat, also drop a temporary copy into hand so it helps
    /// THIS fight too. The deck copy survives future debt-kit sweeps (it's exempt in RemoveAllDebtLoanCards).
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

    /// <summary>Repay path: strip EVERY DebtLoan card (독촉장 + 취업알선 + 납부 혜택 + 환급 + 정산 + 청구서 + 혈납 + any
    /// future deck-granted card) from the deck — the whole debt kit evaporates when you clear the loan. Matches
    /// by declaring assembly so new cards are covered automatically. Local per-peer mutation (like the relic
    /// remove), applied inside the settle path → co-op safe.</summary>
    internal static async Task RemoveAllDebtLoanCards(Player player)
    {
        try
        {
            var own = typeof(DebtLoanGrants).Assembly;
            foreach (var card in new List<CardModel>(player.Deck.Cards))
                // Sweep the whole debt kit — but NOT the 신용 회복 reward (a permanent keepsake; a later loan's
                // repay must not strip a reward you already earned).
                if (card.GetType().Assembly == own && card is not CreditRestoredCard) await CardPileCmd.RemoveFromDeck(card);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] DebtLoan card sweep failed: {e.Message}"); }
    }

    /// <summary>Mid-combat settle: strip the TEMPORARY injected Debt curses (납부/연체/차압/신용 불량/강제 징수) from the
    /// player's COMBAT piles (hand/draw/discard) so they stop taxing and debuffing the instant the loan is paid
    /// off. <see cref="RemoveAllDebtLoanCards"/> only clears the DECK; these injected cards never join the deck,
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
