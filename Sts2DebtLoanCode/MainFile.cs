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
    private const string KeyMaxAct = "maxLoanAct";

    private static readonly string[] ActOptions = { "Act 1", "Act 2", "Act 3" };
    private static int ActIndexOf(string s) => s switch { "Act 2" => 1, "Act 3" => 2, _ => 0 };

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

            b.Dropdown(KeyMaxAct, "Allow loans through act", "Act 1", ActOptions,
                    onChanged: v => DebtLoanConfig.MaxLoanActIndex = ActIndexOf(v))
                .Description("The furthest act where the merchant will extend credit. 'Act 2' allows loans in Acts 1–2, 'Act 3' in Acts 1–3.");
            Loc(b, "대출 허용 막", "상인이 대출해주는 최대 막. 'Act 2' = 1~2막, 'Act 3' = 1~3막에서 대출 가능. 기본값 'Act 1' = 1막에서만.");

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 300.0);
            DebtLoanConfig.MaxLoanActIndex = ActIndexOf(ModConfigBridge.GetValue<string>(ModId, KeyMaxAct, "Act 1"));

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, loans through act {DebtLoanConfig.MaxLoanActIndex + 1}.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
