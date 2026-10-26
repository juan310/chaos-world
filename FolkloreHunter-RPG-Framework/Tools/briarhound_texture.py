"""Step 2 of the Briarhound build: the studio texture is sharp only on the side the concept art shows (about 30% of what
can be seen); the far side was guessed and comes out smeared. A wolf-dog is close to mirror-symmetric, so each texel on
the far side takes its colour from the mirrored point on the picture side (same height, same place along the body,
other flank), when that point lies on the surface and faces the mirrored way. Texels without a good match (the blade's
back, the turned head's far cheek) keep the studio's colours. Writes Assets/Resources/Monsters/Briarhound_basecolor.png.
  python Tools/briarhound_texture.py"""
from pathlib import Path
import json
import numpy as np, cv2
from scipy.spatial import cKDTree

r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
work = r / 'SourceArt/Briarhound'
A = np.load(work / 'briarhound_arrays.npz')
V, F, UV = A['V'], A['F'], A['UV']
tex = cv2.imread(str(work / 'forge_texture.png'), cv2.IMREAD_COLOR)
S = tex.shape[0]
H = np.ptp(V[:, 2])

# Which triangle covers each texel (UV v runs up, image rows run down).
tri = np.full((S, S), -1, np.int32)
P = UV * S; P[..., 1] = S - P[..., 1]
for i, t in enumerate(P):
    cv2.fillConvexPoly(tri, np.round(t * 4).astype(np.int32), int(i), lineType=cv2.LINE_8, shift=2)
ys, xs = np.nonzero(tri >= 0); ids = tri[ys, xs]
# Barycentric position of each covered texel centre -> 3D point and face normal.
a, b, c = P[ids, 0], P[ids, 1], P[ids, 2]
p = np.stack([xs + .5, ys + .5], 1)
v0, v1, v2 = b - a, c - a, p - a
d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1); d20 = (v2 * v0).sum(1); d21 = (v2 * v1).sum(1)
den = d00 * d11 - d01 * d01; den[np.abs(den) < 1e-12] = 1e-12
w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den; w0 = 1 - w1 - w2
T = V[F[ids]]
X = T[:, 0] * w0[:, None] + T[:, 1] * w1[:, None] + T[:, 2] * w2[:, None]
N = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); N /= np.maximum(1e-12, np.linalg.norm(N, axis=1))[:, None]

# The picture side is the flank whose texels carry the detail (sharp from the art, not smeared).
gray = cv2.cvtColor(tex, cv2.COLOR_BGR2GRAY).astype(np.float32)
detail = cv2.GaussianBlur(np.abs(cv2.Laplacian(gray, cv2.CV_32F, ksize=3)), (0, 0), 3)[ys, xs]
flank = np.abs(N[:, 0]) > .5
sharp_pos = detail[flank & (N[:, 0] > 0)].mean(); sharp_neg = detail[flank & (N[:, 0] < 0)].mean()
sign = 1 if sharp_pos > sharp_neg else -1

src = (sign * N[:, 0] > .15) & (sign * X[:, 0] > -.02 * H)
dst = (sign * N[:, 0] < -.05) & (sign * X[:, 0] < .02 * H)
tree = cKDTree(X[src])
M = X[dst] * [-1, 1, 1]
dist, nb = tree.query(M, k=4)
srcN = N[src]; srcCol = tex[ys[src], xs[src]].astype(np.float32)
mirroredN = N[dst] * [-1, 1, 1]
agree = (srcN[nb] * mirroredN[:, None, :]).sum(2)            # the matched surface must face the mirrored way
wt = np.exp(-dist / (.004 * H)) * np.clip(agree, 0, 1) * (dist < .02 * H)
ok = wt.sum(1) > 1e-3
col = (srcCol[nb] * wt[..., None]).sum(1) / np.maximum(1e-6, wt.sum(1))[:, None]
# Feather toward the back and belly midline so the two flanks meet without a seam.
feather = np.clip((-sign * N[dst, 0] - .05) / .35, 0, 1)
out = tex.copy().astype(np.float32)
ty, tx = ys[dst][ok], xs[dst][ok]
f = feather[ok][:, None]
out[ty, tx] = out[ty, tx] * (1 - f) + col[ok] * f
out = np.clip(out, 0, 255).astype(np.uint8)

# Fill the UV gutters from the nearest covered texel so mip levels do not bleed grey into the seams.
mask = (tri >= 0).astype(np.uint8)
inv = 1 - mask
_, lab = cv2.distanceTransformWithLabels(inv, cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
cov_y, cov_x = np.nonzero(mask); order = np.zeros(lab.max() + 1, np.int64)
zero_y, zero_x = np.nonzero(inv == 0)
order[lab[zero_y, zero_x]] = np.arange(len(zero_y))
gy, gx = np.nonzero(inv)
nearest = order[lab[gy, gx]]
out[gy, gx] = out[zero_y[nearest], zero_x[nearest]]

dest = r / 'Assets/Resources/Monsters'; dest.mkdir(parents=True, exist_ok=True)
cv2.imwrite(str(dest / 'Briarhound_basecolor.png'), out)
cv2.imwrite(str(work / 'texture_mirrored_preview.png'), cv2.resize(np.hstack([tex, out]), (2048, 1024)))
rep = {'pictureSide': '+X (animal left)' if sign > 0 else '-X (animal right)', 'detailPictureSide': float(max(sharp_pos, sharp_neg)),
       'detailGuessedSide': float(min(sharp_pos, sharp_neg)), 'texelsCovered': int(len(ids)), 'farSideTexels': int(dst.sum()),
       'mirrored': int(ok.sum()), 'mirroredShare': float(ok.sum() / max(1, dst.sum()))}
(work / 'texture.json').write_text(json.dumps(rep, indent=2))
print('BRIARHOUND_TEXTURE', json.dumps(rep), flush=True)
