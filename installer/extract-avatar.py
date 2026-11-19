from PIL import Image, ImageDraw
from pathlib import Path
source=Path(r'C:\Users\LEGION-PC\Downloads\ChatGPT Image Sep 12, 2026, 11_59_41 AM.png')
r=Path(r'E:\My projects\TimeGuard\assets')
im=Image.open(source).convert('RGBA')
avatar=im.crop((84,205,209,330))
mask=Image.new('L',(500,500)); ImageDraw.Draw(mask).ellipse((0,0,499,499),fill=255)
avatar.putalpha(mask.resize((125,125),Image.Resampling.LANCZOS))
avatar.save(r/'avatar.png')
