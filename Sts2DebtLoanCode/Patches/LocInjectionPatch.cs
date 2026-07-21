using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2DebtLoan;

/// <summary>
/// Injects the Ledger relic + Debt card strings into the live "relics" and "cards" LocTables at runtime,
/// in the CURRENT language (all 13 shipped languages + English — see <see cref="DebtLoanLoc"/>). Loose mod
/// files are never mounted into res://, so we merge straight into the already-loaded tables. Re-runs on
/// every language (re)load, so switching language in-game updates the strings too.
///
/// The relic description is a static per-language TEMPLATE with {borrowed}/{paid} placeholders; the numbers
/// are filled per-relic from each relic's own DynamicVars (DebtLoanRelic), so it is co-op-safe (no global
/// per-player overwrite).
/// </summary>
[HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
internal static class LocInjectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(LocManager __instance) => Inject(__instance);

    internal static void Inject(LocManager? manager)
    {
        try
        {
            manager ??= LocManager.Instance;
            if (manager == null) return;

            var s = DebtLoanLoc.For(manager.Language);

            manager.GetTable("relics").MergeWith(new Dictionary<string, string>
            {
                ["DEBT_LOAN_RELIC.title"] = s.RelicTitle,
                ["DEBT_LOAN_RELIC.description"] = s.RelicDesc,
                ["DEBT_LOAN_RELIC.flavor"] = s.RelicFlavor,
            });

            // The card FACE renders from ".description" (+ ".smartDescription" if present) with SmartFormat +
            // BBCode markup; a plain default leaves the RichTextLabel on "If you can read this, there is a bug."
            manager.GetTable("cards").MergeWith(new Dictionary<string, string>
            {
                ["DEBT_CURSE_CARD.title"] = s.CardTitle,
                ["DEBT_CURSE_CARD.description"] = s.CardDesc,
                ["DEBT_CURSE_CARD.smartDescription"] = s.CardDesc,
            });
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] loc injection skipped: {ex.Message}");
        }
    }
}
