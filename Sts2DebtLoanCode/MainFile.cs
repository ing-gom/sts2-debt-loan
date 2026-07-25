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
    private const string KeyShopCredit = "shopCreditLimit";
    private const string KeyGarnish = "garnishMaxPct";

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
            var b = ModConfigBridge.For(ModId, "The Red Ledger", Logger);

            b.Slider(KeyMaxLoan, "Max loan (gold)", defaultValue: 300.0,
                    onChanged: v => DebtLoanConfig.MaxLoan = (int)v)
                .Range(50f, 600f, 50f, format: "F0")
                .Description("The most total gold you can borrow in a run (across the first loan and any top-ups).");
            Loc(b, "최대 대출액 (골드)", "한 런에서 빌릴 수 있는 총 골드 상한 (최초 대출 + 추가 대출 합계).");

            b.Dropdown(KeyMaxAct, "Allow loans through act", "Act 1", ActOptions,
                    onChanged: v => DebtLoanConfig.MaxLoanActIndex = ActIndexOf(v))
                .Description("The furthest act where the merchant will extend credit. 'Act 2' allows loans in Acts 1–2, 'Act 3' in Acts 1–3.");
            Loc(b, "대출 허용 막", "상인이 대출해주는 최대 막. 'Act 2' = 1~2막, 'Act 3' = 1~3막에서 대출 가능. 기본값 'Act 1' = 1막에서만.");

            b.Slider(KeyShopCredit, "Debt-shop credit per visit (gold)", defaultValue: 150.0,
                    onChanged: v => DebtLoanConfig.ShopCreditLimit = (int)v)
                .Range(50f, 400f, 25f, format: "F0")
                .Description("The most debt you may take on CARD purchases at the debt shop per shop visit (cards cost 50–80). Resets each new shop. Separate from the loan cap above.");
            Loc(b, "상점당 외상 한도 (골드)", "빚 상점에서 카드 구매로 한 상점 방문당 질 수 있는 빚 상한 (카드 50~80골드). 새 상점마다 초기화. 위 대출 한도와는 별개.");

            b.Slider(KeyGarnish, "Income garnishment cap (%)", defaultValue: 40.0,
                    onChanged: v => DebtLoanConfig.GarnishMaxPct = (int)v)
                .Range(0f, 60f, 5f, format: "F0")
                .Description("The most of your gold INCOME the creditor withholds (applied to your debt) at high interest. The rate scales with accrued interest up to this cap. 0 disables garnishment.");
            Loc(b, "이자 원천징수 상한 (%)", "이자가 높을 때 채권자가 획득 골드에서 떼어(빚 상환에 충당) 가는 최대 비율. 실제 비율은 누적 이자에 비례해 이 상한까지 오릅니다. 0이면 원천징수 없음.");

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 300.0);
            DebtLoanConfig.MaxLoanActIndex = ActIndexOf(ModConfigBridge.GetValue<string>(ModId, KeyMaxAct, "Act 1"));
            DebtLoanConfig.ShopCreditLimit = (int)ModConfigBridge.GetValue<double>(ModId, KeyShopCredit, 150.0);
            DebtLoanConfig.GarnishMaxPct = (int)ModConfigBridge.GetValue<double>(ModId, KeyGarnish, 40.0);

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, loans through act {DebtLoanConfig.MaxLoanActIndex + 1}, shop credit {DebtLoanConfig.ShopCreditLimit}g/visit, garnish cap {DebtLoanConfig.GarnishMaxPct}%.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
