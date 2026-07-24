using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;                 // ModelDb, CardModel
using MegaCrit.Sts2.Core.Models.CardPools;       // CurseCardPool
using MegaCrit.Sts2.Core.Nodes.Cards;            // NCard

namespace Sts2DebtLoan;

/// <summary>
/// Gives DebtLoan's DECK cards a bespoke royal-purple + gold frame so the payment-set reads as one premium
/// family. The FRAME is swapped for a custom texture (res://Sts2DebtLoan/frames/frame_&lt;type&gt;.png) — one per
/// card type (attack/skill/power), each recoloured from the vanilla frame shape so it aligns exactly. Because
/// the vanilla frame TextureRect carries the colorless h/s/v desaturate material (which would grey-out our
/// colours), we also clear _frame.Material. The banner / portrait-border / type-plaque keep the earlier
/// slate-lavender h/s/v tint so they harmonise with the purple frame. Display-only → co-op safe.
///
/// NCard.Reload assigns _frame.Texture = Model.Frame and _frame.Material = Model.FrameMaterial every rebuild,
/// so we postfix it and override for our cards only.
///
/// Scope (<see cref="ReColor"/>): the 독촉장 leverage card + the payment-set cards you actually hold. Excluded:
///   • the 저주 (curse) cards — they keep the vanilla curse frame;
///   • the GENERATED tokens 품삯 (Wages) / 성실 납부 (Diligent Payment) — they stay plain colorless.
/// </summary>
[HarmonyPatch(typeof(NCard), "Reload")]
internal static class NCardFramePatch
{
    // DebtLoan cards that get the purple+gold frame. NOT the curse cards, NOT the generated Wages/Diligent
    // Payment tokens (those keep the colorless theme — see the class summary).
    private static readonly HashSet<Type> ReColor = new()
    {
        typeof(DunningLetterCard), typeof(JobPlacementCard), typeof(PaymentBenefitCard),
        typeof(RefundCard), typeof(SettlementCard), typeof(InvoiceCard), typeof(BloodPaymentCard),
        typeof(GarnishmentCard), typeof(LoanStrikeCard), typeof(MortgageCard),
        typeof(CounterclaimCard), typeof(StatementCard), typeof(InterestSupportCard),
        typeof(CollectionCard),   // 추심 — a held payment-set Power (feeds the 집행 token); the token stays plain
    };

    // Slate-lavender h/s/v for the banner / portrait-border / type-plaque (kept from the earlier look so they
    // sit under the gold frame without clashing). TargetH stays a field so solo-verify's hue-sweep can drive it.
    internal static float TargetH = 0.72f;
    private const float TargetS = 0.25f;
    private const float TargetV = 0.80f;

    /// <summary>Drop the cached tint material + custom frames so the NEXT rebuild rebuilds them (solo-verify).</summary>
    internal static void ResetCacheForSweep() { _bannerMat = null; _frames.Clear(); }

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo? FrameF  = typeof(NCard).GetField("_frame", F);
    private static readonly FieldInfo? BannerF = typeof(NCard).GetField("_banner", F);
    private static readonly FieldInfo? BorderF = typeof(NCard).GetField("_portraitBorder", F);
    private static readonly FieldInfo? PlaqueF = typeof(NCard).GetField("_typePlaque", F);

    private static ShaderMaterial? _bannerMat;
    private static Texture2D? _bannerTex; private static bool _bannerTried;
    // cache: card-type key ("attack"/"skill"/"power") -> custom frame texture (null = load failed, don't retry).
    private static readonly Dictionary<string, Texture2D?> _frames = new();

    [HarmonyPostfix]
    private static void Postfix(NCard __instance)
    {
        try
        {
            if (__instance.Model == null || !ReColor.Contains(__instance.Model.GetType())) return;

            // 1) swap the frame for our purple+gold texture, and clear the desaturate material so it shows true.
            var frameTex = FrameFor(__instance.Model);
            if (frameTex != null && FrameF?.GetValue(__instance) is TextureRect frame)
            {
                frame.Texture = frameTex;
                frame.Material = null;
            }

            // 2) swap the banner (nameplate) for our purple-marble + gold-filigree texture, colours as-is.
            var bannerTex = BannerTex();
            if (bannerTex != null && BannerF?.GetValue(__instance) is TextureRect banner)
            {
                banner.Texture = bannerTex;
                banner.Material = null;
            }

            // 3) portrait-border + type-plaque → GOLD via texture swap. The vanilla textures are green-based,
            //    so a Modulate tint only muddies them; swapping to gold-recoloured textures is the reliable fix.
            var pbTex = PBorderFor(__instance.Model);
            if (pbTex != null && BorderF?.GetValue(__instance) is TextureRect pborder)
            { pborder.Texture = pbTex; pborder.Material = null; pborder.SelfModulate = Colors.White; }

            var plTex = PlaqueTex();
            if (plTex != null && PlaqueF?.GetValue(__instance) is NinePatchRect plaque)
            { plaque.Texture = plTex; plaque.Material = null; plaque.SelfModulate = Colors.White; }
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] frame recolor skipped: {e.Message}"); }
    }

    /// <summary>The custom frame texture for a card's type (attack/skill/power). None/Status/Curse fall back to
    /// skill — matching the vanilla FramePath mapping — but our ReColor set never contains those.</summary>
    private static Texture2D? FrameFor(CardModel model)
    {
        var key = model.Type.ToString().ToLowerInvariant();
        if (key != "attack" && key != "skill" && key != "power") key = "skill";
        if (!_frames.TryGetValue(key, out var tex))
        {
            tex = ResourceLoader.Load<Texture2D>($"res://{MainFile.ModId}/frames/frame_{key}.png");
            if (tex == null) MainFile.Logger.Warn($"[{MainFile.ModId}] frame texture missing for '{key}'");
            _frames[key] = tex;
        }
        return tex;
    }

    /// <summary>The custom purple-marble + gold-filigree banner texture (loaded once). Padded to the vanilla
    /// atlas logical size (656×193 with the ribbon at margin offset) so it aligns in the banner TextureRect.</summary>
    private static Texture2D? BannerTex()
    {
        if (!_bannerTried)
        {
            _bannerTried = true;
            _bannerTex = ResourceLoader.Load<Texture2D>($"res://{MainFile.ModId}/frames/banner.png");
            if (_bannerTex == null) MainFile.Logger.Warn($"[{MainFile.ModId}] banner texture missing");
        }
        return _bannerTex;
    }

    private static void EnsureBannerMat()
    {
        if (_bannerMat != null) return;
        var dl = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(typeof(DunningLetterCard)));
        _bannerMat = Retint(dl?.BannerMaterial as ShaderMaterial);
    }

    private static void SetMat(FieldInfo? field, NCard card, ShaderMaterial? mat)
    {
        if (mat != null && field?.GetValue(card) is CanvasItem ci) ci.Material = mat;
    }

    private static readonly Dictionary<string, Texture2D?> _pborders = new();
    private static Texture2D? _plaqueTex; private static bool _plaqueTried;

    /// <summary>Gold-recoloured portrait border (the ring around the art) per card type.</summary>
    private static Texture2D? PBorderFor(CardModel model)
    {
        var key = model.Type.ToString().ToLowerInvariant();
        if (key != "attack" && key != "skill" && key != "power") key = "skill";
        if (!_pborders.TryGetValue(key, out var tex))
        {
            tex = ResourceLoader.Load<Texture2D>($"res://{MainFile.ModId}/frames/pborder_{key}.png");
            _pborders[key] = tex;
        }
        return tex;
    }

    /// <summary>Gold-recoloured type-plaque texture (the "파워"/"공격" tab background). Loaded once.</summary>
    private static Texture2D? PlaqueTex()
    {
        if (!_plaqueTried)
        {
            _plaqueTried = true;
            _plaqueTex = ResourceLoader.Load<Texture2D>($"res://{MainFile.ModId}/frames/plaque.png");
        }
        return _plaqueTex;
    }

    /// <summary>Duplicate a banner ShaderMaterial and push our slate-lavender h/s/v onto it.</summary>
    private static ShaderMaterial? Retint(ShaderMaterial? src)
    {
        if (src?.Shader == null) return null;
        var dup = (ShaderMaterial)src.Duplicate(true);
        dup.SetShaderParameter("h", TargetH);
        dup.SetShaderParameter("s", TargetS);
        dup.SetShaderParameter("v", TargetV);
        return dup;
    }
}
