from PIL import Image
from collections import deque
from pathlib import Path
r=Path(r'E:\My projects\TimeGuard\assets')
im=Image.open(r/'app.png').convert('RGBA'); im.save(r/'app-original.png') if not (r/'app-original.png').exists() else None
w,h=im.size; p=im.load(); seen=set(); q=deque()
# Remove only neutral pixels connected to the outside, preserving white clock/check inside shield.
for x in range(w): q.append((x,0)); q.append((x,h-1))
for y in range(h): q.append((0,y)); q.append((w-1,y))
while q:
 x,y=q.popleft()
 if (x,y) in seen: continue
 seen.add((x,y)); a=p[x,y]; rgb=a[:3]
 if max(rgb)-min(rgb)>45: continue
 p[x,y]=(a[0],a[1],a[2],0)
 for nx,ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
  if 0<=nx<w and 0<=ny<h and (nx,ny) not in seen:q.append((nx,ny))
im.save(r/'app.png'); im.save(r/'app.ico',sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.thumbnail((400,400)); bg=Image.new('RGBA',im.size,'#ffffff'); bg.alpha_composite(im); bg.convert('RGB').save(r/'icon-preview.jpg')
