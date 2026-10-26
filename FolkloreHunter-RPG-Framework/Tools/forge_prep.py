"""Small picture steps around the Forge jobs (Windows, Studio's Python with Pillow).

  python Tools/forge_prep.py pad <in.png> <out.png> [margin share]      border of the picture's own background colour
  python Tools/forge_prep.py cutout <in.png> <out.png>                  near-white background -> transparent (item pictures)
"""
import sys
from PIL import Image, ImageFilter


def pad(src, dst, margin=.14):
    im = Image.open(src).convert('RGB')
    w, h = im.size
    corners = [im.getpixel((2, 2)), im.getpixel((w - 3, 2)), im.getpixel((2, h - 3)), im.getpixel((w - 3, h - 3))]
    bg = tuple(sorted(c[k] for c in corners)[1] for k in range(3))
    m = int(max(w, h) * margin)
    out = Image.new('RGB', (w + 2 * m, h + 2 * m), bg)
    out.paste(im, (m, m))
    out.save(dst)


def cutout(src, dst):
    im = Image.open(src).convert('RGBA')
    px = im.load()
    w, h = im.size
    alpha = Image.new('L', (w, h), 255)
    a = alpha.load()
    for y in range(h):
        for x in range(w):
            r, g, b, _ = px[x, y]
            if r > 232 and g > 232 and b > 232:
                a[x, y] = 0
    alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(.8))
    im.putalpha(alpha)
    bbox = alpha.getbbox()
    (im.crop(bbox) if bbox else im).save(dst)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'pad':
        pad(sys.argv[2], sys.argv[3], float(sys.argv[4]) if len(sys.argv) > 4 else .14)
    elif cmd == 'cutout':
        cutout(sys.argv[2], sys.argv[3])
    print('FORGE_PREP', cmd, sys.argv[3], flush=True)
