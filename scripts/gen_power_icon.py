"""Generate a DebtLoan POWER ICON candidate set via local ComfyUI (Animagine XL 4.0 + sts2_icon LoRA).

Power icons are the small status badges shown on the creature's power bar (256x256 RGBA, transparent bg).
The mod's icon look (see project memory): the sts2_icon LoRA at strength 0.4-0.6 (0.9 lets the style
overwrite the content), a single small centred object with generous margin (so the frame doesn't crop it),
and background removed by CORNER FLOOD-FILL (only the connected bg is keyed out — sealed art is preserved).

Usage: python gen_power_icon.py <out_dir> [n]   (ComfyUI up on 127.0.0.1:8188)
Then pick a candidate, and it's already keyed to transparent 256x256 as <key>_power.png in out_dir.
"""
import json, os, sys, time, urllib.request
from collections import deque
from PIL import Image

HOST = 'http://127.0.0.1:8188'
LORA = 'sts2_icon_lora-e16.safetensors'
LORA_STRENGTH = 0.35   # lower so the icon LoRA styles WITHOUT overwriting the (coin + red slash) content

STYLE = ('sts2icon, game power icon, single small centered object, generous margin, '
         'flat color, bold black outline, cel shading, simple, iconic, clean, '
         'plain solid background, masterpiece, absurdres')
NEG = ('cluttered, busy background, multiple objects, text, watermark, letters, numbers, '
       'ui, border, frame, card, realistic, photorealistic, 3d render, lowres, blurry, '
       'worst quality, low quality, cropped, cut off, edge, humans, hands, fingers')

# 파산 (Bankruptcy) power icon — "can't earn gold / defaulted". A debuff badge: a coin with a red
# slash/prohibition through it (no-income), OR an empty overturned coin purse. Keep it ONE clear object.
ICONS = {
    # keep it DEAD simple — one gold coin, one bold red no-entry ring+slash over it. This is the clearest
    # "no income" read; the earlier purse/stamp prompts drifted into unrelated shapes.
    'bankruptcy_power': ('one single round gold coin in the center, a big bold red circle with a red '
                         'diagonal line across it drawn on top of the coin, prohibition sign, '
                         'forbidden symbol, no-money, flat vector icon, plain white background'),
    'bankruptcy_power_slash': ('a gold coin crossed out by a thick red X, cancelled money, '
                               'simple flat icon, one object centered, plain white background'),
    # 경비 처리 (Expensing) — 영수증 비용 -1. 카드아트 5라운드에서 배운 것: 이 체크포인트는 '서류'를
    # 못 그린다. 아이콘은 LoRA(sts2_icon)가 붙어 별개 파이프라인이지만 같은 함정을 피해 단순 도형으로 간다.
    'expensing_power': ('a gold coin with a small downward green arrow beside it, price cut, '
                        'discount symbol, simple flat icon, one object centered, plain white background'),
    'expensing_power_scissor': ('a pair of scissors cutting a gold coin in half, cost cutting, '
                                'simple flat icon, one object centered, plain white background'),
    # 차입 (Borrowing) — 매 턴 에너지. 카드아트가 모래시계로 확정됐으니 아이콘도 같은 모티프로 묶는다.
    'borrowing_power': ('an hourglass filled with gold coins instead of sand, '
                        'simple flat icon, one object centered, plain white background'),
    # ── 경비 처리 카드아트 합성용 부품. ★카드아트 파이프라인은 '영수증'을 못 그리지만(6라운드 37장 실증),
    # 모드엔 이미 완성된 영수증 심볼(payment_stack_power.png)이 있다. 그래서 날붙이만 여기서 뽑아
    # 그 심볼 위에 합성한다 — 플레이어가 코스트 배지에서 보는 모양과 100% 같은 영수증이 나온다.
    'cut_scissors': ('a pair of open scissors, blades apart, simple flat icon, one object centered, '
                     'plain white background'),
    'cut_knife': ('a single sharp dagger knife pointing down, simple flat icon, one object centered, '
                  'plain white background'),
    'borrowing_power_bolt': ('a gold coin with a bright lightning bolt across it, energy from money, '
                             'simple flat icon, one object centered, plain white background'),
}

def workflow(seed, pos):
    return {
        '4':  {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': 'animagine-xl-4.0.safetensors'}},
        '10': {'class_type': 'LoraLoader', 'inputs': {'lora_name': LORA, 'strength_model': LORA_STRENGTH,
                'strength_clip': LORA_STRENGTH, 'model': ['4', 0], 'clip': ['4', 1]}},
        '5':  {'class_type': 'EmptyLatentImage', 'inputs': {'width': 1024, 'height': 1024, 'batch_size': 1}},
        '6':  {'class_type': 'CLIPTextEncode', 'inputs': {'text': pos + ', ' + STYLE, 'clip': ['10', 1]}},
        '7':  {'class_type': 'CLIPTextEncode', 'inputs': {'text': NEG, 'clip': ['10', 1]}},
        '3':  {'class_type': 'KSampler', 'inputs': {'seed': seed, 'steps': 28, 'cfg': 5.5,
                'sampler_name': 'euler_ancestral', 'scheduler': 'normal', 'denoise': 1.0,
                'model': ['10', 0], 'positive': ['6', 0], 'negative': ['7', 0], 'latent_image': ['5', 0]}},
        '8':  {'class_type': 'VAEDecode', 'inputs': {'samples': ['3', 0], 'vae': ['4', 2]}},
        '9':  {'class_type': 'SaveImage', 'inputs': {'filename_prefix': 'powicon', 'images': ['8', 0]}},
    }

def post(path, payload):
    req = urllib.request.Request(HOST + path, data=json.dumps(payload).encode(),
                                 headers={'Content-Type': 'application/json'})
    return json.loads(urllib.request.urlopen(req, timeout=30).read())

def get(path):
    return json.loads(urllib.request.urlopen(HOST + path, timeout=30).read())

def corner_flood_key(src, dst, tol=40, size=256):
    """Remove the connected background by flood-fill from the 4 corners → transparent, then trim + fit 256."""
    im = Image.open(src).convert('RGBA')
    px = im.load(); w, h = im.size
    seen = [[False]*w for _ in range(h)]
    def near(a, b): return all(abs(a[i]-b[i]) <= tol for i in range(3))
    dq = deque()
    for cx, cy in [(0,0),(w-1,0),(0,h-1),(w-1,h-1)]:
        dq.append((cx, cy, px[cx, cy]))
    while dq:
        x, y, ref = dq.popleft()
        if x < 0 or y < 0 or x >= w or y >= h or seen[y][x]: continue
        r, g, b, a = px[x, y]
        if not near((r, g, b), ref): continue
        seen[y][x] = True; px[x, y] = (r, g, b, 0)
        dq.extend([(x+1,y,ref),(x-1,y,ref),(x,y+1,ref),(x,y-1,ref)])
    # trim to opaque bbox, pad square, resize to 256
    bbox = im.getbbox()
    if bbox: im = im.crop(bbox)
    s = max(im.size); sq = Image.new('RGBA', (s, s), (0,0,0,0))
    sq.paste(im, ((s-im.width)//2, (s-im.height)//2))
    sq.resize((size, size), Image.LANCZOS).save(dst)

def main():
    out = sys.argv[1]; n = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    os.makedirs(out, exist_ok=True)
    jobs = {}
    for key, pos in ICONS.items():
        for i in range(n):
            seed = 800_000 + i * 1013
            r = post('/prompt', {'prompt': workflow(seed, pos)})
            jobs[r['prompt_id']] = (key, i); print('queued', key, i, r['prompt_id'])
    done, t0 = set(), time.time()
    while len(done) < len(jobs) and time.time() - t0 < 1800:
        time.sleep(5)
        for pid, (key, i) in list(jobs.items()):
            if pid in done: continue
            h = get(f'/history/{pid}')
            if pid in h and h[pid].get('outputs'):
                for _, o in h[pid]['outputs'].items():
                    for img in o.get('images', []):
                        url = f"{HOST}/view?filename={img['filename']}&subfolder={img.get('subfolder','')}&type={img['type']}"
                        raw = os.path.join(out, f"{key}_{i}_raw.png")
                        urllib.request.urlretrieve(url, raw)
                        keyed = os.path.join(out, f"{key}_{i}.png")
                        try: corner_flood_key(raw, keyed)
                        except Exception as e: print('key failed', keyed, e)
                        print('saved', keyed)
                done.add(pid)
    print(f'done {len(done)}/{len(jobs)} in {time.time()-t0:.0f}s')

if __name__ == '__main__':
    main()
