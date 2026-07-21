using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;   // NGame
using MegaCrit.Sts2.Core.Runs;    // RunState

namespace Sts2DebtLoan;

/// <summary>
/// On loading a saved run, rebuild the transient LoanRecord for each player from the persisted
/// [SavedProperty] fields on their Merchant's Ledger relic (and re-find the Debt cards in the deck).
/// Same load choke point Sts2RelicForge uses (RunLoadReforgePatch). No-op for players without a loan.
/// </summary>
[HarmonyPatch(typeof(NGame), "LoadRun")]
internal static class RunLoadPatch
{
    private static void Prefix(RunState runState)
    {
        try
        {
            if (runState?.Players == null) return;
            foreach (var p in new List<Player>(runState.Players))
                LoanService.RestoreFromRelic(p);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] loan restore-on-load failed: {e.Message}");
        }
    }
}
