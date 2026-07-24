using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;                 // ModelDb
using MegaCrit.Sts2.Core.Models.CardPools;       // CurseCardPool
using MegaCrit.Sts2.Core.Nodes.Cards;            // NCard

namespace Sts2DebtLoan;

/// <summary>
/// Gives DebtLoan's DECK cards a shared slate-lavender look — the frame recoloured, plus the banner/plaque
/// re-tinted to match — so the payment-set reads as one family. The colour lives in each element's
/// ShaderMaterial, tinted by h/s/v (hue/saturation/value) uniforms rather than a colour uniform. Since those
/// materials are shared + non-overridable via the pool, we postfix NCard.Reload (where the node assigns them)
/// and swap in cached, re-tinted duplicates for our cards only. Display-only → co-op safe.
///
/// Scope (<see cref="ReColor"/>): the 독촉장 leverage card + the payment-set cards you actually hold. Excluded:
///   • the 저주 (curse) cards — they keep the vanilla curse frame;
///   • the GENERATED tokens 품삯 (Wages) / 성실 납부 (Diligent Payment) — they stay plain colorless, so the
///     resources a card spits out read as ordinary colorless cards, distinct from the themed engine cards.
/// All re-coloured cards share ONE cached material pair derived from the 독촉장 model, so they tint identically.
/// </summary>
[HarmonyPatch(typeof(NCard), "Reload")]
internal static class NCardFramePatch
{
    // DebtLoan cards that get the slate-lavender frame. NOT the curse cards, NOT the generated Wages/Diligent
    // Payment tokens (those keep the colorless theme — see the class summary).
    private static readonly HashSet<Type> ReColor = new()
    {
        typeof(DunningLetterCard), typeof(JobPlacementCard), typeof(PaymentBenefitCard),
        typeof(RefundCard), typeof(SettlementCard), typeof(InvoiceCard), typeof(BloodPaymentCard),
        typeof(GarnishmentCard), typeof(LoanStrikeCard), typeof(MortgageCard),
    };
    // 연한 회보라 (slate lavender): violet hue, low saturation (grayish), bright (pale). Tunable.
    // TargetH is a field (not const) so the solo-verify hue-sweep can override it per render; ship value 0.72.
    internal static float TargetH = 0.72f;
    private const float TargetS = 0.25f;
    private const float TargetV = 0.80f;

    /// <summary>Drop the cached re-tinted materials so the NEXT hovered 독촉장 rebuilds them from the current
    /// <see cref="TargetH"/>. Solo-verify only (lets one run screenshot several hues); no-op in normal play.</summary>
    internal static void ResetCacheForSweep() { _frameMat = null; _bannerMat = null; _probed = false; }

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
            if (__instance.Model == null || !ReColor.Contains(__instance.Model.GetType())) return;

            EnsureMats();
            SetMat(FrameF, __instance, _frameMat);
            SetMat(BorderF, __instance, _bannerMat);
            SetMat(BannerF, __instance, _bannerMat);
            SetMat(PlaqueF, __instance, _bannerMat);
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] frame recolor skipped: {e.Message}"); }
    }

    /// <summary>Build the shared frame + banner materials ONCE, from the 독촉장 model, so every re-coloured card
    /// tints from the same base (order-independent — whichever card renders first, the look is identical).</summary>
    private static void EnsureMats()
    {
        if (_frameMat != null) return;
        var dl = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(typeof(DunningLetterCard)));
        var frameBase = (dl?.FrameMaterial as ShaderMaterial)
                        ?? ModelDb.CardPool<CurseCardPool>()?.FrameMaterial as ShaderMaterial;
        _frameMat  = Retint(frameBase, "frame");
        _bannerMat = Retint(dl?.BannerMaterial as ShaderMaterial, "banner");
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
