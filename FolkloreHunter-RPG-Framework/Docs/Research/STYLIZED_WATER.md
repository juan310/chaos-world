# Research: stylized water from noise (the owner's clip, 2026-10-28)

Source: a short breakdown video the owner shared, credited on screen "Original by yolwoocle / No AI used". It shows a
flat, cartoon-banded water surface built from noise in six steps. This note records the technique so we can make our
own version. No assets or code from it are used.

## The recipe (as shown)
1. **Scrolling 3D noise**: smooth value or simplex noise, sampled in (x, y, time) so it changes and drifts.
2. **Shift noise**: a second, sparse and darker noise used to offset (domain-warp) the first. The waves wobble and
   stretch sideways instead of sliding rigidly.
3. **Gradient**: a vertical ramp, light at the top and dark at the bottom (far to near, or shallow to deep). It is
   **combined** with the warped noise, so one end of the surface reads differently from the other.
4. **Colour ramp**: the combined value mapped through a few hard colour bands (teal, mid blue, deep blue). This is
   what gives the flat, posterized, cartoon look. Hard steps, not a smooth gradient.
5. **Highlights**: the noise thresholded very high, so only a few short white glints survive. They are **combined**
   on top of the banded colour.
6. **Perspective and scroll**: the square texture is laid on a receding plane and scrolled, so it reads as open water
   seen from above at an angle.

## How it maps onto our game (WorldForge)
- Our `WorldForge/Water` shader (`WorldForge/Runtime/Resources/WorldForge/Water.shader`) is realistic, dark and glossy:
  normal-map ripples, rain rings and a soft edge from scene depth. It suits cave puddles and the Warden's lair.
- A stylized variant would be a second shader, for example `WorldForge/WaterBanded`, or a `_Style` switch on the same
  shader, for lakes, rivers and the coast:
  - world-space xz in place of a texture, so perspective comes from the 3D camera itself (step 6 is free);
  - 3D noise (xz, time), domain-warped by a second noise layer;
  - a **depth term in place of the screen gradient**: the scene depth under the surface (we already read
    `_CameraDepthTexture` for the soft edge) gives shallow-to-deep banding, with shore foam where it is shallowest;
  - a 3- or 4-band ramp, set as colours in the WorldForge recipe so each area picks its own palette;
  - thresholded glints, optionally lit only on the sun's side.
- It must stay readable next to our realistic characters and terrain. Mixing art styles is the main risk, so it would
  need a SightForge look test before use.

## Status
Research only. Not built. A candidate for the WorldForge water task, if the owner wants the stylized look somewhere.
