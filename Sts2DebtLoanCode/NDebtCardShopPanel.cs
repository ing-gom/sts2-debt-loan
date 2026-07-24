using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;          // MegaLabel
using MegaCrit.Sts2.Core.Entities.Cards;       // PileType, CardPreviewMode
using MegaCrit.Sts2.Core.Entities.Players;     // Player
using MegaCrit.Sts2.Core.Helpers;              // TaskHelper, StsColors
using MegaCrit.Sts2.Core.Models;               // CardModel, ModelDb
using MegaCrit.Sts2.Core.Nodes.Cards;          // NCard
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

    private NMerchantInventory _shop = null!;
    private Player _player = null!;
    private MegaLabel? _labelTemplate;
    private Control _grid = null!;
    private readonly List<Action> _refreshers = new();
    private CanvasLayer _layer = null!;
    private Vector2 _screen;
    private bool _closing;
    private Control? _shopContainer;   // the merchant's own rug container — panned left so the shop "extends" sideways
    private float _shopOrigX;

    public static void Show(NMerchantInventory shop, Player player)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _open?.Close();   // never two at once
        var panel = new NDebtCardShopPanel { _shop = shop, _player = player };
        var layer = new CanvasLayer { Layer = 128 };
        panel._layer = layer;
        layer.AddChild(panel);
        tree.Root.AddChild(layer);
        _open = panel;
    }

    /// <summary>solo-verify only: open the panel over the current scene without a real shop room (the panel is a
    /// centered overlay that needs the shop only for its label font, which falls back to the scene tree).</summary>
    internal static void ShowForTest(Player player)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _open?.Close();
        var panel = new NDebtCardShopPanel { _player = player };   // _shop left null → font from scene tree
        var layer = new CanvasLayer { Layer = 128 };
        panel._layer = layer;
        layer.AddChild(panel);
        tree.Root.AddChild(layer);
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
            MouseFilter = MouseFilterEnum.Stop,
        };
        Rect2 rug = _shopContainer != null && _shopContainer.GetGlobalRect().Size.X > 100f
                    ? _shopContainer.GetGlobalRect()
                    : new Rect2(0f, 72f, _screen.X, _screen.Y - 72f);   // fallback (no shop): below the HUD bar
        _bw = rug.Size.X;
        _bh = rug.Size.Y;
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        board.Size = rug.Size;
        board.Position = rug.Position;
        AddChild(board);

        // Grid metrics: 3 columns spread across the width, 2 rows in the space between the header and the bottom.
        const float sideMargin = 90f, topArea = 128f, bottomArea = 84f;
        _colPitch = (_bw - sideMargin * 2f) / PerRow;
        _gridX = sideMargin;
        _gridTop = topArea;
        _rowPitch = (_bh - topArea - bottomArea) / 2f;   // 6 cards → 2 rows

        // No title text — the merchant (back) icon + the card grid carry the screen.
        // Offers sit directly on the rug in a shop-style grid (no scroll — the grid holds the whole pool).
        _grid = new Control();
        _grid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        board.AddChild(_grid);

        BuildOffers();

        // MERCHANT icon in the TOP-LEFT CORNER — click it to scroll back to the shop (reads as "return to the
        // merchant"). No caption. Falls back to a ◀ text button if the merchant texture can't be loaded.
        const float backSize = 88f;
        var merchantTex = LoadMerchantIcon();
        Control back;
        if (merchantTex != null)
        {
            back = new TextureButton
            {
                TextureNormal = merchantTex,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(backSize, backSize),
                Size = new Vector2(backSize, backSize),
            };
            ((TextureButton)back).Pressed += SlideOutAndClose;
        }
        else
        {
            var b = new Button { Text = "◀", Flat = false };
            if (_labelTemplate?.GetThemeDefaultFont() is Font f) b.AddThemeFontOverride("font", f);
            b.AddThemeFontSizeOverride("font_size", 40);
            b.Size = new Vector2(backSize, backSize);
            b.Pressed += SlideOutAndClose;
            back = b;
        }
        back.Position = new Vector2(96f, 84f);   // top-left, well INSIDE the rug body (clear of its ragged edge)
        board.AddChild(back);

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
        if (_closing) return;
        _closing = true;
        if (_open == this) _open = null;   // stop _Process refreshers from re-touching freed nodes late
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(this, "position:x", _screen.X, 0.34).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        if (_shopContainer != null && GodotObject.IsInstanceValid(_shopContainer))
            tw.TweenProperty(_shopContainer, "position:x", _shopOrigX, 0.34).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tw.Chain().TweenCallback(Callable.From(() => _layer?.QueueFree()));
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

        int col = index % PerRow, row = index / PerRow;
        float cx = _gridX + col * _colPitch + _colPitch / 2f;
        float cardCy = _gridTop + row * _rowPitch + _rowPitch * 0.42f;   // card CENTER y in this row cell

        // The card render (Node2D — positioned in grid-local coords), shop-card sized.
        NCard? card = null;
        try
        {
            card = NCard.Create(model);
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
        bool isSale = rec != null && LoanService.SaleCardFor(rec) == type;
        var costTag = MakeCostTag(price);
        costTag.Position = new Vector2(cx - 42f, cardCy + 124f);
        _grid.AddChild(costTag);
        if (isSale)
        {
            var tagTex = LoadSaleTag();
            if (tagTex != null)
            {
                var tag = new TextureRect
                {
                    Texture = tagTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(60f, 60f),
                    Position = new Vector2(cx + 44f, cardCy - 150f),   // card top-right corner
                };
                _grid.AddChild(tag);
            }
        }

        // "품절" overlay label (hidden until bought), centered over the card.
        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var soldLabel = MakeLabel(ui.Sold, 30, StsColors.red);
        if (soldLabel != null) { soldLabel.Position = new Vector2(cx - 36f, cardCy - 16f); soldLabel.Visible = false; _grid.AddChild(soldLabel); }

        // Click hitbox over the card.
        var buy = new Button { Flat = true, Text = "" };
        buy.Position = new Vector2(cx - 88f, cardCy - 120f);
        buy.Size = new Vector2(176f, 250f);
        buy.Pressed += () => OnBuy(type);
        _grid.AddChild(buy);

        // Local refresher: grey out + show 품절 + disable once bought.
        void Refresh()
        {
            var r = LoanService.For(_player);
            bool sold = r == null || r.PurchasedCards.Contains(type.Name);
            bool active = r != null && r.Active;
            buy.Disabled = sold || !active;
            if (card != null) card.Modulate = sold ? new Color(0.45f, 0.45f, 0.45f) : Colors.White;
            costTag.Visible = !sold;
            if (soldLabel != null) soldLabel.Visible = sold;
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
        // Restore the merchant rug to where the game left it (instant close path, e.g. leaving the room in a test).
        if (_shopContainer != null && GodotObject.IsInstanceValid(_shopContainer))
            _shopContainer.Position = new Vector2(_shopOrigX, _shopContainer.Position.Y);
        _layer?.QueueFree();
    }

    /// <summary>Close whatever panel is open (solo-verify uses this before leaving the shop room).</summary>
    internal static void CloseOpen() => _open?.Close();

    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Keycode: Key.Escape }) SlideOutAndClose();
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
    private Control MakeCostTag(int price)
    {
        var root = new Control { Size = new Vector2(100f, 40f) };
        const float coinSize = 38f;
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
        // GREEN price — signals that everything here is bought on DEBT (goes onto what you owe). Given the coin's
        // height + centered vertical alignment so the number's centre lines up with the coin (was riding high).
        var num = MakeLabel(price.ToString(), 34, new Color(0.42f, 0.86f, 0.38f));
        if (num != null)
        {
            num.VerticalAlignment = VerticalAlignment.Center;
            num.Size = new Vector2(64f, coinSize);
            num.Position = new Vector2(coinSize + 8f, 0f);
            root.AddChild(num);
        }
        return root;
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
