using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2DebtLoan;

/// <summary>
/// Injects the custom relic + Debt card strings into the live "relics" and "cards" LocTables at
/// runtime. Same workaround as Sts2RelicPoc: loose mod files are never mounted into res://, so we
/// merge straight into the already-loaded tables. Re-runs on every language (re)load. English only
/// for now (Korean is a follow-up; see [[feedback_workshop_changenote_lang]] for the lang policy).
/// </summary>
[HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
internal static class LocInjectionPatch
{
    private static readonly Dictionary<string, string> RelicStrings = new()
    {
        ["DEBT_LOAN_RELIC.title"] = "Merchant's Ledger",
        ["DEBT_LOAN_RELIC.description"] = "You borrowed [gold]Gold[/gold] from the merchant. Leave it unpaid and Debt cards seep into your deck — 1 after 14 rooms, 3 after 17, 5 after 20 — each draining [gold]Gold[/gold] at the end of your turn as interest. Repay the principal at any shop, or once interest reaches 200% of the loan the ledger settles itself. Either way, every Debt card is then removed.",
        ["DEBT_LOAN_RELIC.flavor"] = "Every signature is a small surrender.",
    };

    // The card FACE renders from ".description" (and ".smartDescription" if present) with SmartFormat +
    // BBCode markup — a plain string leaves the RichTextLabel on its scene default ("If you can read this,
    // there is a bug."). Mirror the vanilla Debt curse: [gold] markup + the {Gold:diff()} var (our GoldVar).
    private const string DebtCardText =
        "If this card is in your [gold]Hand[/gold] at the end of your turn, lose {Gold:diff()} [gold]Gold[/gold].";

    private static readonly Dictionary<string, string> CardStrings = new()
    {
        ["DEBT_CURSE_CARD.title"] = "Debt",
        ["DEBT_CURSE_CARD.description"] = DebtCardText,
        ["DEBT_CURSE_CARD.smartDescription"] = DebtCardText,
    };

    [HarmonyPostfix]
    private static void Postfix(LocManager __instance) => Inject(__instance);

    internal static void Inject(LocManager? manager)
    {
        try
        {
            manager ??= LocManager.Instance;
            if (manager == null) return;
            manager.GetTable("relics").MergeWith(RelicStrings);
            manager.GetTable("cards").MergeWith(CardStrings);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] loc injection skipped: {ex.Message}");
        }
    }
}
