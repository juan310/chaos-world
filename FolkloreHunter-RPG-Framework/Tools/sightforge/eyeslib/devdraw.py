"""Pictures for the development tools (Studio 0.17), with Pillow only: a world view fitted to what it shows (top-down or
from the side), capsules and balls, labels, and simple line charts with event marks."""
from PIL import Image, ImageDraw, ImageFont

BG = (24, 25, 28)
GRID = (44, 46, 52)
TEXT = (220, 222, 228)
PALETTE = [(90, 170, 255), (255, 170, 60), (120, 220, 120), (240, 90, 200), (250, 230, 90), (110, 230, 230), (255, 110, 110), (180, 140, 255)]


def font(size=13):
    for name in ("segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


class View:
    """A world view: plane 'top' (x right, z up the picture) or 'side' (the horizontal axis given by `along`, y up)."""

    def __init__(self, points, plane="top", size=(560, 560), margin=40, along=(1.0, 0.0)):
        self.plane, self.size, self.margin = plane, size, margin
        ax, az = along
        n = (ax * ax + az * az) ** .5 or 1
        self.along = (ax / n, az / n)
        uv = [self.uv(p) for p in points] or [(0, 0)]
        us, vs = [u for u, _ in uv], [v for _, v in uv]
        self.u0, self.u1, self.v0, self.v1 = min(us), max(us), min(vs), max(vs)
        span = max(self.u1 - self.u0, self.v1 - self.v0, .5)
        cu, cv = (self.u0 + self.u1) / 2, (self.v0 + self.v1) / 2
        self.u0, self.u1, self.v0, self.v1 = cu - span / 2, cu + span / 2, cv - span / 2, cv + span / 2
        self.scale = min(size[0] - 2 * margin, size[1] - 2 * margin) / span
        self.img = Image.new("RGB", size, BG)
        self.d = ImageDraw.Draw(self.img)
        self.f = font(12)
        self.grid()

    def uv(self, p):
        if self.plane == "top":
            return p[0], p[2]
        return p[0] * self.along[0] + p[2] * self.along[1], p[1]

    def px(self, p):
        u, v = self.uv(p)
        return (self.margin + (u - self.u0) * self.scale, self.size[1] - self.margin - (v - self.v0) * self.scale)

    def grid(self, step=None):
        span = self.u1 - self.u0
        step = step or (0.5 if span < 6 else 1 if span < 15 else 5 if span < 60 else 10)
        k = int(self.u0 // step)
        while k * step <= self.u1:
            x = self.margin + (k * step - self.u0) * self.scale
            self.d.line([(x, 0), (x, self.size[1])], fill=GRID)
            k += 1
        k = int(self.v0 // step)
        while k * step <= self.v1:
            y = self.size[1] - self.margin - (k * step - self.v0) * self.scale
            self.d.line([(0, y), (self.size[0], y)], fill=GRID)
            k += 1
        self.d.text((6, self.size[1] - 18), "grid %g m, %s view" % (step, self.plane), fill=(140, 140, 150), font=self.f)

    def capsule(self, a, b, r, color, width=2, fill=None):
        pa, pb = self.px(a), self.px(b)
        rr = max(1.5, r * self.scale)
        for p in (pa, pb):
            self.d.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], outline=color, width=width, fill=fill)
        if (pa[0] - pb[0]) ** 2 + (pa[1] - pb[1]) ** 2 > 1:
            self.d.line([pa, pb], fill=color, width=max(1, int(rr * 2)) if fill else width)

    def dot(self, p, color, r=3):
        q = self.px(p)
        self.d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=color)

    def line(self, a, b, color, width=1):
        self.d.line([self.px(a), self.px(b)], fill=color, width=width)

    def label(self, p, text, color=TEXT):
        q = self.px(p)
        self.d.text((q[0] + 5, q[1] - 7), text, fill=color, font=self.f)

    def title(self, text, line=0):
        self.d.text((8, 6 + line * 16), text, fill=TEXT, font=font(13))

    def save(self, path):
        self.img.save(path)
        return path


def chart(path, series, events=(), title="", size=(900, 300), ylabel="", hlines=()):
    """series: [(name, [(t, v)], color)]; events: [(t, short text)] drawn as ticks; hlines: [(value, text)]."""
    img = Image.new("RGB", size, BG); d = ImageDraw.Draw(img); f = font(11)
    pts = [p for _, s, _ in series for p in s]
    if not pts:
        d.text((10, 10), title + " (no data)", fill=TEXT, font=font(13)); img.save(path); return path
    t0, t1 = min(p[0] for p in pts), max(p[0] for p in pts)
    v1 = max(max(p[1] for p in pts), max([h for h, _ in hlines] or [0])) * 1.08 or 1
    L, R, T, B = 52, 12, 28, 26
    W, H = size[0] - L - R, size[1] - T - B
    X = lambda t: L + (t - t0) / max(1e-9, t1 - t0) * W
    Y = lambda v: T + H - min(v, v1) / v1 * H
    for k in range(5):
        v = v1 * k / 4; y = Y(v)
        d.line([(L, y), (L + W, y)], fill=GRID); d.text((4, y - 7), "%.0f" % v if v1 > 10 else "%.2f" % v, fill=(150, 150, 160), font=f)
    for v, text in hlines:
        y = Y(v); d.line([(L, y), (L + W, y)], fill=(170, 80, 80)); d.text((L + W - 160, y - 13), text, fill=(220, 120, 120), font=f)
    for t, text in events:
        x = X(t); d.line([(x, T + H), (x, T + H + 6)], fill=(200, 200, 120))
    for name, s, color in series:
        if len(s) > 1:
            d.line([(X(t), Y(v)) for t, v in s], fill=color, width=1)
    d.text((L, 6), title, fill=TEXT, font=font(13))
    x = L + 8 + d.textlength(title, font=font(13)) + 20
    for name, _, color in series:
        d.rectangle([x, 10, x + 10, 20], fill=color); d.text((x + 14, 7), name, fill=TEXT, font=f); x += 24 + d.textlength(name, font=f)
    d.text((L, size[1] - 18), "%.1f s" % t0, fill=(150, 150, 160), font=f)
    d.text((L + W - 50, size[1] - 18), "%.1f s" % t1, fill=(150, 150, 160), font=f)
    if ylabel:
        d.text((L + W / 2 - 40, size[1] - 18), ylabel, fill=(150, 150, 160), font=f)
    img.save(path)
    return path
