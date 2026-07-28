using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;          // MegaLabel
using MegaCrit.Sts2.Core.Entities.Cards;       // PileType, CardPreviewMode
using MegaCrit.Sts2.Core.Entities.Players;     // Player
using MegaCrit.Sts2.Core.Helpers;              // TaskHelper, StsColors
using MegaCrit.Sts2.Core.Assets;               // PreloadManager (repay icon fallback)
using MegaCrit.Sts2.Core.HoverTips;            // HoverTip, IHoverTip, HoverTipAlignment
using MegaCrit.Sts2.Core.Models;               // CardModel, ModelDb
using MegaCrit.Sts2.Core.Nodes;                // NGame (card inspect screen)
using MegaCrit.Sts2.Core.Nodes.Cards;          // NCard
using MegaCrit.Sts2.Core.Nodes.HoverTips;      // NHoverTipSet
using MegaCrit.Sts2.Core.Nodes.Screens;        // NInspectCardScreen
using System.Reflection;                        // read the shop's native back button rect
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;  // NMerchantInventory

namespace Sts2DebtLoan;

/// <summary>
/// The "buy cards on debt" panel opened from <see cref="NDebtCardShopButton"/>. Shows the non-power cards the loan
/// has revealed (3 on the first shop visit, 5 on the second, all after) as a scrollable row of real card renders
/// (<see cref="NCard"/>), each tagged with its DEBT price. Clicking an offer calls
/// <see cref="LoanService.BuyCardOnDebt"/> — the price is added onto what you owe and the card drops into your deck
/// (removed on full repay like every other debt card). Bought offers grey out ("품절"). Display + a single local
/// deck mutation on click → co-op: verify with coop-verify before ship.
///
/// Built from decoupled pieces (NMerchantSlot is too tied to gold/inventory to reuse): NCard.Create + UpdateVisuals
/// for the card, a cloned MegaLabel for game-styled text (default Godot font can't render Korean), and a plain
/// Button hitbox for the click. Laid out manually inside a ScrollContainer (the shop has no scroll of its own).
/// </summary>
internal sealed partial class NDebtCardShopPanel : Control
{
    // Shop-sized board; cards laid out in a grid like the merchant's own card rows (3 per row), at shop card size.
    private const float CardScale = 0.55f;
    private const int PerRow = 5;   // one row of 5 (the shop shows 5 offers per visit)

    // Board + grid metrics, computed from the actual screen size in _Ready so the rug fills the screen like the
    // real shop (was a small fixed box that left the merchant's own rug showing around it).
    private float _bw, _bh, _colPitch, _rowPitch, _gridX, _gridTop;

    private static NDebtCardShopPanel? _open;
    /// <summary>solo-verify 전용: 현재 열린 패널(호버 히트박스를 찾기 위해).</summary>
    internal static NDebtCardShopPanel? OpenPanel => _open;

    private static readonly Color PriceGreen = new(0.42f, 0.86f, 0.38f);   // debt price number (green; red when over credit)
    private static readonly Color FreeGold = new(1.00f, 0.84f, 0.35f);     // the slot-0 gift's "FREE" word (gold, not the debt green)
    private static readonly Color RewardGold = new(1.00f, 0.78f, 0.28f);   // 수령 가능한 보상 칩 (금색 — 빚 초록/상환 크림과 구분)
    private static readonly Color ClaimedGreen = new(0.46f, 0.62f, 0.44f);  // 이미 받은 단계 — 있지만 끝난 일
    private static readonly Color LockedGrey = new(0.52f, 0.50f, 0.47f);    // 아직 못 간 단계 — 보이되 가라앉지 않게

    private NMerchantInventory _shop = null!;
    private Player _player = null!;
    private MegaLabel? _labelTemplate;
    private Control _grid = null!;
    private readonly List<Action> _refreshers = new();
    private Vector2 _screen;
    private bool _closing;
    private Control? _shopContainer;   // the merchant's own rug container — panned left so the shop "extends" sideways
    private float _shopOrigX;
    private Node2D? _handSprite;       // the merchant hand's VISUAL node (NMerchantHand's Node2D parent) — z-lifted above us
    private int _handOrigZ;            // its ZIndex before we raised it (restored on close)

    public static void Show(NMerchantInventory shop, Player player)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _open?.Close();   // never two at once
        var panel = new NDebtCardShopPanel { _shop = shop, _player = player };
        // Add into the SHOP's OWN parent — the SAME 2D context/depth as the merchant rug — NOT a separate CanvasLayer.
        // A CanvasLayer renders ABOVE the default 2D canvas regardless of its layer index, so ANY CanvasLayer put the
        // whole panel over everything (settings menu, tooltips, buy fly-in). The shop has no CanvasLayer (it's default
        // 2D), so we sit as its SIBLING → the shop's higher-layer overlays draw over us normally.
        var host = shop.GetParent() ?? tree.Root;
        host.AddChild(panel);

        // Lift the merchant's hand ABOVE this panel so it stays visible while pointing at offers. The hand belongs to
        // the shop (below us — we're the shop's later sibling), so the rug covered it. NMerchantHand is a non-visual
        // Node; the actual hand GRAPHIC is its Node2D PARENT (it does `new MegaSprite(GetParent<Node2D>())`), so we
        // raise THAT node's ZIndex above our panel (default z 0) — reparenting the Node would break its _parent/_rug
        // caches. Restored on close.
        if (shop.MerchantHand is Node handNode && GodotObject.IsInstanceValid(handNode)
            && handNode.GetParent() is Node2D handSprite && GodotObject.IsInstanceValid(handSprite))
        {
            panel._handSprite = handSprite;
            panel._handOrigZ = handSprite.ZIndex;
            handSprite.ZIndex = 4000;   // above the debt panel/board (ZIndex max is 4096)
        }
        _open = panel;
    }

    /// <summary>solo-verify only: open the panel over the current scene without a real shop room (the panel is a
    /// centered overlay that needs the shop only for its label font, which falls back to the scene tree).</summary>
    internal static void ShowForTest(Player player)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _open?.Close();
        var panel = new NDebtCardShopPanel { _player = player };   // _shop left null → font from scene tree
        tree.Root.AddChild(panel);
        _open = panel;
    }

    public override void _Ready()
    {
        // Screen-sized panel that starts OFF to the right. On show, the merchant's own rug container pans LEFT while
        // this loan canvas comes in from the RIGHT — as if the merchant's canvas were extended sideways and you
        // scroll across it. No dim: both are the same rug, so it reads as one continuous surface, not a modal.
        _screen = GetViewportRect().Size;
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        Size = _screen;
        Position = new Vector2(_screen.X, 0f);
        MouseFilter = MouseFilterEnum.Ignore;   // the board blocks mouse on the rug; the HUD above the rug stays clickable

        Node? searchRoot = (Node?)_shop ?? (Engine.GetMainLoop() as SceneTree)?.Root;
        _labelTemplate = searchRoot != null ? FindMegaLabel(searchRoot) : null;
        _shopContainer = _shop?._slotsContainer;
        _shopOrigX = _shopContainer?.Position.X ?? 0f;

        // Board (the "돗자리"/stall): the SHOP'S OWN rug texture, sized + positioned to MATCH the real merchant rug
        // (read from the live shop container) so the width equals the shop's and it does NOT cover the top HUD.
        var board = new TextureRect
        {
            Texture = LoadRug(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,   // board is VISUAL only now; a separate blocker eats clicks (below)
        };
        Rect2 rug = _shopContainer != null && _shopContainer.GetGlobalRect().Size.X > 100f
                    ? _shopContainer.GetGlobalRect()
                    : new Rect2(0f, 72f, _screen.X, _screen.Y - 72f);   // fallback (no shop): below the HUD bar
        _bw = rug.Size.X;
        _bh = rug.Size.Y;
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        board.Size = rug.Size;
        board.Position = rug.Position;

        // Input blocker: swallow clicks on the rug so the shop BEHIND isn't mis-clicked — but ONLY below the top
        // HUD line, so relic/gold/potion hovers up top still work while the shop is open (issue 8: the rug used to
        // eat them). Added BEFORE the board, so the board's card buttons (added after → above in the tree) still win
        // their clicks; this blocker only catches the empty rug area under the HUD. The board itself is Ignore now,
        // so back/repay/grid (its children) keep their exact positions and their own Stop hitboxes.
        const float hudBottom = 100f;
        float blockTop = Math.Max(rug.Position.Y, hudBottom);
        // Keep the blocker BELOW the merchant's native back button so it stays clickable (the native back now closes
        // the debt shop — see ShopBackClosesDebtShopPatch). Without this the blocker ate clicks on the inner part of
        // that button (only its far-left edge, outside the rug, worked).
        if (_shop != null)
        {
            var bbF = typeof(NMerchantInventory).GetField("_backButton", BindingFlags.NonPublic | BindingFlags.Instance);
            if (bbF?.GetValue(_shop) is Control bb && GodotObject.IsInstanceValid(bb))
                blockTop = Math.Max(blockTop, bb.GetGlobalRect().End.Y + 12f);
        }
        var blocker = new Control
        {
            MouseFilter = MouseFilterEnum.Stop,
            Position = new Vector2(rug.Position.X, blockTop),
            Size = new Vector2(rug.Size.X, Math.Max(0f, rug.Size.Y - (blockTop - rug.Position.Y))),
        };
        AddChild(blocker);
        AddChild(board);

        // Grid metrics: PerRow columns across the width; the card row(s) are VERTICALLY CENTERED in the band between
        // the top header and the bottom repay row. A single row of 5 used to hug the top and leave the bottom ~60%
        // of the rug empty — now the block is centered for whatever number of offers this visit reveals.
        const float sideMargin = 90f, topArea = 128f, bottomArea = 172f;   // bottomArea reserves room for the (bigger) repay row
        _colPitch = (_bw - sideMargin * 2f) / PerRow;
        _gridX = sideMargin;

        var recForRows = LoanService.For(_player);
        int offerCount = recForRows != null ? LoanService.RevealedPurchasable(recForRows).Length : 0;
        int rowCount = Math.Max(1, (offerCount + PerRow - 1) / PerRow);
        const float cellH = 330f;                                  // fixed per-row height (card art + price tag)
        _rowPitch = cellH;
        float band = _bh - topArea - bottomArea;                   // vertical space available for the card block
        _gridTop = topArea + MathF.Max(0f, (band - rowCount * cellH) / 2f);   // center the block in that band

        // Offers sit directly on the rug in a shop-style grid (no scroll — the grid holds the whole pool).
        // ★ MouseFilter.Ignore: the grid spans the WHOLE rug, so if it kept the Control default (Stop) it would
        // eat every click over the rug — including the shop's native back button (only the button's far-left edge,
        // outside the rug, still worked). Ignore lets clicks pass through the grid to its own buy buttons (Stop) and,
        // where there's no button, on through to the back button / blocker beneath.
        _grid = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _grid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        board.AddChild(_grid);

        BuildOffers();
        BuildCreditHeader(board);

        // NO custom back icon: the merchant's OWN native back button now closes the debt shop (see
        // ShopBackClosesDebtShopPatch — it hooks NMerchantInventory.Close so the native back returns you to the shop
        // instead of leaving the room while the debt shop is open). Esc also closes (see _UnhandledKeyInput).

        // 원금 상환 (repay loan) — MOVED here from the main merchant shop, so settling the loan lives in the same
        // 빚 상점 where you take cards on debt.
        BuildRepayControl(board);
        BuildPurgeControl(board);

        // Scroll ACROSS: this loan canvas slides in from the right while the merchant's own rug pans left, so the
        // two read as one continuous canvas being scrolled sideways.
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(this, "position:x", 0f, 0.55).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        if (_shopContainer != null)
            tw.TweenProperty(_shopContainer, "position:x", _shopOrigX - _screen.X, 0.55).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>Scroll back: the loan canvas slides out to the right and the merchant's rug pans back into place.</summary>
    private void SlideOutAndClose()
    {
        if (_closing) { RestoreHand(); QueueFree(); return; }   // a 2nd press force-closes (never get stuck open)
        _closing = true;
        if (_open == this) _open = null;   // stop _Process refreshers from re-touching freed nodes late
        RestoreHand();                     // give the merchant's hand back to the shop before we slide/free
        // Restore the merchant rug now (not in a chained callback) so the shop is back in place regardless.
        if (_shopContainer != null && GodotObject.IsInstanceValid(_shopContainer))
        {
            var twShop = CreateTween();
            twShop.TweenProperty(_shopContainer, "position:x", _shopOrigX, 0.34).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        }
        // Slide the loan canvas out, then free THIS panel on the tween's Finished signal (more reliable than a
        // Chain().TweenCallback after SetParallel, which could skip the free and leave the panel stuck open).
        var tw = CreateTween();
        tw.TweenProperty(this, "position:x", _screen.X, 0.34).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tw.Finished += QueueFree;
    }

    private void BuildOffers()
    {
        var rec = LoanService.For(_player);
        if (rec == null) return;
        var offers = LoanService.RevealedPurchasable(rec);
        for (int i = 0; i < offers.Length; i++) BuildOffer(offers[i], i);
    }

    private void BuildOffer(System.Type type, int index)
    {
        var model = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(type));
        if (model == null) return;

        // One offer per visit is stocked ALREADY UPGRADED (see LoanService.UpgradedCardFor). Render / hover-tip /
        // inspect that offer from an upgraded MUTABLE CLONE of the canonical model — never from the ModelDb model
        // itself (that is shared and canonical), and never via RunState.CreateCard (that registers a real run card
        // for a display-only node). This is the game's own preview pattern (NInspectCardScreen.UpdateCardDisplay).
        var recForUpg = LoanService.For(_player);
        bool isUpgradedOffer = recForUpg != null && LoanService.UpgradedCardFor(recForUpg) == type;
        var display = model;
        if (isUpgradedOffer)
        {
            try
            {
                var up = model.ToMutable();
                up.UpgradeInternal();
                up.FinalizeUpgradeInternal();
                display = up;
            }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] upgraded offer render failed ({type.Name}): {e.Message}"); }
        }

        int col = index % PerRow, row = index / PerRow;
        float cx = _gridX + col * _colPitch + _colPitch / 2f;
        float cardCy = _gridTop + row * _rowPitch + _rowPitch * 0.42f;   // card CENTER y in this row cell

        // The card render (Node2D — positioned in grid-local coords), shop-card sized.
        NCard? card = null;
        try
        {
            card = NCard.Create(display);
            if (card != null)
            {
                _grid.AddChild(card);
                card.Position = new Vector2(cx, cardCy);
                card.Scale = new Vector2(CardScale, CardScale);
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] offer card render failed ({type.Name}): {e.Message}"); }

        // Native-style debt cost tag (gold coin + green number). Price = tier ± per-visit variance, with one card
        // per visit ON SALE (~30% off) — flagged with the merchant's own "%" sale tag on the card corner.
        var rec = LoanService.For(_player);
        int price = rec != null ? LoanService.ShopPriceFor(rec, type) : LoanService.CardDebtPrice(type);
        bool isFree = rec != null && LoanService.IsFreeOffer(rec, type);   // slot 0 — gift: no coin, no sale tag, no credit gate
        bool isSale = !isFree && rec != null && LoanService.SaleCardFor(rec) == type;
        int original = isSale && rec != null ? LoanService.ShopBasePrice(rec, type) : 0;   // pre-sale, struck through
        var costTag = MakeCostTag(price, original, isFree
            ? DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng").Free
            : null);
        // Coin + price sit at the SAME spot on EVERY card (like the shop) so the row of prices lines up; on a sale
        // card the struck-through original just extends to the RIGHT — it never shifts the coin.
        costTag.Position = new Vector2(cx - 42f, cardCy + 124f);
        _grid.AddChild(costTag);
        Control? saleTag = null;
        if (isSale)
        {
            var tagTex = LoadSaleTag();
            if (tagTex != null)
            {
                saleTag = new TextureRect
                {
                    Texture = tagTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(60f, 60f),
                    Position = new Vector2(cx + 44f, cardCy - 150f),   // card top-right corner
                };
                _grid.AddChild(saleTag);
            }
        }

        // "품절" overlay label (hidden until bought), centered over the card.
        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var soldLabel = MakeLabel(ui.Sold, 30, StsColors.red);
        if (soldLabel != null) { soldLabel.Position = new Vector2(cx - 36f, cardCy - 16f); soldLabel.Visible = false; _grid.AddChild(soldLabel); }

        // "한도 초과" overlay label (hidden unless this offer's price exceeds the remaining per-visit credit).
        var overLabel = MakeLabel(ui.OverLimit, 26, StsColors.red);
        if (overLabel != null) { overLabel.Position = new Vector2(cx - 58f, cardCy - 14f); overLabel.Visible = false; _grid.AddChild(overLabel); }

        // Click hitbox over the card — also ENLARGES the card on hover (like previewing a card in the shop). The
        // hitbox is a bit larger than the enlarged card so hovering its edge doesn't flip-flop.
        var theCard = card;
        var theTag = costTag;
        var theSold = (Control?)soldLabel;
        var theOver = (Control?)overLabel;
        var theSale = saleTag;
        var center = new Vector2(cx, cardCy);
        var tagPos = costTag.Position;                       // grid-local origin of the price tag
        var soldPos = soldLabel != null ? soldLabel.Position : center;
        var overPos = overLabel != null ? overLabel.Position : center;
        var salePos = saleTag != null ? saleTag.Position : center;
        // FocusMode None = no white focus/click outline on the button.
        var buy = new Button { Flat = true, Text = "", FocusMode = FocusModeEnum.None };
        buy.Position = new Vector2(cx - 104f, cardCy - 145f);
        buy.Size = new Vector2(208f, 300f);
        buy.Pressed += () => OnBuy(type);
        // Hover enlarges the card AND its price tag + 품절/한도 초과 labels + sale mark together (all raised in ZIndex
        // so nothing is hidden behind the enlarged card — fixes overlay text vanishing / price + tags staying put).
        var theModel = display;   // hover tips + right-click inspect show the OFFERED version (upgraded if 강화판)
        buy.MouseEntered += () =>
        {
            HoverOffer(theCard, theTag, theSold, theOver, theSale, center, tagPos, soldPos, overPos, salePos, true);
            _shop?.MerchantHand?.PointAtTarget(buy, Vector2.Zero);   // the merchant's hand points at the hovered offer (shop feel)
            ShowOfferTips(buy, theModel);
        };
        buy.MouseExited += () =>
        {
            HoverOffer(theCard, theTag, theSold, theOver, theSale, center, tagPos, soldPos, overPos, salePos, false);
            _shop?.MerchantHand?.StopPointing(0.15f);
            NHoverTipSet.Remove(buy);
        };
        // Right-click → the game's card INSPECT screen (huge render + the 강화 미리보기 tickbox), exactly what the
        // real shop does (NMerchantSlot.OnMouseReleased → OnPreview). Fired on RELEASE like the vanilla slot, and
        // only for a click that also STARTED on this button, so a drag ending here can't pop the screen open.
        bool rightDownHere = false;
        buy.GuiInput += (InputEvent e) =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right } mb) return;
            if (mb.Pressed) { rightDownHere = true; return; }
            if (!rightDownHere) return;
            rightDownHere = false;
            OpenInspect(buy, theModel);
        };
        _grid.AddChild(buy);

        // Local refresher: grey out + show 품절 once bought, or dim + show 한도 초과 when this shop's credit line
        // can't cover the price, and disable the buy in either case.
        void Refresh()
        {
            var r = LoanService.For(_player);
            bool sold = r == null || r.PurchasedCards.Contains(type.Name);
            bool active = r != null && r.Active;
            bool overCredit = !sold && active && r != null && !LoanService.CanAffordCredit(r, type);
            buy.Disabled = sold || !active || overCredit;
            if (card != null)
                card.Modulate = sold ? new Color(0.45f, 0.45f, 0.45f)
                              : overCredit ? new Color(0.62f, 0.58f, 0.52f)   // dimmed = unaffordable on this visit's credit
                              : Colors.White;
            costTag.Visible = !sold;
            if (soldLabel != null) soldLabel.Visible = sold;
            if (overLabel != null) overLabel.Visible = overCredit;
            // Price number turns RED when it's over this visit's remaining credit (unbuyable here), else stays green.
            // font_color override (not SelfModulate — that would multiply the green and muddy it).
            // (skipped on the free offer — its "FREE" word is gold and never over-credit, so recolouring it green
            //  would just wash the gift tag out)
            if (!isFree && costTag.GetNodeOrNull<MegaLabel>("priceNum") is { } priceNum)
                priceNum.AddThemeColorOverride("font_color", overCredit ? StsColors.red : PriceGreen);
        }
        _refreshers.Add(Refresh);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_closing) return;
        // Keep the offers in sync with the loan state (grey-out after a buy, disable when the loan settles).
        foreach (var r in _refreshers) { try { r(); } catch { } }
    }

    private void OnBuy(System.Type type)
    {
        TaskHelper.RunSafely(BuyFlow(type));
    }

    private async System.Threading.Tasks.Task BuyFlow(System.Type type)
    {
        try
        {
            bool ok = await LoanService.BuyCardOnDebt(_player, type);
            if (ok) MainFile.Logger.Info($"[{MainFile.ModId}] debt-shop bought {type.Name}.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop buy failed: {e.Message}"); }
        finally { foreach (var r in _refreshers) { try { r(); } catch { } } }
    }

    private void Close()
    {
        if (_open == this) _open = null;
        RestoreHand();
        // Restore the merchant rug to where the game left it (instant close path, e.g. leaving the room in a test).
        if (_shopContainer != null && GodotObject.IsInstanceValid(_shopContainer))
            _shopContainer.Position = new Vector2(_shopOrigX, _shopContainer.Position.Y);
        QueueFree();
    }

    /// <summary>Restore the merchant hand's original ZIndex — we raised it in Show so it drew above this panel.</summary>
    private void RestoreHand()
    {
        if (_handSprite != null && GodotObject.IsInstanceValid(_handSprite))
            _handSprite.ZIndex = _handOrigZ;
        _handSprite = null;
    }

    /// <summary>Close whatever panel is open (solo-verify uses this before leaving the shop room).</summary>
    internal static void CloseOpen() => _open?.Close();

    /// <summary>If the debt shop is open, slide it closed and return true (the caller should then NOT proceed with
    /// its own action). Used by ShopBackClosesDebtShopPatch so the merchant's native back button returns you to the
    /// shop (closing the debt panel) instead of leaving the room. Returns false when no debt panel is open.</summary>
    internal static bool SlideCloseIfOpen()
    {
        if (_open == null) return false;
        _open.SlideOutAndClose();
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        // The inspect screen (right-click preview) binds Escape to its OWN close via NHotkeyManager and does not
        // mark the key handled, so without this guard one Escape would close BOTH it and the debt shop underneath.
        if (IsInspectOpen()) return;
        SlideOutAndClose();
    }

    /// <summary>Is the game's card inspect screen currently up? (Opened by right-clicking an offer.)</summary>
    private static bool IsInspectOpen()
    {
        var insp = NGame.Instance?.InspectCardScreen;
        return insp != null && GodotObject.IsInstanceValid(insp) && insp.Visible;
    }

    /// <summary>Show the offered card's OWN hover tips (keywords, 납부/빚 etc.) while the mouse is over its buy
    /// button. The button covers the <see cref="NCard"/> and eats its mouse events, so the card can never surface
    /// them itself — we raise them on the button instead, side-picked like the merchant's own slots
    /// (<c>NMerchantCard.CreateHoverTip</c>).</summary>
    private void ShowOfferTips(Control owner, CardModel? model)
    {
        if (model == null) return;
        try
        {
            NHoverTipSet.Remove(owner);   // never stack two sets on one owner (CreateAndShow would throw on re-add)
            NHoverTipSet.CreateAndShow(owner, model.HoverTips, HoverTip.GetHoverTipAlignment(owner));
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] offer hover tips skipped: {e.Message}"); }
    }

    /// <summary>Open the game's inspect screen on this offer — the big card render with the 강화 미리보기 tickbox,
    /// same as right-clicking a card in the real shop. The hover tip is dropped first (the screen covers us, so
    /// MouseExited may never fire and the tip would hang over the overlay).</summary>
    private void OpenInspect(Control owner, CardModel? model)
    {
        if (model == null) return;
        try
        {
            NHoverTipSet.Remove(owner);
            NGame.Instance?.GetInspectCardScreen()?.Open(new List<CardModel> { model }, 0);
            GetViewport()?.SetInputAsHandled();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] card inspect failed: {e.Message}"); }
    }

    /// <summary>Clone the game's MegaLabel (a Label → Korean-capable game font) and set its text. The clone inherits
    /// the template's SCENE anchors, so reset to top-left first or Position is ignored (label drifts off-board).</summary>
    private MegaLabel? MakeLabel(string text, int fontSize, Color color)
    {
        try
        {
            if (_labelTemplate?.Duplicate() is not MegaLabel ml) return null;
            ml.Visible = true;
            ml.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);   // drop the template's scene anchors
            ml.GrowHorizontal = GrowDirection.End;
            ml.GrowVertical = GrowDirection.End;
            ml.CustomMinimumSize = Vector2.Zero;
            ml.Size = Vector2.Zero;
            ml.Scale = Vector2.One;
            ml.HorizontalAlignment = HorizontalAlignment.Left;
            ml.VerticalAlignment = VerticalAlignment.Top;
            ml.ClipText = false;
            ml.AutowrapMode = TextServer.AutowrapMode.Off;
            ml.Text = text;
            ml.AddThemeFontSizeOverride("font_size", fontSize);
            ml.Modulate = color;
            return ml;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] label clone failed: {e.Message}"); return null; }
    }

    /// <summary>A shop-style cost tag: the merchant's gold-coin icon + the debt number, so the price reads like a
    /// native shop price (the 외상 구매 title + debt framing make clear it's charged to your loan, not gold).</summary>
    /// <summary>Price tag under an offer. <paramref name="freeText"/> (the visit's slot-0 gift) replaces the whole
    /// coin+number with a single gold "FREE" word at the same anchor — no coin, because there is no debt to show.</summary>
    private Control MakeCostTag(int price, int original = 0, string? freeText = null)
    {
        var root = new Control { Size = new Vector2(160f, 44f) };
        const float coinSize = 38f;
        if (freeText != null)
        {
            var free = MakeLabel(freeText, 34, FreeGold);
            if (free != null)
            {
                free.Name = "priceNum";   // Refresh() looks this up; it just never recolours a free tag
                free.VerticalAlignment = VerticalAlignment.Center;
                free.Size = new Vector2(160f, coinSize);
                free.Position = new Vector2(0f, 0f);
                root.AddChild(free);
            }
            return root;
        }
        var coin = LoadCoin();
        if (coin != null)
        {
            var icon = new TextureRect
            {
                Texture = coin,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Size = new Vector2(coinSize, coinSize),
                Position = new Vector2(0f, 0f),
            };
            root.AddChild(icon);
        }
        // GREEN price = the amount charged (goes onto your debt), size-matched to the coin. Named so the offer's
        // Refresh can recolour it RED when the price is over the remaining per-visit credit (unaffordable here).
        var num = MakeLabel(price.ToString(), 34, PriceGreen);
        if (num != null)
        {
            num.Name = "priceNum";
            num.VerticalAlignment = VerticalAlignment.Center;
            num.Size = new Vector2(60f, coinSize);
            num.Position = new Vector2(coinSize + 8f, 0f);
            root.AddChild(num);
        }
        // ON SALE: the pre-sale price to its right, dimmed + struck through (like the merchant's discounted price).
        if (original > price)
        {
            float ox = coinSize + 8f + 42f;   // sit closer to the sale price (tighter gap)
            var orig = MakeLabel(original.ToString(), 24, new Color(0.72f, 0.72f, 0.72f));
            if (orig != null)
            {
                orig.VerticalAlignment = VerticalAlignment.Center;
                orig.Size = new Vector2(48f, coinSize);
                orig.Position = new Vector2(ox, 4f);
                root.AddChild(orig);
            }
            var line = new ColorRect
            {
                Color = new Color(0.88f, 0.32f, 0.32f),
                Size = new Vector2(original >= 100 ? 46f : 32f, 3f),
                Position = new Vector2(ox + 1f, coinSize / 2f + 1f),
            };
            root.AddChild(line);
        }
        return root;
    }

    /// <summary>Tween a Control (the merchant back button) to a hover scale.</summary>
    private void HoverScale(Control node, float scale)
    {
        if (!GodotObject.IsInstanceValid(node)) return;
        CreateTween().TweenProperty(node, "scale", new Vector2(scale, scale), 0.10)
                     .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    /// <summary>Enlarge (or restore) an offer on hover — the card AND its price tag + 품절 label together, all
    /// raised in ZIndex so nothing is hidden behind the enlarged card. Shop-style card preview.</summary>
    private void HoverOffer(NCard? card, Control? tag, Control? sold, Control? over, Control? sale, Vector2 center, Vector2 tagPos, Vector2 soldPos, Vector2 overPos, Vector2 salePos, bool on)
    {
        float f = on ? 1.16f : 1f;
        int z = on ? 5 : 0;
        if (card != null && GodotObject.IsInstanceValid(card))
        {
            card.ZIndex = z;
            CreateTween().TweenProperty(card, "scale", new Vector2(CardScale * f, CardScale * f), 0.10)
                         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        }
        ScaleAround(tag, center, tagPos, f, z);
        ScaleAround(sold, center, soldPos, f, z);
        ScaleAround(over, center, overPos, f, z);
        ScaleAround(sale, center, salePos, f, z);
    }

    /// <summary>Scale a Control by <paramref name="f"/> about <paramref name="center"/> (its grid-local origin is
    /// <paramref name="orig"/>) and raise its ZIndex, so it grows in step with the hovered card.</summary>
    private static void ScaleAround(Control? node, Vector2 center, Vector2 orig, float f, int z)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;
        node.ZIndex = z;
        node.Scale = new Vector2(f, f);
        node.Position = center + (orig - center) * f;
    }

    /// <summary>The "원금 상환" (repay loan) control — MOVED here from the main merchant shop so settling the loan
    /// lives in the same 빚 상점 where you take cards on debt. Bottom-center action row: caption + ledger icon +
    /// the outstanding principal as a real GOLD price (cream if affordable, red if not — distinct from the
    /// debt-green offer prices). Click → <see cref="LoanService.Repay"/>. Hidden while there's no active loan.</summary>
    /// <summary>Top-center header showing this shop's remaining / limit debt-shop credit line, so the player can see
    /// how much more they may borrow on cards HERE before the offers grey out. Refreshes as they buy.</summary>
    private void BuildCreditHeader(Control board)
    {
        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var label = MakeLabel("", 34, StsColors.cream);
        if (label == null) return;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Size = new Vector2(420f, 48f);
        label.Position = new Vector2(_bw / 2f - 210f, 58f);   // top-center, under the HUD line
        board.AddChild(label);

        // 신용도 줄 — 외상 한도 아래. ★이건 이번 방문의 한도(위 줄)와 전혀 다른 축이다: 리셋되지 않는
        // 런 단위 진행 트랙이라, 청산을 반복해도 계속 쌓이고 청산 보상의 등급을 결정한다.
        var scoreFmt = DebtLoanLoc.CreditScoreFormatFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var rw = DebtLoanLoc.RewardUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var shortFmt = DebtLoanLoc.CreditShortFormatFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var scoreLabel = MakeLabel("", 28, StsColors.cream);
        if (scoreLabel != null)
        {
            scoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
            scoreLabel.VerticalAlignment = VerticalAlignment.Center;
            scoreLabel.Size = new Vector2(420f, 40f);
            scoreLabel.Position = new Vector2(_bw / 2f - 210f, 104f);
            board.AddChild(scoreLabel);
        }

        // 신용도 라벨 위의 투명 히트박스 — 여기에 호버하면 보상 사다리 전체가 뜬다. ★라벨(MegaLabel) 자체에
        // 마우스 이벤트를 걸지 않는 이유: 라벨은 MouseFilter 기본값이 제각각이고 Size 가 텍스트에 따라
        // 흔들려서, 고정 크기의 빈 Button 을 겹쳐 두는 편이 히트 영역이 안정적이다.
        var scoreHit = new Button
        {
            Name = "dl_credit_hit",   // solo-verify 가 시그널을 직접 발화해 호버를 재현한다(자동화에선 MouseEntered 가 안 난다)
            Flat = true, Text = "", FocusMode = FocusModeEnum.None,
            Size = new Vector2(420f, 40f), Position = new Vector2(_bw / 2f - 210f, 104f),
        };
        scoreHit.MouseEntered += () =>
        {
            NHoverTipSet.Remove(scoreHit);   // 재호버 시 선행 제거 필수 — _activeHoverTips 가 중복 키에 throw
            NHoverTipSet.CreateAndShow(scoreHit, MakeCreditTip(rw), HoverTipAlignment.Left);
        };
        scoreHit.MouseExited += () => NHoverTipSet.Remove(scoreHit);
        board.AddChild(scoreHit);

        // ★★사다리는 **한 칸씩 열린다**(유저 요청): 지금 받을 차례인 단계 하나만 칩으로 보여주고, 그걸
        //   받아야 다음 칸이 나타난다. 네 개를 한꺼번에 늘어놓으면 "지금 할 일"이 흐려지고, 무엇보다
        //   보너스 단계가 무한이라 전부 그리는 건 애초에 불가능하다.
        //   보너스 단계(신용도 12 초과)는 **강화 / 제거 중 택1**이라 칩이 두 개로 갈라진다.
        var chipA = new Button
        {
            Name = "dl_reward_chip",   // solo-verify 가 이름으로 찾아 클릭/호버를 재현한다
            Flat = true, Text = "", FocusMode = FocusModeEnum.None,
            Size = new Vector2(230f, 44f),
        };
        var chipB = new Button
        {
            Name = "dl_reward_chip_alt",
            Flat = true, Text = "", FocusMode = FocusModeEnum.None, Visible = false,
            Size = new Vector2(230f, 44f),
        };
        board.AddChild(chipA);
        board.AddChild(chipB);
        var labelA = MakeLabel("", 26, RewardGold);
        var labelB = MakeLabel("", 26, RewardGold);
        foreach (var l in new[] { labelA, labelB })
        {
            if (l == null) continue;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Size = new Vector2(230f, 44f);
            board.AddChild(l);
        }
        if (labelB != null) labelB.Visible = false;

        chipA.Pressed += () => TaskHelper.RunSafely(ClaimFlow());
        chipB.Pressed += () => TaskHelper.RunSafely(ClaimFlow());
        foreach (var (btn, isRemove) in new[] { (chipA, false), (chipB, true) })
        {
            var b = btn; bool rm = isRemove;
            b.MouseEntered += () =>
            {
                NHoverTipSet.Remove(b);
                NHoverTipSet.CreateAndShow(b, MakeRungTip(rw), HoverTipAlignment.Left);
                _shop?.MerchantHand?.PointAtTarget(b, Vector2.Zero);
            };
            b.MouseExited += () => { NHoverTipSet.Remove(b); _shop?.MerchantHand?.StopPointing(0.15f); };
        }

        void Refresh()
        {
            var r = LoanService.For(_player);
            int remaining = r != null ? LoanService.RemainingShopCredit(r) : DebtLoanConfig.ShopCreditLimit;
            label.Text = string.Format(ui.Credit, remaining, DebtLoanConfig.ShopCreditLimit);
            if (scoreLabel != null)
                scoreLabel.Text = string.Format(scoreFmt, LoanService.CreditScore(r), r?.CreditPaid ?? 0);   // ★신용도가 세는 값
            // Warn-tint when the line is used up (nothing more can be bought here this visit).
            label.SelfModulate = remaining <= 0 ? StsColors.red : StsColors.cream;

            int idx = LoanService.NextRewardIndex(r);
            int tier = LoanService.RewardTierAt(idx);
            bool ready = LoanService.CanClaimNextReward(r);
            bool bonus = LoanService.IsBonusReward(idx);
            string pts = string.Format(shortFmt, tier / Math.Max(1, DebtLoanConfig.GoldPerCreditPoint));

            // 보너스면 두 칸(강화/제거)을 나란히, 아니면 가운데 한 칸.
            bool two = false;   // ★보너스는 교대라 선택이 없다 — 칩은 항상 하나
            float w = 230f, gap = 12f;
            float totalW = two ? w * 2 + gap : w;
            float x0 = _bw / 2f - totalW / 2f;
            chipA.Position = new Vector2(x0, 148f);
            chipB.Position = new Vector2(x0 + w + gap, 148f);
            if (labelA != null) labelA.Position = chipA.Position;
            if (labelB != null) labelB.Position = chipB.Position;

            chipA.Disabled = !ready;
            chipB.Visible = two;
            chipB.Disabled = !two;
            if (labelB != null) labelB.Visible = two;

            if (labelA != null)
            {
                labelA.Text = !ready ? pts
                            : bonus ? $"{pts} {(LoanService.BonusIsRemoval(idx) ? rw.BonusRemove : rw.BonusUpgrade)}"
                            : $"{pts} {rw.Claim}";
                labelA.Modulate = ready ? RewardGold : LockedGrey;
            }
            if (labelB != null && two)
            {
                labelB.Text = $"{pts} {rw.BonusRemove}";
                labelB.Modulate = RewardGold;
            }
        }
        _refreshers.Add(Refresh);
        Refresh();
    }

    /// <summary>지금 차례인 단계 하나짜리 툴팁 — 그 보상이 무엇이고 지금 어떤 상태인지. 아직 못 간 단계는
    /// <b>앞으로 몇 골드</b>가 남았는지 말해준다(사다리가 목표로 읽히게 하는 핵심 문구).
    /// 보너스 단계에서는 <paramref name="removeChoice"/> 로 갈라진 두 칩이 각자의 설명을 갖는다.</summary>
    private IHoverTip MakeRungTip(DebtLoanLoc.RewardUiRow rw)
    {
        var rec = LoanService.For(_player);
        int paid = rec?.CreditPaid ?? 0;   // 사다리는 신용도 기준값으로 잰다
        int idx = LoanService.NextRewardIndex(rec);
        int tier = LoanService.RewardTierAt(idx);
        bool bonus = LoanService.IsBonusReward(idx);
        string[] fixedNames = { rw.RungCard, rw.RungUpgrade, rw.RungUpgradeAny, rw.RungRemoveAny };
        string name = bonus ? (LoanService.BonusIsRemoval(idx) ? rw.RungRemoveAny : rw.RungUpgradeAny) : fixedNames[idx];
        string body = paid >= tier ? rw.Ready : string.Format(rw.ToGo, tier - paid);
        // 헤더와 같은 서식을 재사용 → "신용도 3  (누적 300 골드 상환)". 단위(신용도)와 실제 금액을 한 줄에.
        var scoreFmt = DebtLoanLoc.CreditScoreFormatFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        string head = string.Format(scoreFmt, tier / Math.Max(1, DebtLoanConfig.GoldPerCreditPoint), tier);
        string tail = bonus ? $"\n{string.Format(rw.BonusNote, DebtLoanConfig.BonusRewardCredits)}" : "";
        return new HoverTip { Title = name, Description = $"[gold]{head}[/gold]\n{body}{tail}", Id = "sts2debtloan_rung" };
    }

    /// <summary>수령 → 새로고침. 900/1200 은 여기서 카드 선택 화면이 열리고, co-op 이면 그 선택을 엔진이
    /// 양 피어 간에 맞춘다(<see cref="LoanService.ClaimCreditReward"/> 주석 참조).</summary>
    private async Task ClaimFlow()
    {
        if (_player == null) return;
        try
        {
            if (!await LoanService.ClaimCreditReward(_player)) return;
            await Task.Delay(120);
            foreach (var f in _refreshers) f();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] claim flow failed: {e.Message}"); }
    }

    /// <summary>신용 보상 사다리 툴팁 — 도달/수령 상태를 단계별로 보여주고, 아직 못 넘긴 다음 문턱까지
    /// 몇 골드가 남았는지 말해준다. ★이 화면이 없으면 사다리의 존재 자체가 인게임에서 비공개다(유물 문구는
    /// "오래 갚지 않을수록 나빠진다"만 말하니 빨리 갚는 게 정답처럼 읽힌다).</summary>
    private IHoverTip MakeCreditTip(DebtLoanLoc.RewardUiRow rw)
        => new HoverTip { Title = rw.TipTitle, Description = CreditLadderText(_player), Id = "sts2debtloan_creditladder" };

    /// <summary>사다리 본문을 만드는 <b>단일 출처</b>. 툴팁 노드와 분리해 둔 이유 = solo-verify 가 이 문자열을
    /// 직접 읽어 검증할 수 있게 하기 위해서다(네이티브 호버는 자동화에서 화면에 안 잡힌다 —
    /// <c>Input.WarpMouse</c> 로도, 시그널 직접 발화로도 스크린샷에 남지 않았다).</summary>
    internal static string CreditLadderText(Player? player)
    {
        string lang = MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng";
        var rw = DebtLoanLoc.RewardUiFor(lang);
        var shortFmt = DebtLoanLoc.CreditShortFormatFor(lang);
        var rec = LoanService.For(player);
        int paid = rec?.CreditPaid ?? 0;   // 사다리는 신용도 기준값으로 잰다
        int next = LoanService.NextRewardIndex(rec);
        var fixedTiers = LoanService.CreditRewardTiers;
        string[] names = { rw.RungCard, rw.RungUpgrade, rw.RungUpgradeAny, rw.RungRemoveAny };

        // 이 툴팁은 '지도'다 — 고정 4단계는 끝까지 보여줘서 무엇을 향해 가는지 알려주되, 상태(✓/▶/남은 골드)로
        // 어디까지 왔는지를 구분한다. 칩은 순차로 한 칸씩만 열리므로 둘의 역할이 겹치지 않는다.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < fixedTiers.Length; i++)
        {
            int t = fixedTiers[i];
            string mark = i < next ? "[gold]✓[/gold]" : paid >= t ? "[gold]▶[/gold]" : "  ";
            string tail = i < next ? $"  ({rw.Claimed})"
                        : paid >= t ? $"  ([gold]{rw.Ready}[/gold])"
                        : $"  ({string.Format(rw.ToGo, t - paid)})";
            string pts = string.Format(shortFmt, t / Math.Max(1, DebtLoanConfig.GoldPerCreditPoint));
            sb.Append($"{mark} [gold]{pts}[/gold]  {names[i]}{tail}\n");
        }
        // 무한 보너스 안내 — 12 이후로도 상환이 계속 값어치를 갖는다는 것 자체가 정보다.
        sb.Append($"[gold]∞[/gold] {string.Format(rw.BonusNote, DebtLoanConfig.BonusRewardCredits)}");
        // ★목돈 상환이 어디까지 신용이 되는지 = 사다리를 읽는 데 반드시 필요한 규칙.
        int capPts = DebtLoanConfig.LumpSumCreditCap / Math.Max(1, DebtLoanConfig.GoldPerCreditPoint);
        sb.Append("\n" + string.Format(DebtLoanLoc.LumpCapNoteFor(lang),
                                       string.Format(DebtLoanLoc.CreditShortFormatFor(lang), capPts)));
        return sb.ToString();
    }

    /// <summary>"카드 제거" 행 — 바닥 액션 줄의 <b>왼쪽</b>(상환 버튼은 가운데). 상인의 제거 슬롯 1회에
    /// <b>더해</b> 파는 추가 제거이고, 값은 그 상점의 제거가와 같되 <b>골드가 아니라 빚</b>으로 문다.
    /// <para>★같은 카운터(<c>CardShopRemovalsUsed</c>)를 올리므로 상인의 다음 제거값도 같이 오른다 —
    /// "덤"이 아니라 "한 번 더 살 기회"다. ★<b>방문당 1회</b>이며 외상 한도와는 <b>무관</b>하다
    /// (카드 구매와 예산을 다투지 않는다).</para></summary>
    private void BuildPurgeControl(Control board)
    {
        var rw = DebtLoanLoc.RewardUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        float bandY = _bh - 84f;
        const float w = 268f, h = 52f;
        // ★왼쪽 끝에 붙이지 않는다: 화면 바닥-왼쪽 모서리에는 게임의 드로파일 위젯이 있어
        // x=56 으로 두었더니 실쪼에서 캐프션이 그 위젯과 겹쳋다(스크린샷으로 확인).
        float x = 150f;

        // ★상인의 카드 제거 슬롯 아이콘을 **그대로 재사용**한다(유저 요청). 그 슬롯(NMerchantCardRemoval)은
        // 상점 씬의 자식이라 PackedScene 으로 새로 찍을 수 없고, 노드째 Duplicate 하면 `_Ready`/`UpdateVisual`
        // 이 null 엔트리로 돌아 터진다 → **스프라이트의 텍스처만** 빌려 우리 버튼에 입힌다. 결과적으로
        // 같은 그림·같은 자리 문법(아이콘 + 가격)인데 클릭은 우리 빚 결제 흐름으로 간다.
        var icon = LoadRemovalIcon();
        // ★크기는 상점의 제거 슬롯이 실제 렌더되는 크기를 그대로 가져온다(유저 요청).
        // 재질 측정이 안 된 경우에만 상환 버튼과 같은 92px 로 떨어진다(패널 내 아이콘 기본치).
        float iconSz = _removalIconSize.X > 8f ? Mathf.Clamp(_removalIconSize.X, 64f, 140f) : 92f;
        TextureButton? iconBtn = null;
        if (icon != null)
        {
            iconBtn = new TextureButton
            {
                TextureNormal = icon,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(iconSz, iconSz),
                Size = new Vector2(iconSz, iconSz),
                Position = new Vector2(x, bandY - iconSz / 2f),
                PivotOffset = new Vector2(iconSz / 2f, iconSz / 2f),
            };
            board.AddChild(iconBtn);
        }
        float textX = x + (icon != null ? iconSz + 14f : 0f);

        var btn = new Button { Flat = true, Text = "", FocusMode = FocusModeEnum.None,
                               Size = new Vector2(w, h), Position = new Vector2(textX, bandY - h / 2f) };
        board.AddChild(btn);

        var caption = MakeLabel(rw.PurgeTitle, 32, StsColors.cream);
        if (caption != null)
        {
            caption.HorizontalAlignment = HorizontalAlignment.Left;
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Size = new Vector2(w - 80f, h);
            caption.Position = new Vector2(textX, bandY - h / 2f);
            board.AddChild(caption);
        }
        var priceNum = MakeLabel("", 32, PriceGreen);
        if (priceNum != null)
        {
            priceNum.HorizontalAlignment = HorizontalAlignment.Right;
            priceNum.VerticalAlignment = VerticalAlignment.Center;
            priceNum.Size = new Vector2(76f, h);
            priceNum.Position = new Vector2(textX + w - 76f, bandY - h / 2f);
            board.AddChild(priceNum);
        }

        btn.Pressed += () => TaskHelper.RunSafely(PurgeFlow());
        if (iconBtn != null) iconBtn.Pressed += () => TaskHelper.RunSafely(PurgeFlow());
        btn.MouseEntered += () =>
        {
            NHoverTipSet.Remove(btn);
            bool ok = LoanService.CanPurgeOnDebt(_player);
            // 이번 방문의 제거 기회를 이미 썼으면 상점의 자기 문구인 "품절"을 그대로 쓴다
            // (바닐라 제거 슬롯도 쓰면 회색으로 죽는다) — 새 loc 문자열 없이 14언어가 이미 있다.
            var recTip = LoanService.For(_player);
            string body = ok ? string.Format(rw.PurgeTip, LoanService.PurgePrice(_player))
                        : (recTip?.PurgedThisVisit ?? false) ? DebtLoanLoc.DebtShopUiFor(
                              MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng").Sold
                        : rw.PurgeNone;
            NHoverTipSet.CreateAndShow(btn, new HoverTip { Title = rw.PurgeTitle, Description = body, Id = "sts2debtloan_purge" },
                                       HoverTipAlignment.Right);
            _shop?.MerchantHand?.PointAtTarget(btn, Vector2.Zero);
        };
        btn.MouseExited += () => { NHoverTipSet.Remove(btn); _shop?.MerchantHand?.StopPointing(0.15f); };

        void Refresh()
        {
            int price = LoanService.PurgePrice(_player);
            bool ok = LoanService.CanPurgeOnDebt(_player);
            bool hasLedger = LoanService.PlayerHasLedger(_player);
            btn.Visible = hasLedger;
            if (iconBtn != null) { iconBtn.Visible = hasLedger; iconBtn.Modulate = ok ? Colors.White : new Color(0.62f, 0.58f, 0.52f); }
            if (caption != null) { caption.Visible = hasLedger; caption.Modulate = ok ? StsColors.cream : new Color(0.62f, 0.58f, 0.52f); }   // 한도 초과 오퍼와 같은 딜
            if (priceNum != null)
            {
                priceNum.Visible = hasLedger;
                priceNum.Text = price.ToString();
                // 한도 초과 / 제거 불가면 빨강 — 오퍼 가격표와 같은 규칙(font_color override, SelfModulate 아님).
                priceNum.AddThemeColorOverride("font_color", ok ? PriceGreen : StsColors.red);
            }
        }
        _refreshers.Add(Refresh);
        Refresh();
    }

    /// <summary>상인의 카드 제거 슬롯이 쓰는 <b>바로 그 텍스처</b>. 라이브 상점 씬의
    /// <c>%MerchantCardRemoval</c> → <c>%Visual</c>(Sprite2D)에서 읽어 캐시한다.
    /// <para>★캐시가 필요한 이유 = 이 패널은 상점 없이도 열릴 수 있고(solo-verify 의 ShowForTest), 그때는
    /// 씬에 상점이 없어 원본을 못 찾는다. 한 번이라도 진짜 상점을 본 뒤라면 캐시로 같은 그림을 쓴다.
    /// 못 찾으면 null → 아이콘 없이 캡션+가격만 그린다(기능은 그대로).</para></summary>
    private static Texture2D? _removalIconCache;
    /// <summary>상인 슬롯이 실제로 화면에 그려지는 크기(px). ★텍스처 원본 크기가 아니라
    /// <b>스프라이트의 전역 스케일까지 곱한 값</b>을 쓴다 — 상점 씨이 슬롯을 줄여 배치하므로
    /// 원본 크기를 그대로 쓰면 훨씬 커진다(유저 요청 = “기존 상점 UI 를 참조해 크기를 정하라”).</summary>
    private static Vector2 _removalIconSize = Vector2.Zero;

    private Texture2D? LoadRemovalIcon()
    {
        if (_removalIconCache != null) return _removalIconCache;
        try
        {
            Node? root = (Node?)_shop ?? (Engine.GetMainLoop() as SceneTree)?.Root;
            var slot = root != null ? FindNodeByType<NMerchantCardRemoval>(root) : null;
            // %Visual 은 슬롯 씬 안의 unique-name 노드. 이름 탐색이 실패하면 첫 Sprite2D 자식으로 폴백.
            var sprite = slot?.GetNodeOrNull<Sprite2D>("%Visual") ?? (slot != null ? FindNodeByType<Sprite2D>(slot) : null);
            if (sprite?.Texture != null)
            {
                _removalIconCache = sprite.Texture;
                // 실제 렌더 크기 = 텍스처 크기 × 전역 스케일. Sprite2D 는 Control 이 아니라 GetRect() 가
                // 로컬 rect 이므로 GlobalScale 을 곱해야 화면상 크기가 된다.
                var sz = sprite.GetRect().Size * sprite.GlobalScale.Abs();
                if (sz.X > 8f && sz.Y > 8f) _removalIconSize = sz;
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] removal icon reuse failed: {e.Message}"); }
        return _removalIconCache;
    }

    private static T? FindNodeByType<T>(Node root) where T : Node
    {
        if (root is T hit) return hit;
        foreach (var child in root.GetChildren())
        {
            var found = FindNodeByType<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private async Task PurgeFlow()
    {
        if (_player == null) return;
        try
        {
            if (!await LoanService.PurgeCardOnDebt(_player)) return;
            await Task.Delay(150);
            foreach (var f in _refreshers) f();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] purge flow failed: {e.Message}"); }
    }

    private void BuildRepayControl(Control board)
    {
        const float iconSize = 92f;   // enlarged repay button (was 60) — the primary action on this screen
        float bandY = _bh - 84f;      // vertical center of the bottom action row (raised to fit the bigger button)
        float cx = _bw / 2f;
        var ui = DebtLoanLoc.RepayUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");

        var icon = new TextureButton
        {
            TextureNormal = LoadRepayIcon(),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            Size = new Vector2(iconSize, iconSize),
            Position = new Vector2(cx - iconSize / 2f, bandY - iconSize / 2f),
            PivotOffset = new Vector2(iconSize / 2f, iconSize / 2f),
        };
        board.AddChild(icon);

        // Caption "원금 상환" to the LEFT of the icon (right-aligned so it butts up against it).
        var caption = MakeLabel(ui.Title, 36, StsColors.cream);
        if (caption != null)
        {
            caption.HorizontalAlignment = HorizontalAlignment.Right;
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Size = new Vector2(240f, 52f);
            caption.Position = new Vector2(cx - iconSize / 2f - 14f - 240f, bandY - 26f);
            board.AddChild(caption);
        }

        // Cost (coin + gold number) to the RIGHT of the icon.
        TextureRect? coinIcon = null;
        var coin = LoadCoin();
        if (coin != null)
        {
            coinIcon = new TextureRect
            {
                Texture = coin,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Size = new Vector2(46f, 46f),
                Position = new Vector2(cx + iconSize / 2f + 14f, bandY - 23f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            board.AddChild(coinIcon);
        }
        var costLabel = MakeLabel("", 42, StsColors.cream);
        if (costLabel != null)
        {
            costLabel.VerticalAlignment = VerticalAlignment.Center;
            costLabel.Size = new Vector2(110f, 52f);
            costLabel.Position = new Vector2(cx + iconSize / 2f + 14f + 54f, bandY - 26f);
            board.AddChild(costLabel);
        }

        icon.MouseEntered += () =>
        {
            HoverScale(icon, 1.15f);
            var rec = LoanService.For(_player);
            int cost = rec?.Principal ?? 0;
            bool hasLoan = rec != null && rec.Active && cost > 0;
            bool here = LoanService.CanRepayHere(_player);   // 빌린 그 상점에서는 갚을 수 없다
            bool usable = hasLoan && here && (int)_player.Gold >= cost;
            string body = !hasLoan ? ui.NoLoan
                        : !here ? ui.SameShop
                        : usable ? string.Format(ui.PayBack, cost) : string.Format(ui.NotEnough, cost);
            NHoverTipSet.CreateAndShow(icon, MakeRepayTip(ui.Title, body), HoverTipAlignment.Left);
            _shop?.MerchantHand?.PointAtTarget(icon, Vector2.Zero);   // merchant points at the repay action too
        };
        icon.MouseExited += () => { HoverScale(icon, 1f); NHoverTipSet.Remove(icon); _shop?.MerchantHand?.StopPointing(0.15f); };
        icon.Pressed += () => TaskHelper.RunSafely(RepayFlow());

        void Refresh()
        {
            var rec = LoanService.For(_player);
            int cost = rec?.Principal ?? 0;
            bool hasLoan = rec != null && rec.Active && cost > 0;
            bool affordable = (int)_player.Gold >= cost && LoanService.CanRepayHere(_player);
            icon.Visible = hasLoan;
            if (coinIcon != null) coinIcon.Visible = hasLoan;
            if (caption != null) caption.Visible = hasLoan;
            if (costLabel != null)
            {
                costLabel.Visible = hasLoan;
                costLabel.Text = cost.ToString();
                costLabel.Modulate = affordable ? StsColors.cream : StsColors.red;
            }
            icon.Modulate = (hasLoan && affordable) ? Colors.White : StsColors.halfTransparentWhite;
        }
        _refreshers.Add(Refresh);
        Refresh();
    }

    private async System.Threading.Tasks.Task RepayFlow()
    {
        try
        {
            var rec = LoanService.For(_player);
            if (rec == null || !rec.Active || (int)_player.Gold < rec.Principal) return;
            if (!LoanService.CanRepayHere(_player)) return;
            bool ok = await LoanService.Repay(_player);
            if (ok) MainFile.Logger.Info($"[{MainFile.ModId}] debt-shop repay succeeded.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop repay failed: {e.Message}"); }
        finally { foreach (var r in _refreshers) { try { r(); } catch { } } }
    }

    private static IHoverTip MakeRepayTip(string title, string body)
        => new HoverTip { Title = title, Description = body, Id = "sts2debtloan_debtshop_repay" };

    /// <summary>Repay icon: the mod's ledger art from the pck, else a loose dev PNG next to the DLL, else a
    /// vanilla fallback (copied from the old NMerchantRepayButton so the button keeps its look after the move).</summary>
    private static Texture2D? LoadRepayIcon()
    {
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://Sts2DebtLoan/icons/debt_loan_relic.png", null, ResourceLoader.CacheMode.Reuse);
            if (tex != null) return tex;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay icon pck load failed: {e.Message}"); }
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(typeof(NDebtCardShopPanel).Assembly.Location);
            string? file = dir != null ? System.IO.Path.Combine(dir, "repay_shop_icon.png") : null;
            if (file != null && System.IO.File.Exists(file))
            {
                var img = Image.LoadFromFile(file);
                if (img != null) return ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay icon loose load failed: {e.Message}"); }
        return PreloadManager.Cache.GetTexture2D("res://images/ui/rest_site/option_reforge.png");
    }

    /// <summary>A merchant icon for the "back to the shop" button (the game's run-summary merchant portrait, else
    /// the shop history icon).</summary>
    private static Texture2D? LoadMerchantIcon()
    {
        foreach (var p in new[] { "res://images/ui/game_over_screen/run_summary_merchant.png", "res://images/ui/run_history/shop.png" })
        {
            try { var t = ResourceLoader.Load<Texture2D>(p, null, ResourceLoader.CacheMode.Reuse); if (t != null) return t; }
            catch { /* try next */ }
        }
        return null;
    }

    private static Texture2D? LoadCoin()
    {
        try { return ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres", null, ResourceLoader.CacheMode.Reuse); }
        catch { return null; }
    }

    /// <summary>The merchant's own "%" sale tag, placed on the discounted offer this visit.</summary>
    private static Texture2D? LoadSaleTag()
    {
        try { return ResourceLoader.Load<Texture2D>("res://images/rooms/merchant_room/shop_sales_tag.png", null, ResourceLoader.CacheMode.Reuse); }
        catch { return null; }
    }

    /// <summary>The merchant's own rug texture, so the debt shop sits on the exact same 돗자리 as the store.</summary>
    private static Texture2D? LoadRug()
    {
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://images/rooms/merchant_room/shop_rug.png", null, ResourceLoader.CacheMode.Reuse);
            if (tex != null) return tex;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] rug load failed: {e.Message}"); }
        return null;
    }

    private static MegaLabel? FindMegaLabel(Node n)
    {
        if (n is MegaLabel ml) return ml;
        foreach (var c in n.GetChildren())
        {
            var r = FindMegaLabel(c);
            if (r != null) return r;
        }
        return null;
    }
}
