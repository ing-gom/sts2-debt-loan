using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Relics;   // NRelic
using MegaCrit.Sts2.Core.Runs;            // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// Evolving-relic juice: stamps a tier-specific "corruption" overlay onto the Merchant's Ledger icon that
/// grows as the debt escalates (tier 2/3/4) — one relic, evolving FORM (not a different relic). STS2 only
/// refreshes a relic's icon art on widget (re)bind, never on live state change, so we own the overlay: a
/// TextureRect child on the relic's Icon, (re)attached whenever NRelic.Reload runs (see NRelicReloadPatch),
/// and refreshed live on RunManager.RoomEntered so it evolves as you walk the map. Tier 1 shows nothing
/// (clean base icon); tiers 2/3/4 load res://Sts2DebtLoan/icons/ledger_t{tier}.png (missing art → hidden,
/// never crashes). Purely local display (no sim state, no networked commands) → co-op safe.
/// </summary>
internal static class LedgerOverlay
{
    private const string OverlayName = "DebtLedgerTierOverlay";
    private static readonly List<NRelic> _hosts = new();   // NRelic widgets carrying our overlay (pruned on refresh)
    private static bool _subscribed;

    /// <summary>Called from NRelic.Reload's postfix. If this widget shows our relic, make sure the overlay
    /// child exists and reflects the current tier.</summary>
    internal static void Ensure(NRelic? relic)
    {
        try
        {
            if (relic?.Model is not DebtLoanRelic) return;
            SubscribeOnce();
            var icon = relic.Icon;
            if (icon == null) return;

            var ov = icon.GetNodeOrNull<TextureRect>(OverlayName);
            if (ov == null)
            {
                ov = new TextureRect
                {
                    Name = OverlayName,
                    MouseFilter = Control.MouseFilterEnum.Ignore,     // never steal hover from the relic
                    // ★ IgnoreSize: without it a TextureRect's minimum size = the texture's NATIVE size (256px+),
                    // so the overlay ignored the anchors and rendered huge. IgnoreSize makes it follow its
                    // layout rect (the icon) instead. Set BEFORE the size/anchor preset (order matters in Godot).
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                };
                icon.AddChild(ov);                                    // last child → drawn over the base icon
                ov.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);   // fill the icon EXACTLY (anchors + offsets)
                if (!_hosts.Contains(relic)) _hosts.Add(relic);
            }
            Apply(relic, ov);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] ledger overlay attach failed: {e.Message}"); }
    }

    private static void Apply(NRelic relic, TextureRect ov)
    {
        int tier = (relic?.Model as DebtLoanRelic)?.CurrentTier ?? 0;
        if (tier < 2) { ov.Visible = false; return; }                 // tier 1 (or no loan) = clean base icon
        var tex = ResourceLoader.Load<Texture2D>($"res://Sts2DebtLoan/icons/ledger_t{tier}.png", null, ResourceLoader.CacheMode.Reuse);
        ov.Texture = tex;
        ov.Visible = tex != null;                                     // missing art → stay hidden (graceful)
    }

    private static void SubscribeOnce()
    {
        if (_subscribed) return;
        var rm = RunManager.Instance;
        if (rm == null) return;   // not in a run yet; a later Ensure will subscribe
        rm.RoomEntered += RefreshAll;
        _subscribed = true;
    }

    /// <summary>Force an immediate re-evaluation of every live overlay (used by the dl_tier debug command,
    /// which changes the tier without a room transition).</summary>
    internal static void Refresh() => RefreshAll();

    /// <summary>On every room change, re-evaluate the tier for each live overlay so the ledger visibly evolves
    /// as the debt escalates (dropping any widgets that have since been freed).</summary>
    private static void RefreshAll()
    {
        try
        {
            for (int i = _hosts.Count - 1; i >= 0; i--)
            {
                var relic = _hosts[i];
                if (relic == null || !GodotObject.IsInstanceValid(relic) || relic.Icon == null)
                { _hosts.RemoveAt(i); continue; }
                var ov = relic.Icon.GetNodeOrNull<TextureRect>(OverlayName);
                if (ov == null || !GodotObject.IsInstanceValid(ov)) { _hosts.RemoveAt(i); continue; }
                Apply(relic, ov);
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] ledger overlay refresh failed: {e.Message}"); }
    }
}
