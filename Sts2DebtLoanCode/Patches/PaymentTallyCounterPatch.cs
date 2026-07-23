using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;          // Player
using MegaCrit.Sts2.Core.Context;                   // LocalContext
using MegaCrit.Sts2.Core.Nodes.Combat;              // NCombatUi
using MegaCrit.Sts2.Core.Combat;                    // CombatState

namespace Sts2DebtLoan;

/// <summary>
/// 납부 실적 (Payment Tally) — our own combat resource, shown on a CUSTOM HUD counter near the energy orb (like
/// Regent's Stars, but our own so there is no collision). Not a power buff. The value lives in
/// <see cref="LoanService"/>; this node listens to <see cref="LoanService.TallyChanged"/> and re-renders. It is added
/// to the combat UI's <c>EnergyCounterContainer</c>, offset so it does not overlap the native Star counter, and hides
/// itself at 0. Display-only + local player → co-op safe (the underlying tally is computed identically on every peer).
/// </summary>
internal sealed partial class NPaymentTallyCounter : Control
{
    private const string IconPath = "res://Sts2DebtLoan/power_icons/payment_stack_power.png";
    private static Texture2D? _iconTex;

    private Player? _player;
    private Label _label = null!;

    internal static NPaymentTallyCounter Create(Player player)
    {
        var c = new NPaymentTallyCounter { _player = player, Name = "DebtLoanTallyCounter" };
        c.MouseFilter = MouseFilterEnum.Ignore;
        c.CustomMinimumSize = new Vector2(96, 96);

        // ledger icon
        if (_iconTex == null)
        {
            try { _iconTex = ResourceLoader.Load<Texture2D>(IconPath, null, ResourceLoader.CacheMode.Reuse); }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally icon load failed: {e.Message}"); }
        }
        var icon = new TextureRect
        {
            Texture = _iconTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(72, 72),
            Position = new Vector2(0, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        c.AddChild(icon);

        // count label (bottom-right of the icon, like an energy/star count)
        c._label = new Label
        {
            Position = new Vector2(40, 40),
            Size = new Vector2(56, 48),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        c._label.AddThemeFontSizeOverride("font_size", 40);
        c._label.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
        c._label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        c._label.AddThemeConstantOverride("outline_size", 8);
        c.AddChild(c._label);

        LoanService.TallyChanged += c.OnTallyChanged;
        c.Refresh();
        return c;
    }

    private void OnTallyChanged(Player p, int value)
    {
        if (ReferenceEquals(p, _player)) Refresh();
    }

    private void Refresh()
    {
        int v = LoanService.PaymentsThisCombat(_player);
        _label.Text = v.ToString();
        Visible = v > 0;
    }

    public override void _ExitTree()
    {
        LoanService.TallyChanged -= OnTallyChanged;
        base._ExitTree();
    }
}

/// <summary>Injects <see cref="NPaymentTallyCounter"/> into the combat HUD when combat starts.</summary>
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class PaymentTallyCounterInjectPatch
{
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        try
        {
            var container = __instance.EnergyCounterContainer;
            if (container == null) return;
            Player me = LocalContext.GetMe(state);
            if (me == null) return;
            var counter = NPaymentTallyCounter.Create(me);
            container.AddChild(counter);
            // sit ABOVE the energy/star cluster so it never overlaps the native Star counter (which reparents onto
            // the energy counter). Tuned against the 1780×1080 combat HUD; offset is relative to the energy container.
            counter.Position = new Vector2(-8f, -150f);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] tally counter inject failed: {e.Message}"); }
    }
}
