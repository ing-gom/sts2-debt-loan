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

    private static readonly Color PriceGreen = new(0.42f, 0.86f, 0.38f);   // debt price number (green; red when over credit)
    private static readonly Color FreeGold = new(1.00f, 0.84f, 0.35f);     // the slot-0 gift's "FREE" word (gold, not the debt green)

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

        void Refresh()
        {
            var r = LoanService.For(_player);
            int remaining = r != null ? LoanService.RemainingShopCredit(r) : DebtLoanConfig.ShopCreditLimit;
            label.Text = string.Format(ui.Credit, remaining, DebtLoanConfig.ShopCreditLimit);
            // Warn-tint when the line is used up (nothing more can be bought here this visit).
            label.SelfModulate = remaining <= 0 ? StsColors.red : StsColors.cream;
        }
        _refreshers.Add(Refresh);
        Refresh();
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
            bool usable = hasLoan && (int)_player.Gold >= cost;
            string body = !hasLoan ? ui.NoLoan : usable ? string.Format(ui.PayBack, cost) : string.Format(ui.NotEnough, cost);
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
            bool affordable = (int)_player.Gold >= cost;
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
