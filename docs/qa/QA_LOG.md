# QA Log — Hero's Sidekick M1 slice

Each task passes the plan's QA gate: compile clean → tests green → play-mode smoke (console clean) → visual evidence → findings logged here.
Screenshots: `docs/qa/shots/`.

## Task 0 — Toolchain + scaffold (M0)
- MCP: `mcp-unity` isn't loaded as a native tool in this session, so `tools/unity-mcp.mjs` drives it over stdio (list/call/resource/batch). Verified: 34 tools listed, scene info, console, play-mode status.
- Found/fixed: MCP `recompile_scripts` does not import new files → added `Tools/Agent/Refresh` + compile reporter (`Library/Agent/compile.json`) in a dependency-free editor assembly so it survives game compile errors.
- Found/fixed: MCP `run_tests` has a 10 s Unity-side timeout and cannot survive PlayMode domain reloads → timeout raised to 900 s and a reload-proof reporter (`Tools/Agent/Run Tests` → `Library/Agent/tests.json`).
- Found/fixed (by test): `SimLoop.Ensure()` left `Instance` stale in Edit Mode (Awake not called) → explicit assignment.
- Settings: runInBackground, linear colour, Enter Play Mode without domain reload (all statics reset via SubsystemRegistration), editor throttling off.
- Assets: 7 Quaternius CC0 kits downloaded from itch.io (UBC, UAL1, UAL2, Fantasy Outfits, Fantasy Props, Stylized Nature, Medieval Village). Verified outfit rigs share head/neck/spine bone positions with the UBC Superhero rig (Δ=0.000 m); arms/legs differ ≤2.4 cm (Regular vs Superhero proportions).
- Only MacStandaloneSupport is installed → Windows/WebGL builds need the owner to add modules in Unity Hub.

## Task 1 — Input, Cinemachine, fixed-angle two-target camera
- `GameInput`: code-defined actions for KB+M and gamepad (every verb bound on both).
- `CameraRig`: TargetGroup → CinemachineFollow (world-space, pitch 50°) + GroupFraming (DollyOnly, ChangePosition, dolly 14–34 m).
- PlayMode test `FixedAngleCamera_KeepsBothTargetsInFrame`: PASS (targets 17 m and 25 m apart stay within 4–96% of viewport; pitch stays 45–55°).
- Visual: `shots/task1_camera_sandbox.png` — both placeholders framed, grid readable, shadows OK.

## Task 2 (M0b) — Character pipeline, 12+ animations, unified look
Pipeline: `tools/blender/build_characters.py` (headless Blender 5.2) grafts UBC heads onto Fantasy outfits, adds hair,
applies proportions, bakes facing, exports FBX → `tools/recolor_textures.py` palette recolours → Unity:
`ArtImportPostprocessor` + `HumanoidConfigurator` (explicit 52-bone map) + `AnimatorBuilder` (31 states, 2 layers) +
`CharacterPrefabBuilder` (toon materials, props at bone sockets) + `HS/Toon` shader.

Cast (lore fit): Sidekick (ochre peasant, kitchen knife — the unlisted helper), Callum (royal-blue knight, steel pauldrons,
longsword + shield), bandit "cheaters" in red (Thug/club, Brute/axe, Crossbowman, Turncoat/dagger), Ambusher in olive
camo, Curator faction in violet-black (Gallery Archers, Lord Ashgrave with the dull pendant tell). Alt male sidekick built.

Defects found by QA and fixed:
1. Skin "battlements" on vest backs → Superhero trapezius poking through the Regular-proportion outfit; head graft now
   keeps only verts dominated by Head/neck weights.
2. Dropped hood exposed unpainted back → keep hood trimmed to a lowered cowl; mirrored pauldron for a knightly silhouette.
3. Humanoid avatar failed ("Ambiguous Transform Head") → grafted mesh renamed `Face`.
4. Characters imported facing −Z (props pointed backwards) → 180° baked at export.
5. Magenta props → kit remaps were saved as `{instanceID:0}` (materials created inside StartAssetEditing) → two-pass remap.
6. Props shrunk to ~1 cm → kit FBX roots carry ×100 unit scale; builders now multiply, never overwrite.
7. Props mis-oriented → bounds must be taken in socket space (FBX root has −90° X).
8. Headless + backwards animated characters → inconsistent auto-mapping (Hips→root) + clips baked with "original"
   orientation from a −Z mannequin → explicit bone map for all avatars, clips use body orientation + feet height.
9. Arms overhead in idle → explicit skeleton kept the −Z source T-pose; source reference turned to +Z.
10. Salute/surrender IK silent → `AnimatorController.layers` returns a copy; IK pass now re-assigned.
11. IK overlays leaked into later actions → each action releases overlays it doesn't own.
12. Salute blade tilted → rigid prop socket is rotated blade-up during the salute (exact regardless of wrist retarget).

Evidence: `shots/task2_lineup_front.png`, `task2_top_callum.png`, `task2_anim_idle.png`, `task2_anim_jog.png`,
`task2_actions_sheet3.png` (salute, attack wind-up, overhead, jab, throw, shoot, guard, roll, hit).
Remaining (tracked): foot-slide metric to be verified with real movement in Task 3; sitting needs a seat prop (campfire).

## Task 3 — Sidekick controller
`SidekickAgent` (move 6 / walk 1.5 / sneak 1.8 m/s, 3-charge roll w/ 0.3 s i-frames, 8-dmg kitchen knife on nearest
target in arc, ping, interact, channels, 25 m support range), `PlayerCommands` (per-frame latch → per-tick consume),
`ScriptedCommands`/`QaAutoPilot` (bots/QA), `VisualInterpolator` (60 Hz sim → smooth render), gameplay prefab builder.
Tests (PlayMode): jog 6 m/s ±5%; dodge charges/recharge; i-frames; knife 8 dmg front-only; support-range flip — PASS.
**Foot-slide gate (slice gate 5)** — new automated metric (planted-foot along-travel skate speed, 30 Hz):
- Initial: 6.0 m/s (feet not animating) → cause: Animator culling with no camera in test (test fixed; runtime culling kept).
- 3.0 m/s → real bug: sim-tick `SetLocomotion` damped with render `deltaTime` → parameter crawled at high FPS; now damped per rendered frame.
- Sprint clip's effective stride far below its root-motion speed → removed from tree; jog rate-scaled above threshold.
- Walk clip is a 0.9 m/s stroll → walk speed 1.5 m/s; sneak clip stance only ~0.42 m/s → sneak 1.8 m/s with calibrated rate.
- Final: jog 0.34, walk 0.45, sneak 0.45 m/s skate (< 0.6 threshold) — PASS.
Camera readability: at wide separation characters shrank to ~40–60 px → framing tightened (FOV 32°, 12–28 m, padding 1.2).
Evidence: `shots/task3_sandbox_sheet.png`. Totals: EditMode 9/9, PlayMode 10/10.

## Task 4 — HeroBrain framework
`HeroBrain` (priority selector, reaction delay for Concussion), `HeroRule`/`HeroRuleFactory`, data-driven
`HeroRuleSetDef` (per-Stage lists with fallback), `RouteFollower` (threshold pauses: min pause + sidekick near, max wait,
external hold, chokepoints), `RouteGraph`/`RouteMarker`, `HeroAgent`/`HeroModule`, `RuleIconDisplay` (billboard,
constant pixel size, pop on change). Icons: procedural PIL glyphs (`tools/make_icons.py`), no AI imagery.
Tests: 8 new EditMode (priority, events, reaction delay, threshold pause/release/max-wait/hold, stage fallback, idle last) — 17/17.
Defects found/fixed: icons imported before the Sprite rule compiled (reimport tool added); `RouteMarker` MonoBehaviour
lived in `RouteGraph.cs` → "missing script" on save (moved to own file); camera framed ground roots so heads/icons
clipped the top edge → chest-height `CamTarget`s. Evidence: `shots/task4_icon.png` (threshold icon while waiting at the gate).

## Task 5 — Room module prefab format + seeded assembler
Format: `RoomModule` (+Z travel, entry z=0, exit z=Length, `Variants/Variant_N`), markers `SpawnMarker`, `EncounterZone`,
`ArmableAnchor`, `StoneAnchor`, `HazardMarker`, `ExploreAnchor`, `RouteGraph`. `RoomAssembler` (pure, seeded, library-order
independent), `ChapterBuilder` (chains modules, caps, campfire/boss off-side, static batching), `RoomStreamer` (room ±1).
Authored (code, CC0 kits): Crossroads Shrine (ambush), Toll Gate (combat), Ruined Gatehouse (trap corridor), Wagon Camp
(combat), Campfire, Rigged Duel arena (octagon, Oath Glyph, 3 archer perches w/ stairs+ramps), road caps. Procedural
textures: grass/dirt/flagstone (`tools/make_ground_textures.py`), FX (`tools/make_fx_textures.py`), props SpikePlate/Tripwire.
Tests: EditMode assembler ×5 (22/22 total); PlayMode chapter build: continuity, forward route, 1 threshold/room, determinism,
build 32–51 ms — PASS.
Texture/visual defects found and fixed:
1. Every room shared one ground mesh asset (same save path) → room ends showed the skybox; unique mesh per room.
2. Foliage rendered white → kit leaves ship white masks (for a tint shader) + pre-coloured `_C` twins; resolver now
   prefers `_C`, and a full remap reads source tints before remapping (after an ordering bug in the resolver).
3. Kit bushes used the autumn-red atlas → clashed with the late-summer road and the "red = bandit" read; green recolour.
4. Builder bug: `Save()` read `root.name` after `DestroyImmediate`.
Evidence: `shots/task5_rooms_game_sheet.png`, `task5_rooms_top_sheet.png`, `task5_arena_top.png`, `task5_campfire.png`.

## Task 6 — Skill system
`SkillDefinition` (SO, rank values, dishonour tag, uses text only), `SkillCatalog` (Resources), `SkillSystem` (soup picks,
rank 2 cap, slots 4/5/6, passives slot-free, capstone exclusive, camp-only swaps, cooldowns, snapshot/restore).
48 skills authored as data from GDD §5 (6 implemented for the slice). EditMode 9 new tests — 31/31.

## Task 7 — Six slice skills (+ enemies, projectiles, armable props, encounters)
Pocket Sand, Loosen Bolt (+`ArmableProp`: arm channel, cap 2/3, collapse on ping or hero-pass-with-enemy-underneath,
90 heavy AoE, armed danger ring), Quiet Feet (2/1.5 m detection, hero cone ×0.67/0.5), Crossbow (38/44, reload
3.5/3 s, rank-2 pierce, cover blocks, can hit the hero), Bandage (3 s channel, 40/60 HoT, treats a minor wound, refund
on interrupt, cancels if the hero walks off), Cover Story (earshot 14 m, raises CoverStory(12/20, 3/4 s)).
Support: `EnemyAgent` (7 archetypes; loud hero vs unlisted sidekick detection; fake surrender + cheap shot; hidden
ambush; ranged aim tells; deterministic), `ProjectileSystem`, `EncounterDirector` (dormant posts, zone/provoke start,
room-exit gate), `SimTimers`, heal-over-time, pooled procedural VFX, `HS/FX` shader.
Tests: PlayMode 7 skill tests + all prior — 18/18 PlayMode, 31/31 EditMode.
Defects found/fixed: test-fixture race (deferred Destroy let tests register into a dying SimLoop → immediate teardown);
two test-authoring mistakes (sand range, boundary distance) and float-clock tolerance; a runtime `namespace HS.Agent`
shadowed the `Agent` type (compile break) → moved to HS.QA; crossbow bolts unreadable at gameplay distance → thicker
bolts with colour-coded trails (warm = incoming fire, pale = yours). Observed correct emergent behaviour: dormant thugs
spotted the sidekick in their view cone and engaged the (loud) hero.
Evidence: `shots/task7_skills_sheet.png` (arming, armed ring, sand cloud, bolt, collapse, aftermath).

## Task 8 — Callum's S0/S1 rules, Honor, witness cone, Riposte
Rules as data (`Callum_RuleSet`): fallback(3) > wait_unready(3 s S0 / 2 s S1) > salute(1.2 s) > fight > challenge(15 m)
> threshold_pause > follow_route. `CallumModule`: duel state, sword + Riposte (holds guard while ready and the opponent
telegraphs; parry + 2× counter), Honor (Major −20 / Minor −8, halved at S1; <40 → −25% dmg + scold; +15 on room clear),
witness cone 120°/12 m with line of sight (Quiet Feet ×0.67 angle / ×0.6 range; Doubt widens it), Spoiled-duel and
Unseen-deed events for the Rapport judges, stone hook. Ground cone view (`WitnessConeView`).
Tests: 12 PlayMode Callum tests (salute timing, S0/S1 wait caps, turncoat cheap shot vs S1 spare, Riposte, Honor costs,
low-Honor damage, Quiet Feet cone + walls, committed fall-back + single-file narrows, own-blows-not-Unready, attack tokens).
Totals: EditMode 32/32, PlayMode 30/30.

**Integrated QA (new `QA_Chapter` scene: seeded chapter + encounters + Callum + follow-bot sidekick + camera,
`ChapterWatch` event log + timed/event captures).** The first integrated run exposed defects no unit test had caught:
1. *Rule thrash* — fight↔fallback flipped every few ticks as the engager count crossed 3 → the retreat is now a committed
   manoeuvre (max 4 s) followed by "holding the narrows" (12 s, bandits come single file).
2. *Own hits counted as "Unready"* — every sword hit / riposte applied a stagger and Callum politely waited after each
   blow ("Get your feet under you", 6× in 5 s) → statuses now record their source; his own staggers don't count.
3. *Bark spam* (a line every ~1.3 s) → per-line-set rest (9 s / 5 s urgent), 3.5 s gap for chatter; Caught/Treachery
   always speak.
4. *Unsoloable first room* — 4 bandits swinging at once (~45 DPS vs 260 HP) killed him in 12 s → melee **attack tokens**
   (2 swingers per victim, the rest circle at menace range and rotate in only when someone is waiting). Room 0 is now
   soloable (cleared at 50/260 HP) — in line with the Ch1 "hero can solo ~70%" target; harness to confirm (Task 15).
5. *Challenged an unreachable perch crossbowman* and stood under him until killed → perch shooters are never challenged
   (they are the sidekick's job — "Unseen assist"); they don't lock the room gate; he barks "Come down and face me".
6. *Camera* kept the hero and his icon at the top edge with nothing visible ahead → look-ahead target along the route.
7. *Witness cone invisible* — the mesh had no UVs, sampling the transparent corner of the soft texture → UVs + stronger
   alpha, emphasised while the sidekick sneaks or aims a skill.
8. *Sidekick stuck at spawn* — spawned inside the start cap's back blocker → moved.
9. *Flaky crossbow test → real determinism bug*: the sim ticks in `Update`, and collider transforms only synced when a
   physics step happened to run that frame (a resized floor was 10 m wide in the physics scene, so an enemy fell through
   it). `SimLoop.Step()` now calls `Physics.SyncTransforms()` first — physics queries no longer depend on frame timing.
Idle-sidekick S0 run (seed 1): clears Room 0, walks past the perch crossbowmen in Room 1, and dies in Room 2 to the
turncoat's fake-surrender cheap shot while waiting on him — exactly the S0 flaw the sidekick exists to cover.
Evidence: `shots/ch1_s0c_*` (duel salute, fallback shield icon, hourglass wait on the surrender), `shots/cone_04_t008.png`.

## Task 9 — Opportunity Director + Rapport ledger
`RapportLedger` (pure C#): offers/captures/closes/penalties/refunds per chapter; budgets 60/90/100/110/40 (offers stop at
the budget); capture rate = (earned − penalties) / offered so far, clamped at 0; penalties capped per chapter at 30% of that
chapter's offered points; snapshot/restore for Restore Points; event log for the Post-Mortem.
`OpportunityDirector` (sim tick, Judges order) runs the hero's Dance judge. `CallumDance` (GDD §6.1):
Unseen assist 3 (cheater/archer killed or disabled during a duel, deed and doer outside his cone and no active stone),
Averted cheat 4 (fake surrender stopped before the stab; hedge ambusher flushed out before he springs), Covered lapse 2
(Cover Story inside its window after a Caught removes that penalty; only offered if Cover Story is equipped — never an
uncapturable offer), Wound treated after a duel 2. Penalties: Caught −3 / −6 second time in a fight, Spoiled duel −2,
Friendly fire −3 (>10% of his HP per fight), Abandon −3 (>20 m while he's <40% HP in combat, 2 s grace).
Tests: EditMode 9 ledger tests (rate math, single capture, closed windows, uncaptured-still-offered, cap + per-chapter cap,
budget, refunds, snapshot). PlayMode 11 judge tests on real agents (unseen crossbow assist; witnessed assist = Caught not
credit; sanded fake surrender; flushed ambusher; landed ambush closes uncaptured; Cover Story refund; honest misses cost
nothing; friendly fire; abandon; spoiled salute; caught twice = 3 + 6). Totals: EditMode 41/41, PlayMode 41/41.
Defects found/fixed while building it:
1. *Honest failure was penalised*: sand on an empty road and a prop collapsing on nobody raised Major sabotage (Honor loss +
   Caught) → sabotage now requires a hostile victim.
2. *Ambush soft-lock*: Crossroads Shrine ambushers sit ~5 m off the route but only sprang inside 3.6 m — they never
   triggered, the room could never clear, and the hero would be held at the exit forever → 6.2 m trigger with a 0.38 s
   lunge, and once every visible enemy is down the hidden ones come out swinging.
3. *Unfair ambushes*: hidden ambushers had no tell → a steel glint in the hedge every 2.4 s, visible only when the
   (unlisted) sidekick is within 14 m — the hero never notices, which is the point.
Integrated check (seed 1, idle sidekick): offers appear in context (unseen assist on the turncoat at duel start, averted
cheat when he fakes his surrender, closed when the cheap shot lands) — `Library/Agent/watch_ch1_rap.log`.

## Task 10 — Stage evaluator
`StageEvaluator`: bands 30/55/75; caps Ch2 ≤ S1, Ch3 ≤ S2, door ≤ S3; door recovery cap +1; fall ≤ 1 per check.
Tests (EditMode, 28): band edges (incl. ±1e-4 around each threshold), 12 rule cases, and exhaustive properties over every
Stage × check × 209 rates (never above cap, fall ≤ 1, door rise ≤ 1, falls only when the rate is below the band,
monotonic in rate, reaches the band when nothing limits it, full-campaign paths).
Defect found by the exhaustive tests: a Stage above the check's cap (unreachable in a campaign) made "never above the cap"
and "fall at most one" contradict → the cap is absolute and the one-step fall is measured from the capped Stage.

## Task 11 — Wound system (+ live trap-corridor hazards)
`WoundSet` on the hero (`HeroAgent : IWounded`): a hit ≥ 25% of max HP wounds him; type is deterministic from the damage kind
(heavy → cracked ribs, club → concussion, arrow → fever, trap → sprained ankle, blade → sword-arm strain); maluses per the GDD
table (−15% speed, −20% max HP, −20% damage, fever drain 0.45% max HP/s never below 15%, +0.5 s rule reaction); 3+ wounds =
Crippled (−30% speed). Bandage treats the oldest minor wound; camp treats the worst (serious first). Callum barks per wound.
`WoundChanged` event for HUD/harness. The "wound treated after a duel" Moment is offered only for bandage-able wounds.
**Hazards were data-only** (spike plates and tripwire rendered in the trap corridor but did nothing) → `HazardMarker` is now
live: spike plate 18% max HP, tripwire trips (1.2 s) for 8% and rings the alarm (dormant bandits within 24 m engage);
hazards always wound the hero; bandits know their own traps; the sidekick sees them — walking/crouching/standing she steps
over, only a careless run across the middle springs one — and can disarm them (Interact, 1.2 s).
Tests: 9 PlayMode (threshold, type→malus, Crippled, fever floor, bandage/camp order, real Bandage on Callum capturing the
Moment, spike plate wounds, careful sidekick + disarm, tripwire alarm). Totals: EditMode 69/69, PlayMode 50/50.
Integrated (seed 1, idle sidekick): S0 dies to the turncoat's cheap shot in Room 0; S1 spares him at 2 s, clears Room 0 and
— entering Room 1 at ~50 HP — was killed outright by a flat 70-damage spike plate → hazard damage now scales (18%), since the
wound is the hazard's real cost. Runs are reproducible tick-for-tick for a given build and seed.

## Task 12 — Chronicle stones + Curator Intel
`ChronicleStone` (agent + `IBreakable`, immovable): Dormant / Active / Broken; sees 200° / 14 m with line of sight; its view
is drawn on the ground in cool blue while active (so "unseen" can be planned); 24 HP — three knife cuts or one bolt; broken
stones tilt, sink and grey out. `StoneSystem` + `CallumStonePolicy` (GDD §6.1 "stones stream only formal duels"): stones
with Callum in view wake when he begins a duel and stay awake for it (+2 s); an active stone is a witness for Honor/Caught;
it records his S0 flaws (waiting on an Unready foe, falling for treachery — one clip per flaw per stone per duel).
`CuratorIntel` (0–3, 2 clips per level): clips are pending until relayed at the chapter's end; breaking a stone first
destroys what it holds. Stones are now placed in every room prefab (model + agent).
Tests: 5 PlayMode (wake/sleep around a duel and range, witnesses a deed behind Callum's back, walls block its view, flaw
clips → Intel → relay, knife breaks it and its clips die with it). Totals: EditMode 69/69, PlayMode 56/56.
**Defect found while building the stones (latent since Task 5/7): every moving part of a room was frozen into the
chapter's static batch** — in a real chapter a loosened beam would have dealt its damage without visibly falling, and
sprung traps/broken stones could not change. Measured 16/16 dynamic renderers batched → `IDynamicVisual` marker
(ArmableProp, HazardMarker, StoneAnchor) and `ChapterBuilder.CombineStatic` batches scenery only → 0/24, with a regression
test. Also fixed: `AgentSeparation` would have shoved stones around (now immovable agents push others the full overlap);
unsafe `GetComponent ?? AddComponent` / `is` patterns on Unity's fake-null objects.
Integrated (seed 1, S1): the wagon-camp stone woke when a duel came into its view and clipped his etiquette flaw while he
waited on the fake surrender. Evidence: `shots/ch1_stone_04_*.png`, `shots/ch1_stone_07_*.png`.
Noted for final QA: the turncoat's surrender pose is hidden behind Callum's model when he kneels right next to him.

## Task 13 — Mini rigged duel (Lord Ashgrave) + UI layer + end screens
`RiggedDuelDirector`: terms (Ashgrave states "no aid"; S0 Callum orders you out of the circle, S1 asks you to stay near
the edge) → formal duel (Callum challenges, salutes) → Ashgrave's tell at T+20 → fixed signal at T+25 s: the three hidden
gallery archers stand and loose volleys at Callum. "No aid" terms: any harm the sidekick does that he (or the arena's
chronicle stone) witnesses is a Major breach. S0: nags you out of the Oath circle (Doubt widens his cone) and fights on
through the arrows ("Arrows? No matter."). S1: the first volley lands in full, then his guard is up (arrows ×0.4). Every
archer is an Unseen-assist Moment (hidden ones too — the hardest assist). Win → Callum's line (S0 credits fortune, S1
notices help); loss → `HERO: CALLUM. DECEASED` in settling red glyphs → the Curator's diagnosis (by Stage and cause of
death) → qualitative Post-Mortem → Restore buttons. Scripted enemies (no early spotting, no ambush lunges from perches).
**UI layer** (code-built uGUI, overlay canvas; TMP essentials imported; Share Tech Mono (OFL) for System text): hero panel
(HP with damage ghost, Honor, wounds, Hero Insight rule label on Tab), sidekick panel (HP, dodge charges), skill bar with
cooldowns, interact prompt + channel bar, boss bar, speech bubbles (speaker-coloured, above the rule icon, de-overlapped,
clear of the HUD band), sidekick thought lines, the System window, off-screen threat chevrons for aiming shooters, end
screen. QA captures now include the overlay UI.
Balance (deterministic, from `RiggedDuelTests.Balance_Report`): S0 alone — lost to the volleys, Ashgrave on 19%;
S1 alone — guard up, lost with Ashgrave on 2 HP; S1 + one archer silenced — won (17 HP); S0 + one silenced — lost;
S0 + all three silenced — won (80 HP). Neglect loses at both Stages; the slope favours the hero who has learned to look up.
Tests: 7 PlayMode (sequence + T+25 signal, S0 alone loses to arrows, S1 guard + lasts longer, silencing wins at S0 (stone
broken first), the S0/S1 one-archer slope, terms breach caught), 1 EditMode Post-Mortem (words, never numbers).
Totals: EditMode 70/70, PlayMode 63/63.
Defects found/fixed during the duel's QA:
1. The trap never sprang — Callum killed a 560-HP Ashgrave in 19.6 s, before the T+25 signal → durable, lighter-hitting
   duellist (1250 HP) so the duel outlasts the signal by several volleys.
2. Gallery archers were offered as "averted ambush" Moments → excluded (they're Unseen-assist Moments).
3. Test-fixture bug that hid a real hazard: the projectile system survived a sim-loop reset still registered to the dead
   loop, so arrows silently never moved → `ProjectileSystem.Ensure()` re-registers with the current loop.
4. Bubble under the boss bar / over each other / over the rule icon → laid out above the icon, de-overlapped, kept out of
   the HUD band; the boss bar now appears when the duel starts.
5. Arrows from off-screen archers were unattributable → edge chevrons for aiming shooters.
6. The camera-side arena walls hid the floor → the three near sides are low walls; banners moved to the north wall.
7. Post-Mortem said "Nothing was offered" after a loss → open Moments close at the end of a fight (they still count as
   offered), and the log keeps each Moment's subject so the line can say "a gallery archer".
8. Unsupported glyphs (◆ █ ▓) rendered as boxes in Share Tech Mono → checked the font's cmap and used » _ # @ etc.
Evidence: `shots/duel_s0b_07_t037.png` (glyph, volley), `shots/duel_s1_08_t038.png` (S1 guard line, low walls),
`shots/duel_s1_15_end.png` (S1 diagnosis, restore buttons).
Noted for final QA: a speech bubble necessarily covers whatever stands just north of the speaker at this camera angle.

## Task 14 — Campfire (two variants) + the playable slice flow
`CampfireDirector`: on arrival the hidden Stage check runs (`StageEvaluator`, slice check = Ch2-style, max S1) and the scene
shows the result — cold S0 (he sits apart with his back to you, a small fire, "We camp here. I'll take the first watch.",
recites the Code *at* you, "And keep your sand in your pockets" if he saw dishonour; your thought: "He hasn't looked at me
once.") vs warm S1 (a bigger fire, he faces you: "Sit. The fire's big enough for two.", the Code "feels lighter tonight",
"You were... useful today. I noticed."). Rest tends his worst wound and heals you both; stones relay their clips to the
Curator; level-up picks + loadout (camp-only swaps); Restore Point snapshot. `XpTracker`: fixed pots per room — 50% clear,
25% assist, 25% exploring (caches are now searchable and show lore notes in the System window).
`GameFlow` (Main scene, in Build Settings): opening (class roll → red glyphs → HERO's SIDEKICK; 3-second version on later
runs) → first two picks → the Old Road → fade → campfire → fade → the Rigged Duel → end screen. Deaths on the road get a
one-line Curator diagnosis by cause (cheap shot, ambush, trap, arrows, fever...). Restore Points: chapter start (skills
kept, Rapport restored) and before the duel. AutoPlay mode drives the whole slice with a bot (QA + harness).
UI: `SkillPicker` (skill soup + loadout), `OpeningView`, `ScreenFade`.
Tests: 4 PlayMode (cold < 30% / warm ≥ 30% / never above S1; rest order + heals; picks; Restore Point round trip through the
real flow). Totals: EditMode 70/70, PlayMode 70/70.
Defects found/fixed while integrating the flow:
1. AutoPlay left the camp after 0.24 s, so the camp's lines played over the duel's terms → bots now sit through the scene,
   and the camp director goes quiet once done.
2. Leftover road enemies were deactivated at camp, which the encounter director counted as "Room 1 cleared" (XP + a Code
   recital at the fire) → the road's encounter director is shut down first.
3. Speech bubbles overlapped the System window → bubbles stay below it while it shows.
4. A real-time animation test (jog foot-slide) failed once under full-suite load (0.72 vs typical 0.30 m/s) — frame stalls,
   not a regression → stall windows are skipped and the median is used.
Evidence: `shots/flow2_22_camp.png` (cold camp), `shots/flow2_27_end.png` (S0 loss: diagnosis + Post-Mortem naming every
missed Moment), `shots/flow1_15_hazard_Tripwire.png`.

## Task 15 — Test harness (determinism, bots, batch balance)
`StateHash` (FNV-1a over every agent's id/position/HP/state, ledger totals, tick) + `Same_Seed_Same_Bot_Same_State_Hash`
(identical hash for the same seed and bot after the same ticks; a different seed differs). Bots: **Idle** (trails far
behind, never acts — solo measurement with encounters isolated), **Sloppy** (near him, random skills, never aims),
**Supportive** (scripted Moment capture: cover a lapse, sand a false surrender, scout and flush hedge ambushes while he's far
off, bandage him when safe, unseen crossbow/sand on cheaters planned with the game's own witness check, blind or knife
flankers at his back, disarm traps, search caches, break the duel stone then shoot the gallery), run with four builds
(Supportive sand+bandage, Fixer quiet-feet+sand, Handler sand+cover-story, Shadow quiet-feet+crossbow). `BalanceHarness`
runs seeds × bots in throwaway scenes at 40 sim ticks/frame → `docs/qa/balance/runs.csv` + `summary.md`
(`Tools/HS/QA/Build Harness Scene`, args in Library/Agent/harness_args.json). `GameFlow.Fast` / `IsolateEncounters`.

Final batch (10 seeds each; `docs/qa/balance/summary.md`):

| bot | reached duel | S1 at camp | duel won | solo/rooms passed |
|---|---|---|---|---|
| idle (isolated) | 3/10 | 0 | 0 | 65% (GDD target ~70%) |
| sloppy | 2/10 | 0 | 0 | 71% |
| supportive | 10/10 | 0 | 0 | 100% |
| fixer | 9/10 | 1 | 1 | 97% |
| handler | 6/10 | 4 | 4 | 87% |
| shadow | 2/10 | 0 | 0 | 70% |

Every S1 duel was won; every S0 duel was lost — the Stage gradient holds. Two builds reached S1 (Handler reliably, Fixer
once): the GDD gate "≥2 viable builds" is met only narrowly (see the final reports).

**What the harness found (all fixed, with tests where applicable):**
1. *Determinism*: the harness's first frames ran on real-time accumulation → fast-ticks from the first frame.
2. *Every staggering sidekick hit read as "striking the helpless" (Major)*: the damage event fired after the hit's own
   stagger was applied, so Callum judged the victim as already off balance → the event is published before the stagger.
3. *The turncoat's fake surrender looped* (two 66-damage stabs in one fight) → once per turncoat.
4. *NullReferenceException once the chapter's 60-point offer budget was spent* → null-safe judge + test.
5. *Uncapturable Moments counted against the player* (an "unseen assist" on his own duel opponent) → withdrawn (new
   `RapportLedger.Withdraw`), keeping measurement valid (GDD §11.3).
6. *Pre-emptive play wasn't credited*: ambush Moments opened only within 16 m, when he was already looking → 26 m.
7. *Exception spam*: the System-window typewriter overran on text ending in a rich-text tag (cache notes).
8. *Unwinnable attrition*: rooms with shooters he won't chase never "clear", so no recovery → second wind (+50% max HP,
   wounds stay) on reaching the next room; a tripwire no longer wounds (a trap corridor alone could cripple him).
9. *Bandaging a walking hero always failed* (he left reach mid-channel) → he holds still while being dressed.
10. *The camera framed empty grass* when the pair split up the road → axis-aware leash (the player's sidekick keeps the
    frame) + a gold edge marker with his HP; tests for both.
11. *The crossbow could not hit perch shooters* (flat shots) → bolts aimed at a marked enemy fly to them in 3D.
12. *Balance*: honest bandits retuned (thug 14→11, brute 24→18, crossbowman 22→16 …) so the danger is the cheating;
    measured solo rate 22% → 65%. Crouch speed 1.8→1.6 (the sneak clip was at its playback clamp).
13. *Flaky animation test*: real-time foot-slide sampling failed at 14 fps after hours of editor use → lockstep sim +
    animation via `Time.captureDeltaTime` (now identical readings every run: jog 0.45, walk 0.37, crouch 0.56 m/s).
Totals: EditMode 71/71, PlayMode 73/73.

## Final integrated QA (build, performance, presentation, audio)
**Player build**: macOS standalone (`Tools/HS/Build/macOS Player` → `Builds/macOS/HerosSidekick.app`, 179 MB, ~20 s
incremental). The build log's remaining 3 "errors" all come from the third-party MCP Unity package (a test file shipped
without a .meta), none from the game. Automated player runs (`-hs-autoplay handler -hs-seed 2 -hs-perf … -hs-quit`):
the full slice plays to the end with **zero exceptions**, reproducing the harness result (S1 at camp → duel won).
**Performance** (Apple M1, 1920×1080 windowed): avg 5.94 ms (168 fps), p95 7.8 ms, p99 9.0 ms, worst in-play frame
34 ms; only startup frames hitch (2.2 s scene build + 0.55 s first-render shader compilation), now behind the opening's
black pre-roll. `docs/qa/perf_player.json`.
Defects found and fixed in this pass:
1. `EncounterDirector` had a private method named `Start(…)` → Unity logged "Start() can not take parameters" on every
   domain reload → renamed (and swept the codebase for other lifecycle-name collisions).
2. Native-symbol upload to Unity Cloud failed (403) on every build → disabled (the project isn't cloud-linked).
3. Audio caused 0.3–1.2 s mid-fight hitches in the player (first-use clip loads) → all clips load during startup.
4. System window covered the road ahead (top-centre, where the look-ahead camera shows threats) → top-right.
5. Speech bubbles sat over whoever stood "north" of the speaker (his duel opponent) → bubbles sit beside the head.
6. Skill-slot cooldown numbers printed over the skill names → name along the bottom, number on top.
7. Warm camp scene composition: fire half off-screen, bot still steering the sidekick → the scene owns the pair and the
   camera frames the fire.
8. One click in the skill picker spent a permanent pick → first click reads, second click learns.
9. Opening class-roll could stutter on the startup hitch → 1 s black pre-roll.
**Audio** added (the slice was silent; GDD §9 "royalty-free or self-made"): 49 CC0 Kenney clips (impacts, knife, cloth,
UI, bell, jingles) + 8 self-made synthesized clips (whooshes, crossbow twang, horn, fire and forest loops, duel and camp
pads; `tools/make_sfx.py`). `AudioDirector` maps the event bus to sound (presentation only), rate-limits repeats, pans by
screen position, crossfades beds by game state, stays silent without a listener and in harness batches. Tests: every
sound key resolves to a clip; events → the right sounds; repeats rate-limited. Credits in `THIRD_PARTY.md`.
Totals: EditMode 71/71, PlayMode 75/75.

## Follow-up: the opening's missing beats (GDD §8)
**Gap (found on review):** the opening shipped starting at the status window. GDD §8 beat 1 ("The walk, earbuds and a
royalty-free song. A horn rises, the song cuts, white screen.") and beat 4 ("When you first meet the hero, 'HERO' quietly
swaps for his name.") were missing, and the opening ran ~10 s instead of ~40 s. The Task 14 entry above and the first
version of the reports called the opening done; they shouldn't have.
**Built:**
- *Beat 1 (UI and audio only, as specified):* a phone lock screen that bobs with every step (step counter, two
  notifications, "Main Character · Nobody in Particular" playing on EARBUDS). The song plays in the earbuds; the street is
  muffled. At 12.5 s the truck rises out of the muffle (engine, two horn blasts, then a held one, brakes). Twin headlights
  grow from the right, the phone drops away (you look up), and at 16.0 s the song, street and truck cut on the same
  sample, on the drop the song's build promised. White screen, ringing, fade to black.
- *Beats 2–3:* the roll slows like a slot machine through … Barista, Tax Auditor, Dark Lord → HERO, with sounds (ticks,
  confirm, error buzz, glitches, a "bong" as HERO's SIDEKICK settles). Total ≈ 35 s. Skipping takes two presses.
- *Beat 5:* the 3-second replay version opens with a horn sting and a headlight flash, as a callback.
- *Beat 4:* the HUD reads "YOU HERO's SIDEKICK". 1.6 s into the road Callum introduces himself ("Callum, of the Code. Keep
  up, sidekick, and fight fair.", draft copy) and the label glitches into "CALLUM's SIDEKICK". Restored runs start
  swapped.
- *Audio (self-made, `tools/make_opening.py`):*
  - a 16 s, 120 BPM synth-pop song (FM e-piano, disco-octave bass, pulse lead, drums, riser);
  - the street (Kenney CC0 concrete footsteps at 113 steps/min, traffic, crossing-signal ticks, all low-passed by the
    earbuds);
  - the truck, the replay sting and the ringing.
  
  The clips are scheduled on the audio DSP clock (`AudioDirector.Schedule`, dedicated voices); the picture follows the
  audio clock.

**QA:**
- *Audio analysis* (spectrogram, RMS, pitch track):
  - the lead renders as composed, ending on C♯6, the leading tone into the cut;
  - during the walk the song sits at −16 dB RMS and the street at −32 dB, with the footsteps 1–10 dB under the song in
    their band (audible);
  - the truck climbs from −52 dB to the loudest moment of the mix (−13.7 dB) and pans from right to centre;
  - the mix peaks at 0.85 (no clipping), and every layer ends on a 4 ms fade (no click at the cut).
- *Tests* — `OpeningTests` (7):
  - the walk → headlights → hard cut to white;
  - the roll's order and thoughts, and the ≈35 s total;
  - the 3-second version;
  - the two-press skip;
  - clip lengths match the timeline (one-sample cut);
  - HERO → CALLUM;
  - the meeting in a real `GameFlow`.
- *Captures:* `open3_*` (the full opening), `open4_*` (the replay version), `meet_*` (the meeting).
- *Player build, live opening* (`docs/qa/perf_opening.json`): avg 6.50 ms (154 fps), p99 8.3 ms, worst frame 15 ms.
  The only stalls are the two startup ones, behind the black pre-roll.
- *Player build, full automated run* (`docs/qa/perf_player.json`, `docs/qa/player_run.log`): won (S1 at camp → duel won),
  0 exceptions, avg 6.06 ms (165 fps), p99 8.4 ms, worst in-play frame 40 ms. A first attempt showed 0.3–0.9 s stalls;
  they lined up with an editor compile and test run on the same machine, and the clean re-run had none.

**Defects found and fixed in this pass:**
1. The new clips were imported as mono, because Unity imported them before the changed import rule compiled → added
   `Tools/Agent/Reimport Audio`; a test asserts the song and truck are stereo.
2. The shared glow sprite never reached alpha 0 at its edges (22/255). Scaled up, it showed square edges: the wallpaper
   blobs and headlights, and faintly the HUD shades → windowed to 0 at the edge.
3. A new notification slid down through the card above → it now grows in place.
4. 5-px bars drawn with a 9-slice sprite rendered as dashes → plain rects.
5. The headlights read as a grey fog → small twin lamps with hot cores and a lens streak early; the flood comes late.
6. A 316 ms stall as the phone first appeared: hidden UI isn't drawn, so its masked-UI shader variants compiled on the
   first visible frame → it's drawn at an invisible alpha during the black pre-roll.
7. "click to skip" sat over the climax → hidden from the horn until the System window.
8. The picture follows the audio clock, but with no audio device that clock never moves, and the opening would have
   frozen for good → it falls back to real time after 0.3 s without audio (test).

Totals: EditMode 71/71, PlayMode 83/83.

## Opening v2 (owner feedback): the street, the truck, the glitch
**Asked for:** a dynamic first beat with an actual road, a crosswalk, wired earphones plugged into the phone and an
actual truck model (keep the phone); a faster, longer class spin (~5 s, many more classes) landing on a gold HERO; an
error with pop-up windows filling the empty sides (5–6 s, some unreadable glyphs) that clear over ~3 s into HERO's
SIDEKICK, red, in a window that's otherwise blue again. Design: docs/superpowers/specs/2026-10-01-opening-street-design.md.

**Built:**
- *Assets (headless Blender + PIL, three parallel modelling passes, all self-made):* a cab-over box truck ("TENSEI",
  DESTINY FREIGHT livery, 43.5k tris, separate wheels, headlight locators) and five cars; 16 buildings (four detailed
  corners — DAILY GRIND café, 24/7 MART, PHARMACY, DINER — eight mid-block shopfronts with fake interiors, four towers);
  the street kit (signal masts, pedestrian heads with countdown, lamps, bus shelter with a HERO SUMMONER ad, hydrants,
  news boxes, café furniture, bike rack, manholes, drains, pigeons); the phone in a posed UBC hand with an ochre hoodie
  sleeve, and wired earphone parts. `tools/blender/opening_*.py`, `tools/opening_*_textures.py`; staging in
  `build_art/opening`, imported by `Tools/HS/Build/Opening Assets` (`OpeningAssetsBuilder`: manifests → HS/Toon
  materials, transparent glass, `Resources/OpeningAssets`, "Opening" layer). Road textures: `tools/make_street_textures.py`.
- *`OpeningStreet`:* procedural road, sidewalks with rounded curb returns, curb ramps with tactile paving, zebra
  crossings on all four legs, lane markings; a first-person walk timed to the audio's cues (footfall bob, glance up when
  the light turns WALK at 7 s, cars on the audio's passes, a van that waits for its green, pigeons that scatter, steam,
  the truck running its red, braking with a nose dive and tyre smoke); its own camera, sun, sky, fog and grade (rack
  focus, motion blur on the whip-pan, chromatic aberration, lens distortion and a flood into the cut). The chapter's
  cameras and sun stand down and are restored at the cut.
- *`EarphoneCable`:* a Y-shaped verlet cable from the phone's jack to the ears, drawn as tubes.
- *`ClassRoll`:* the slot reel (69 classes, ~22/s, slowing ticks, gold landing with sparkles), the error storm (32
  pop-ups in the margins: titles, codes, rotting text, glyph noise, jolts, flicker) and the recovery; the short version
  gets a scaled-down storm. The lock screen moved onto the 3D phone ("EARBUDS" → "HEADPHONES").

**QA tooling:** `HS.QA.OpeningScrub` + `tools/opening_scrub.py` capture exact opening times (`walk*`, `climax1_*`,
`roll*`, `short*` shots); `Tools/Agent/Inspect Model Hierarchy`; `Tools/Agent/Inspect Game View`.

**Defects found and fixed:**
1. Pop-ups waiting to spawn showed as ghosts: alpha 0.004 becomes ~13/255 on black in linear space → the invisible
   warm-up draw happens only behind the black pre-roll.
2. A QA capture replayed the previous shot: a pooled render target kept old pixels when a render failed → fresh, cleared
   target per shot.
3. Black frames near the truck: its headlight spots sat on the lens geometry (the locators carry the import's root turn,
   so they pointed down), overflowed half precision, and bloom spread the NaN over the frame → beams aimed in street
   space 0.3 m ahead of the lens; HS/Toon output clamped.
4. Kit models stood on end, then faced backwards: the import keeps a 90° X turn on the root (placement now keeps it) and
   kit fronts face −Z (turned 180°).
5. Signals looked lit in every colour (lens colours show in daylight) → unlit lenses go dark; the WALK and hand icons
   share a panel, so the unlit one is clipped away and both are double-sided.
6. The pedestrian head we watch sat behind a mast pole → moved to the open end of the crossing.
7. The earphone cable vanished behind the hand and below the frame → more slack, a hoodie-deep chest, a thicker
   outlined cable and a slightly lower gaze; it now hangs in a loop in view.
8. The replay version resolved HERO's SIDEKICK mid-storm (fixed offsets inverted in a sub-second storm) → timings scale
   with each phase.
9. Player hitches on first use (3.5 s when the first car with glass passed, ~1 s at the whip-pan, smaller ones when the
   climax effects switched on) → the pre-roll now looks every way with every car, the lit truck, pigeons mid-flap, smoke,
   every signal state and the climax's post effects in front of the lens, and the chapter's camera keeps rendering under
   it until the song starts.

**Results:**
- *Player build (macOS, 1920x1080 windowed, `docs/qa/perf_opening_v2.json`):* avg 7.7 ms (129 fps), p99 17.3 ms; the
  only hitches are at startup behind the black pre-roll (world build 2.2 s, warm-up 1.2 s); 0 exceptions; the flow hands
  over to the skill picker with the chapter's camera and HUD restored (`flowop_*`).
- *Tests:* `OpeningTests` 13 (new: the street's cues, the chapter's world handed back, the earphones from jack to ears,
  the 5 s spin through dozens of classes, the storm in the margins clearing to blue with the name red; the replay
  version's pop-ups). EditMode 71/71; PlayMode 87/88 — the one failure is `CameraTests.FixedAngleCamera_KeepsBothTargetsInFrame`
  (hero 1–4% from the frame edge at 25 m apart; it depends on the editor's Game view shape and fails at 16:9 too). It is
  untouched by this work and is being looked at separately.
