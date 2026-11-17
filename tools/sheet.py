"""Combine shot0..shotN-1.png from a directory into sheet_shots.png (2 columns, half size)."""
import os
import sys
from PIL import Image, ImageDraw
d = sys.argv[1]
n = int(sys.argv[2])
ims = [Image.open(os.path.join(d, 'shot%d.png' % i)).convert('RGB').resize((400, 300)) for i in range(n)]
rows = (n + 1) // 2
sheet = Image.new('RGB', (800, 300 * rows))
dr = ImageDraw.Draw(sheet)
for i, im in enumerate(ims):
    x, y = (i % 2) * 400, (i // 2) * 300
    sheet.paste(im, (x, y))
    dr.text((x + 4, y + 4), str(i), fill=(255, 255, 0))
sheet.save(os.path.join(d, 'sheet_shots.png'))
