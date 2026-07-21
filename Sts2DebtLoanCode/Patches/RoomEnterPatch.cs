using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;              // LocalContext
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;                 // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// Every map room entry advances the loan's room counter and, when a schedule threshold is crossed,
/// seeps Debt cards into the deck (spec: 14th room → 1, 17th → 3, 20th → 5).
///
/// <c>RunManager.EnterRoom(AbstractRoom)</c> is an <c>async Task</c>, so we CHAIN onto its
/// <c>__result</c> and await the escalation in-order — the same co-op-deterministic idiom as
/// Sts2RelicForge's awaited hooks, so host and client inject on the identical lockstep step. We only
/// tick the LOCAL player (each peer owns its own deck); the counter/injection for the remote player
/// runs on that peer.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterRoom))]
internal static class RoomEnterPatch
{
    private static void Postfix(ref Task __result) => __result = After(__result);

    private static async Task After(Task original)
    {
        await original;
        try
        {
            var run = RunManager.Instance;
            if (run?.State == null) return;
            bool sp = run.IsSingleplayerOrFakeMultiplayer;
            foreach (var p in new List<Player>(run.State.Players))
                if (sp || LocalContext.IsMe(p))
                    await LoanService.OnRoomEntered(p);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] room-enter loan tick failed: {e.Message}");
        }
    }
}
