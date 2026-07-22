using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.HoverTips;   // NHoverTipSet
using MegaCrit.Sts2.Core.Nodes.Relics;      // NRelic

namespace Sts2DebtLoan;

/// <summary>
/// Keeps the Merchant's Ledger relic's HOVER TOOLTIP fully on-screen — the text description AND the 빚 독촉
/// card preview (from the relic's ExtraHoverTips). The game positions them via NHoverTipSet.SetAlignmentForRelic
/// (flips left/right by the relic's screen position), but a wide card preview near a screen edge can still
/// clip. We postfix that method and, ONLY for our relic, shift the text + card containers TOGETHER (preserving
/// the gap the game placed between them, so the card never lands on top of the text) by just enough to pull
/// the whole group back inside the viewport. Runs after the game's own positioning, display-only → co-op safe.
/// </summary>
[HarmonyPatch(typeof(NHoverTipSet), "SetAlignmentForRelic")]
internal static class RelicHoverCardClampPatch
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo? CardField = typeof(NHoverTipSet).GetField("_cardHoverTipContainer", F);
    private static readonly FieldInfo? TextField = typeof(NHoverTipSet).GetField("_textHoverTipContainer", F);

    [HarmonyPostfix]
    private static void Postfix(NHoverTipSet __instance, NRelic relic)
    {
        try
        {
            if (relic?.Model is not DebtLoanRelic) return;
            var card = CardField?.GetValue(__instance) as Control;
            var text = TextField?.GetValue(__instance) as Control;

            // Group bounding box across whichever containers are actually shown.
            const float margin = 12f;
            float min = float.MaxValue, max = float.MinValue;
            void Include(Control? c) { if (c != null && c.Visible) { min = Mathf.Min(min, c.GlobalPosition.X); max = Mathf.Max(max, c.GlobalPosition.X + c.Size.X); } }
            Include(card); Include(text);
            if (min > max) return;   // nothing visible

            float vw = __instance.GetViewportRect().Size.X;
            float shift = 0f;
            if (max > vw - margin) shift = (vw - margin) - max;   // overflow right → shift left
            if (min + shift < margin) shift = margin - min;       // then guard the left edge
            if (Mathf.IsZeroApprox(shift)) return;

            // Move BOTH containers by the same amount → they stay separated (no overlap) and fully on-screen.
            if (card != null && card.Visible) card.GlobalPosition += Vector2.Right * shift;
            if (text != null && text.Visible) text.GlobalPosition += Vector2.Right * shift;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] relic hover clamp failed: {e.Message}"); }
    }
}
