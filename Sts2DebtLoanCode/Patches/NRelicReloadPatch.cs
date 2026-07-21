using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Relics;   // NRelic

namespace Sts2DebtLoan;

/// <summary>
/// Attach/refresh the evolving-ledger overlay whenever a relic widget (re)binds its icon. NRelic.Reload() is
/// the game's own icon-set method (runs on _Ready and on model/size changes) — the one point where the icon
/// texture is (re)loaded — so it's exactly where we ensure our tier overlay rides along. No-op for every
/// relic except the Merchant's Ledger (LedgerOverlay.Ensure gates on the model type). Display-only.
/// </summary>
[HarmonyPatch(typeof(NRelic), "Reload")]
internal static class NRelicReloadPatch
{
    private static void Postfix(NRelic __instance) => LedgerOverlay.Ensure(__instance);
}
