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
            var dl = DebtLoanLoc.DunningLetterFor(manager.Language);

            var relics = new Dictionary<string, string>
            {
                ["DEBT_LOAN_RELIC.title"] = s.RelicTitle,
                ["DEBT_LOAN_RELIC.description"] = s.RelicDesc,
                ["DEBT_LOAN_RELIC.flavor"] = s.RelicFlavor,
            };
            // Merchant loan/repay barks (indexed variants, one picked at random by MerchantBark).
            foreach (var kv in MerchantBark.LocKeys(MerchantBark.For(manager.Language))) relics[kv.Key] = kv.Value;
            // Per-card INDIRECT next-card hints (DEBT_HINT_<CARDKEY>) the merchant alludes to on each grant.
            foreach (var kv in MerchantBark.HintLocKeys(manager.Language)) relics[kv.Key] = kv.Value;
            // 납부 (Payment) hover tip shown on the 빚 독촉 card (custom HoverTip references these keys).
            var pay = DebtLoanLoc.PaymentFor(manager.Language);
            relics["DEBT_PAYMENT.title"] = pay.Title;
            relics["DEBT_PAYMENT.description"] = pay.Desc;
            manager.GetTable("relics").MergeWith(relics);

            // The card FACE renders from ".description" (+ ".smartDescription") with SmartFormat + BBCode.
            // Class name → Id.Entry: DebtCurseCard=DEBT_CURSE_CARD (빚 독촉), DelinquencyCard=DELINQUENCY_CARD
            // (연체), SeizureCard=SEIZURE_CARD (차압).
            manager.GetTable("cards").MergeWith(new Dictionary<string, string>
            {
                ["DEBT_CURSE_CARD.title"] = s.DunTitle,
                ["DEBT_CURSE_CARD.description"] = s.DunDesc,
                ["DEBT_CURSE_CARD.smartDescription"] = s.DunDesc,
                ["DELINQUENCY_CARD.title"] = s.DelTitle,
                ["DELINQUENCY_CARD.description"] = s.DelDesc,
                ["DELINQUENCY_CARD.smartDescription"] = s.DelDesc,
                ["SEIZURE_CARD.title"] = s.SeiTitle,
                ["SEIZURE_CARD.description"] = s.SeiDesc,
                ["SEIZURE_CARD.smartDescription"] = s.SeiDesc,
                ["BAD_CREDIT_CARD.title"] = s.BcTitle,
                ["BAD_CREDIT_CARD.description"] = s.BcDesc,
                ["BAD_CREDIT_CARD.smartDescription"] = s.BcDesc,
                ["FORCED_COLLECTION_CARD.title"] = s.FcTitle,
                ["FORCED_COLLECTION_CARD.description"] = s.FcDesc,
                ["FORCED_COLLECTION_CARD.smartDescription"] = s.FcDesc,
                // 독촉장 (Dunning Letter) leverage card — DunningLetterCard → DUNNING_LETTER_CARD.
                ["DUNNING_LETTER_CARD.title"] = dl.Title,
                ["DUNNING_LETTER_CARD.description"] = dl.CardDesc,
                ["DUNNING_LETTER_CARD.smartDescription"] = dl.CardDesc,
            });

            // The persistent power's tooltip lives in the "powers" table. DunningLetterPower → DUNNING_LETTER_POWER.
            var powers = new Dictionary<string, string>
            {
                ["DUNNING_LETTER_POWER.title"] = dl.Title,
                ["DUNNING_LETTER_POWER.description"] = dl.PowerDesc,
            };
            foreach (var kv in DebtLoanLoc.ExtraPowerLoc(manager.Language)) powers[kv.Key] = kv.Value;   // 취업알선/납부 혜택/환급
            manager.GetTable("powers").MergeWith(powers);

            // The new payment-set cards (품삯 / 취업알선 / 납부 혜택 / 환급 / 성실 납부 / 정산 / 청구서 / 혈납).
            var extraCards = new Dictionary<string, string>();
            foreach (var kv in DebtLoanLoc.ExtraCardLoc(manager.Language))
            {
                extraCards[kv.Key] = kv.Value;
                if (kv.Key.EndsWith(".description"))                       // the card face path also reads smartDescription
                    extraCards[kv.Key.Replace(".description", ".smartDescription")] = kv.Value;
            }
            manager.GetTable("cards").MergeWith(extraCards);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] loc injection skipped: {ex.Message}");
        }
    }
}
