"""Moves every top-level MonoBehaviour class into its own file named after the class.
Unity only serializes MonoBehaviours whose file name matches the class name."""
import re, sys, pathlib

root = pathlib.Path(sys.argv[1])
files = list(root.rglob('*.cs'))
text = {f: f.read_text() for f in files}
mb = {'MonoBehaviour'}
# Resolve inheritance chains (e.g. NpcActor : Interactable : MonoBehaviour).
decl = re.compile(r'^    (?:public |internal )?(?:abstract |sealed )?class (\w+)\s*:\s*(\w+)', re.M)
changed = True
while changed:
    changed = False
    for t in text.values():
        for name, base in decl.findall(t):
            if base in mb and name not in mb:
                mb.add(name); changed = True

def class_spans(t):
    # top-level classes inside a namespace are indented 4 spaces; include preceding attribute/doc lines
    for m in decl.finditer(t):
        name = m.group(1)
        start = m.start()
        # walk back over doc comments / attributes
        lines_before = t[:start].split('\n')
        k = len(lines_before) - 1
        while k > 0 and re.match(r'^    (///|\[)', lines_before[k - 1]):
            k -= 1
        start = len('\n'.join(lines_before[:k])) + (1 if k > 0 else 0)
        i = t.index('{', m.end())
        depth = 0
        for j in range(i, len(t)):
            if t[j] == '{': depth += 1
            elif t[j] == '}':
                depth -= 1
                if depth == 0:
                    end = j + 1
                    while end < len(t) and t[end] == '\n': end += 1
                    yield name, start, end
                    break

for f, t in text.items():
    spans = [s for s in class_spans(t) if s[0] in mb and s[0] != f.stem]
    if not spans: continue
    header = t[:t.index('namespace')]
    ns = re.search(r'namespace ([\w.]+)', t).group(1)
    for name, a, b in sorted(spans, key=lambda s: -s[1]):
        body = t[a:b].rstrip('\n')
        out = f.parent / (name + '.cs')
        out.write_text(header + 'namespace ' + ns + '\n{\n' + body + '\n}\n')
        t = t[:a] + t[b:]
        print('moved', name, '->', out.name)
    f.write_text(t)
