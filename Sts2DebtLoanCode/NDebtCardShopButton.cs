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
/// A second merchant-screen control, sitting beside the "Repay Loan" button: "외상 구매" (Buy on credit). Present
/// only while the LOCAL player carries an active loan. Clicking opens <see cref="NDebtCardShopPanel"/>, where the
/// non-power cards are bought on debt. Same attach pattern as <see cref="NMerchantRepayButton"/> — a bare
/// TextureButton wrapped in a Control, parented onto the shop's slots container via a postfix on
/// <see cref="NMerchantInventory._Ready"/>, positioned in the shop-action row (LEFT of the repay button).
/// </summary>
internal sealed partial class NDebtCardShopButton : Control
{
    private const float IconSize = 60f;
    private const float HoverScale = 1.2f;
    private const float BelowGap = 12f;
    private const float LeftShift = 96f;   // sit LEFT of the repay button so the two don't overlap

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

    /// <summary>Sit below the relic/potion grid, LEFT of the repay button (which right-aligns to the grid edge).</summary>
    private void PositionInShop()
    {
        if (_positioned) return;
        Control? board = _shop._slotsContainer;
        if (board == null) return;

        float maxRight = float.MinValue, gridBottom = float.MinValue;
        int count = 0;
        foreach (var c in new[] { _shop._relicContainer, _shop._potionContainer })
        {
            if (c == null) continue;
            foreach (var slot in c.GetChildren().OfType<NMerchantSlot>())
            {
                Rect2 r = slot.GetGlobalRect();
                if (r.Size.X < 2f) continue;
                maxRight = Math.Max(maxRight, r.End.X);
                gridBottom = Math.Max(gridBottom, r.End.Y);
                count++;
            }
        }
        if (count == 0) return;

        float iconCx = maxRight - IconSize / 2f - LeftShift;   // left of the repay button's slot
        float iconCy = gridBottom + BelowGap + IconSize / 2f;
        Transform2D inv = board.GetGlobalTransform().AffineInverse();
        Vector2 local = inv * new Vector2(iconCx, iconCy);
        Position = local - new Vector2(IconSize / 2f, IconSize / 2f);
        _positioned = true;
    }

    private static Texture2D? LoadIcon()
    {
        // A card-stack / storefront icon: loose PNG next to the DLL if present, else the vanilla card-reward icon.
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
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] debt-shop icon loose load failed: {e.Message}"); }
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
