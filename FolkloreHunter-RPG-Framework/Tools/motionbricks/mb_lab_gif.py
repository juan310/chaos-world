# Joins mb_lab_view.py's frames into one moving picture per clip: a column per setting (ours first), labelled, as an
# animated GIF and a contact sheet of every 4th frame.
#   python mb_lab_gif.py <view folder>
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
rep = json.load(open(root / 'view.json'))
for name, r in rep.items():
    d = root / name
    cols = r['columns']
    n = r['frames']
    out = []
    for i in range(n):
        ims = [Image.open(d / ('c%d_f%03d.png' % (c, i))).convert('RGB') for c in range(len(cols))]
        w, h = ims[0].size
        f = Image.new('RGB', (w * len(ims), h + 26), (24, 24, 28))
        for c, im in enumerate(ims):
            f.paste(im, (c * w, 26))
            ImageDraw.Draw(f).text((c * w + 8, 6), cols[c], fill=(235, 235, 235))
        out.append(f.resize((f.width // 2, f.height // 2)))
    out[0].save(root / (name + '.gif'), save_all=True, append_images=out[1:], duration=33, loop=0)
    pick = out[::4]
    sheet = Image.new('RGB', (pick[0].width, pick[0].height * len(pick)))
    for i, f in enumerate(pick): sheet.paste(f, (0, i * f.height))
    sheet.save(root / (name + '_sheet.jpg'), quality=85)
    print('GIF', name, len(out), 'frames', cols, flush=True)
