"""Parametric DebtLoan card-frame / banner design explorer.

Recolours the vanilla frame templates (frame_templates/vanilla_<type>.png, extracted from the game
ui_atlas via GDRE) into premium purple+gold variants across a parameter space, for an autonomous
design-search loop. Deterministic (seeded) so a given param dict always renders the same image.

render(params, type) -> PIL.Image   (usable, aligned to vanilla frame shape)
CLI: python gen_frame_variants.py <params.json> <type> <outdir> [montage.png]
     params.json = { "<id>": {param dict}, ... }   -> renders each, optional montage grid.
"""
import sys, os, json, math
import numpy as np
from PIL import Image, ImageFilter, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
# frame_templates lives in the scratchpad; allow override via env for portability.
TPL = os.environ.get("FRAME_TPL_DIR",
      r"C:\Users\kl95\AppData\Local\Temp\claude\C--Users-kl95-sts2-card-advisor-dev\6f5e5b95-e6f4-438c-be36-eff494d55103\scratchpad\frame_templates")

# ---- colour ramps (dark, mid, light) ----
GOLD = {
    "rich":     [(120,78,26),(232,180,74),(255,244,196)],
    "antique":  [(78,52,16),(176,132,50),(226,196,132)],
    "champagne":[(150,120,60),(224,200,140),(252,244,214)],
    "deep":     [(104,58,14),(214,150,52),(250,224,150)],   # reddish gold
    "bright":   [(150,105,20),(246,206,70),(255,250,205)],
}
PURPLE = {
    "royal":  [(20,9,40),(88,54,134),(150,110,196)],
    "deep":   [(14,6,30),(64,36,104),(120,84,168)],         # dark aubergine
    "violet": [(22,14,52),(78,58,158),(140,118,214)],       # bluer
    "plum":   [(34,12,44),(104,48,120),(168,104,182)],      # redder
    "jewel":  [(24,8,48),(96,42,158),(158,96,216)],         # saturated
}

def _ramp3(t, c):
    t=np.clip(t,0,1); lo=t<0.5; a=np.where(lo,t*2,(t-0.5)*2)[...,None]; cond=lo[...,None]
    c0,c1,c2=[np.array(x,float) for x in c]
    return np.where(cond,c0*(1-a)+c1*a,c1*(1-a)+c2*a)

def _band(a,i,o):
    eo=np.asarray(a.filter(ImageFilter.MinFilter(o*2+1))).astype(np.float32)/255.0
    ei=np.asarray(a.filter(ImageFilter.MinFilter(i*2+1))).astype(np.float32)/255.0
    return np.clip(ei-eo,0,1)

_NCACHE={}
def _fnoise(h,w,octaves,seed,base=3):
    k=(h,w,octaves,seed,base)
    if k in _NCACHE: return _NCACHE[k]
    rng=np.random.default_rng(seed); out=np.zeros((h,w)); amp=1.0; tot=0
    for o in range(octaves):
        gh,gw=base*(2**o),base*(2**o); gr=rng.random((gh,gw)).astype(np.float32)
        im=Image.fromarray((gr*255).astype(np.uint8)).resize((w,h),Image.BICUBIC)
        out+=amp*(np.asarray(im).astype(np.float32)/255.0); tot+=amp; amp*=0.5
    _NCACHE[k]=out/tot; return _NCACHE[k]

# per-template heavy ops (van, luminance, alpha, border mask, pinstripe bands) — param-independent.
_TCACHE={}
def _tpl(ctype):
    if ctype in _TCACHE: return _TCACHE[ctype]
    van=Image.open(os.path.join(TPL,f"vanilla_{ctype}.png")).convert("RGBA")
    w,h=van.size; r,g,b,a=van.split()
    lum=np.asarray(Image.merge("RGB",(r,g,b)).convert("L")).astype(np.float32)/255.0
    A=np.asarray(a).astype(np.float32)/255.0
    er=np.asarray(a.filter(ImageFilter.MinFilter(61))).astype(np.float32)/255.0
    border=((A>0.04)&(er<0.5)).astype(np.float32)
    b1=_band(a,34,39); b2=_band(a,43,46)
    yy,xx=np.mgrid[0:h,0:w].astype(np.float32)
    d=dict(van=van,w=w,h=h,a=a,lum=lum,A=A,border=border,b1=b1,b2=b2,yy=yy,xx=xx)
    _TCACHE[ctype]=d; return d

def render(p, ctype):
    seed=int(p.get("seed",5))
    T=_tpl(ctype); van=T["van"]; w,h=T["w"],T["h"]; a=T["a"]
    lum=T["lum"]; A=T["A"]; yy,xx=T["yy"],T["xx"]
    prc=PURPLE[p.get("purple","royal")]; gld=GOLD[p.get("gold","rich")]

    # ---- panel ----
    panel=p.get("panel","marble")
    tp=np.clip((lum-0.12)/0.72,0,1)
    if panel=="flat":
        base=_ramp3(tp**0.95, prc)
    else:
        wx=_fnoise(h,w,3,seed+1); wy=_fnoise(h,w,3,seed+2)
        sx=np.clip(xx+(wx-0.5)*46,0,w-1).astype(np.int32); sy=np.clip(yy+(wy-0.5)*46,0,h-1).astype(np.int32)
        marb=_fnoise(h,w,5,seed)[sy,sx]
        amt=0.35 if panel=="subtle_marble" else 0.55
        pt=np.clip(tp*(1-amt)+marb*amt,0,1)
        base=_ramp3(pt, prc)

    # ---- gold veins in panel ----
    vein=p.get("veins","edge")
    if vein!="none":
        wx=_fnoise(h,w,3,seed+11); wy=_fnoise(h,w,3,seed+12)
        sx=np.clip(xx+(wx-0.5)*46,0,w-1).astype(np.int32); sy=np.clip(yy+(wy-0.5)*46,0,h-1).astype(np.int32)
        rid=1.0-np.abs(2*_fnoise(h,w,6,seed+7)[sy,sx]-1.0)
        thr={"edge":0.80,"sparse":0.86,"veiny":0.70,"corner":0.82}.get(vein,0.80)
        rid=np.clip((rid-thr)/(1-thr),0,1)**1.4
        cx0,cy0=w*0.5,h*0.74
        dist=np.sqrt(((xx-cx0)/(w*0.42))**2+((yy-cy0)/(h*0.30))**2)
        if vein in ("edge","corner"):
            rid=rid*np.clip(dist-0.35,0,1)          # keep centre (text) clean
        goldv=np.array(gld[2])*0.9+np.array(gld[1])*0.1
        base=base*(1-rid[...,None]*0.9)+goldv*(rid[...,None]*0.9)

    # ---- rich metallic gold border ----
    border=T["border"]
    lo,hi=(np.percentile(lum[border>0.5],[8,96]) if (border>0.5).any() else (0.2,0.8))
    tg=np.clip((lum-lo)/max(1e-3,hi-lo),0,1)**0.72
    gold=_ramp3(tg, gld)
    spec=np.clip((tg-0.82)/0.18,0,1)[...,None]; gold=gold*(1-spec)+np.array([255,252,232])*spec
    gold=np.clip(gold*(1+np.clip(1-yy/h,0,1)[...,None]*0.10),0,255)
    out=base*(1-border[...,None])+gold*border[...,None]

    # ---- inner pinstripe ----
    if p.get("pinstripe",True):
        lm=np.clip(T["b1"]+T["b2"]*0.85,0,1)[...,None]
        out=out*(1-lm)+np.array([255,236,160])*lm

    out=np.clip(out,0,255).astype(np.uint8)
    res=Image.merge("RGBA",(Image.fromarray(out[...,0]),Image.fromarray(out[...,1]),Image.fromarray(out[...,2]),a))

    # ---- corner ornaments ----
    orn=p.get("ornament","scroll")
    if orn!="none":
        ov=Image.new("RGBA",(w,h),(0,0,0,0)); dr=ImageDraw.Draw(ov)
        s={"scroll":27,"scroll_big":36,"scroll_small":20}.get(orn,27)
        def scroll(cx,cy,sx,sy,sc):
            for i in range(160):
                th=i/160.0*math.pi*2.5; rr=sc*(1-i/180.0)*(0.5+0.5*math.cos(th*0.5))
                x=cx+sx*(rr*math.cos(th)+i/160.0*sc*1.5); y=cy+sy*(rr*math.sin(th))
                rad=max(1,int(3.4*(1-i/160.0))); dr.ellipse([x-rad,y-rad,x+rad,y+rad],fill=(255,226,152,245))
        m=68
        for c in [(m,m,1,1),(w-m,m,-1,1),(m,h-m,1,-1),(w-m,h-m,-1,-1)]: scroll(*c,s)
        if orn=="filigree":   # richer: add a counter-scroll per corner
            def cscroll(cx,cy,sx,sy,sc):
                for i in range(110):
                    th=i/110.0*math.pi*2.0; rr=sc*(1-i/130.0)
                    x=cx+sx*(rr*math.cos(-th)); y=cy+sy*(rr*math.sin(-th)+i/110.0*sc*1.2)
                    rad=max(1,int(2.6*(1-i/110.0))); dr.ellipse([x-rad,y-rad,x+rad,y+rad],fill=(248,214,140,230))
            for c in [(m,m,1,1),(w-m,m,-1,1),(m,h-m,1,-1),(w-m,h-m,-1,-1)]: cscroll(*c,18)
        ov=ov.filter(ImageFilter.GaussianBlur(0.5))
        oa=np.asarray(ov.split()[3]).astype(np.float32)/255.0*A; ov.putalpha(Image.fromarray((oa*255).astype(np.uint8)))
        res=Image.alpha_composite(res,ov)

    # ---- central purple gem (amethyst cabochon) — from the ref analysis ----
    gem=p.get("gem","none")
    if gem in ("top","both"):
        R=int(p.get("gem_r",26)); gx,gy=w//2,36
        gv=Image.new("RGBA",(w,h),(0,0,0,0)); gd=ImageDraw.Draw(gv)
        gd.ellipse([gx-R-5,gy-R-5,gx+R+5,gy+R+5],fill=(226,182,92,255))   # gold rim
        gd.ellipse([gx-R-2,gy-R-2,gx+R+2,gy+R+2],fill=(90,60,16,255))     # rim shadow
        arr=np.asarray(gv).astype(np.float32).copy()
        yy2,xx2=T["yy"],T["xx"]; d2=np.sqrt((xx2-gx)**2+(yy2-gy)**2); t2=np.clip(1-d2/R,0,1)
        edge=np.array([46,18,86]); mid=np.array([120,60,190]); hot=np.array([204,152,242])
        col=edge*(1-t2)[...,None]+mid*t2[...,None]
        hi=np.clip((t2-0.6)/0.4,0,1)[...,None]; col=col*(1-hi)+hot*hi
        inside=d2<=R
        arr[inside,0:3]=col[inside]; arr[inside,3]=255
        sp=((xx2-(gx-R*0.35))**2+(yy2-(gy-R*0.35))**2)<(R*0.20)**2
        arr[sp]=[255,255,255,235]
        gv=Image.fromarray(np.clip(arr,0,255).astype(np.uint8),"RGBA").filter(ImageFilter.GaussianBlur(0.4))
        res=Image.alpha_composite(res,gv)
    return res

def montage(imgs_labels, cols, out, cell=(300,300), thumb=270, bg=(232,232,238,255)):
    n=len(imgs_labels); rows=math.ceil(n/cols); cw,ch=cell
    sheet=Image.new("RGBA",(cols*cw,rows*ch),bg); d=ImageDraw.Draw(sheet)
    try: f=ImageFont.truetype("C:/Windows/Fonts/malgunbd.ttf",15)
    except: f=ImageFont.load_default()
    for i,(im,lbl) in enumerate(imgs_labels):
        r,c=divmod(i,cols); x,y=c*cw,r*ch
        t=im.copy(); t.thumbnail((thumb,ch-26)); sheet.alpha_composite(t,(x+(cw-t.width)//2,y+22+(ch-22-t.height)//2))
        d.rectangle([x,y,x+cw,y+20],fill=(30,30,40,255)); d.text((x+5,y+3),lbl,font=f,fill=(240,220,150,255))
    sheet.save(out); return out

if __name__=="__main__":
    params_json, ctype, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    mont = sys.argv[4] if len(sys.argv)>4 else None
    os.makedirs(outdir,exist_ok=True)
    specs=json.load(open(params_json,encoding="utf-8"))
    items=[]
    for vid,p in specs.items():
        im=render(p,ctype); im.save(os.path.join(outdir,f"{vid}.png")); items.append((im,vid))
    print(f"rendered {len(items)} -> {outdir}")
    if mont:
        cols=int(os.environ.get("MONT_COLS","5"))
        montage(items,cols,mont); print("montage",mont)
