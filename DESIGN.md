# Sts2DebtLoan — 상점 대출 / Merchant Loans

상점에서 부족한 골드를 **대출**받아 아이템을 사고, 갚지 않으면 **빚(Debt) 저주 카드**가
덱에 스며들어 이자를 뜯어가는 자매 모드. v0.1.0 골조.

---

## 컨셉 한 줄

> 1막 상점에서 살 돈이 조금 모자란 아이템에 대해 **부족분만큼** 대출 → 대출 유물 획득 →
> 안 갚으면 방문 노드 수에 따라 빚 카드가 늘고(14/17/20방=1/3/5장), 각 빚 카드가 골드를
> 이자로 뜯어감 → **원금 상환** 또는 **이자 200% 도달** 시 유물 비활성화 + 빚 카드 전부 제거.

---

## 확정된 설계 결정 (사용자 Q&A)

| 항목 | 결정 |
|---|---|
| 노드 카운트 기준 | **모든 맵 노드** (전투/상점/휴식/이벤트/보물 전부 +1) |
| 비활성화 시 빚 카드 | **항상 제거** (원금 상환·이자 200% 무관) |
| 이자 크기 (질문 답 미수신) | **기본 고정 10골드/발동** (바닐라 Debt 패리티), config 조절 가능 |
| 빚 카드 발동 | 바닐라 Debt 그대로 — **손패에 있을 때 턴 종료 시** `min(10, 보유골드)` 차감 |
| 0골드 이하 | 뜯을 골드 없음 → 그 발동은 이자 미납 (바닐라 `min` 로 자동 처리) |

### 대출 규칙
- **최초 1회** 대출 시 유물 획득 → 이때부터 노드 카운터 시작.
- 대출액 = `구매가 − 보유골드` (부족분), **총 원금 ≤ 300** (config `maxLoan`).
- **1막에서만** 대출 (config `allowOtherActs` 로 전 막 허용 가능).
- **추가 대출**: 유물 보유 중 + 원금 < 300 + `노드 < 14`(패널티 시작 전) + 1막.
- 로더블 아이템 가격표는 빨강 대신 **노랑** — *미구현, 아래 TODO*.

---

## 상태 머신

```
[1막 상점] 구매 시도, 보유골드 < 구매가
   └ CanLoanCover? (부족분 ≤ 300−원금, 1막, 최초이거나 노드<14)
        └ GrantLoanFor: 부족분 골드 지급(GainGold+sync) → 원금 += 부족분
                        └ 최초면 유물 지급 + 노드 카운터=0
   → 구매 재실행(이제 EnoughGold) → 정상 결제

[EnterRoom 마다]  OnRoomEntered: 노드++ →
   14방:1장 / 17방:3장 / 20방:5장(상한)  빚 카드를 Deck 에 주입

[전투: 빚 카드가 손패서 턴종료]  min(이자,보유골드) 차감 → AccrueInterest
   이자누적 ≥ 원금×200% → Retire

[상점 재방문 원금 상환]  Repay: LoseGold(원금)+sync → Retire

Retire: 유물 비활성(Active=false) + 빚 카드 전부 RemoveFromDeck
```

---

## 파일 맵

| 파일 | 역할 |
|---|---|
| `MainFile.cs` | 부트스트랩 + ModConfig (maxLoan/interest/capPct/otherActs) |
| `AssemblyResolverBootstrap.cs` | ModKit DLL 사이드로드 (표준 패턴) |
| `DebtLoanConfig.cs` | 런타임 조절 값 + 빚 카드 스케줄 |
| `LoanService.cs` | **핵심 상태 머신** — 자격/금액/원금/이자/노드/상환/Retire |
| `DebtCurseCard.cs` | 커스텀 Debt 저주 카드 (바닐라 Debt 복제 + 이자 적립) |
| `DebtLoanRelic.cs` | "상인의 장부" 유물 + 지급 헬퍼 |
| `Patches/RelicInjectionPatches.cs` | 유물 풀 등록 (자동 스캔) |
| `Patches/LocInjectionPatch.cs` | 유물+카드 로컬라이제이션 (relics/cards 테이블) |
| `Patches/RoomEnterPatch.cs` | `RunManager.EnterRoom` 체인 → 노드 카운트+빚 카드 주입 |
| `Patches/MerchantLoanPurchasePatch.cs` | `OnTryPurchaseWrapper` 인터셉트 → 대출 결제 |

---

## Co-op 노트

- 골드 변경은 전부 **로컬 플레이어 한정 + RewardSynchronizer** (RelicForge 패턴).
- 노드 카운트/카드 주입도 로컬 플레이어만 (각 피어가 자기 덱 소유).
- 빚 카드 이자 차감은 카드 자체의 `OnTurnEndInHand` (결정론적 전투 sim) → 바닐라 Debt 처럼
  명시 sync 없이 양쪽 수렴 (검증 필요).
- 배포 전 **coop-guard**(정적) → **coop-verify**(2인스턴스 실측) 필수.

---

## 검증 상태 (solo-verify 2026-07-20)

`solo-verify` 1-인스턴스 실측 **RESULT: OK (5/5 PASS)**:
- ✅ 대출→유물 지급 (ledger, 원금100, active)
- ✅ 노드 에스컬레이션 정확 (r13=0, r14=1, r17=3, r20=5)
- ✅ 원금 상환 → 비활성+빚카드0
- ✅ 이자 200%(100/100) → 비활성+빚카드0
- ✅ **유물 아이콘 인게임 렌더** (gem 스프라이트, 유물 트레이 확인)
- ✅ **빚 카드 덱 주입 + "Debt" 타이틀 렌더** 확인

핵심 상태머신은 실엔진에서 검증됨. 주의: 테스트는 `LoanService.GrantLoanDirect` 직접 호출로
로직을 검증 — **실제 상점 구매 인터셉트/상환 버튼 UI 경로는 아직 인게임 미실측** (상점 진입 필요).

## TODO / 검증 체크리스트

우선순위 순:

1. **[확인 필요] 빚 카드 페이스 설명** — `.description`/`.smartDescription`을 vanilla Debt 포맷
   (`{Gold:diff()}` + `[gold]` 마크업)으로 주입 완료. solo-verify 스크린샷의 "If you can read this,
   there is a bug." 플레이스홀더는 **이벤트 화면 fallback**(decomp 196923 = EVENT 코드)이지 카드
   페이스가 아님 — pump가 들어간 이벤트 캡처. 카드 페이스 설명(NCard `_descriptionLabel`)은 손패/덱뷰
   UiTest로 별도 확인 필요.
2. ✅ **[완료] 상점 결제 인터셉트 + 상환 버튼** — solo-verify 상점 페이즈로 실측: 살 수 없는
   유물을 대출로 구매(cost=201→대출 충당→유물 획득), `NMerchantRepayButton`이 실제 shop 노드에
   attach + cost=원금 표시 확인. (버튼 아이콘=loose png 배선, 멀티모드 상점서 육안 확정만 남음)
3. ✅ **[완료] 초록 가격표** — 대출 가능(살 수 없지만 loan-coverable) 아이템 가격 라벨을 초록으로.
   `MerchantPriceColorPatch` = 각 슬롯 subtype `UpdateVisual` 포스트픽스, `CanLoanCover` 시
   `_costLabel.Modulate=StsColors.green` (EnoughGold 불변). solo-verify 육안 확인.
4. ✅ **[완료] 유물 라이브 배지** — 장부 유물 아이콘에 현재 빚 원금 배지. `DisplayAmount`=원금 +
   `ShowCounter`=활성, `[SavedProperty]` 세터가 `InvokeDisplayAmountChanged` 호출. per-relic라 co-op-safe.
   (전체 이자/노드 breakdown 호버는 per-relic DynamicVars 필요 — 추후 확장.)
5. ✅ **[완료] grant-only 유물** — Ledger를 `RelicRarity.Event`로 (보상/상점 풀은 Common/Uncommon/
   Rare/Shop만 롤 → 랜덤 드롭 불가, 대출로만 획득). solo-verify 지급 정상 확인.
6. ✅ **[완료] 세이브/로드 지속성** — DebtLoanRelic `[SavedProperty]` 4개(자동 왕복) +
   LoanService write-through/RestoreFromRelic + `NGame.LoadRun` 훅. solo-verify save/load PASS.
7. ✅ **[완료] 유물 아이콘** — gem 스프라이트 pck 빌드+인게임 렌더 확인.
8. ✅ **[완료] 상환 버튼** — `NMerchantRepayButton` (RelicForge cleanse 패턴). UI 실측은 #2.
9. **[정책] 이자 크기 기본값** — 고정 10. 원금 비례(5%) 원하면 변경.
10. ⚠️ **[SP 게이트] co-op** — coop-guard 결과 desync 위험(유물 지급 `SyncLocalObtainedRelic` 누락 +
    빚카드 add/remove 복제 미확인). 안전을 위해 `CanLoanCover`에서 **co-op은 대출 OFF**로 게이트
    (desync 원천 차단). 골드 경로는 이미 로컬+Sync로 안전. **완전 co-op 지원(future)** = 유물/카드
    RewardSynchronizer 배선 + `coop-verify` 2-인스턴스 실측 필요.

빌드: `dotnet build Sts2DebtLoan/Sts2DebtLoan.csproj -c Debug` → 게임 mods 폴더 자동 복사.
