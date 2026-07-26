using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;             // MegaLabel
using MegaCrit.Sts2.Core.Context;                 // LocalContext
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;            // LocManager
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;     // NMerchantInventory
using MegaCrit.Sts2.Core.Runs;                    // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// "대출 3 / 3" — the remaining loan DRAWS, shown at the top of the merchant rug while the shop is open.
///
/// WHY HERE and not on the Ledger relic's hover (the other candidate): the draw limit only matters at the moment
/// you are deciding whether this relic is worth one of your three, and the Ledger does not exist yet before the
/// FIRST loan — so a hover-only readout would show nothing precisely when the first decision is made. The chip is
/// always on screen while the shop is open, including at 3/3 before any borrowing.
///
/// Read-only display: the count itself lives on <see cref="LoanRecord.LoanDraws"/> and the gate is
/// <see cref="LoanService.CanLoanCover"/>. When the draws run out this turns red and the merchant's loanable price
/// tags stop being green (MerchantPriceColorPatch already keys off the same gate), so the two agree for free.
/// Hidden entirely when the limit is disabled (MaxLoanDraws ≤ 0) or loans aren't allowed in this act.
/// Attached per merchant screen by <see cref="LoanDrawsChipPatch"/>, mirroring NDebtCardShopButton.
/// </summary>
internal sealed partial class NLoanDrawsChip : Control
{
    private const float PadX = 26f, PadY = 12f, FontSize = 34f;

    private static readonly Color Ink = new(1.00f, 0.90f, 0.62f);   // warm gold — "credit available"
    private static readonly Color InkSpent = new(0.92f, 0.36f, 0.34f);
    private static readonly Color Plate = new(0.09f, 0.07f, 0.12f, 0.72f);
    private static readonly Color Edge = new(0.72f, 0.58f, 0.28f, 0.85f);

    private NMerchantInventory _shop = null!;
    private Player? _player;
    private MegaLabel? _label;
    private Panel? _plate;
    private string _lastText = "";
    private bool _positioned;

    public static void Attach(NMerchantInventory shop)
    {
        var w = new NLoanDrawsChip { _shop = shop };
        Node parent = (Node?)shop._slotsContainer ?? shop;
        parent.AddChild(w);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;   // pure readout — never eats a click meant for the rug
        Visible = false;
        _player = LocalContext.GetMe(RunManager.Instance.State?.Players ?? Enumerable.Empty<Player>())
                  ?? _shop.Inventory?.Player
                  ?? RunManager.Instance.State?.Players.FirstOrDefault();

        _plate = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Plate,
            BorderColor = Edge,
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        });
        AddChild(_plate);

        // Clone one of the shop's own MegaLabels: the stock Godot font has no CJK glyphs, so a plain Label would
        // render tofu for the Korean/Japanese/Chinese strings (the debt-shop panel hit this too).
        _label = CloneLabel();
        if (_label != null) AddChild(_label);
    }

    public override void _Process(double delta)
    {
        try
        {
            bool show = _shop.IsOpen && DebtLoanConfig.MaxLoanDraws > 0 && LoanService.ActAllowsLoan(_player);
            if (Visible != show) Visible = show;
            if (!show) return;
            Refresh();
            Position();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] loan-draws chip _Process failed: {e.Message}"); }
    }

    private void Refresh()
    {
        if (_label == null) return;
        int left = LoanService.DrawsLeftFor(_player);
        int max = DebtLoanConfig.MaxLoanDraws;
        if (left > max) left = max;   // DrawsLeftFor returns int.MaxValue only when unlimited (already filtered)
        string ui = DebtLoanLoc.DebtShopUiFor(LocManager.Instance?.Language ?? "eng").Draws;
        string text = string.Format(ui, left, max);
        if (text != _lastText)
        {
            _label.Text = text;
            _lastText = text;
        }
        _label.Modulate = left > 0 ? Ink : InkSpent;
    }

    /// <summary>Top-centre of the rug, above the item grid — the one strip that is empty in every shop layout,
    /// and directly above the price tags the number governs. Re-measured until the rug has a real size (the shop
    /// lays out over a few frames), then latched.</summary>
    private void Position()
    {
        if (_label == null || _plate == null) return;
        Vector2 text = _label.GetMinimumSize();
        Size = new Vector2(text.X + PadX * 2f, text.Y + PadY * 2f);
        _plate.Size = Size;
        _plate.Position = Vector2.Zero;
        _label.Position = new Vector2(PadX, PadY);

        if (_positioned) return;
        Control? rug = _shop._slotsContainer;
        if (rug == null || rug.Size.X < 100f) return;   // not laid out yet
        SetPosition(new Vector2(rug.Size.X * 0.5f - Size.X * 0.5f, rug.Size.Y * 0.045f));
        _positioned = true;
    }

    private MegaLabel? CloneLabel()
    {
        try
        {
            Node? root = (Node?)_shop._slotsContainer ?? _shop;
            if (FindMegaLabel(root) is not MegaLabel tpl) return null;
            if (tpl.Duplicate() is not MegaLabel ml) return null;
            ml.Visible = true;
            ml.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            ml.GrowHorizontal = GrowDirection.End;
            ml.GrowVertical = GrowDirection.End;
            ml.CustomMinimumSize = Vector2.Zero;
            ml.Size = Vector2.Zero;
            ml.Scale = Vector2.One;
            ml.HorizontalAlignment = HorizontalAlignment.Center;
            ml.VerticalAlignment = VerticalAlignment.Center;
            ml.ClipText = false;
            ml.AutowrapMode = TextServer.AutowrapMode.Off;
            ml.MouseFilter = MouseFilterEnum.Ignore;
            ml.AddThemeFontSizeOverride("font_size", (int)FontSize);
            return ml;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] loan-draws chip label clone failed: {e.Message}"); return null; }
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

/// <summary>Attaches the loan-draws chip to every merchant screen (same hook NDebtCardShopButton uses).</summary>
[HarmonyLib.HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory._Ready))]
internal static class LoanDrawsChipPatch
{
    private static void Postfix(NMerchantInventory __instance)
    {
        try { NLoanDrawsChip.Attach(__instance); }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] loan-draws chip attach failed: {e.Message}"); }
    }
}
