# The Red Ledger — Design (Sts2DebtLoan)

상점에서 부족한 골드를 **대출**받아 아이템을 사고, 그 빚을 **영수증 기반 결제 카드 세트**로 갚아
나가는 자매 모드. 갚지 못하면 저주가 덱에 스며들고, 막판엔 **파산**으로 판을 뒤집을 수도 있다.

> ⚠️ 플레이테스트 단계 — 카드 밸런스·수치는 언제든 바뀔 수 있음. 이 문서는 현재 구조를 기술한다.

---

## 컨셉 한 줄

> 상점에서 **부족분만큼 대출**(총 원금 ≤ 300, 하드캡 400) → **빚 장부**(Debt Ledger) 유물 →
> 방을 지날수록 이자가 붙고 티어에 따라 **저주 카드**가 스며듦 → **정기 납부**가 주는 **납부** 카드로
> 원금을 깎고, 매 납부가 **영수증**을 적립해 결제셋 카드를 굴림 → **원금 완납 시** 장부·빚 카드 전부
> 제거 + 신용 회복(재대출 가능). 막다른 길이면 **파산 선언**으로 빚을 힘으로 환산.

---

## 대출 규칙

- **최초 1회** 대출 시 **빚 장부** 유물 획득 → 이때부터 이자/티어 카운터 시작(`LoanFloor`).
- 대출액 = `구매가 − 보유골드`(부족분). **소프트캡 `MaxLoan`=300**, 구매를 위해 그 위로
  **`OverCapAllowance`=100**까지 허용 → **하드캡 400**. 소프트캡 초과 시 전투당 빚 카드 수가 1 높게 시작.
- **`MaxLoanActIndex`** 막까지만 대출(기본 Act 1). 상환은 어느 막/상점에서든 가능.
- **★인출 횟수 제한(`MaxLoanDraws`=3)**: 한 대출에서 골드를 나눠 받을 수 있는 횟수(최초 대출 + 추가 인출
  각각 1회, `LoanRecord.LoanDraws`, 유물 영속화). 금액 상한만 있던 시절엔 소액 대출을 무한 반복해 한 상점을
  쓸어담을 수 있었다 — 이제 **300골드를 세 번의 결정으로 나눠 써야** 하고 50골드 인출도 슬롯 하나를 먹는다.
  - 카운트는 **적용 경로 `ApplyActiveLoan`에서 증가**(SP 직접 / co-op은 dl_sync로 양 피어가 재생) →
    **와이어 인자를 늘리지 않고** 수렴. 게이트는 `CanLoanCover`(구매자 로컬), 표시는 상점 칩.
  - **완납 시 리셋**(record가 `ResetFor`로 사라짐) — 이게 신용 회복의 "재대출 가능"이 뜻하는 것.
  - **빚 상점 카드 구매는 이 카운터와 무관**(거긴 방문당 외상 한도 `ShopCreditLimit`).
- **★상점 칩(`NLoanDrawsChip`)**: 상인 러그 상단 중앙에 "대출 {남음} / {최대}". 소진 시 빨강 + 대출 가능
  가격표의 초록이 함께 꺼진다(`MerchantPriceColorPatch`가 같은 `CanLoanCover` 게이트를 보므로 자동 일치).
  유물 호버가 아니라 상점에 둔 이유 = **첫 대출 전에는 빚 장부 유물이 없어서** 정작 첫 결정 순간에 아무것도
  못 보여준다. 14언어(`DebtShopUiRow.Draws`).
- **추가 대출**은 유물 보유 + 원금 여유가 있을 때. 상점 아이템 중 대출로 살 수 있는 것은 가격표가
  **초록**(`MerchantPriceColorPatch`).

### 이자

- **대출 즉시 origination fee**(원금에 즉시 가산) + **방 이동마다 node interest**(`NodeInterestPct`,
  상한까지 누적)가 전부 **`Principal`(갚을 금액)에 baked-in**. 상점 상환 비용/유물 배지/호버가 모두 이 값.
- co-op에선 빚진 인원 수에 따라 node-interest 상한이 상승(아래 co-op 참조).

---

## 빚 카드 티어(연체 페널티)

대출 후 방이 지날수록(`RoomsUntilNextTier` / `TargetDebtCards`) 덱에 빚 부담이 늘어난다.

- **네이티브 Debt**: 빚 상점을 이용(방문당 첫 구매)하면 게임 기본 `Debt` 저주 1장이 덱에 추가.
- **티어 저주(커스텀)**: 시간이 지나며 **연체(Delinquency) → 차압(Seizure) → 신용 불량(Bad Credit)**
  +끈질긴 **강제 징수(Forced Collection/DebtorCard)**.
  - **연체(DelinquencyCard)**: Unplayable 저주. **손에 들어올(draw) 때 취약 1 부여**
    (`DelinquencyDrawPatch` = `CardModel.InvokeDrawn` postfix; ctor `Drawn` 이벤트는 clone 비호환이라 회피).
- **완납/은퇴 시** 네이티브 Debt + 커스텀 빚 카드 전부 `RemoveAllDebtLoanCards`로 제거.

---

## 결제 시스템 (영수증)

- **영수증(Receipt)** = 커스텀 전투 자원. `LoanService._tally`(ConditionalWeakTable + `TallyChanged`),
  에너지 옆 커스텀 HUD 카운터(`NPaymentTallyCounter`). 카드 코스트 배지 = `IUsesPaymentTally`
  (`PaymentCostOverlayPatch`, `_energyIcon` 자식으로 부착).
- **정기 납부(DunningLetterCard/Power = Standing Order)**: 대출 시 지급되는 파워. 매턴 **납부(DebtCurseCard
  = Payment)** 카드를 손에 공급. 납부 카드를 내면 골드로 원금을 깎고(`PrincipalRepayShare`=0.2 만큼
  원금 상환, 나머지는 이자), **영수증 +1**.
- **결제셋 카드**(빚 상점 구매 또는 지급):
  - *반응 파워* — 납부혜택/환급/이자지원/상계(Counterclaim)/명세서(Statement): 납부 때마다 방어도·카드·골드 등 환급.
  - *영수증 소비* — **정산(Settlement)** = 방어도 4×X, **청구서(Invoice)** = 다단 히트(둘 다 보유 영수증 전량 소비).
  - **추심(Collection)→집행(Shakedown)** — 매턴 토큰이 영수증 1을 써 **활력(Vigor)**.
  - **성실 납부(DiligentPayment)** — **소멸된 납부 카드 수**만큼 방어도.
  - **취업알선(JobPlacement)** — 스킬. 영수증 2 + 빚 20을 지고 **품삯(Wages)** 카드를 손+더미에 공급
    (구 파워형은 스톨링 골드 파밍 가능해 스킬로 재설계).

---

## 빚 상점 (NDebtCardShopPanel)

빚이 있으면 상인 방에서 **빚 상점**으로 진입해 결제셋 카드를 **외상**으로 구매(가격이 원금에 가산).

- **진열**: `RevealedPurchasable` = `(LoanFloor, DebtShopVisits)` 결정적 셔플, 방문 수에 따라 3/5/전체,
  매 방문 1장 세일(~45%).
- **★가격/한도 튜닝(무료 슬롯 도입과 함께)**: 기본가 65~90(밴드 **60~95**), 세일 **−45%**, 강화 프리미엄
  **+20%**, 방문당 한도 **120**. 의도 = *유료는 한 방문에 1장, 할인 카드를 집으면 2장*. 검산:
  정가 최저조합 60+65=**125 > 120**(2장 불가) / 세일 35~50 + 최저 60 = **95~110 ≤ 120**(항상 2장 가능) /
  세일+2장 160(불가) / 강화판 최대 95×1.20=**115 ≤ 120**(잠기지 않음).
  ⚠️강화 프리미엄을 30%로 되돌리면 95×1.3=124로 **강화판이 영구 구매 불가**가 된다.
- **★맨 왼쪽(슬롯 0) = 무료 선물**: 빚도 안 지고, 외상 한도도 안 쓰고, **네이티브 Debt 저주도 안 붙는다**
  (`IsFreeOffer` 단일 출처 → 가격/한도/세일·강화판 제외/`ApplyBuyCard`의 저주 게이트가 전부 이걸 본다).
  빚 상점이 "들어가면 무조건 더 깊어지는 화면"이 아니게 되고, 압박은 오른쪽 유료 4장에만 남는다.
  어느 카드가 무료가 되는지는 같은 결정적 셔플이라 방문마다 달라진다(바닥이 보장된 변동 보상).
  - **세일(~45% 할인)·외상 한도는 유료 슬롯 전용**(슬롯 0 제외 — 무료 카드에 할인 태그는 무의미).
  - **★강화판은 슬롯 0에도 걸릴 수 있다** = 상점의 잭팟(무료 + 강화판). `ShopPriceFor`가 슬롯 0에서
    프리미엄을 적용하기 전에 0을 반환하므로 +20% 할증은 애초에 존재하지 않는다.
  - **`채무 조정`은 무료 슬롯에서 제외**(`FreeSlotIneligible`) — 원금 250 탕감을 공짜로 주게 되므로, 슬롯 0에
    걸리면 오른쪽의 첫 적격 오퍼와 **자리를 맞바꾼다**(선물 자체는 항상 유지, 결정적이라 co-op 일치).
  - **네이티브 Debt는 그 방문에 "유료" 구매를 했을 때만** 붙는다. 무료 카드만 집고 나가면 아무 대가도 없고,
    같은 방문에 나중에 유료 구매를 하면 그때 붙는다(`LastDebtGrantFloor` 스탬프도 유료일 때만 찍는다).
  - co-op은 기존 `dl_sync buy <card> <price> <upg>` 와이어 그대로 — **price 0이 전선을 타므로** 원격 피어가
    패널을 연 적 없어도 재유도 없이 일치한다.
- **★상점당 외상 한도(`ShopCreditLimit`=120)**: 한 상점 방문에서 카드로 질 수 있는 빚 상한(대출 하드캡과
  별개). `ShopSpentThisVisit`가 누적, 새 상점 진입 시 리셋(`CountShopVisit`), 유물에 영속화. 초과 오퍼는
  회색+빨간 가격+"한도 초과" 라벨+구매 비활성. 상단에 "대출 가능 {잔액}/{한도}" 헤더.
- **UI**: 상점 돗자리와 같은 2D 뎁스(CanvasLayer 아님, 상점 부모의 형제)에 슬라이드 인. 입력 blocker가
  뒤 상점 오조작 차단(단 상단 HUD·네이티브 back 버튼은 통과). **네이티브 뒤로가기**로 상점 복귀
  (`ShopBackClosesDebtShopPatch`). **상인 손**이 오퍼/상환을 가리킴(NMerchantHand 부모 Node2D를 z-리프트).

---

## 파산 선언 (BankruptcyCard)

스킬. 보유한 **네이티브 Debt 카드 전부 소멸**(전투 파일 + 런덱=영구) → 소멸 수만큼 **힘(Strength)** +
**파산(BankruptcyPower)** 부여.

- **파산 = 이번 전투 + 전투 후 보상까지 골드 획득 0.** 전투 중은 `ModifyGoldGained`(소유자 0), 전투 후
  보상 골드는 `BankruptGoldBlockPatch`(`PlayerCmd.GainGold` prefix, `IsBankrupt` 플래그) — 파워가 사라진
  뒤에도 차단. 플래그는 다음 전투 시작 시 리셋.
- "과소비로 쌓은 빚 = 파산 시 탄약"의 올인 피벗.

---

## 시각/로컬라이제이션

- **커스텀 카드 프레임**: 결제셋 카드에 보라+금 프레임(`NCardFramePatch`, `NCard.Reload` postfix로
  frame/banner/portraitBorder/typePlaque 텍스처 교체). 저주·생성 토큰(품삯/성실납부)은 제외.
- **에너지 오브 캐릭터색 상속**(`EnergyIconPatch`): Colorless 회색 오브를 소유 캐릭터 pool의 에너지
  아이콘으로 교체. canonical(프리뷰)은 현재 런 로컬 플레이어로 fallback, 커스텀 캐릭터/타 모드 postfix
  되돌림까지 대응(CallDeferred 재적용).
- **로컬라이제이션**: 카드·파워·유물 14언어(`DebtLoanLoc.cs` + `LocInjectionPatch`).

---

## Co-op (검증 완료)

빚은 공유된 짐 — coop-verify(2-인스턴스) 실측으로 수렴 확인:

- **빚 상점 구매 네트워크화**(`dl_sync buy`): 구매 복제 + 원금/덱/sold-set가 양 피어 일치.
- **MP 이자**: 빚진 인원 N에 따라 node-interest 상한 = 40 + `min(40, 10·(N−1))`%.
- **대납(Bailout, AnyAlly 네이티브 아군 타겟)**: 동료 빚 대신 상환. 미납 시 돈 있는 아군에 대납 카드 지급.
- **★기술**: `TargetType.AnyAlly` = 네이티브 co-op 아군타겟 자동 lockstep. co-op 턴종료 =
  `EndPlayerTurnAction` enqueue(`SetReadyToEndTurn`은 로컬 1of2). 골드 변경은 로컬+RewardSynchronizer.

---

## 설정 (ModConfig / RitsuLib)

| 키 | 의미 | 기본 |
|---|---|---|
| `maxLoan` | 런당 총 대출 상한(골드) | 300 |
| `maxLoanAct` | 대출 허용 최대 막 | Act 1 |
| `shopCreditLimit` | 상점 방문당 외상(카드 구매) 한도 | 120 |
| `maxLoanDraws` | 한 대출당 골드 인출 횟수 (0=무제한) | 3 |

`ModConfig` 또는 `RitsuLib` 중 어느 것으로도 조절(둘 다 선택; 없으면 기본값). 등록 전 `GetValue` 금지
(타입 기본값 반환), 신 API는 리플렉션+폴백으로 first-wins 버전 스큐 대비.

---

## 파일 맵

| 파일 | 역할 |
|---|---|
| `MainFile.cs` | 부트스트랩 + ModConfig(maxLoan/maxLoanAct/shopCreditLimit) |
| `DebtLoanConfig.cs` | 런타임 조절 값(캡/이자/티어/외상 한도) |
| `LoanService.cs` | **핵심 상태 머신** — 대출/이자/티어/상환/빚상점/영수증/외상한도/co-op |
| `DebtLoanRelic.cs` | 빚 장부 유물 + `[SavedProperty]` 영속화 |
| `DunningLetterCard/Power.cs` | 정기 납부(Standing Order) — 납부 카드 공급 엔진 |
| `DebtCurseCard.cs` | 납부(Payment) — 원금 상환 + 영수증 적립 |
| `DelinquencyCard.cs` / `SeizureCard.cs` / `BadCreditCard.cs` / `DebtorCard.cs` | 티어 저주 |
| `Settlement/Invoice/Refund/PaymentBenefit/InterestSupport/Counterclaim/Statement/Collection/BloodPayment/Garnishment/LoanStrike/Mortgage/JobPlacement/Wages/DiligentPayment*.cs` | 결제셋 카드/파워 |
| `BankruptcyCard.cs` / `BankruptcyPower.cs` | 파산 선언 + 파산 파워 |
| `NDebtCardShopPanel.cs` | 빚 상점 UI(그리드/상환/외상 한도/상인 손) |
| `NLoanDrawsChip.cs` | 상인 상점 상단 "대출 N/3" 칩 |
| `Patches/MerchantLoanPurchasePatch.cs` | 상점 구매 인터셉트 → 대출 결제 |
| `Patches/ShopBackClosesDebtShopPatch.cs` | 네이티브 뒤로가기 → 빚 상점 닫기 |
| `Patches/EnergyIconPatch.cs` / `NCardFramePatch.cs` / `PaymentCostOverlayPatch.cs` | 카드 시각(에너지/프레임/영수증 배지) |
| `Patches/BankruptGoldBlockPatch.cs` / `DelinquencyDrawPatch.cs` | 파산 골드 차단 / 연체 취약 |
| `Patches/RelicInjectionPatches.cs` / `LocInjectionPatch.cs` | 유물 등록 / 14언어 로컬라이제이션 |

---

## 검증

- **solo-verify**(1-인스턴스): 대출→유물, 티어 에스컬레이션, 상환→제거, 영수증/결제셋, 파산(빚→0·힘·
  골드차단), 연체→취약(draw), 빚 상점 렌더 — ALL PASS.
- **coop-verify**(2-인스턴스): 빚 상점 구매 복제·대납 지급·대납 사용 수렴, desync 없음.
- 배포 전 게이트: Release DLL UTF-16 `selftest` 프로브 0 + mods 폴더 `selftest.*` 잔여물 제거.

빌드: `dotnet build Sts2DebtLoan.csproj -c Release` → 게임 mods 폴더. 리소스 팩(.pck)은 `pck_src/`에서
Godot 4.5.1 `--export-pack`.
