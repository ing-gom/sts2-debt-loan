# -*- coding: utf-8 -*-
"""경비 처리 카드아트 = 모드 자신의 영수증 심볼을 '베는' 합성.

★왜 합성인가: 카드아트 체크포인트는 6라운드 37장 동안 '영수증/서류'를 단 한 번도 못 그렸다
(전부 양피지 두루마리로 뭉개짐). 그런데 모드엔 이미 완성된 영수증 심볼이 있다 —
power_icons/payment_stack_power.png, 플레이어가 카드 코스트 배지에서 매번 보는 바로 그 모양이다.
그걸 그대로 크게 써서 반으로 가르면, 생성 모델이 못 하는 '알아볼 수 있는 영수증'이 공짜로 해결되고
배지와 카드아트가 같은 기호를 공유하게 된다.
"""
import io, math, os
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PCK = os.path.join(HERE, "..", "pck_src", "Sts2DebtLoan")
OUT = os.environ.get("EXPENSING_OUT", HERE)   # 결과 저장 위치(기본=이 폴더)
RECEIPT = os.path.join(PCK, "power_icons", "payment_stack_power.png")
# 가위 원본 = gen_card_art.py 의 expensing_scissors_v2 시드 0을 그대로 보관한 것.
# 다시 뽑으려면: python scripts/gen_card_art.py <out_dir> 1 expensing_scissors_v2
SCISSORS = os.environ.get("EXPENSING_SCISSORS",
                          os.path.join(HERE, "art_src", "expensing_scissors_v2_0.png"))
W, H = 1000, 760


def deck_gradient():
    """덱의 보라 그라디언트. 기존 카드아트에서 실제로 뽑은 색이라 나란히 놓아도 안 튄다."""
    top, bot = (108, 66, 190), (150, 108, 224)
    g = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(g)
    for y in range(H):
        t = y / (H - 1)
        d.line([(0, y), (W, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    return g


def key_out_purple(img, thresh=42):
    """보라 배경을 알파로 날린다.
    ★전역 색거리 방식은 쓰지 않는다 — 배경이 위아래로 밝기가 변하는 그라디언트라, 임계를 좁히면
    아래쪽 배경이 남고(가위 좌하단에 보라 사각형이 남았던 원인) 넓히면 피사체의 어두운 면까지 먹는다.
    대신 네 모서리에서 flood-fill 로 '바깥과 이어진 배경'만 지운다 — 밝기가 변해도 이웃 픽셀끼리는
    비슷하므로 그라디언트를 따라 잘 번지고, 피사체 안쪽의 비슷한 색은 바깥과 끊겨 있어 살아남는다."""
    import numpy as np
    arr = np.array(img.convert("RGB")).astype(int)
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    # 1) '보라다' 판정 — 배경만 b가 g보다 크게 높다. 금색(g>b)·흰색(b≈g)·검정 외곽선은 통과 못 한다.
    purple = ((b - g) > 22) & (b > 72)   # ★연결성 판정이 뒤에 붙으므로 색 임계는 넉넉해도 안전하다 (모델이 그린 어두운 보라 그림자까지 잡아야 손잡이 옆 사각 잔여물이 사라진다)
    # 2) 그중 '테두리와 이어진' 것만 배경. 피사체 안쪽의 보랏빛 음영(칼날 그림자 등)은 바깥과
    #    끊겨 있어 살아남는다 — 색 판정만 쓰면 그게 구멍이 된다.
    from scipy import ndimage
    lab, n = ndimage.label(purple)
    border = set(lab[0, :]) | set(lab[-1, :]) | set(lab[:, 0]) | set(lab[:, -1])
    border.discard(0)
    reached = np.isin(lab, list(border))
    alpha = Image.fromarray(np.where(reached, 0, 255).astype("uint8"))
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.7))
    out = img.convert("RGBA")
    out.putalpha(alpha)
    return out


def split_diagonal(sprite, angle_deg=-28, gap=26):
    """스프라이트를 대각선으로 갈라 두 조각으로 돌려준다(각각 살짝 벌어지고 기울어진 채).
    자른 자리가 '베였다'로 읽히려면 조각이 평행이동만 하면 안 되고 약간 회전해야 한다."""
    w, h = sprite.size
    big = int(math.hypot(w, h)) + 4
    out = []
    for side in (0, 1):
        m = Image.new("L", (big, big), 0)
        d = ImageDraw.Draw(m)
        # big 캔버스 중앙을 지나는 수평선 기준으로 위/아래를 채운 뒤 통째로 회전 → 대각 절단면
        d.rectangle([0, 0, big, big // 2] if side == 0 else [0, big // 2, big, big], fill=255)
        m = m.rotate(angle_deg, resample=Image.BICUBIC)
        m = m.crop(((big - w) // 2, (big - h) // 2, (big - w) // 2 + w, (big - h) // 2 + h))
        piece = sprite.copy()
        piece.putalpha(Image.fromarray(
            (__import__("numpy").array(piece.split()[3]) * (__import__("numpy").array(m) / 255.0)).astype("uint8")))
        # 절단면 방향(각도의 법선)으로 벌린다
        rad = math.radians(angle_deg)
        dx, dy = math.sin(rad) * gap, -math.cos(rad) * gap
        if side == 1:
            dx, dy = -dx, -dy
        piece = piece.rotate(3 if side == 0 else -3, resample=Image.BICUBIC, expand=False)
        out.append((piece, (int(dx), int(dy))))
    return out


def outlined(piece, width=11):
    """조각 뒤에 검은 실루엣을 깔아 ★잘린 단면에도 외곽선을 준다.
    이게 없으면 갈라진 게 아니라 '줄이 그어진 종이'로 읽힌다 — 덱 전체가 두꺼운 검정 외곽선
    스타일이라 단면만 외곽선이 없으면 즉시 어색해진다."""
    import numpy as np
    a = np.array(piece.split()[3])
    m = Image.fromarray(a).filter(ImageFilter.MaxFilter(width if width % 2 else width + 1))
    sil = Image.new("RGBA", piece.size, (16, 14, 20, 0))
    sil.putalpha(m)
    out = Image.alpha_composite(sil, piece)
    return out


CUT_ANGLE = -28          # split_diagonal 과 반드시 같은 값 (섬광이 절단선과 어긋나면 즉시 티가 난다)
CREAM, EDGE = (238, 234, 222), (16, 14, 20)


def _clip(layer_full, mask, invert=False):
    """캔버스 크기 레이어의 알파에 마스크를 곱한다(invert면 마스크 바깥만 남긴다)."""
    import numpy as np
    a = np.array(layer_full.split()[3]).astype(float) / 255.0
    m = np.array(mask).astype(float) / 255.0
    if invert:
        m = 1.0 - m
    out = layer_full.copy()
    out.putalpha(Image.fromarray((a * m * 255).astype("uint8")))
    return out


def _rot_paste(bg, layer, center, angle, mask=None, outside=False):
    """cut-space(가로축=절단선)에서 그린 레이어를 절단 각도로 돌려 캔버스에 얹는다.
    회전된 좌표로 직접 그리는 것보다 도형 정의가 단순하고, 각도를 한 곳에서만 바꾸면 된다."""
    rot = layer.rotate(angle, resample=Image.BICUBIC, expand=False)
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.alpha_composite(rot, (center[0] - layer.width // 2, center[1] - layer.height // 2))
    if mask is not None:
        out = _clip(out, mask, invert=outside)
    return Image.alpha_composite(bg, out)


def slash_streak(bg, center, mask, length=400, thick=23):
    """벌어진 틈을 따라 흐르는 흰 섬광 — '베인 자국'이 아니라 '방금 지나간 날'로 읽히게 하는 핵심.
    양 끝이 뾰족한 타원이라 만화적 슬래시가 된다. 바깥에 옅은 글로우, 안쪽에 밝은 코어.
    ★길이는 영수증 대각 현(620/cos28° ≈ 700)에 맞춘다. 처음엔 length=760(=총 1520)으로 잡아
    카드를 가로지르는 검기처럼 보였다 — 종이 밖으로는 살짝만 삐져나와야 "잘린 자리의 섬광"이 된다."""
    S = max(W, H) * 2
    lay = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    cx, cy = S // 2, S // 2
    d.ellipse([cx - length, cy - thick * 1.9, cx + length, cy + thick * 1.9], fill=(255, 240, 190, 70))
    glow = lay.filter(ImageFilter.GaussianBlur(16))
    d2 = ImageDraw.Draw(glow)
    d2.ellipse([cx - length, cy - thick, cx + length, cy + thick], fill=(255, 246, 214, 235))
    d2.ellipse([cx - length * 0.94, cy - thick * 0.42, cx + length * 0.94, cy + thick * 0.42],
               fill=(255, 255, 255, 255))
    # ★영수증 실루엣 안으로 클리핑. 안 하면 종이 밖으로 흰 창처럼 뻗어 '두 번째 칼날'이 된다.
    return _rot_paste(bg, glow, center, CUT_ANGLE, mask=mask)


def paper_scraps(bg, center, mask, n=8):
    """절단면에서 튀는 종이 파편. 덱이 전부 두꺼운 검정 외곽선이라 파편에도 외곽선을 준다 —
    없으면 흰 얼룩으로 보인다. 난수는 고정 시드(재현 가능해야 아트를 다시 뽑아도 같은 그림)."""
    import random
    rnd = random.Random(20260727)
    S = max(W, H) * 2
    lay = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    cx, cy = S // 2, S // 2
    for i in range(n):
        t = rnd.uniform(-0.85, 0.85)
        x = cx + t * 400
        side = -1 if i % 2 == 0 else 1
        y = cy + side * rnd.uniform(120, 300)   # 실루엣 밖까지 밀어낸다
        s = rnd.uniform(26, 52)   # ★작으면 외곽선만 남아 '검은 점'이 된다
        pts = [(x + rnd.uniform(-s, s), y + rnd.uniform(-s, s)) for _ in range(4)]
        pts.sort(key=lambda p: math.atan2(p[1] - y, p[0] - x))
        d.polygon(pts, fill=CREAM, outline=EDGE, width=5)
    # ★종이 '바깥'에만 남긴다. 실루엣 위에 얹히면 튀어나간 파편이 아니라 종이에 붙은 얼룩이 된다.
    return _rot_paste(bg, lay, center, CUT_ANGLE, mask=mask, outside=True)


def impact_burst(bg, at, r=110):
    """날이 종이에 닿는 지점의 금색 4점 섬광. 시선을 '지금 잘리는 자리'로 모은다."""
    S = int(r * 4)
    lay = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    c = S // 2
    d.ellipse([c - r, c - r * 0.16, c + r, c + r * 0.16], fill=(255, 214, 92, 240))
    d.ellipse([c - r * 0.16, c - r * 0.62, c + r * 0.16, c + r * 0.62], fill=(255, 214, 92, 240))
    d.ellipse([c - r * 0.30, c - r * 0.30, c + r * 0.30, c + r * 0.30], fill=(255, 250, 226, 255))
    lay = lay.rotate(-14, resample=Image.BICUBIC)
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.alpha_composite(lay, (at[0] - c, at[1] - c))
    return Image.alpha_composite(bg, out)


def build(variant):
    bg = deck_gradient().convert("RGBA")
    receipt = Image.open(RECEIPT).convert("RGBA")
    rh = 620
    receipt = receipt.resize((int(receipt.width * rh / receipt.height), rh), Image.LANCZOS)
    rx, ry = (W - receipt.width) // 2, (H - receipt.height) // 2 + 10

    # 바닥 그림자 — 덱의 다른 아트가 전부 접지 그림자를 갖고 있어 없으면 붕 떠 보인다.
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse([rx - 40, ry + rh - 40, rx + receipt.width + 40, ry + rh + 40],
                               fill=(30, 12, 60, 110))
    bg = Image.alpha_composite(bg, sh.filter(ImageFilter.GaussianBlur(18)))

    cx_, cy_ = rx + receipt.width // 2, ry + receipt.height // 2
    # 자르기 전 영수증의 실루엣 — 이펙트를 안/밖으로 가르는 기준이 된다.
    rmask = Image.new('L', (W, H), 0)
    rmask.paste(receipt.split()[3], (rx, ry))
    rmask = rmask.filter(ImageFilter.MaxFilter(9))
    fx = variant.split("+")[1:] if "+" in variant else []

    if variant == "whole":
        bg.alpha_composite(receipt, (rx, ry))
    else:
        # 섬광은 조각 '아래'에 깔아야 틈 사이로만 보인다 — 위에 얹으면 종이를 덮어 흰 띠가 된다.
        if "fx" in fx or "full" in fx:
            bg = slash_streak(bg, (cx_, cy_), rmask)
        for piece, (dx, dy) in split_diagonal(receipt, gap=34):
            layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            layer.alpha_composite(outlined(piece), (rx + dx, ry + dy))
            bg = Image.alpha_composite(bg, layer)
        # 파편은 조각 '위' — 종이 바깥으로 튀어나가는 것이므로 가려지면 안 된다.
        if "fx" in fx or "full" in fx:
            bg = paper_scraps(bg, (cx_, cy_), rmask)

    if variant.startswith("scissors"):
        sc = key_out_purple(Image.open(SCISSORS))
        bbox = sc.split()[3].getbbox()          # 여백을 잘라 실제 오브젝트만 남긴다
        sc = sc.crop(bbox)
        sh2 = 700
        sc = sc.resize((int(sc.width * sh2 / sc.height), sh2), Image.LANCZOS)
        # ★가위 소스는 아래쪽이 프레임에 잘려 있다(손잡이가 원본 밖으로 나감). 그대로 얹으면 그 단면이
        # 네모난 덩어리로 보인다 — 그래서 손잡이를 캔버스 아래로 흘려보내 잘린 자리를 화면 밖에 숨긴다.
        # 도구가 화면 밖에서 들어오는 구도는 일러스트에서 자연스럽고, 날이 절단선 위에 놓인다.
        sx = min(int(W * 0.44), W - sc.width - 16)
        sy = H - sh2 + 90                      # 아래로 90px 흘려보냄
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        layer.alpha_composite(sc, (sx, sy))
        bg = Image.alpha_composite(bg, layer)
        if "full" in fx:
            # 날이 '종이'와 만나는 지점 — 회전축(손잡이 쪽)이 아니라 날 안쪽이라 위·왼쪽으로 당긴다
            bg = impact_burst(bg, (sx + int(sc.width * 0.10), sy + int(sc.height * 0.30)), r=92)

    return bg.convert("RGB")


VARIANTS = [
    "cut",                  # 이펙트 없음 (비교용)
    "cut+fx",               # 섬광 + 파편
    "scissors",             # 가위만
    "scissors+fx",          # 가위 + 섬광 + 파편
    "scissors+full",        # 가위 + 섬광 + 파편 + 충돌 스파크
]
for v in VARIANTS:
    p = os.path.join(OUT, "expensing_%s.png" % v.replace("+", "_"))
    build(v).save(p)
    print(p)
