using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;                      // CardModel
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;   // NCardLibrary, NCardPoolFilter

namespace Sts2DebtLoan;

/// <summary>
/// Adds a DEDICATED "DebtLoan" pool-filter button to the Card Library so this mod's cards can be browsed as
/// their own category (instead of being scattered across the vanilla Colorless + Misc filters).
///
/// The library's pool filters are authored scene nodes (NCardPoolFilter), fetched by unique path in
/// NCardLibrary._Ready and registered in <c>_poolFilters</c> with a <c>Func&lt;CardModel,bool&gt;</c>
/// predicate; a card shows only when it matches the SELECTED filter, and <c>UpdateCardPoolFilter</c> drives
/// exclusive selection over whatever is in <c>_poolFilters</c>. So we DUPLICATE an existing filter node (it
/// re-resolves its own icon/material in _Ready and keeps its Toggled→UpdateCardPoolFilter wiring), give it a
/// DebtLoan icon, and register a predicate matching every card declared in this assembly. Selecting it then
/// shows exactly the DebtLoan set. Paired with <see cref="CardLibraryGridPatch"/> (puts the cards in the grid)
/// and <see cref="CardLibraryUnlockPatch"/> (art, not silhouettes). Main-menu, display-only → co-op safe.
/// </summary>
[HarmonyPatch(typeof(NCardLibrary), "_Ready")]
internal static class CardLibraryModFilterPatch
{
    private static readonly Assembly Own = typeof(CardLibraryModFilterPatch).Assembly;
    private const string FilterName = "DebtLoanPool";
    private const string IconPath = "res://Sts2DebtLoan/icons/debt_loan_relic.png";

    private static void Postfix(NCardLibrary __instance)
    {
        try
        {
            NCardPoolFilter template = __instance._miscPoolFilter;   // publicized
            if (template == null || __instance._poolFilters == null) return;
            var parent = ((Node)template).GetParent();
            if (parent == null || parent.HasNode(FilterName)) return;   // idempotent

            // Duplicate keeps the script; entering the tree runs the dup's _Ready, which re-binds its own
            // _image / _hsv. ★Duplicate does NOT copy the C# Callable Toggled→UpdateCardPoolFilter connection
            // (runtime delegate, not scene-serialized), so we MUST reconnect it explicitly — otherwise clicking
            // the filter toggles its own selection but never deselects the others or refreshes the grid (the
            // "selected together with the neighbouring 저주 filter + no cards show" bug).
            var dup = (NCardPoolFilter)((Node)template).Duplicate();
            ((Node)dup).Name = FilterName;
            parent.AddChild(dup);                                // enters tree → dup._Ready binds _image / _hsv
            parent.MoveChild(dup, parent.GetChildCount() - 1);   // place at the end of the filter row

            // The icon's selected/unselected LOOK is driven by the _hsv shader material (bright when selected,
            // dim when not) + a scale tween. ★Duplicate() SHARES the material resource with the Misc filter, so
            // when clicking ours deselects Misc, Misc's OnToggle repaints that shared material back to "dim" —
            // and our icon (same material) never lights up, even though the card list is correct. Give ours a
            // PRIVATE material so its selection tint is its own. Also swap the glyph to the Ledger relic art.
            try
            {
                if (dup._image is CanvasItem ci && dup._hsv?.Shader != null)
                {
                    var mat = (ShaderMaterial)dup._hsv.Duplicate(true);
                    ci.Material = mat;
                    dup._hsv = mat;
                }
                var tex = ResourceLoader.Load<Texture2D>(IconPath, null, ResourceLoader.CacheMode.Reuse);
                var tr = (dup._image as TextureRect) ?? FindTextureRect((Node)dup);
                if (tex != null && tr != null) tr.Texture = tex;
            }
            catch (Exception ie) { MainFile.Logger.Warn($"[{MainFile.ModId}] mod-filter icon set failed: {ie.Message}"); }

            dup.IsSelected = false;                              // paint the unselected state onto OUR material
            ((GodotObject)dup).Connect(NCardPoolFilter.SignalName.Toggled,
                                       Callable.From<NCardPoolFilter>(__instance.UpdateCardPoolFilter));

            __instance._poolFilters[dup] = c => c != null && c.GetType().Assembly == Own;
            MainFile.Logger.Info($"[{MainFile.ModId}] added dedicated DebtLoan card-library filter.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] dedicated card-library filter add failed: {e.Message}"); }
    }

    private static TextureRect? FindTextureRect(Node n)
    {
        if (n is TextureRect t) return t;
        foreach (var c in n.GetChildren())
        {
            var r = FindTextureRect(c);
            if (r != null) return r;
        }
        return null;
    }
}
