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
    private const string KeyInterest = "interestPerDraw";
    private const string KeyCapPct = "interestCapPct";
    private const string KeyOtherActs = "allowOtherActs";

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
            var b = ModConfigBridge.For(ModId, "Merchant Loans", Logger);

            b.Slider(KeyMaxLoan, "Max loan (gold)", defaultValue: 300.0,
                    onChanged: v => DebtLoanConfig.MaxLoan = (int)v)
                .Range(50f, 600f, 50f, format: "F0")
                .Description("The most total gold you can borrow in a run (across the first loan and any top-ups).");
            Loc(b, "최대 대출액 (골드)", "한 런에서 빌릴 수 있는 총 골드 상한 (최초 대출 + 추가 대출 합계).");

            b.Slider(KeyInterest, "Interest per Debt card (gold)", defaultValue: 10.0,
                    onChanged: v => DebtLoanConfig.InterestPerDraw = (int)v)
                .Range(0f, 50f, 5f, format: "F0")
                .Description("Gold each Debt card drains when it triggers. Higher = interest piles up faster. 10 matches the vanilla Debt curse.");
            Loc(b, "빚 카드당 이자 (골드)", "빚 카드가 발동할 때마다 빠지는 골드. 클수록 이자가 빨리 쌓입니다. 10 = 바닐라 Debt 저주와 동일.");

            b.Slider(KeyCapPct, "Interest ceiling (% of loan)", defaultValue: 200.0,
                    onChanged: v => DebtLoanConfig.InterestCapMultiplier = v / 100.0)
                .Range(100f, 400f, 50f, format: "F0")
                .Description("When total interest paid reaches this share of the principal, the loan is retired: the relic goes inert and all Debt cards are removed.");
            Loc(b, "이자 상한 (원금 대비 %)", "누적 이자가 원금의 이 비율에 도달하면 대출이 종료됩니다: 유물이 비활성화되고 모든 빚 카드가 제거됩니다.");

            b.Toggle(KeyOtherActs, "Allow loans outside Act 1", defaultValue: false,
                    onChanged: v => DebtLoanConfig.AllowLoansOutsideAct1 = v)
                .Description("By default the merchant only extends credit in Act 1. Turn ON to allow borrowing at any act's shop.");
            Loc(b, "1막 밖에서도 대출 허용", "기본값은 1막 상점에서만 대출됩니다. 켜면 어느 막의 상점에서도 빌릴 수 있습니다.");

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 300.0);
            DebtLoanConfig.InterestPerDraw = (int)ModConfigBridge.GetValue<double>(ModId, KeyInterest, 10.0);
            DebtLoanConfig.InterestCapMultiplier = ModConfigBridge.GetValue<double>(ModId, KeyCapPct, 200.0) / 100.0;
            DebtLoanConfig.AllowLoansOutsideAct1 = ModConfigBridge.GetValue<bool>(ModId, KeyOtherActs, false);

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, interest {DebtLoanConfig.InterestPerDraw}g/draw, cap {DebtLoanConfig.InterestCapMultiplier:P0}, act1-only {!DebtLoanConfig.AllowLoansOutsideAct1}.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
