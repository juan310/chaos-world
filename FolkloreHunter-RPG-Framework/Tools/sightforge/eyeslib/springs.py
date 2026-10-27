"""Spring bone check (Studio 0.17): the follow-through of loose parts (tails, ears, capes) a game recorded frame by frame
(the Folklore Hunter game's SpringBone lag, in perf.json: degrees between each spring bone and where its clip puts it).

  dead       never trails (under 2 degrees the whole run): the spring is too stiff or is not running
  pinned     held at its angle limit a large share of the time: too loose, or the limit is too small
  jitter     the lag reverses direction frame after frame (a buzz instead of a swing): too little damping, or a fight
             with another script moving the same bone
  restless   stays well off its pose for seconds on end: it never settles
A chart per spring shows its lag over the run with the hunter's moves marked."""
import json
import os

from .findings import finding
from . import devdraw as D


def load(path):
    if os.path.isdir(path):
        path = os.path.join(path, "perf.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh), path


def analyse(data):
    heads = data.get("springs", [])
    heads = [h if isinstance(h, dict) else {"label": h, "max": None} for h in heads]
    fr = [f for f in data.get("frames", []) if f.get("springs")]
    out = []
    for i, h in enumerate(heads):
        s = [(f["t"], f["springs"][i]) for f in fr if i < len(f["springs"])]
        if not s:
            continue
        lag = [v for _, v in s]
        mx = h.get("max") or max(lag)
        pinned = sum(1 for v in lag if v >= mx - .5) / len(lag)
        d = [b - a for a, b in zip(lag, lag[1:])]
        flips = sum(1 for a, b in zip(d, d[1:]) if a * b < 0 and abs(a) > .8 and abs(b) > .8)
        jitter = flips / max(1, len(d) - 1)
        longest, run, t0 = 0.0, None, None
        for t, v in s:
            if v > max(5.0, mx * .35):
                if run is None: run = t
                longest = max(longest, t - run)
            else:
                run = None
        out.append({"label": h["label"], "max": mx, "peak": max(lag), "mean": sum(lag) / len(lag), "pinned": pinned, "jitter": jitter,
                    "restless": longest, "series": s})
    return out


def findings(rs):
    out = []
    for r in rs:
        if r["peak"] < 2:
            out.append(finding("likely", "spring never trails", r["label"], "its lag never passed %.1f degrees" % r["peak"], measured=r["peak"], threshold=2,
                               fix="lower the stiffness, or check the spring runs after the animation"))
        if r["pinned"] > .2:
            out.append(finding("likely", "spring pinned at its limit", r["label"], "%.0f %% of frames at its %.0f degree limit" % (r["pinned"] * 100, r["max"]),
                               measured=r["pinned"], threshold=.2, fix="raise the stiffness or the angle limit"))
        if r["jitter"] > .12:
            out.append(finding("likely", "spring jitter", r["label"], "the lag reverses direction on %.0f %% of frames (a buzz, not a swing)" % (r["jitter"] * 100),
                               measured=r["jitter"], threshold=.12, fix="more damping; make sure no other script turns the same bone"))
        if r["restless"] > 3:
            out.append(finding("uncertain", "spring never settles", r["label"], "it stayed well off its pose for %.1f s on end" % r["restless"],
                               measured=r["restless"], threshold=3, fix="more damping or stiffness"))
    return out


def charts(data, rs, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    ev = [(e["t"], e["kind"]) for e in data.get("events", []) if e.get("kind") in ("move", "clip")]
    pics = []
    for k in range(0, len(rs), 4):
        group = rs[k:k + 4]
        pics.append(D.chart(os.path.join(out_dir, "springs_%d.png" % (k // 4)), [(r["label"], r["series"], D.PALETTE[j % len(D.PALETTE)]) for j, r in enumerate(group)], ev,
                            "Spring lag (degrees off the clip's pose)", hlines=[(group[0]["max"], "limit")] if group else ()))
    return pics


def text(rs):
    lines = ["%-32s %6s %6s %6s %8s %7s %9s" % ("spring", "limit", "peak", "mean", "pinned", "jitter", "restless")]
    for r in rs:
        lines.append("%-32s %6.0f %6.1f %6.1f %7.0f%% %6.0f%% %8.1fs" % (r["label"][:32], r["max"], r["peak"], r["mean"], r["pinned"] * 100, r["jitter"] * 100, r["restless"]))
    return "\n".join(lines)


def register(sp, ctx):
    p = sp.add_parser("springs", help="spring bones (tails, ears, capes): never trailing, pinned, jitter, never settling")
    p.add_argument("capture", help="a perf.json with spring lag, or a folder with one")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    data, path = load(a.capture)
    rs = analyse(data)
    out = ctx["run_dir"](a.run, "springs", prj)
    res = findings(rs) if rs else [finding("not_completed", "spring bones", os.path.basename(path), "no spring lag in this record")]
    page, rep = ctx["report"].write(out, "Springs: %d spring bones" % len(rs), res,
                                    sections=[("Per spring", [], text(rs)), ("Lag over the run", charts(data, rs, os.path.join(out, "evidence")), None)],
                                    meta={"source": os.path.abspath(path)})
    return page, rep
