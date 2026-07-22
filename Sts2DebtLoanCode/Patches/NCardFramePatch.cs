using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;                 // ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;       // CurseCardPool
using MegaCrit.Sts2.Core.Nodes.Cards;            // NCard

namespace Sts2DebtLoan;

/// <summary>
/// Gives the 독촉장 (Dunning Letter) card a custom slate-lavender look — the curse frame recoloured, plus the
/// "Power" banner/plaque re-tinted to match (otherwise the default green banner clashes). The frame/banner
/// colour lives in each element's ShaderMaterial, tinted by h/s/v (hue/saturation/value) uniforms rather than
/// a colour uniform. Since those materials are shared + non-overridable via the pool, we postfix NCard.Reload
/// (where the node assigns them) and swap in cached, re-tinted duplicates for our card only. Display-only →
/// co-op safe.
/// </summary>
[HarmonyPatch(typeof(NCard), "Reload")]
internal static class NCardFramePatch
{
    // 연한 회보라 (slate lavender): violet hue, low saturation (grayish), bright (pale). Tunable.
    private const float TargetH = 0.72f;
    private const float TargetS = 0.25f;
    private const float TargetV = 0.80f;

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo? FrameF   = typeof(NCard).GetField("_frame", F);
    private static readonly FieldInfo? BannerF  = typeof(NCard).GetField("_banner", F);
    private static readonly FieldInfo? BorderF  = typeof(NCard).GetField("_portraitBorder", F);
    private static readonly FieldInfo? PlaqueF  = typeof(NCard).GetField("_typePlaque", F);

    private static ShaderMaterial? _frameMat;
    private static ShaderMaterial? _bannerMat;
    private static bool _probed;

    [HarmonyPostfix]
    private static void Postfix(NCard __instance)
    {
        try
        {
            if (__instance.Model is not DunningLetterCard) return;

            _frameMat  ??= Retint((__instance.Model.FrameMaterial as ShaderMaterial)
                                   ?? ModelDb.CardPool<CurseCardPool>()?.FrameMaterial as ShaderMaterial, "frame");
            _bannerMat ??= Retint(__instance.Model.BannerMaterial as ShaderMaterial, "banner");

            SetMat(FrameF, __instance, _frameMat);
            SetMat(BorderF, __instance, _bannerMat);
            SetMat(BannerF, __instance, _bannerMat);
            SetMat(PlaqueF, __instance, _bannerMat);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] frame recolor skipped: {e.Message}"); }
    }

    private static void SetMat(FieldInfo? field, NCard card, ShaderMaterial? mat)
    {
        if (mat != null && field?.GetValue(card) is CanvasItem ci) ci.Material = mat;
    }

    /// <summary>Duplicate a frame/banner ShaderMaterial and push our slate-lavender h/s/v onto it.</summary>
    private static ShaderMaterial? Retint(ShaderMaterial? src, string label)
    {
        if (src?.Shader == null) return null;
        var dup = (ShaderMaterial)src.Duplicate(true);
        if (!_probed && label == "frame")
        {
            _probed = true;
            MainFile.Logger.Info($"[{MainFile.ModId}] frame baseline h/s/v = " +
                $"{dup.GetShaderParameter("h")}/{dup.GetShaderParameter("s")}/{dup.GetShaderParameter("v")}");
        }
        dup.SetShaderParameter("h", TargetH);
        dup.SetShaderParameter("s", TargetS);
        dup.SetShaderParameter("v", TargetV);
        return dup;
    }
}
