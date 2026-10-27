# Pictures of a hands_dump.py file: each hand's vertices from the back of the hand (x across, y along) and from the
# thumb side (z through, y along), coloured by mesh island, edges drawn.
#   python Tools/slim/hands_plot.py <dump.json> <out folder>
import json, sys, os
from PIL import Image, ImageDraw
d = json.load(open(sys.argv[1])); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
COL = [(230, 80, 80), (80, 200, 90), (80, 140, 240), (240, 200, 60), (200, 90, 220), (60, 210, 210), (250, 140, 50), (160, 160, 160), (255, 255, 255), (120, 80, 40)]
for side, h in d.items():
    V = {int(k): v for k, v in h["verts"].items()}
    for view, (a, b) in (("back", (0, 1)), ("side", (2, 1))):
        xs = [v[a] for v in V.values()]; ys = [v[b] for v in V.values()]
        lo_x, hi_x, lo_y, hi_y = min(xs), max(xs), min(ys), max(ys)
        S = 900 / max(hi_x - lo_x, hi_y - lo_y); W = int((hi_x - lo_x) * S) + 60; H = int((hi_y - lo_y) * S) + 60
        im = Image.new("RGB", (W, H), (18, 18, 22)); dr = ImageDraw.Draw(im)
        P = lambda v: (30 + (v[a] - lo_x) * S, H - 30 - (v[b] - lo_y) * S)
        for e in h["edges"]:
            dr.line([P(V[e[0]]), P(V[e[1]])], fill=(70, 70, 80), width=1)
        for i, v in V.items():
            x, y = P(v); c = COL[v[3] % len(COL)]
            dr.ellipse([x - 3, y - 3, x + 3, y + 3], fill=c)
        dr.text((8, 8), "hand.%s %s view: %d verts, %d islands; 1 cm = %.0f px" % (side, view, len(V), h["islands"], S / 100), fill=(255, 255, 255))
        im.save(os.path.join(out, "hand_%s_%s.png" % (side, view)))
print("plotted")
