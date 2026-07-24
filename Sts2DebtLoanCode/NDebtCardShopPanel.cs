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
    private const float BoardW = 1180f, BoardH = 620f;
    private const float OfferPitch = 200f, CardScale = 0.62f, RowTopPad = 90f, RowH = 380f;

    private static NDebtCardShopPanel? _open;

    private NMerchantInventory _shop = null!;
    private Player _player = null!;
    private MegaLabel? _labelTemplate;
    private Control _inner = null!;
    private readonly List<Action> _refreshers = new();
    private CanvasLayer _layer = null!;

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
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        Node? searchRoot = (Node?)_shop ?? (Engine.GetMainLoop() as SceneTree)?.Root;
        _labelTemplate = searchRoot != null ? FindMegaLabel(searchRoot) : null;

        // Dim backdrop that swallows clicks to the shop behind (but NOT a click-to-close, to avoid misfires).
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.72f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // Board (the "돗자리"/stall): the SHOP'S OWN rug texture (res://images/rooms/merchant_room/shop_rug.png —
        // the same TextureRect the merchant's SlotsContainer uses) so the panel reads as an extension of the shop.
        var board = new TextureRect
        {
            Texture = LoadRug(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ClipContents = false,
        };
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        board.Size = new Vector2(BoardW, BoardH);
        board.Position = new Vector2(-BoardW / 2f, -BoardH / 2f);
        AddChild(board);

        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");

        var title = MakeLabel(ui.Title, 36, StsColors.cream);
        if (title != null) { title.Position = new Vector2(40f, 26f); board.AddChild(title); }

        // Scroll region holding the offer row.
        var scroll = new ScrollContainer
        {
            Position = new Vector2(24f, RowTopPad),
            Size = new Vector2(BoardW - 48f, RowH + 40f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        board.AddChild(scroll);

        _inner = new Control();
        scroll.AddChild(_inner);

        BuildOffers();

        // Close button.
        var close = new Button { Text = ui.Close, Flat = false };
        if (_labelTemplate?.GetThemeDefaultFont() is Font f) close.AddThemeFontOverride("font", f);
        close.Position = new Vector2(BoardW - 180f, BoardH - 70f);
        close.Size = new Vector2(150f, 44f);
        close.Pressed += Close;
        board.AddChild(close);
    }

    private void BuildOffers()
    {
        var rec = LoanService.For(_player);
        if (rec == null) return;
        var offers = LoanService.RevealedPurchasable(rec);
        float x = 20f;
        foreach (var type in offers)
        {
            BuildOffer(type, x);
            x += OfferPitch;
        }
        _inner.CustomMinimumSize = new Vector2(Math.Max(x, BoardW - 48f), RowH);
    }

    private void BuildOffer(System.Type type, float x)
    {
        var model = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(type));
        if (model == null) return;

        float cx = x + OfferPitch / 2f;
        const float cardCy = 150f;   // card CENTER y inside the scroll content (whole card fits the viewport)

        // The card render (Node2D — positioned in inner-Control local coords).
        NCard? card = null;
        try
        {
            card = NCard.Create(model);
            if (card != null)
            {
                _inner.AddChild(card);
                card.Position = new Vector2(cx, cardCy);
                card.Scale = new Vector2(CardScale, CardScale);
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] offer card render failed ({type.Name}): {e.Message}"); }

        // Debt price tag under the card.
        int price = LoanService.CardDebtPrice(type);
        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        var priceLabel = MakeLabel(string.Format(ui.Price, price), 26, new Color(1f, 0.82f, 0.28f));
        if (priceLabel != null) { priceLabel.Position = new Vector2(cx - 34f, cardCy + 150f); _inner.AddChild(priceLabel); }

        // "품절" overlay label (hidden until bought), centered over the card.
        var soldLabel = MakeLabel(ui.Sold, 30, StsColors.red);
        if (soldLabel != null) { soldLabel.Position = new Vector2(cx - 34f, cardCy - 16f); soldLabel.Visible = false; _inner.AddChild(soldLabel); }

        // Click hitbox over the card.
        var buy = new Button { Flat = true, Text = "" };
        buy.Position = new Vector2(x + 12f, cardCy - 130f);
        buy.Size = new Vector2(OfferPitch - 24f, 280f);
        buy.Pressed += () => OnBuy(type);
        _inner.AddChild(buy);

        // Local refresher: grey out + show 품절 + disable once bought.
        void Refresh()
        {
            var r = LoanService.For(_player);
            bool sold = r == null || r.PurchasedCards.Contains(type.Name);
            bool active = r != null && r.Active;
            buy.Disabled = sold || !active;
            if (card != null) card.Modulate = sold ? new Color(0.45f, 0.45f, 0.45f) : Colors.White;
            if (priceLabel != null) priceLabel.Visible = !sold;
            if (soldLabel != null) soldLabel.Visible = sold;
        }
        _refreshers.Add(Refresh);
        Refresh();
    }

    public override void _Process(double delta)
    {
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
        _layer?.QueueFree();
    }

    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Keycode: Key.Escape }) Close();
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
