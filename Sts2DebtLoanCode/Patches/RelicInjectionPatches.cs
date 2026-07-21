using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace Sts2DebtLoan;

/// <summary>Reflection registry over every custom relic this mod defines (currently just the
/// Merchant's Ledger). Mirrors Sts2RelicPoc — never <c>new</c>, resolve the registered instance.</summary>
internal static class CustomRelicRegistry
{
    internal static IEnumerable<Type> Types =>
        typeof(CustomRelicRegistry).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(RelicModel)));

    internal static IEnumerable<RelicModel> Instances()
    {
        foreach (var t in Types)
        {
            RelicModel? r = null;
            try { r = ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(t)); }
            catch { /* models not built yet — skip this pass */ }
            if (r != null) yield return r;
        }
    }
}

/// <summary>
/// Appends the custom relic to <see cref="SharedRelicPool"/> so it has a valid ModelId + Pool
/// (the relic UI relies on Pool) and so the <c>relic &lt;slug&gt;</c> console command can grant it
/// for testing.
///
/// TODO (grant-only): the Merchant's Ledger should be obtainable ONLY by taking a loan, never as a
/// random relic reward. Registering it here makes it theoretically droppable. Before release, either
/// exclude it from reward generation or give it a non-droppable rarity — see DESIGN.md.
/// </summary>
[HarmonyPatch(typeof(SharedRelicPool), "GenerateAllRelics")]
internal static class SharedRelicPoolPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<RelicModel> __result)
    {
        var extras = CustomRelicRegistry.Instances().ToList();
        if (extras.Count > 0)
            __result = __result.Concat(extras);
    }
}
