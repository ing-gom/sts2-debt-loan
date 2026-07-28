using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using Sts2.ModKit.Bootstrap;
using Sts2.ModKit.Config;

namespace Sts2DebtLoan;

/// <summary>
/// Entry point. ModBootstrap.Run patches each Harmony class independently and runs body() where we
/// register the ModConfig knobs (deferred one frame so ModConfig finishes its own Initialize first,
/// then Register BEFORE any GetValue — see [[feedback_modconfig_read_after_register]]).
/// </summary>
[ModInitializer(nameof(Initialize))]
public class MainFile
{
    public const string ModId = "Sts2DebtLoan";

    private const string KeyMaxLoan = "maxLoan";
    private const string KeyShopCredit = "shopCreditLimit";
    private const string KeyLoanDraws = "maxLoanDraws";
    private const string KeyGarnish = "garnishMaxPct";
    private const string KeyInterestCap = "interestGoldCap";


    public static readonly MegaCrit.Sts2.Core.Logging.Logger Logger = ModBootstrap.CreateLogger(ModId);

    public static void Initialize() =>
        ModBootstrap.Run(ModId, Logger, typeof(MainFile).Assembly, body: () =>
        {
            Logger.Info($"[{ModId}] merchant-loan prototype active.");
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            tree.CreateTimer(0.0).Timeout += RegisterConfig;
#if DEBTLOAN_SELFTEST
            SoloTest.ArmIfRequested();   // dormant unless selftest.sp.flag is present (solo-verify)
            CoopTest.ArmIfRequested();   // dormant unless selftest.coop.flag is present (coop-verify)
#endif
        });

    /// <summary>Attach a Korean label/description to the last-added entry via REFLECTION so an older
    /// bundled ModKit degrades to English instead of throwing (the first-wins skew lesson).</summary>
    private static void Loc(ConfigEntryBuilder b, string koLabel, string koDesc)
    {
        try
        {
            var t = b.GetType();
            t.GetMethod("LocalizedLabels")?.Invoke(b, new object[] { new Dictionary<string, string> { ["kor"] = koLabel } });
            t.GetMethod("LocalizedDescriptions")?.Invoke(b, new object[] { new Dictionary<string, string> { ["kor"] = koDesc } });
        }
        catch (Exception e) { Logger.Info($"[{ModId}] config localization skipped (old ModKit loaded): {e.Message}"); }
    }

    private static void RegisterConfig()
    {
        try
        {
            var b = ModConfigBridge.For(ModId, "The Red Ledger", Logger);

            b.Slider(KeyMaxLoan, "Max loan (gold)", defaultValue: 0.0,
                    onChanged: v => DebtLoanConfig.MaxLoan = (int)v)
                .Range(0f, 600f, 50f, format: "F0")
                .Description("Gold ceiling on total borrowing across a run. 0 (default) means NO gold cap — the number of loan draws is the only limit, so three draws can finance three relics.");
            Loc(b, "최대 대출액 (골드)", "한 런에서 빌릴 수 있는 총 골드 상한. 0(기본)이면 금액 제한 없음 — 인출 횟수만이 유일한 제약이라, 3번이면 유물 3개도 이론상 가능합니다.");

            b.Slider(KeyLoanDraws, "Loan draws per loan", defaultValue: 3.0,
                    onChanged: v => DebtLoanConfig.MaxLoanDraws = (int)v)
                .Range(0f, 10f, 1f, format: "F0")
                .Description("How many separate times you may draw gold on ONE loan (the first borrow plus each top-up). With the default 3, a run's borrowing has to be split across three decisions instead of nibbled away. 0 = unlimited. Repaying the loan in full restores a fresh set. Debt-shop card purchases don't count — they have their own per-visit credit line.");
            Loc(b, "대출당 인출 횟수", "한 대출에서 골드를 나눠 받을 수 있는 횟수 (최초 대출 + 추가 인출 각각 1회). 기본 3이면 빌릴 기회를 세 번의 결정으로 나눠 써야 합니다. 0 = 무제한. 빚을 완납하면 다시 3회로 회복됩니다. 빚 상점 카드 구매는 별도 한도라 여기 포함되지 않습니다.");

            b.Slider(KeyShopCredit, "Debt-shop credit per visit (gold)", defaultValue: 120.0,
                    onChanged: v => DebtLoanConfig.ShopCreditLimit = (int)v)
                .Range(50f, 400f, 25f, format: "F0")
                .Description("The most debt you may take on CARD purchases at the debt shop per shop visit (cards cost 60–95; the leftmost offer is FREE). At the default 120 a visit buys one paid card — or two if you take the discounted one. Resets each new shop. Separate from the loan cap above.");
            Loc(b, "상점당 외상 한도 (골드)", "빚 상점에서 카드 구매로 한 상점 방문당 질 수 있는 빚 상한 (카드 60~95골드, 맨 왼쪽 1장은 무료). 기본 120이면 한 방문에 유료 1장 — 할인 카드를 집으면 2장까지. 새 상점마다 초기화. 위 대출 한도와는 별개.");

            b.Slider(KeyGarnish, "Garnishment at max interest (%)", defaultValue: 40.0,
                    onChanged: v => DebtLoanConfig.GarnishMaxPct = (int)v)
                .Range(0f, 60f, 5f, format: "F0")
                .Description("Once your loan's interest hits its MAXIMUM, the creditor withholds this % of your gold income and applies it to your debt. No garnishment before max interest. 0 disables it.");
            Loc(b, "이자 최대 시 원천징수 (%)", "대출 이자가 최대에 도달하면 채권자가 획득 골드에서 이 비율만큼 떼어 빚 상환에 충당합니다. 이자 최대 전에는 원천징수가 없습니다. 0이면 끔.");

            b.Slider(KeyInterestCap, "Max total interest (gold)", defaultValue: 100.0,
                    onChanged: v => DebtLoanConfig.InterestGoldCap = (int)v)
                .Range(50f, 600f, 25f, format: "F0")
                .Description("Absolute ceiling on the total interest a loan can accrue (origination + node, across the loan and card debt). Interest never grows past this.");
            Loc(b, "최대 총 이자 (골드)", "한 대출이 쌓을 수 있는 총 이자(origination + node, 대출+카드 빚 전체)의 절대 상한. 이자는 이 값을 넘지 않습니다.");

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 0.0);
            DebtLoanConfig.ShopCreditLimit = (int)ModConfigBridge.GetValue<double>(ModId, KeyShopCredit, 120.0);
            DebtLoanConfig.MaxLoanDraws = (int)ModConfigBridge.GetValue<double>(ModId, KeyLoanDraws, 3.0);
            DebtLoanConfig.GarnishMaxPct = (int)ModConfigBridge.GetValue<double>(ModId, KeyGarnish, 40.0);
            DebtLoanConfig.InterestGoldCap = (int)ModConfigBridge.GetValue<double>(ModId, KeyInterestCap, 100.0);

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, draws/loan {DebtLoanConfig.MaxLoanDraws}, shop credit {DebtLoanConfig.ShopCreditLimit}g/visit, garnish cap {DebtLoanConfig.GarnishMaxPct}%.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
