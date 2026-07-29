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
    // ⚠️KeyInterestCap("interestGoldCap") 제거 — 슬라이더를 없앴다(RegisterConfig 의 주석 참조).
    // 되살릴 거면 키 문자열을 그대로 쓸 것: 이미 저장된 유저 설정이 이 키로 남아 있다.


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

            // ★step 은 10 이어야 한다 — RitsuLib 은 Godot Mathf.Snapped(value, step) 로 0 기준 격자에 스냅한다.
            // step 25 면 격자가 25의 배수라 기본값 120 이 125 로 스냅돼, 슬라이더를 한 번 건드린 유저는 기본값으로
            // 영영 못 돌아온다(밸런스 기준선인 "합 120" 규칙이 깨진다). 격자에 기본값이 올라와 있는지 항상 확인할 것.
            b.Slider(KeyShopCredit, "Debt-shop credit per visit (gold)", defaultValue: 120.0,
                    onChanged: v => DebtLoanConfig.ShopCreditLimit = (int)v)
                .Range(50f, 400f, 10f, format: "F0")
                .Description("The most debt you may take on CARD purchases at the debt shop per shop visit. Paid cards run 45–95 gold; the leftmost offer is FREE and one other is discounted. At the default 120 the rule is: TWO cards if their listed prices add up to 120 or less, otherwise one premium card. Resets each new shop. Card removal is bought on credit too, but it has its own once-per-visit limit and does not touch this line. Separate from the loan cap above.");
            Loc(b, "상점당 외상 한도 (골드)", "빚 상점에서 카드 구매로 한 상점 방문당 질 수 있는 빚 상한. 유료 카드는 45~95골드이고, 맨 왼쪽 1장은 무료·다른 1장은 할인입니다. 기본 120이면 규칙은 이렇습니다 — 표시된 두 값을 더해 120 이하면 두 장, 아니면 고급 한 장. 새 상점마다 초기화. 카드 제거도 외상으로 사지만 방문당 1회라는 별도 제한이라 이 한도를 쓰지 않습니다. 위 대출 한도와는 별개.");

            b.Slider(KeyGarnish, "Garnishment at max interest (%)", defaultValue: 40.0,
                    onChanged: v => DebtLoanConfig.GarnishMaxPct = (int)v)
                .Range(0f, 60f, 5f, format: "F0")
                .Description("Once your loan's interest hits its MAXIMUM, the creditor withholds this % of your gold income and applies it to your debt. No garnishment before max interest. 0 disables it.");
            Loc(b, "이자 최대 시 원천징수 (%)", "대출 이자가 최대에 도달하면 채권자가 획득 골드에서 이 비율만큼 떼어 빚 상환에 충당합니다. 이자 최대 전에는 원천징수가 없습니다. 0이면 끔.");

            // ⚠️interestGoldCap 슬라이더는 제거했다. 설명이 "총 이자(origination + node)의 절대 상한"이었는데
            // GrantLoan 은 origination 을 캡에 걸지 않는다 — 500골드 넘게 빌리면 origination 만으로 캡을 넘어
            // (825 → 165) 슬라이더가 **자기 설명대로 동작하지 않는 구간**이 있었다. 캡의 실제 의미는
            // '노드 이자 상한'이므로(DebtLoanConfig.InterestGoldCap 참조) 조절값으로 노출하지 않고 상수로 둔다.
            // 되살릴 거면 라벨을 '노드 이자 상한'으로 바꾸고 origination 과의 관계를 설명에 명시할 것.

            b.Register();

            DebtLoanConfig.MaxLoan = (int)ModConfigBridge.GetValue<double>(ModId, KeyMaxLoan, 0.0);
            DebtLoanConfig.ShopCreditLimit = (int)ModConfigBridge.GetValue<double>(ModId, KeyShopCredit, 120.0);
            DebtLoanConfig.MaxLoanDraws = (int)ModConfigBridge.GetValue<double>(ModId, KeyLoanDraws, 3.0);
            DebtLoanConfig.GarnishMaxPct = (int)ModConfigBridge.GetValue<double>(ModId, KeyGarnish, 40.0);

            Logger.Info($"[{ModId}] config: maxLoan {DebtLoanConfig.MaxLoan}g, draws/loan {DebtLoanConfig.MaxLoanDraws}, shop credit {DebtLoanConfig.ShopCreditLimit}g/visit, garnish cap {DebtLoanConfig.GarnishMaxPct}%.");
        }
        catch (Exception e) { Logger.Warn($"[{ModId}] config registration failed: {e.Message}"); }
    }
}
