# 저주 카드 아트 제작 파이프라인

DebtLoan의 6종 저주 카드 포트레잇(`pck_src/Sts2DebtLoan/card_art/*.png`, 1000×760)을 어떻게 만들었는지 기록. LoRA 학습·생성 자체는 **이 레포 밖**(로컬 `C:\Users\kl95\OneTrainer`)에서 진행했고, 여기에는 산출물과 방법만 남긴다.

## 카드 ↔ 아트 매핑

| 파일 | 카드 클래스 | 개념 |
|---|---|---|
| `debt_dunning.png` | `DebtCurseCard` (base) | 봉인된 독촉장이 배달되는 형태 |
| `debt_dunning_plus.png` | `DebtCurseCard` (`IsUpgraded`) | 후드 쓴 상인 추심원 + 붉은 청구서 |
| `overdue.png` | `DelinquencyCard` | 기한 지난 시계 |
| `seizure.png` | `SeizureCard` | 플레이 카드가 사슬에 압류 |
| `bad_credit.png` | `BadCreditCard` | 텅 빈 지갑 + 나방(무일푼) |
| `forced_levy.png` | `ForcedCollectionCard` | 강철 건틀릿이 금화를 강제 탈취 |

## 파이프라인

1. **스타일 LoRA 학습** — 바닐라 STS2 저주 카드 그림체를 재현하는 SDXL LoRA.
   - 데이터셋: 게임 pck에서 GDRE Tools로 추출한 저주 포트레잇 24장(`card_portraits/curse/` 18 + `curse/beta/` 6).
   - 베이스: Animagine XL 4.0 (SDXL, 애니 태그 기반). 학습툴: OneTrainer (RTX 5070 Ti, cu130).
   - 트리거 토큰 `sts2curse`, rank32/α16, 1024, 120 epoch(~35분). 산출물 `sts2_curse_lora.safetensors`(로컬 보관, 미커밋).

2. **카드별 생성** — `Animagine + sts2curse LoRA`로 카드별 컨셉 생성.
   - ★프롬프트는 **77 CLIP 토큰 이내**로, 장면 묘사를 맨 앞에(초과 시 카드별 묘사가 잘려 전 카드가 비슷해짐).
   - 슬더스 팔레트: `deep purple background, cool grey, violet rim light`, 갈색/고채도는 네거티브.

3. **상인 반영(추심 카드)** — IP-Adapter(SDXL vit-h)에 게임 상인 초상(`run_summary_merchant.png`, 파란 후드+선글라스)을 참조로, ip_scale 0.3. 빚독촉/빚독촉+/강제징수에만.

4. **배경 분위기** — 빈 여백 카드(연체·차압·신용불량·강제징수)는 img2img(denoise 0.45)로 승인본을 유지한 채 슬더스풍 소용돌이 스모크 배경만 추가. 빚 독촉 2장은 원본 유지.

## 게임 삽입 방식

- 각 카드 클래스에서 `CardModel.PortraitPath`(+`BetaPortraitPath`)를 오버라이드해 `res://Sts2DebtLoan/card_art/<n>.png`를 가리킨다. `DebtCurseCard`는 `IsUpgraded` 분기.
- 렌더러가 `_portrait.Texture = Model.Portrait => ResourceLoader.Load<Texture2D>(PortraitPath)`를 직접 호출(HasPortrait 분기 없음)하므로, pck에 PNG만 있으면 표시된다.
- PNG는 `pck_src/Sts2DebtLoan/card_art/`에 두고 build-pck(Godot 4.5.1 import→export-pack)로 `Sts2DebtLoan.pck`에 패킹. pck는 `.gitignore`(`*.pck`)라 커밋하지 않고 빌드시 재생성.
- 검증: solo-verify로 6장 전부 인게임 `1000×760` 로드 확인(PASS).

## 재현

LoRA·생성 스크립트는 로컬 `C:\Users\kl95\OneTrainer`(`build_curse_config.py`, `gen_debt_curses_v*.py`, `gen_*_smoke.py`). 아트를 다시 뽑으려면 그쪽에서 실행 후 결과 PNG를 `pck_src/Sts2DebtLoan/card_art/`에 1000×760으로 넣고 build-pck.
