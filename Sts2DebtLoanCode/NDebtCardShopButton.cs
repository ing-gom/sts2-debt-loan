using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Assets;               // PreloadManager
using MegaCrit.Sts2.Core.Context;              // LocalContext
using MegaCrit.Sts2.Core.Entities.Players;     // Player
using MegaCrit.Sts2.Core.Helpers;              // StsColors
using MegaCrit.Sts2.Core.HoverTips;            // HoverTip, IHoverTip, HoverTipAlignment
using MegaCrit.Sts2.Core.Nodes.HoverTips;      // NHoverTipSet
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;  // NMerchantInventory, NMerchantSlot
using MegaCrit.Sts2.Core.Runs;                 // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// The merchant-screen "외상 구매" (Buy on credit) control. Present only while the LOCAL player carries an active
/// loan. Clicking opens <see cref="NDebtCardShopPanel"/>, where the non-power cards are bought on debt AND the loan
/// can be repaid (the "원금 상환" button now lives inside that panel, not on the main shop). A bare TextureButton
/// wrapped in a Control, parented onto the shop's slots container via a postfix on
/// <see cref="NMerchantInventory._Ready"/>, positioned in the shop-action row below the grid.
/// </summary>
internal sealed partial class NDebtCardShopButton : Control
{
    private const float IconSize = 96f;   // standalone (no caption) → a bit larger so it reads as a button
    private const float HoverScale = 1.2f;

    private NMerchantInventory _shop = null!;
    private Player? _player;
    private TextureButton _icon = null!;
    private Tween? _hoverTween;
    private bool _positioned;

    public static void Attach(NMerchantInventory shop)
    {
        var w = new NDebtCardShopButton { _shop = shop };
        Node parent = (Node?)shop._slotsContainer ?? shop;
        parent.AddChild(w);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _player = LocalContext.GetMe(RunManager.Instance.State?.Players ?? Enumerable.Empty<Player>())
                  ?? _shop.Inventory?.Player
                  ?? RunManager.Instance.State?.Players.FirstOrDefault();

        _icon = new TextureButton
        {
            TextureNormal = LoadIcon(),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            Size = new Vector2(IconSize, IconSize),
        };
        _icon.Pressed += OnPressed;
        _icon.MouseEntered += () => { ScaleWidget(HoverScale); NHoverTipSet.CreateAndShow(_icon, MakeTip(), HoverTipAlignment.Left); };
        _icon.MouseExited += () => { ScaleWidget(1f); NHoverTipSet.Remove(_icon); };
        AddChild(_icon);
        PivotOffset = new Vector2(IconSize / 2f, IconSize / 2f);
        // No always-visible caption — the card-stack icon speaks for itself; hovering grows it (ScaleWidget) and
        // shows the tooltip ("누르면 빚 상점으로 이동됩니다", via MakeTip).
    }

    public override void _Process(double delta)
    {
        try
        {
            bool show = _shop.IsOpen && HasActiveLoan();
            if (Visible != show) Visible = show;
            if (show) PositionInShop();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop button _Process failed: {e.Message}"); }
    }

    private bool HasActiveLoan()
    {
        var rec = LoanService.For(_player);
        return rec != null && rec.Active && rec.Principal > 0;
    }

    private void ScaleWidget(float s)
    {
        _hoverTween?.Kill();
        _hoverTween = CreateTween();
        _hoverTween.TweenProperty(this, "scale", Vector2.One * s, 0.12)
                   .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    /// <summary>Sit in the EMPTY right zone of the merchant rug (past the item grid / chevron area), where nothing
    /// else is drawn — so the button + its caption are unobstructed. Positioned as a fraction of the rug's size
    /// (the rug is <see cref="NMerchantInventory._slotsContainer"/>, our parent).</summary>
    private void PositionInShop()
    {
        if (_positioned) return;
        Control? rug = _shop._slotsContainer;
        if (rug == null) return;
        Vector2 sz = rug.Size;
        if (sz.X < 100f) return;   // rug not laid out yet
        // Far-right empty strip, high up — clear of the card grid, the relic/potion column, and the native
        // re-roll coin (which sits right-CENTER). The caption sits just under the icon.
        Position = new Vector2(sz.X * 0.90f - IconSize / 2f, sz.Y * 0.30f);
        _positioned = true;
    }

    private static Texture2D? LoadIcon()
    {
        // Dedicated fanned card-stack icon from the pck (res://Sts2DebtLoan/icons/debt_shop_icon.png).
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://Sts2DebtLoan/icons/debt_shop_icon.png", null, ResourceLoader.CacheMode.Reuse);
            if (tex != null) return tex;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop icon pck load failed: {e.Message}"); }
        // Dev override: a loose PNG next to the DLL.
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(typeof(NDebtCardShopButton).Assembly.Location);
            string? file = dir != null ? System.IO.Path.Combine(dir, "debt_shop_icon.png") : null;
            if (file != null && System.IO.File.Exists(file))
            {
                var img = Image.LoadFromFile(file);
                if (img != null) return ImageTexture.CreateFromImage(img);
            }
        }
        catch { /* fall through */ }
        // Fallbacks: the mod's ledger, then a vanilla icon.
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://Sts2DebtLoan/icons/debt_loan_relic.png", null, ResourceLoader.CacheMode.Reuse);
            if (tex != null) return tex;
        }
        catch { /* fall through */ }
        return PreloadManager.Cache.GetTexture2D("res://images/ui/rest_site/option_dig.png");
    }

    private void OnPressed()
    {
        if (_player == null || !HasActiveLoan()) return;
        try { NDebtCardShopPanel.Show(_shop, _player); }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] open debt-shop panel failed: {e.Message}"); }
    }

    private IHoverTip MakeTip()
    {
        var ui = DebtLoanLoc.DebtShopUiFor(MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng");
        return new HoverTip { Title = ui.Title, Description = ui.Hint, Id = "sts2debtloan_shopbtn" };
    }
}

/// <summary>Attaches the "buy on credit" control to every merchant screen.</summary>
[HarmonyLib.HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory._Ready))]
internal static class MerchantDebtShopButtonPatch
{
    private static void Postfix(NMerchantInventory __instance)
    {
        try { NDebtCardShopButton.Attach(__instance); }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop button add failed: {e.Message}"); }
    }
}
