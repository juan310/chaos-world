"""Two-view capture, step 2: film one move with the local video model (Wan 2.2 TI2V-5B, Apache 2.0, on this PC).

    venv python Tools/capture2v/film.py <creature dir> <run folder> <move> "<action sentence>" [--seeds 1,2] [--frames 97]
        [--steps 30]

The first frame is the creature's two views side by side (prep_blender.py renders them; the layout is anymove's: front
camera on the left, side camera on the right, both orthographic at the same scale, on a grey studio wall and floor). The
prompt pins everything but the action (anymove's prompt template), so both views move together. One generation per
seed: <run>/videos/<move>/s<seed>/clip.mp4 (24 fps, the video model's own 1280 x 704; detect.py maps it back onto the
layout), prompt.txt, sheet.jpg (every 8th frame) and film.json. Local and free: no service, no key.
"""
import argparse, json, os, sys, time
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'anymove'))
FORGE = Path(os.environ.get('FORGE3D', r'C:\AW\forge3d'))
sys.path.insert(0, str(FORGE))

PROMPT = ("Animate the exact {noun} game model shown twice in this split-screen image. Left: a front camera, the {noun} faces "
          "the viewer. Right: a side camera on its right {flank}, the {noun} faces right. Both are locked-off cameras filming the "
          "same {noun} at the same moment, so every movement happens in both views at exactly the same time, frame for frame. "
          "Keep the model exactly as it is: {describe}. Its {limbs} are colour-coded and the colours never change or swap: "
          "{colour_code}. {count} Action: {action} It stays on the same spot, centred in both views. One continuous shot at "
          "natural speed, no slow motion. The cameras never move, cut, pan or zoom; the plain grey studio background, the "
          "floor band and the dark vertical divider stay fixed.")
NEG = ("camera movement, zoom, pan, cut, scene change, the two views out of step, swapped limb colours, extra limbs, extra "
       "characters, weapon, dust, smoke, text, morphing, blurry, slow motion")


def reference(creature_dir, run):
    """the two views side by side on the studio backdrop (anymove's layout.compose)"""
    os.environ['MOCAP_CREATURE'] = str(creature_dir)
    from mocap import layout
    f = np.array(Image.open(run / 'ref' / 'ref_front.png').convert('RGBA'))
    s = np.array(Image.open(run / 'ref' / 'ref_side.png').convert('RGBA'))
    ref = layout.compose(f, s)
    Image.fromarray(ref).save(run / 'ref' / 'ref.png')
    return Image.fromarray(ref)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('creature'); ap.add_argument('run'); ap.add_argument('move'); ap.add_argument('action')
    ap.add_argument('--seeds', default='1'); ap.add_argument('--frames', type=int, default=97)
    ap.add_argument('--steps', type=int, default=30)
    a = ap.parse_args()
    creature_dir, run = Path(a.creature), Path(a.run)
    c = json.load(open(creature_dir / 'creature.json'))
    ref = reference(creature_dir, run)
    img = ref.resize((1280, 704), Image.LANCZOS)                    # the video model's own 720p frame
    prompt = PROMPT.format(noun=c['noun'], flank=c.get('flank', 'side'), describe=c['describe'], limbs=c.get('limbs', 'limbs'),
                           colour_code=c['colour_code'], count=c['count'], action=a.action.strip())
    from video2sprite import generate                               # the Forge's Wan 2.2 runner (memory-safe on 16 GB)
    # the VAE's tiling (which the runner turns on to save memory) skips Wan 2.2's patching on a frame this wide: the
    # first frame's encoding failed ('expected 12 channels, got 3'); untiled it encodes as the model expects
    import diffusers
    diffusers.AutoencoderKLWan.enable_tiling = lambda self, *a, **k: None
    import imageio
    for seed in [int(s) for s in a.seeds.split(',')]:
        out = run / 'videos' / a.move / ('s%d' % seed)
        out.mkdir(parents=True, exist_ok=True)
        (out / 'prompt.txt').write_text(prompt, encoding='utf-8')
        t0 = time.time()
        fr, peak = generate(img, prompt, NEG, a.frames, seed, a.steps)
        imageio.mimsave(out / 'clip.mp4', fr, fps=24)
        pick = fr[::8]
        w, h = 640, 352
        sheet = Image.new('RGB', (w * 3, h * ((len(pick) + 2) // 3)), (20, 20, 20))
        for i, f in enumerate(pick):
            sheet.paste(Image.fromarray(f).resize((w, h)), ((i % 3) * w, (i // 3) * h))
        sheet.save(out / 'sheet.jpg', quality=88)
        rep = {'move': a.move, 'seed': seed, 'frames': len(fr), 'fps': 24, 'steps': a.steps, 'seconds': round(time.time() - t0),
               'peak_gpu_gb': round(peak, 1), 'model': 'Wan 2.2 TI2V-5B (Apache 2.0), local', 'prompt': prompt}
        (out / 'film.json').write_text(json.dumps(rep, indent=1))
        print('CAPTURE_FILMED', out / 'clip.mp4', rep['seconds'], 's', flush=True)


if __name__ == '__main__':
    main()
