using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;                      // CardModel, ModelDb
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;   // NCardLibraryGrid

namespace Sts2DebtLoan;

/// <summary>
/// Injects this mod's cards into the Card Library grid so they can be browsed in the 도감 at all.
///
/// The grid seeds itself from <c>ModelDb.AllCards</c> (NCardLibraryGrid._Ready), and that set is built from
/// pool MEMBERSHIP — <c>AllCardPools.SelectMany(p =&gt; p.AllCards)</c> plus character starting decks. Our
/// cards are registered models but they only BORROW a pool through their <c>Pool</c> getter; they are not
/// members of any pool's <c>AllCards</c>, so they never enter <c>ModelDb.AllCards</c> and therefore never
/// enter the grid — invisible under every filter (this is why the earlier visibility/filter patches, both
/// downstream of the grid's card list, had nothing to act on).
///
/// This postfix appends every card model declared in this assembly to the grid's <c>_allCards</c> after the
/// stock seed. Paired with <see cref="CardLibraryModFilterPatch"/> (groups them under the Misc filter) and
/// <see cref="CardLibraryUnlockPatch"/> (promotes them from locked silhouettes to Visible), their art + text
/// then render. Main-menu compendium, display-only → co-op safe.
/// </summary>
[HarmonyPatch(typeof(NCardLibraryGrid), "_Ready")]
internal static class CardLibraryGridPatch
{
    private static readonly Assembly Own = typeof(CardLibraryGridPatch).Assembly;

    private static void Postfix(NCardLibraryGrid __instance)
    {
        try
        {
            var all = __instance._allCards;   // publicized: List<CardModel>
            if (all == null) return;
            int added = 0;
            foreach (var t in Own.GetTypes())
            {
                if (t.IsAbstract || !typeof(CardModel).IsAssignableFrom(t)) continue;
                CardModel? m = null;
                try { m = ModelDb.GetByIdOrNull<CardModel>(ModelDb.GetId(t)); }
                catch { /* unregistered / non-model type — skip */ }
                if (m != null && m.ShouldShowInCardLibrary && !all.Contains(m)) { all.Add(m); added++; }
            }
            if (added > 0) MainFile.Logger.Info($"[{MainFile.ModId}] added {added} card(s) to the Card Library.");
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] card-library grid inject failed: {e.Message}"); }
    }
}
