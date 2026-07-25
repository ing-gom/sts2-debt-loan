using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;                 // LocalContext
using MegaCrit.Sts2.Core.Entities.Players;        // Player
using MegaCrit.Sts2.Core.Models;                  // CardModel, CardPoolModel
using MegaCrit.Sts2.Core.Nodes.Cards;             // NCard
using MegaCrit.Sts2.Core.Runs;                    // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// Our cards live in the ColorlessCardPool (so they never hit the MockCardPool "You monster!" getter), which
/// renders the plain GREY energy orb. This postfix on <see cref="NCard"/>.Reload repaints the energy pip with the
/// owning CHARACTER's energy icon instead — so a debt card in an Ironclad run shows Ironclad's (red) energy, a
/// custom-character run shows that character's energy background, etc.
/// <para>Two card states reach Reload:</para>
/// <list type="bullet">
/// <item>MUTABLE (in-combat / owned): read <c>Owner.Character.CardPool</c>.</item>
/// <item>CANONICAL (deck view, card reward, shop preview): these have NO owner and <c>Owner</c> even THROWS
/// (CanonicalModelException) — the old code hit that every preview and fell to grey. We fall back to the current
/// run's LOCAL player character pool so previews inherit the character energy too.</item>
/// </list>
/// Display-only (no run/combat mutation) → co-op safe. Runs alongside the other NCard.Reload postfixes.
/// </summary>
[HarmonyPatch(typeof(NCard), "Reload")]
internal static class EnergyIconPatch
{
    private static readonly Assembly Own = typeof(EnergyIconPatch).Assembly;
    private static readonly FieldInfo? EnergyIconF = typeof(NCard).GetField("_energyIcon",
        BindingFlags.NonPublic | BindingFlags.Instance);

    [HarmonyPostfix]
    private static void Postfix(NCard __instance)
    {
        try
        {
            var model = __instance.Model;
            if (model == null || model.GetType().Assembly != Own) return;   // only OUR cards

            var pool = PoolFor(model);
            if (pool == null) return;                                        // no character to inherit from → keep grey

            string path = pool.EnergyIconPath;                               // energy_<color>.tres for that character
            var tex = ResourceLoader.Load<Texture2D>(path, null, ResourceLoader.CacheMode.Reuse);
            if (tex == null)
            {
                // Custom character whose EnergyColorName maps to a .tres we can't load (not in the game atlas / its
                // own pck). Log the exact path so we can wire that mod's energy resource if this ever fires.
                MainFile.Logger.Warn($"[{MainFile.ModId}] energy orb '{path}' (color '{pool.EnergyColorName}') " +
                                     $"did not load — {model.GetType().Name} pip stays grey");
                return;
            }

            Apply(__instance, tex);
            // Re-apply AFTER every other NCard.Reload postfix. Custom-character frameworks (RitsuLib etc.) also hook
            // Reload and can repaint the pip from the card's OWN VisualCardPool (= Colorless → grey) after us; postfix
            // order is undefined. A deferred write runs once the frame's synchronous postfixes are done → we get the
            // LAST word and the character-coloured orb sticks.
            var captured = tex;
            Callable.From(() =>
            {
                try { if (GodotObject.IsInstanceValid(__instance)) Apply(__instance, captured); } catch { }
            }).CallDeferred();
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] energy-icon recolor skipped: {e.Message}"); }
    }

    /// <summary>The character card pool whose energy orb this card should inherit — the owner's for an owned
    /// (mutable) card, else the current run's local player for a canonical preview.</summary>
    private static CardPoolModel? PoolFor(CardModel model)
    {
        // Owned in-combat card. Owner throws on a canonical model, so gate on IsMutable first (IsCanonical == !IsMutable).
        if (model.IsMutable)
        {
            try { return model.Owner?.Character?.CardPool; } catch { return null; }
        }

        // Canonical preview (no owner) → inherit from the current run's local player character.
        try
        {
            var players = RunManager.Instance?.State?.Players;
            if (players == null) return null;
            Player? me = null;
            try { me = LocalContext.GetMe(players); } catch { }
            me ??= players.FirstOrDefault();
            return me?.Character?.CardPool;
        }
        catch { return null; }
    }

    private static void Apply(NCard card, Texture2D tex)
    {
        if (EnergyIconF?.GetValue(card) is TextureRect pip && GodotObject.IsInstanceValid(pip))
            pip.Texture = tex;
    }
}
