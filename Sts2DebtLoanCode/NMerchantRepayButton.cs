using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.addons.mega_text;          // MegaLabel
using MegaCrit.Sts2.Core.Assets;               // PreloadManager
using MegaCrit.Sts2.Core.Context;              // LocalContext
using MegaCrit.Sts2.Core.Entities.Players;     // Player
using MegaCrit.Sts2.Core.Helpers;              // TaskHelper, StsColors
using MegaCrit.Sts2.Core.HoverTips;            // HoverTip, IHoverTip, HoverTipAlignment
using MegaCrit.Sts2.Core.Nodes.HoverTips;      // NHoverTipSet
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;  // NMerchantInventory, NMerchantSlot
using MegaCrit.Sts2.Core.Runs;                 // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// A "Repay Loan" control on the merchant screen. Shows only while the LOCAL player carries an active
/// loan; its cost is the outstanding principal ("원금 X골드"). Clicking pays it back — retiring the
/// Merchant's Ledger relic and clearing every Debt card via <see cref="LoanService.Repay"/> (which
/// already does the co-op-safe LoseGold + SyncLocalGoldLost + retire).
///
/// Structure is copied from Sts2RelicForge's NMerchantCleanseButton — a bare <see cref="TextureButton"/>
/// wrapped in a Control, parented onto the shop's <c>_slotsContainer</c> via a postfix on
/// <see cref="NMerchantInventory._Ready"/>, with the cost display cloned from the card-removal slot so
/// it matches the vanilla price look. TODO(coop-guard): mutates run gold from a UI hook — verify before ship.
/// </summary>
internal sealed partial class NMerchantRepayButton : Control
{
    private const float IconSize = 60f;
    private const float HoverScale = 1.2f;
    private const float CostScale = 0.75f;
    private const float CostGap = 6f;
    private const float BelowGap = 12f;
    private const float LeftNudge = 24f;

    private NMerchantInventory _shop = null!;
    private Player? _player;
    private bool _busy;
    private TextureButton _icon = null!;
    private string _tipTitle = "Repay Loan";
    private string _tipText = "";
    private Control? _costNode;
    private Tween? _hoverTween;
    private float _contentW = IconSize;
    private float _iconH = IconSize;
    private bool _positioned;

    public static void Attach(NMerchantInventory shop)
    {
        var w = new NMerchantRepayButton { _shop = shop };
        Node parent = (Node?)shop._slotsContainer ?? shop;
        parent.AddChild(w);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        Position = new Vector2(200f, 420f);
        // Resolve the LOCAL player (co-op): FirstOrDefault() would be the host, so a client would
        // otherwise repay the host's loan.
        _player = LocalContext.GetMe(RunManager.Instance.State?.Players ?? Enumerable.Empty<Player>())
                  ?? _shop.Inventory?.Player
                  ?? RunManager.Instance.State?.Players.FirstOrDefault();

        _icon = new TextureButton
        {
            TextureNormal = LoadRepayIcon(),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            Size = new Vector2(IconSize, IconSize),
        };
        _icon.Pressed += OnPressed;
        _icon.MouseEntered += () => { ScaleWidget(HoverScale); NHoverTipSet.CreateAndShow(_icon, MakeTip(_tipTitle, _tipText), HoverTipAlignment.Left); };
        _icon.MouseExited += () => { ScaleWidget(1f); NHoverTipSet.Remove(_icon); };
        AddChild(_icon);

        BuildCostDisplay();
        LayoutChildren();

        if (_player != null) _player.GoldChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_player != null) _player.GoldChanged -= Refresh;
    }

    public override void _Process(double delta)
    {
        try
        {
            // Only present while the shop is open AND the local player actually owes something.
            bool show = _shop.IsOpen && HasActiveLoan();
            if (Visible != show) Visible = show;
            if (show) { LayoutChildren(); PositionInShop(); Refresh(); }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay button _Process failed: {e.Message}"); }
    }

    private bool HasActiveLoan()
    {
        var rec = LoanService.For(_player);
        return rec != null && rec.Active && rec.Principal > 0;
    }

    private void LayoutChildren()
    {
        float iconW = _icon.Size.X > 0 ? _icon.Size.X : IconSize;
        _iconH = _icon.Size.Y > 0 ? _icon.Size.Y : IconSize;
        float costW = (_costNode?.Size.X ?? 0f) * CostScale;
        float w = Math.Max(iconW, costW);
        _contentW = w;
        _icon.Position = new Vector2((w - iconW) / 2f, 0f);
        if (_costNode != null)
            _costNode.Position = new Vector2((w - costW) / 2f, _iconH + CostGap);
        PivotOffset = new Vector2(w / 2f, _iconH / 2f);
    }

    private void ScaleWidget(float s)
    {
        _hoverTween?.Kill();
        _hoverTween = CreateTween();
        _hoverTween.TweenProperty(this, "scale", Vector2.One * s, 0.12)
                   .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    /// <summary>Sit centered below the relic/potion grid (board-local coords).</summary>
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

        float iconW = _icon.Size.X > 0 ? _icon.Size.X : IconSize;
        // Below the grid, RIGHT-aligned to the grid's right edge. RelicForge's reforge/cleanse buttons
        // sit below-grid LEFT (minLeft), so this keeps us in the shop-action row without overlapping them.
        float iconCx = maxRight - iconW / 2f - LeftNudge;
        float iconCy = gridBottom + BelowGap + _iconH / 2f;

        Transform2D inv = board.GetGlobalTransform().AffineInverse();
        Vector2 local = inv * new Vector2(iconCx, iconCy);
        Position = local - new Vector2(_contentW / 2f, _iconH / 2f);
        _positioned = true;
    }

    /// <summary>Repay icon: a loose PNG next to the DLL if present, else the mod's relic art from the
    /// pck, else a vanilla fallback.</summary>
    private static Texture2D? LoadRepayIcon()
    {
        // Load the ledger art the SAME way RelicModel.Icon does — ResourceLoader.Load with Reuse
        // (decomp 294521). This resolves the mounted mod-pck path; ResourceLoader.Exists returns false
        // for it, so we must NOT guard on Exists (that was why the previous GD.Load was skipped).
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://Sts2DebtLoan/icons/debt_loan_relic.png", null, ResourceLoader.CacheMode.Reuse);
            if (tex != null) return tex;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay icon pck load failed: {e.Message}"); }
        // Dev override: a loose PNG next to the DLL.
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(typeof(NMerchantRepayButton).Assembly.Location);
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

    private void BuildCostDisplay()
    {
        try
        {
            if (_shop._cardRemovalNode?.GetNodeOrNull("Cost") is not Control template) return;
            if (template.Duplicate() is not Control clone) return;
            _costNode = clone;
            clone.Scale = Vector2.One * CostScale;
            AddChild(clone);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay cost display failed: {e.Message}"); }
    }

    private static void SetCostText(Node n, string text, Color color)
    {
        if (n is MegaLabel ml) { ml.SetTextAutoSize(text); ml.Modulate = color; }
        foreach (var c in n.GetChildren()) SetCostText(c, text, color);
    }

    private void Refresh()
    {
        var rec = LoanService.For(_player);
        int cost = rec?.Principal ?? 0;
        bool hasLoan = rec != null && rec.Active && cost > 0;
        bool affordable = _player != null && (int)_player.Gold >= cost;
        bool usable = hasLoan && affordable;

        if (_costNode != null) SetCostText(_costNode, cost.ToString(), affordable ? StsColors.cream : StsColors.red);
        _icon.Modulate = usable ? Colors.White : StsColors.halfTransparentWhite;

        _tipText = !hasLoan
            ? "No loan to repay."
            : usable
                ? $"Pay back {cost} gold to retire the Merchant's Ledger and clear all Debt cards."
                : $"Not enough gold — you owe {cost}.";
    }

    private void OnPressed()
    {
        if (_busy || _player == null) return;
        if (!HasActiveLoan()) return;
        var rec = LoanService.For(_player);
        if (rec == null || (int)_player.Gold < rec.Principal) return;
        _busy = true;
        TaskHelper.RunSafely(Flow());
    }

    private async Task Flow()
    {
        try
        {
            bool ok = await LoanService.Repay(_player!);
            if (ok) MainFile.Logger.Info($"[{MainFile.ModId}] shop repay succeeded.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] shop repay failed: {e.Message}"); }
        finally { _busy = false; Refresh(); }
    }

    /// <summary>Plain title+body hover tip using the game's own HoverTip (setters reachable via the
    /// ModKit publicizer), rendered natively by NHoverTipSet.</summary>
    private static IHoverTip MakeTip(string title, string body)
    {
        var t = new HoverTip();
        t.Title = title;
        t.Description = body;
        t.Id = "sts2debtloan_shop_" + title;
        return t;
    }
}

/// <summary>Attaches the repay control to every merchant screen.</summary>
[HarmonyLib.HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory._Ready))]
internal static class MerchantRepayButtonPatch
{
    private static void Postfix(NMerchantInventory __instance)
    {
        try { NMerchantRepayButton.Attach(__instance); }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] repay button add failed: {e.Message}"); }
    }
}
