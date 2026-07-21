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
    private const string KeyMinLoan = "minLoan";
    private const string KeyInterest = "interestPerDraw";
    private const string KeyRepayPct = "principalRepayPct";
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

            b.Slider(KeyMinLoan, "Min loan (gold)", defaultValue: 100.0,
                    onChanged: v => DebtLoanConfig.MinLoan = (int)v)
                .Range(0f, 300f, 25f, format: "F0")
                .Description("A loan always advances at least this much (capped by the remaining room), so being just short doesn't create a trivial tiny debt — you borrow a meaningful amount and keep the change.");
            Loc(b, "최소 대출액 (골드)", "대출은 항상 최소 이 금액만큼 나갑니다(잔여 한도 내). 조금만 모자라도 1골드짜리 하찮은 빚이 생기지 않고, 의미 있는 금액을 빌리고 잔돈은 가집니다.");

            b.Slider(KeyInterest, "Interest per Debt card (gold)", defaultValue: 10.0,
                    onChanged: v => DebtLoanConfig.InterestPerDraw = (int)v)
                .Range(0f, 50f, 5f, format: "F0")
                .Description("Gold each Debt card drains when it triggers. Higher = interest piles up faster. 10 matches the vanilla Debt curse.");
            Loc(b, "빚 카드당 이자 (골드)", "빚 카드가 발동할 때마다 빠지는 골드. 클수록 이자가 빨리 쌓입니다. 10 = 바닐라 Debt 저주와 동일.");

            b.Slider(KeyRepayPct, "Payment toward principal (%)", defaultValue: 20.0,
                    onChanged: v => DebtLoanConfig.PrincipalRepayShare = v / 100.0)
                .Range(0f, 100f, 10f, format: "F0")
                .Description("Share of each Debt-card payment that pays DOWN the loan (the rest is interest). Higher = the debt amortizes faster and the shop repay cost shrinks sooner.");
            Loc(b, "원금 상환 비율 (%)", "빚 카드가 걷어가는 골드 중 원금 상환에 쓰이는 비율 (나머지는 이자). 높을수록 빚이 빨리 줄고 상점 상환액도 빨리 낮아집니다.");

            b.Toggle(KeyOtherActs, "Allow loans outside Act 1", defaultValue: false,
                    onChanged: v => DebtLoanConfig.AllowLoansOutsideAct1 = v)
                .Description("By default the merchant only extends credit in Act 1. Turn ON to allow borrowing at any act's shop.");
            Loc(b, "1막 밖에서도 대출 허용", "기본값은 1막 상점에서만 대출됩니다. 켜면 어느 막의 상점에서도 빌릴 수 있습니다.");

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 300.0);
            DebtLoanConfig.MinLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMinLoan, 100.0);
            DebtLoanConfig.InterestPerDraw = (int)ModConfigBridge.GetValue<double>(ModId, KeyInterest, 10.0);
            DebtLoanConfig.PrincipalRepayShare = ModConfigBridge.GetValue<double>(ModId, KeyRepayPct, 20.0) / 100.0;
            DebtLoanConfig.AllowLoansOutsideAct1 = ModConfigBridge.GetValue<bool>(ModId, KeyOtherActs, false);

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, minLoan {DebtLoanConfig.MinLoan}g, interest {DebtLoanConfig.InterestPerDraw}g/draw, principal-repay {DebtLoanConfig.PrincipalRepayShare:P0}, act1-only {!DebtLoanConfig.AllowLoansOutsideAct1}.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
