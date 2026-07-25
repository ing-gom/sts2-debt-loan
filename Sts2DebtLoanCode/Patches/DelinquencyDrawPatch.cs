using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;                    // PowerCmd
using MegaCrit.Sts2.Core.GameActions.Multiplayer;     // BlockingPlayerChoiceContext
using MegaCrit.Sts2.Core.Helpers;                     // TaskHelper
using MegaCrit.Sts2.Core.Models;                      // CardModel
using MegaCrit.Sts2.Core.Models.Powers;               // VulnerablePower

namespace Sts2DebtLoan;

/// <summary>
/// Applies [b]Vulnerable[/b] to the owner whenever a 연체 (Delinquency) card is DRAWN into hand — the card's
/// "on draw" trigger. Implemented as a Harmony postfix on <see cref="CardModel"/>.InvokeDrawn (the method the
/// game calls when a card enters hand) rather than the card's own <c>Drawn</c> event, because combat cards are
/// cloned (ToMutable) and a ctor-subscribed event handler binds to the ORIGINAL instance (Owner=null) — it never
/// fires for the in-combat copy. The postfix keys off <c>__instance</c> = the actual drawn card, so it's
/// clone-safe. Self-apply of a native power off the lockstep draw → deterministic on both co-op peers; the async
/// apply is dispatched via TaskHelper.RunSafely since InvokeDrawn is synchronous. Display/combat only.
/// </summary>
[HarmonyPatch(typeof(CardModel), "InvokeDrawn")]
internal static class DelinquencyDrawPatch
{
    private static void Postfix(CardModel __instance)
    {
        try
        {
            if (__instance is not DelinquencyCard) return;
            var creature = __instance.Owner?.Creature;
            if (creature == null) return;
            TaskHelper.RunSafely(PowerCmd.Apply<VulnerablePower>(
                new BlockingPlayerChoiceContext(), creature, DelinquencyCard.VulnerableOnDraw, creature, null));
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] delinquency draw-vulnerable failed: {e.Message}"); }
    }
}
