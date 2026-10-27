"""Originality scan (Studio 0.17): player-facing text checked against other games' names, so nothing a player sees borrows
a trademark or a signature name (the owner's rule: "we don't want to get sued").

What a player sees: the display fields of data files ("name", "title", "text", "description", "label", "line" ... in
JSON), and string literals in game code that reach the screen (Text/Note/Notice/Flash/Toast/Message/Show/Label/Title/
LastEvent calls, and assignments to .text). Code identifiers, comments and internal ids are not player-facing and are
not reported. Each hit is a finding: "confirmed" for a trademark or game title, "likely" for a signature move, item or
creature name of that game, "uncertain" for a common word that the game also uses (the owner decides).

The term lists are a starting point, not a legal opinion; add to them in the project's originality.json:
  {"terms": {"confirmed": [...], "likely": [...], "uncertain": [...]}, "allow": ["<exact text that is fine>"],
   "data": ["<glob of data files>"], "code": ["<glob of code files>"]}
"""
import fnmatch
import glob
import json
import os
import re

from .findings import finding

TERMS = {
    # trademarks and titles
    "confirmed": ["Monster Hunter", "Capcom", "Felyne", "Palico", "Palamute", "Rathalos", "Rathian", "Nargacuga", "Zinogre",
                  "Tigrex", "Diablos", "Rajang", "Magnamalo", "Malzeno", "Gore Magala", "Fatalis", "Nergigante", "Kirin",
                  "Wirebug", "Silkbind", "Kinsect", "Hunter Rank", "Master Rank", "Guild Card", "Astera", "Kamura", "Elgado",
                  "Seliana", "Canteen", "Meowscular", "Poogie"],
    # signature move, item and system names of that series
    "likely": ["Spirit Roundslash", "Spirit Helm Breaker", "Helm Breaker", "Spirit Blade", "Spirit Gauge", "Iai Spirit Slash",
               "Iai Slash", "Foresight Slash", "Serene Pose", "Sacred Sheathe", "True Charged Slash", "Perfect Rush",
               "Dragon Piercer", "Power Shot", "Arc Shot", "Charging Sidestep", "Tracer Arrow", "Focus Mode", "Wound Destroyer",
               "Mounting", "Wyvern Riding", "Clutch Claw", "Slinger", "Power Clash", "Counter Charge", "Guard Point",
               "Mega Potion", "Max Potion", "Well-done Steak", "Hot Drink", "Cool Drink", "Pitfall Trap", "Shock Trap",
               "Tranq Bomb", "Flash Pod", "Dango", "Bunny Dango", "Wyverian", "Elder Dragon", "Hunter's Notes"],
    # ordinary words the series also uses: fine in general, the owner decides
    "uncertain": ["Charge Blade", "Switch Axe", "Insect Glaive", "Hunting Horn", "Heavy Bowgun", "Light Bowgun", "Gunlance",
                  "Dual Blades", "Great Sword", "Long Sword", "Sword and Shield", "Roundslash", "Topple", "Wound", "Tenderize"],
}
DISPLAY_KEYS = {"name", "title", "text", "description", "desc", "label", "line", "lines", "caption", "subtitle", "hint",
                "tooltip", "message", "prompt", "notice", "flash", "tag", "tagline", "greeting", "farewell", "summary"}
CODE_CALLS = re.compile(r'\b(?:Text|Note|Notice|Flash|Toast|Message|Show|Label|Title|Hint|Prompt|Say|Banner|Log)\s*\(\s*(\$?@?"(?:[^"\\]|\\.)*")')
CODE_TEXT = re.compile(r'(?:\.text|LastEvent|Caption|Title|Hint)\s*=\s*(\$?@?"(?:[^"\\]|\\.)*")')
CODE_NAME = re.compile(r'\bname\s*[:=]\s*(\$?@?"(?:[^"\\]|\\.)*")')


def _terms(cfg):
    t = {k: list(v) for k, v in TERMS.items()}
    for k, v in (cfg.get("terms") or {}).items():
        t.setdefault(k, []).extend(v)
    return {k: [(w, re.compile(r"(?<![A-Za-z])" + re.escape(w).replace(r"\ ", r"[\s_-]*") + r"(?![A-Za-z])", re.I)) for w in v] for k, v in t.items()}


def _strings_json(obj, path=""):
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, str) and k.lower() in DISPLAY_KEYS:
                yield path + "." + k, v
            elif isinstance(v, list) and k.lower() in DISPLAY_KEYS:
                for i, x in enumerate(v):
                    if isinstance(x, str): yield "%s.%s[%d]" % (path, k, i), x
            else:
                yield from _strings_json(v, path + "." + k)
    elif isinstance(obj, list):
        for i, v in enumerate(obj):
            yield from _strings_json(v, "%s[%d]" % (path, i))


def scan(root, cfg=None):
    cfg = cfg or {}
    terms = _terms(cfg); allow = set(cfg.get("allow") or [])
    data = cfg.get("data") or ["Assets/**/Resources/**/*.json", "Assets/**/Data/**/*.json"]
    code = cfg.get("code") or ["Assets/**/*.cs"]
    skip = cfg.get("skip") or ["*/Editor/*", "*Verifier*.cs", "*Tests*", "*/Tests/*"]
    hits = []

    def check(where, text):
        if text in allow: return
        for kind in ("confirmed", "likely", "uncertain"):
            for word, rx in terms.get(kind, []):
                if rx.search(text):
                    hits.append({"kind": kind, "term": word, "where": where, "text": text[:160]})
                    return

    seen = set()
    for pat in data:
        for f in glob.glob(os.path.join(root, pat), recursive=True):
            if f in seen or any(fnmatch.fnmatch(f.replace("\\", "/"), s) for s in skip): continue
            seen.add(f)
            try:
                obj = json.load(open(f, encoding="utf-8"))
            except (OSError, ValueError):
                continue
            for path, s in _strings_json(obj):
                check("%s %s" % (os.path.relpath(f, root), path), s)
    for pat in code:
        for f in glob.glob(os.path.join(root, pat), recursive=True):
            if f in seen or any(fnmatch.fnmatch(f.replace("\\", "/"), s) for s in skip): continue
            seen.add(f)
            try:
                lines = open(f, encoding="utf-8", errors="replace").read().splitlines()
            except OSError:
                continue
            for n, line in enumerate(lines, 1):
                if line.strip().startswith("//"): continue
                for rx in (CODE_CALLS, CODE_TEXT, CODE_NAME):
                    for m in rx.finditer(line):
                        check("%s:%d" % (os.path.relpath(f, root), n), m.group(1).strip('$@"'))
    return hits


def register(sp, ctx):
    p = sp.add_parser("originality", help="player-facing text against other games' names")
    p.add_argument("--config", help="originality.json (extra terms, allowed texts, file globs)")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    cfg = {}
    path = a.config or os.path.join(prj.root, "Tools", "sightforge", "originality.json")
    if os.path.isfile(path):
        cfg = json.load(open(path, encoding="utf-8"))
    hits = scan(prj.root, cfg)
    out = ctx["run_dir"](a.run, "originality", prj)
    res = [finding(h["kind"], "another game's name in player-facing text", h["where"], "\"%s\" in: %s" % (h["term"], h["text"]),
                   fix="use this game's own name (and add it to the allow list if it is ours)") for h in hits]
    by = {}
    for h in hits: by.setdefault(h["term"], []).append(h["where"])
    lines = ["%-26s %3d  %s" % (t, len(w), "; ".join(w[:4])) for t, w in sorted(by.items(), key=lambda kv: -len(kv[1]))]
    page, data = ctx["report"].write(out, "Originality: %d hits in player-facing text" % len(hits), res,
                                     sections=[("Terms found (count, first places)", [], "\n".join(lines) or "none")],
                                     meta={"root": prj.root, "terms": {k: len(v) for k, v in TERMS.items()}})
    return page, data
