# Opening v2: the street, the truck, the glitch (design)

Status: approved by the owner on 2026-10-01 (cab-over box truck). Replaces GDD §8 beat 1 "UI and audio only".

## 1. What changes

**Scene 1, the walk (0–16 s of the opening clock).** The 2D phone on black becomes a first-person 3D street. The
existing lock-screen UI stays as it is, now shown on the screen of a 3D phone held in the walker's right hand (ochre
hoodie sleeve). White wired earphones run from the phone's jack (simulated cable). A real road with a zebra crosswalk,
pedestrian and traffic signals, shopfronts and street furniture. A modelled cab-over box truck replaces the glow
sprites. The audio (tools/make_opening.py) does not change; the picture is built around its cues.

**Scene 2, the System window.** A 5 s slot-reel spin through ~70 classes → HERO (gold) → a 5.5 s error storm (window
red, glyphs, ~30 error pop-ups filling the empty left/right margins) → ~3 s recovery (pop-ups close, window back to
System blue) → HERO's SIDEKICK, which stays red.

## 2. Scene 1 timeline (t = seconds from the song's first note; audio cues in brackets)

| t | picture | audio (existing) |
|---|---|---|
| 0.0–1.0 | fade in looking up the street: crosswalk ahead, red hand on the far signal, phone low in frame | song starts |
| 0.15 + 0.53k | a footfall: camera dip + sway (k = 0…26) | footsteps |
| 1.0–2.0 | gaze drops, phone rises into the walking pose | |
| 2.4 | car passes left→right on the cross street | car pass |
| 3–4 | pigeons on the sidewalk flutter off | |
| 4.2 | notification "Shift at 8:00"; phone lifted to read | |
| 6.6 | taxi passes right→left (last car before the light changes) | car pass |
| 7.0 | ped signal: red hand → WALK; N-S traffic lights → green | ticks speed up |
| 7.1–8.0 | glance up at the signal; reach the curb ramp (tactile paving) at ~7.9, step onto the crosswalk | |
| 8.6 | notification "Your daily summon is ready!"; phone lifted to read | |
| 10.2 | a car overtakes on the left (N-S street, northbound) | car pass |
| 11.0 | ped signal → flashing hand + countdown | |
| 12.5 | truck enters (45 m east, westbound lane), its headlight beams spill onto the road at the right of frame | engine rises |
| 12.95, 13.75 | horn blasts; glare grows on the phone | horn |
| 14.0 | last step; the walker stops dead in the truck's lane | |
| 14.2 | look up: whip-pan right (+80° yaw, pitch to level), phone drops out of frame | |
| 15.3 | brakes: nose dive, tyre smoke | brake squeal |
| 15.6–16.0 | grille and headlights fill the frame, light floods | |
| 16.0 | hard cut to white | everything cuts on one sample |

Walk: 0.70 m stride → 1.32 m/s along x = 0, from z = 0 to z ≈ 18.45 (stop). Eye height 1.56 m.
Truck front-bumper x(t): 45.4 m at 12.5, 23.3 m at 14.2, 9.0 m at 15.3 (13 m/s), braking at 7 m/s² to 1.6 m at 16.0.

## 3. Layout (Unity space of the opening root: +Z north = walking direction, +X east, +Y up, metres)

- Road surface y = 0; sidewalks and curbs top y = 0.15.
- Our sidewalk: x ∈ [-2.2, 2.6], wrapping the SE corner (E-W south sidewalk z ∈ [6.1, 10.5]).
- E-W street (crossed): z ∈ [10.5, 22.5]: parking 10.5–12.5, eastbound 12.5–16.5, centre line 16.5, westbound
  16.5–20.5 (truck at z = 18.5), parking 20.5–22.5.
- N-S street (on our left): x ∈ [-14.2, -2.2]: parking -4.2…-2.2, northbound centre -6.2, centre line -8.2,
  southbound centre -10.2, parking -14.2…-12.2.
- Crosswalk: x ∈ [-1.6, 1.6] across the E-W street, continental bars 0.5 m wide with 0.5 m gaps.
  Curb ramps with yellow tactile paving at both ends.
- Building front lines: SE block x = 2.6 (west face) / z = 6.1 (north face); NE block x = 2.6 / z = 26.9;
  NW block x = -18.6 / z = 26.9; SW block x = -18.6 / z = 6.1.
- Lighting: low morning sun from the south-east (azimuth ~135°, elevation ~18°), hazy pale blue-grey sky, linear fog
  ~40–260 m; street lamps and shop interiors still lit.

## 4. Scene 2 timeline (from SystemAt = 20 s)

| t − 20 | |
|---|---|
| 0–0.25 | window pops in, header types "» SYSTEM assigning class…" |
| 0.8–5.8 | reel spin: ramp up, ~18 names/s, decelerate; last three Barista → Tax Auditor → Dark Lord → HERO (gold) |
| 5.8–8.6 | HERO holds in gold; "YES. I finally get to be the hero." |
| 8.6–14.1 | error storm: window red + shakes, glyphs cycle, ~30 pop-ups spawn in the left/right margins, accelerating |
| 14.1–17.1 | pop-ups close one by one, the window fades back to blue, the name resolves to HERO's SIDEKICK (red) |
| 17.9 | "...Wait. What?" |
| ~21 | done (≈ 41 s total with the 1 s pre-roll) |

Short (replay) version, 3 s: truck flash → window → HERO → a few pop-ups → HERO's SIDEKICK.

## 5. Art conventions (all opening kits)

- Style: the game's stylised low-poly "toy diorama" look, HS/Toon shader in Unity (stepped light, rim, 1.4 px
  outline). Clean chunky silhouettes, small bevels on hard edges so they catch light, flat colours + a few textures
  (signage, livery, brick, windows). Not photoreal.
- Sources: procedural (headless Blender 5.2 bpy/bmesh, PIL/numpy textures) or the CC0 Quaternius kits already in
  `ThirdParty/Quaternius`. No downloads, no AI-generated meshes or images.
- Scripts live in `tools/blender/opening_<kit>.py` (and `tools/opening_<kit>_textures.py`), are deterministic and
  re-runnable: `/Applications/Blender.app/Contents/MacOS/Blender -b -P <script> -- <out_dir>`.
- Output (staging, outside the Unity project): `build_art/opening/<kit>/{fbx,textures,previews}` + `manifest.json`.
  Never write into `Escort/`; the integrator imports from staging.
- Units metres, real-world scale. Model in Blender with the object's front facing **−Y** and +Z up; origin at ground
  level (z = 0). Export with
  `bpy.ops.export_scene.fbx(filepath=…, use_selection=True, object_types={'MESH','EMPTY'}, apply_unit_scale=True,
  apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True,
  add_leaf_bones=False)`. In Unity the front faces +Z.
- Hierarchies: one root empty per FBX named after the asset; parts that animate are separate child objects with their
  origin at the pivot (e.g. wheels at the hub centre, spin axis = Blender X). Locators are empties.
- Normals: smooth by angle (~35°) so curved parts are smooth and hard edges stay crisp. Apply scale/rotation before export.
- Material slot names are the interface: prefix per kit (`MV_` vehicles, `MS_` street, `MB_` buildings, `MP_` phone and
  hand). Unity remaps each name to an HS/Toon material built from `manifest.json`:
  `{"materials": {"MV_Paint": {"color": "#F2F2EE", "texture": null, "emission": null, "note": "cab paint"}, …},
    "models": {"Truck": {"file": "fbx/Truck.fbx", "tris": 41234, "locators": ["HL_L", …], "notes": "…"}}}`
  `texture` is a file name in `textures/` (PNG, ≤ 1024², sRGB); `emission` is `{"color": "#FFF4D6", "intensity": 4}`
  for lamps/signs. Tileable surfaces use UVs of 1 unit = 2 m.
- Previews: render each asset (Eevee or Workbench, material colours) from a 3/4 view **and** from the in-game camera
  view described in its brief; look at them and fix what reads badly before reporting.
- Draft copy (signs, livery, ads) is placeholder text for the owner to rewrite; keep it short and list it in the manifest.
- Palette: asphalt #3A3F47, sidewalk #B9B4AA, curb #CFCAC0, poles/street furniture #2F3A36 or #26292D, brick #9B4A3A /
  #7A4B3A, stone #D8CDB5, teal paint #4F7D7A, slate #4A4F5A, café green #2F6B4F, taxi #F2B705, System red #FF564A,
  hoodie ochre #C98A3A, sidekick skin #C59B76.
