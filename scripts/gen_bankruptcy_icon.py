"""Procedurally draw the 파산 (Bankruptcy) power icon: a gold coin with a red prohibition ring + slash
("no income"). Shapes render cleanly in PIL where the AI icon gen kept drifting. 256x256 RGBA."""
import math, sys
from PIL import Image, ImageDraw

S = 512
im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
cx = cy = S // 2

# gold coin
R = int(S * 0.30)
d.ellipse([cx-R-8, cy-R-8, cx+R+8, cy+R+8], fill=(30, 22, 8, 255))          # dark outline
for rr, col in [(R, (196, 148, 40, 255)), (int(R*0.86), (240, 196, 70, 255)), (int(R*0.6), (255, 224, 120, 255))]:
    d.ellipse([cx-rr, cy-rr, cx+rr, cy+rr], fill=col)
d.ellipse([cx-int(R*0.72), cy-int(R*0.72), cx+int(R*0.72), cy+int(R*0.72)], outline=(150, 110, 30, 255), width=6)
d.ellipse([cx-int(R*0.16), cy-int(R*0.16), cx+int(R*0.16), cy+int(R*0.16)], outline=(150, 110, 30, 255), width=7)

# red prohibition ring
PR = int(S * 0.40)
ring_w = int(S * 0.055)
d.ellipse([cx-PR-7, cy-PR-7, cx+PR+7, cy+PR+7], outline=(40, 0, 0, 255), width=6)
d.ellipse([cx-PR, cy-PR, cx+PR, cy+PR], outline=(225, 35, 35, 255), width=ring_w)
inr = PR - ring_w
d.ellipse([cx-inr, cy-inr, cx+inr, cy+inr], outline=(40, 0, 0, 255), width=6)

# diagonal slash
ang = math.radians(45)
dx, dy = math.cos(ang), math.sin(ang)
x1, y1 = cx - dx*PR, cy - dy*PR
x2, y2 = cx + dx*PR, cy + dy*PR
for w, col in [(int(S*0.075)+12, (40, 0, 0, 255)), (int(S*0.075), (225, 35, 35, 255))]:
    d.line([x1, y1, x2, y2], fill=col, width=w)

im = im.resize((256, 256), Image.LANCZOS)
out = sys.argv[1]
im.save(out)
print('saved', out, im.size)
