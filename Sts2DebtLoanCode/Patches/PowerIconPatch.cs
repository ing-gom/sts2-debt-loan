using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;   // PowerModel

namespace Sts2DebtLoan;

/// <summary>
/// Custom power status icons. STS2 resolves a power's icon straight from the game's own <c>res://images/…</c>
/// namespace by <c>Id.Entry</c> — <see cref="PowerModel.Icon"/> loads <c>atlases/power_atlas.sprites/&lt;entry&gt;.tres</c>
/// (small status pip) and <see cref="PowerModel.BigIcon"/> loads <c>powers/&lt;entry&gt;.png</c> (tooltip / apply flash),
/// falling back to <c>missing_power.png</c>. Both getters are non-virtual, so a modded PowerModel can't override
/// them: our powers would show a blank small pip and the "?" missing-power big icon.
///
/// We PREFIX both getters and, for our five powers, return a texture loaded from the mod pck
/// (<c>res://Sts2DebtLoan/power_icons/&lt;key&gt;.png</c>), skipping the vanilla resource load entirely (so no
/// missing-resource error spam). <see cref="MegaCrit.Sts2.Core.Nodes.Combat.NPower"/> reads <c>_model.Icon</c> /
/// <c>_model.BigIcon</c>, so it picks these up with no node patching. Display-only (no run/combat mutation) → co-op safe.
/// </summary>
internal static class PowerIconAssets
{
    // Keyed on C# type (not Id.Entry) → the file name is ours to choose; kept == the entry for clarity.
    private static readonly Dictionary<Type, string> Files = new()
    {
        [typeof(DunningLetterPower)]  = "dunning_letter_power",
        [typeof(PaymentBenefitPower)] = "payment_benefit_power",
        [typeof(RefundPower)]         = "refund_power",
        [typeof(JobPlacementPower)]   = "job_placement_power",
        [typeof(BadCreditPower)]      = "bad_credit_power",
        [typeof(CounterclaimPower)]   = "money_attack_power",
        [typeof(StatementPower)]      = "statement_power",
        [typeof(InterestSupportPower)] = "interest_support_power",
        [typeof(PaymentStackPower)]    = "payment_stack_power",
    };
    private static readonly Dictionary<Type, Texture2D?> Cache = new();

    internal static Texture2D? For(PowerModel? model)
    {
        if (model == null) return null;
        var t = model.GetType();
        if (!Files.TryGetValue(t, out var file)) return null;
        if (Cache.TryGetValue(t, out var cached)) return cached;
        Texture2D? tex = null;
        try
        {
            // Same load path RelicModel.Icon uses for mod-pck art: ResourceLoader.Load with Reuse. ResourceLoader
            // .Exists returns false for mounted mod-pck paths, so we must NOT guard on Exists.
            tex = ResourceLoader.Load<Texture2D>($"res://Sts2DebtLoan/power_icons/{file}.png", null, ResourceLoader.CacheMode.Reuse);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] power icon load failed for {file}: {e.Message}"); }
        Cache[t] = tex;   // cache even null → don't retry a missing file every render
        return tex;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Icon), MethodType.Getter)]
internal static class PowerIconSmallPatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        var tex = PowerIconAssets.For(__instance);
        if (tex == null) return true;    // not ours (or art missing) → run vanilla
        __result = tex;
        return false;                    // ours → skip vanilla's missing-.tres load
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.BigIcon), MethodType.Getter)]
internal static class PowerIconBigPatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        var tex = PowerIconAssets.For(__instance);
        if (tex == null) return true;    // not ours (or art missing) → run vanilla (missing_power fallback)
        __result = tex;
        return false;                    // ours → skip vanilla's missing_power resolution
    }
}
