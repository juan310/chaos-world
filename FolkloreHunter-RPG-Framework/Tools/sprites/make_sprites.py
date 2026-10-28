"""Makes the drawn monsters' frames in the game project (Assets/Resources/Sprites/<name>) from their scripts, on the PC that
builds the game: the frames are too large to keep in git (the troll's three views are about 55 MB), so git holds only the
scripts and the concept sheet. Runs from sync.ps1; does nothing when the scripts and the sheet have not changed since the
frames were last made (a stamp beside sprite.json).

  python Tools/sprites/make_sprites.py [--force]"""
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SPRITES = {'Troll': ([HERE / 'troll_puppet.py', HERE / 'troll_views.py', ROOT / 'Docs/Concepts/Troll/troll-turnaround.png'],
                     [['troll_puppet.py', 'game'], ['troll_views.py']])}


def main():
    for name, (inputs, steps) in SPRITES.items():
        out = ROOT / 'Assets/Resources/Sprites' / name
        h = hashlib.sha1(b''.join(p.read_bytes() for p in inputs)).hexdigest()
        stamp = out / 'made_from.txt'
        if '--force' not in sys.argv and stamp.exists() and stamp.read_text().strip() == h:
            print('SPRITES %s: up to date' % name); continue
        tmp = out.with_name(name + '_making')
        if tmp.exists(): shutil.rmtree(tmp)
        for args in steps:
            subprocess.run([sys.executable, str(HERE / args[0])] + args[1:] + [str(tmp)], check=True, cwd=str(ROOT))
        (tmp / 'made_from.txt').write_text(h)
        if out.exists(): shutil.rmtree(out)
        tmp.rename(out)
        print('SPRITES %s: made' % name)


if __name__ == '__main__':
    main()
