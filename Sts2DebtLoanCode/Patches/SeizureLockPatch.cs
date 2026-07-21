using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;              // UnplayableReason
using MegaCrit.Sts2.Core.Models;                      // CardModel, AbstractModel

namespace Sts2DebtLoan;

/// <summary>
/// Enforces the 차압 (Seizure) type-lock globally: postfix on <see cref="CardModel.CanPlay"/> (the out-param
/// variant the UI uses to grey cards out and the play path uses to validate). If <see cref="SeizureLock"/>
/// says this card is blocked (wrong type while a Seizure card clogs the hand), we flip the result to
/// unplayable with <see cref="UnplayableReason.BlockedByHook"/>. Pure read of lockstep-synced state, so it
/// decides identically on both co-op peers.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.CanPlay),
    new Type[] { typeof(UnplayableReason), typeof(AbstractModel) },
    new ArgumentType[] { ArgumentType.Out, ArgumentType.Out })]
internal static class SeizureLockPatch
{
    private static void Postfix(CardModel __instance, ref bool __result, ref UnplayableReason reason)
    {
        if (!__result) return;                       // already unplayable for some other reason
        if (SeizureLock.IsBlocked(__instance))
        {
            reason |= UnplayableReason.BlockedByHook;
            __result = false;
        }
    }
}
