"""Generate luxury card-frame / banner INSPIRATION references via local ComfyUI (Animagine XL 4.0).

These are NOT usable in-game (freeform, unaligned) — they are motif references to mine for the
procedural frame/banner explorer. Debt/loan theme: purple + gold, marble, filigree, coins, ledger,
crescent-moon (the merchant's mask motif).

Usage: python gen_frame_refs.py <outdir> [n_per_prompt]   (ComfyUI up on 127.0.0.1:8188)
"""
import json, os, sys, time, urllib.request
HOST='http://127.0.0.1:8188'

STYLE=('ornate, intricate, symmetric, highly detailed, luxury, premium, elegant, '
       'centered composition, empty center, masterpiece, high score, great score, absurdres')
NEG=('text, watermark, signature, letters, numbers, person, character, face, hands, '
     'lowres, blurry, worst quality, low quality, cluttered photo, asymmetric')

# motif × material combos for a luxury purple+gold debt-themed frame/banner
PROMPTS = {
 'frame_marble_filigree': 'an ornate empty rectangular card frame border, deep purple marble with gold veins, intricate gold filigree scrollwork, baroque',
 'frame_artnouveau':      'an empty ornate card frame border, purple and gold art nouveau, flowing gold vines, elegant',
 'frame_coins':           'an ornate empty card frame border made of stacked gold coins and purple gemstones, wealth motif',
 'frame_amethyst_gold':   'an empty luxury card frame border, polished amethyst purple stone with gold inlay trim, jewelry',
 'frame_celtic':          'an empty ornate card frame border, purple enamel and gold celtic knotwork, medieval luxury',
 'frame_laurel':          'an empty card frame border, gold laurel wreath and purple velvet, imperial roman luxury',
 'frame_deco':            'an empty ornate card frame border, art deco purple and gold, geometric gold rays, gatsby',
 'frame_crescent':        'an empty ornate card frame border, purple and gold, crescent moon emblem motif, mystic fortune teller',
 'banner_marble_ribbon':  'an ornate ribbon banner nameplate, white marble and gold filigree edges, luxury heraldic scroll',
 'banner_gold_scroll':    'an ornate empty ribbon banner, gold and deep purple, filigree scroll ends, heraldic nameplate',
 'ornament_corner':       'a single ornate gold filigree corner ornament flourish on purple, baroque scrollwork, isolated',
 'ledger_motif':          'an ornate gold and purple emblem, quill pen ledger coin balance scale motif, luxury seal',
}

def wf(seed,pos):
    return {
     '4':{'class_type':'CheckpointLoaderSimple','inputs':{'ckpt_name':'animagine-xl-4.0.safetensors'}},
     '5':{'class_type':'EmptyLatentImage','inputs':{'width':1024,'height':1024,'batch_size':1}},
     '6':{'class_type':'CLIPTextEncode','inputs':{'text':pos+', '+STYLE,'clip':['4',1]}},
     '7':{'class_type':'CLIPTextEncode','inputs':{'text':NEG,'clip':['4',1]}},
     '3':{'class_type':'KSampler','inputs':{'seed':seed,'steps':26,'cfg':5.5,'sampler_name':'euler_ancestral',
          'scheduler':'normal','denoise':1.0,'model':['4',0],'positive':['6',0],'negative':['7',0],'latent_image':['5',0]}},
     '8':{'class_type':'VAEDecode','inputs':{'samples':['3',0],'vae':['4',2]}},
     '9':{'class_type':'SaveImage','inputs':{'filename_prefix':'frameref','images':['8',0]}},
    }
def post(p,d):
    r=urllib.request.Request(HOST+p,data=json.dumps(d).encode(),headers={'Content-Type':'application/json'})
    return json.loads(urllib.request.urlopen(r,timeout=30).read())
def get(p): return json.loads(urllib.request.urlopen(HOST+p,timeout=30).read())

def main():
    out=sys.argv[1]; n=int(sys.argv[2]) if len(sys.argv)>2 else 4
    os.makedirs(out,exist_ok=True); jobs={}
    sbase=int(os.environ.get("SEED_BASE","400000"))
    for key,pos in PROMPTS.items():
        for i in range(n):
            seed=sbase+i*929
            r=post('/prompt',{'prompt':wf(seed,pos)}); jobs[r['prompt_id']]=(key,i)
            print('queued',key,i)
    done,t0=set(),time.time()
    while len(done)<len(jobs) and time.time()-t0<2400:
        time.sleep(5)
        for pid,(key,i) in jobs.items():
            if pid in done: continue
            h=get(f'/history/{pid}')
            if pid in h and h[pid].get('outputs'):
                for _,o in h[pid]['outputs'].items():
                    for img in o.get('images',[]):
                        url=f"{HOST}/view?filename={img['filename']}&subfolder={img.get('subfolder','')}&type={img['type']}"
                        urllib.request.urlretrieve(url,os.path.join(out,f"{key}_{i}.png"))
                done.add(pid); print('saved',key,i)
    print(f'done {len(done)}/{len(jobs)} in {time.time()-t0:.0f}s')

if __name__=='__main__': main()
