using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;   // NCard

namespace Sts2DebtLoan;

/// <summary>
/// A card that spends the 납부 실적 (payment tally) resource. <see cref="TallyCost"/> is what shows on the card's
/// custom cost badge: <c>-1</c> = X (spend the WHOLE tally, scaling with it — 청구서/정산); <c>&gt;= 0</c> = a fixed
/// amount (power cards, wired later). Enforcement (gate/consume) lives in each card's own OnPlay/IsPlayable.
/// </summary>
internal interface IUsesPaymentTally
{
    int TallyCost { get; }
}

/// <summary>
/// Draws a small "납부 실적" cost badge (square ledger symbol + the cost number, or "X") on every card that
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
            var existing = __instance.GetNodeOrNull<Control>(BadgeName);
            if (__instance.Model is not IUsesPaymentTally tally)
            {
                existing?.QueueFree();     // pooled node reused by a non-tally card → strip our badge
                return;
            }

            int cost = tally.TallyCost;
            string text = cost < 0 ? "X" : cost.ToString();
            var badge = existing ?? BuildBadge(__instance);
            if (badge.GetNodeOrNull<Label>("txt") is { } label) label.Text = text;
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally badge skipped: {e.Message}"); }
    }

    private static Control BuildBadge(NCard card)
    {
        if (_icon == null)
        {
            try { _icon = ResourceLoader.Load<Texture2D>(IconPath, null, ResourceLoader.CacheMode.Reuse); }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally badge icon load failed: {e.Message}"); }
        }

        var badge = new Control { Name = BadgeName, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 5 };

        // sit just UNDER the energy pip (card-local coords: (0,0) = card centre). Fall back to a top-left slot.
        Vector2 pos = new Vector2(-104f, -78f);
        if (EnergyIconF?.GetValue(card) is Control energy) pos = energy.Position + new Vector2(0f, 74f);
        badge.Position = pos;

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
        txt.AddThemeFontSizeOverride("font_size", 38);
        txt.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.75f));
        txt.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        txt.AddThemeConstantOverride("outline_size", 6);
        badge.AddChild(txt);

        card.AddChild(badge);
        return badge;
    }
}
