from PIL import Image
from pathlib import Path
p=Path(r'E:\My projects\TimeGuard\assets\avatar.png')
im=Image.open(p).convert('RGBA'); pix=im.load()
# The annotation marker touches the crop corner; exclude that corner entirely.
for y in range(30):
 for x in range(20):
  if x < 14 and y < 28: pix[x,y]=(0,0,0,0)
im.save(p)
