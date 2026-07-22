using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;                 // ModelVisibility
using MegaCrit.Sts2.Core.Models;                      // CardModel
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;   // NCardLibraryGrid

namespace Sts2DebtLoan;

/// <summary>
/// Makes THIS mod's custom cards fully VISIBLE in the main-menu Card Library (도감) instead of locked
/// silhouettes, so their art + text can be browsed all in one place.
///
/// Why they were hidden: NCardLibraryGrid adds every card whose <c>ShouldShowInCardLibrary</c> is true
/// (ours default to true) to the grid, but colours each by <c>GetCardVisibility</c> — which returns
/// <see cref="ModelVisibility.Locked"/> (a blank silhouette) for any card that isn't in a vanilla pool's
/// UNLOCKED-card set. Our cards only borrow a pool via their <c>Pool</c> getter; they aren't real members
/// of that pool's card list, so they fall out of the unlocked set and render locked. This postfix promotes
/// ONLY our own cards to Visible so the real art shows.
///
/// The Card Library is a MAIN-MENU compendium screen with no run/combat state → display-only, co-op safe.
/// </summary>
[HarmonyPatch(typeof(NCardLibraryGrid), "GetCardVisibility")]
internal static class CardLibraryUnlockPatch
{
    // Every card model this mod defines lives in this assembly; the vanilla cards do not. Matching on the
    // declaring assembly covers the whole DebtLoan set (debt curses + payment cards) without an explicit list.
    private static readonly Assembly Own = typeof(CardLibraryUnlockPatch).Assembly;

    private static void Postfix(CardModel card, ref ModelVisibility __result)
    {
        try
        {
            if (card != null && card.GetType().Assembly == Own)
                __result = ModelVisibility.Visible;
        }
        catch { /* a compendium glitch must never take down the menu screen */ }
    }
}
