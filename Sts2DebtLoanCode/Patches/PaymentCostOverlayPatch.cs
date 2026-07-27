using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;        // CardModel
using MegaCrit.Sts2.Core.Nodes.Cards;   // NCard

namespace Sts2DebtLoan;

/// <summary>
/// A card that spends the 영수증 (payment tally) resource. <see cref="TallyCost"/> is what shows on the card's
/// custom cost badge: <c>-1</c> = X (spend the WHOLE tally, scaling with it — 청구서/정산); <c>&gt;= 0</c> = a fixed
/// amount (power cards, wired later). Enforcement (gate/consume) lives in each card's own OnPlay/IsPlayable.
/// </summary>
internal interface IUsesPaymentTally
{
    int TallyCost { get; }
}

/// <summary>
/// Draws a small "영수증" cost badge (square ledger symbol + the cost number, or "X") on every card that
/// implements <see cref="IUsesPaymentTally"/> — a CUSTOM overlay, since our resource isn't the engine's energy/star
/// so the native cost pips don't cover it. Mirrors the native star-cost pip idea: a badge just under the energy
/// cost. We postfix <see cref="NCard"/>.Reload (same hook the frame recolor uses) and add/update/remove a named
/// child so pooled card nodes stay correct. Display-only → co-op safe.
/// </summary>
[HarmonyPatch(typeof(NCard), "Reload")]
internal static class PaymentCostOverlayPatch
{
    private const string BadgeName = "DebtLoanTallyBadge";
    private const string IconPath = "res://Sts2DebtLoan/power_icons/payment_stack_power.png";
    private const float Size = 66f;

    private static Texture2D? _icon;
    private static readonly FieldInfo? EnergyIconF = typeof(NCard).GetField("_energyIcon",
        BindingFlags.NonPublic | BindingFlags.Instance);

    [HarmonyPostfix]
    private static void Postfix(NCard __instance)
    {
        try
        {
            // The badge is parented to the ENERGY ICON (not the card), so it always tracks the energy pip's real
            // position no matter the layout (hand / library / shop / reward) — no card-local math that goes stale.
            var energy = EnergyIconF?.GetValue(__instance) as Control;
            var existing = energy?.GetNodeOrNull<Control>(BadgeName)
                           ?? __instance.GetNodeOrNull<Control>(BadgeName);   // also catch a legacy card-parented badge

            if (__instance.Model is not IUsesPaymentTally tally || energy == null)
            {
                existing?.QueueFree();     // pooled node reused by a non-tally card (or no energy pip) → strip our badge
                return;
            }

            // A pooled node can carry a badge left under the CARD by an older build — reparent by rebuilding cleanly.
            if (existing != null && existing.GetParent() != energy) { existing.QueueFree(); existing = null; }

            var badge = existing ?? BuildBadge(energy);
            // ALWAYS re-place every Reload: pooled nodes + relayout mean the energy pip's size/anchor can differ per
            // card, so a once-computed position drifts ("scattered"). Centre under the pip, in energy-local coords.
            float ex = energy.Size.X > 0 ? energy.Size.X : Size;
            float ey = energy.Size.Y > 0 ? energy.Size.Y : Size;
            badge.Position = new Vector2((ex - Size) * 0.5f, ey - 6f);

            // ★Print the DISCOUNTED price (경비 처리), not the raw one — the playable gate and the consume call both
            // read EffectiveTallyCost, so a raw badge would grey the card out at a price different from the printed
            // one. Tint it the standard "reduced cost" green when a discount is actually live, same signal the engine
            // uses for cost-reduced energy.
            int raw = tally.TallyCost;
            int cost = LoanService.EffectiveTallyCost(raw, (__instance.Model as CardModel)?.Owner);
            if (badge.GetNodeOrNull<Label>("txt") is { } label)
            {
                label.Text = cost < 0 ? "X" : cost.ToString();
                label.AddThemeColorOverride("font_color", cost < raw
                    ? new Color(0.45f, 1f, 0.45f)        // discounted
                    : new Color(1f, 0.97f, 0.75f));      // normal parchment
            }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally badge skipped: {e.Message}"); }
    }

    private static Control BuildBadge(Control energyIcon)
    {
        if (_icon == null)
        {
            try { _icon = ResourceLoader.Load<Texture2D>(IconPath, null, ResourceLoader.CacheMode.Reuse); }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally badge icon load failed: {e.Message}"); }
        }

        // No ZIndex bump: the badge is a CHILD of the energy pip, so it already draws with it at the SAME depth
        // (a +5 ZIndex lifted it above the pip's own layer, so it rendered on a different depth than the orb).
        var badge = new Control { Name = BadgeName, MouseFilter = Control.MouseFilterEnum.Ignore };

        badge.AddChild(new TextureRect
        {
            Texture = _icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(Size, Size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var txt = new Label
        {
            Name = "txt",
            Size = new Vector2(Size, Size),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        txt.AddThemeFontSizeOverride("font_size", 30);   // smaller than the receipt so the number/X has margin
        txt.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.75f));
        txt.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        txt.AddThemeConstantOverride("outline_size", 6);
        badge.AddChild(txt);

        energyIcon.AddChild(badge);   // parented to the pip → moves with it, survives relayout & node pooling
        return badge;
    }
}
