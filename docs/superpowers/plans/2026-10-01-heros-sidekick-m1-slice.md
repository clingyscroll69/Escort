# Hero's Sidekick — M0 → M1 Slice Implementation Plan

> **For agentic workers:** Executed inline (user asked for direct execution; no subagents). Steps use checkbox (`- [ ]`) syntax for tracking. Code lives in the files named per task rather than being duplicated here; each task lists its interfaces, acceptance tests and QA gate.

**Goal:** Build GDD §11.5 "first agent tasks" 1–15 in order, producing the §11.2 vertical slice: Sir Callum, one chapter of 3 seeded rooms, a campfire in two Stage variants, and a mini rigged-duel boss, with 6 sidekick skills and Stage 0 vs Stage 1 hero behaviour.

**Architecture:** Deterministic fixed-tick gameplay simulation (`SimLoop`, 60 Hz) that all gameplay components register with; presentation (Animator, VFX, UI, camera) only reads sim state, so the same scene can run at 20–50× speed for the balance harness. Data-driven content (ScriptableObjects for skills/moments/tuning, C# layout tables for rooms) built into prefabs by editor scripts that the agent triggers through the Unity MCP bridge (`execute_menu_item`). Characters come from one CC0 base family (Quaternius UBC + Fantasy outfits + UAL1/UAL2 clips) assembled and recoloured by headless Blender scripts, imported as Humanoid.

**Tech Stack:** Unity 6000.3.25f1, URP 17.3, Input System 1.20 (code-defined actions), Cinemachine 3.1.7, uGUI + TextMeshPro (in ugui 2.0), Unity Test Framework 1.6, Blender 5.2.2 (headless bpy), Node MCP client `tools/unity-mcp.mjs` → mcp-unity bridge.

**Spec:** `GDD.md` (repo root). Read §4 (systems), §5 (skills), §6.1 (Callum), §11.2 (slice gates), §11.3 (fairness), §11.5 (task list).

## Global Constraints

- **No random hit chances or crits anywhere in gameplay.** Only randomness: the per-run seed used to assemble levels (`DetRandom`, xorshift, never `UnityEngine.Random` in gameplay).
- **No numeric Rapport anywhere** in player-facing UI. A dev overlay exists only under `UNITY_EDITOR || DEVELOPMENT_BUILD` and is off by default.
- All numbers are starting values in data assets (`Tuning`, `SkillDefinition`, `MomentDefinition`), never literals buried in logic.
- One shared Humanoid rig for every character (Quaternius UE-style skeleton, 65 bones). No AI-generated meshes.
- Every third-party asset is CC0 and recorded in `Assets/_Game/THIRD_PARTY.md` with source URL.
- Camera: fixed-angle ~50°, both characters in frame, Cinemachine.
- Input: keyboard+mouse and gamepad for every verb.
- Performance: 60 fps on this M1 (16 GB) in the Editor Game view at 1080p for the slice.
- Tone: hero is sincere and competent; the joke is a rule meeting a situation. Barks are drafts for the owner to rewrite (AI-written text → Steam disclosure).
- Only macOS build support is installed; Windows/WebGL builds are out of reach until the owner installs those modules.

## File Structure (Assets/_Game)

| Folder | Responsibility |
|---|---|
| `Core/` | `SimLoop`, `ISimTickable`, `DetRandom`, `GameEvents`, `Health`, `DamageInfo`, `Agent`, `AgentRegistry`, `CharacterMotor`, `Tuning`, `GameFlow`, `RunState` |
| `Hero/` | `HeroBrain`, `IHeroRule`, `HeroRuleSet`, `RouteGraph`/`RouteFollower`, `Callum/*` rules, `HonorMeter`, `WitnessCone`, `InjurySystem`, `HeroCombat` |
| `Sidekick/` | `SidekickController`, `ISidekickCommands` (+ `PlayerCommands`, bots), `DodgeRoll`, `KnifeAttack`, `Ping`, `PresenceTracker`, `SidekickProgression` |
| `Enemies/` | `EnemyBrain`, `EnemyArchetype` data, `Detection`, archetype behaviours (Thug, Crossbowman, Turncoat, Ambusher, Brute, Archer, Ashgrave) |
| `Skills/` | `SkillDefinition` (SO), `SkillSystem`, `SkillContext`, `Skills/*` (PocketSand, LoosenBolt, QuietFeet, Crossbow, Bandage, CoverStory), `ArmableProp`, `Projectile` |
| `Rapport/` | `MomentDefinition` (SO), `OpportunityDirector`, `RapportLedger`, `StageEvaluator`, `PenaltyTable` |
| `Stones/` | `ChronicleStone`, `StoneSystem`, `CuratorIntel` |
| `Rooms/` | `RoomModule`, `RoomSpawnGroup`, `RoomAssembler`, `RoomLayouts` (authored data), `ExplorePoint`, `Threshold` |
| `Curator/` | `RiggedDuelDirector`, `CuratorLines` |
| `Campfire/` | `CampfireDirector`, scene variants |
| `UI/` | `Hud`, `SystemWindow`, `RuleIconDisplay`, `LevelUpPanel`, `RestoreMenu`, `PostMortem`, `OpeningCutscene`, `Barks` |
| `Presentation/` | `AnimDriver`, `VisualInterpolator`, `Vfx`, `Sfx`, `GroundCone` |
| `Editor/` | MCP-triggered builders: import pipeline, character prefab builder, animator builder, room prefab builder, scene builder, QA capture, balance harness runner |
| `Tests/EditMode`, `Tests/PlayMode` | asmdef'd tests |
| `Art/` | `Characters/` (Blender-exported FBX), `Animations/`, `Environment/`, `Props/`, `Shaders/Toon.shader`, `Materials/`, `Fonts/` |

Outside Assets: `tools/unity-mcp.mjs` (MCP client), `tools/blender/*.py` (headless pipelines), `ThirdParty/Quaternius/*` (raw CC0 downloads), `docs/qa/` (QA log + screenshots).

## QA Gate (applies to every task)

1. `recompile_scripts` → 0 errors, 0 warnings from `_Game` code.
2. `run_tests` (EditMode + PlayMode) → all green.
3. Play-mode smoke: enter play, run the task's scenario, `get_console_logs` → no errors/exceptions.
4. Visual evidence: `Tools/QA/Capture` screenshot(s) read back and inspected (missing/pink materials, z-fighting, T-poses, clipping, foot sliding, UI overlap, legibility).
5. Log result + issues found/fixed in `docs/qa/QA_LOG.md`.

---

### Task 0 (M0): Toolchain + scaffold
**Files:** `tools/unity-mcp.mjs` (done), `Assets/_Game/**` folders, `Game.Runtime.asmdef`, `Game.Editor.asmdef`, `Game.Tests.EditMode.asmdef`, `Game.Tests.PlayMode.asmdef`, `Packages/manifest.json` (+cinemachine 3.1.7), `Editor/QA/QaCapture.cs`, `Editor/Setup/ProjectSetup.cs`.
- [x] Add Cinemachine to manifest; resolve via `Client.Resolve()` menu.
- [x] Project settings: runInBackground, Input System only, linear colour, PC quality.
- [x] `Tools/QA/Capture Game View` → renders Main Camera to `docs/qa/shots/<name>.png`.
- [x] Acceptance: compile clean, a trivial EditMode test passes via MCP.

### Task 1: Scaffold, Input, Cinemachine, fixed-angle two-target camera
**Interfaces produced:** `GameInput` (code-defined actions: Move, Aim, Attack, Dodge, Ping, Interact, Crouch, Skill1..4, Pause, Confirm, Cancel, Sprint), `CameraRig.Build(Transform hero, Transform sidekick)`.
- [x] Test (PlayMode): two targets 20 m apart are both inside viewport after 1 s.
- [x] Implement Cinemachine camera: `CinemachineTargetGroup` + `CinemachineFollow` (world-space offset at 50° pitch) + `CinemachineGroupFraming` (dolly only, min 12 m, max 34 m).

### Task 2 (M0b): Character pipeline + locomotion
**Files:** `tools/blender/build_characters.py`, `tools/blender/recipes.json`, `tools/blender/make_props.py`, `tools/blender/make_salute.py`, `Editor/Import/CharacterImportPostprocessor.cs`, `Editor/Builders/AnimatorBuilder.cs`, `Presentation/AnimDriver.cs`, `Art/Shaders/Toon.shader`.
- [x] Blender: graft Superhero head onto Fantasy outfit bodies (identical head/neck bone positions verified), add hair/brows/eyes, recolour to palette, attach props to hand bones, export FBX. Characters: Sidekick, Callum, Thug, Crossbowman, Turncoat, Brute, Ashgrave (dull pendant), Archer.
- [x] Unity: Humanoid import, toon materials, Animator controller with ≥12 clips (Idle, Walk, Jog, Sprint, Crouch idle/walk, Roll, Hit_Chest, Hit_Head, Death, Sword attack ×3, Block, Salute (authored), Throw, Shoot, Fixing_Kneeling, Sitting).
- [x] Foot-slide fix: locomotion playback speed = actual speed / clip root speed (measured from `_RM` clips).
- [x] Acceptance: turntable screenshots of every character (no pink, no T-pose, props in hands); walk/run capture shows planted feet.

### Task 3: Sidekick controller
**Interfaces:** `SidekickController : Agent` with `ISidekickCommands` source; `DodgeRoll(charges 3, iframes 0.3 s)`; `KnifeAttack(8 dmg)`; `PresenceTracker.InSupportRange (25 m)`; `Ping.Mark(Vector3|Agent)`.
- [x] EditMode tests: dodge charges consume/recharge; knife deals 8; leash flag flips at 25 m.
- [x] PlayMode: scripted commands move 6 m/s ±5%.

### Task 4: HeroBrain framework
**Interfaces:** `IHeroRule { string Id; Sprite Icon; bool Evaluate(HeroContext); void Tick(HeroContext, float dt); }`, `HeroBrain.ActiveRuleId`, `RouteGraph` nodes with `Threshold` flag, `RouteFollower.PauseAtThreshold`.
- [x] EditMode: highest-priority satisfiable rule wins; route follower pauses at thresholds; icon id published.

### Task 5: Room modules + seeded assembler
**Interfaces:** `RoomModule` (entry/exit sockets, route nodes, spawn groups, props, stones, explore points, moment slots), `RoomAssembler.Assemble(int seed, ChapterDef) → AssembledChapter`.
- [x] EditMode: same seed ⇒ identical module list/variants/mirroring; different seeds ⇒ ≥2 distinct layouts across 10 seeds.
- [x] Room builder creates 4 Old Road modules + campfire + boss arena prefabs from CC0 kits.
- [x] Visual QA: overhead + gameplay-angle captures of each module.

### Task 6: Skill system
**Interfaces:** `SkillDefinition` (id, family, type, rank values, cooldown, dishonor tag, description of uses), `SkillSystem.Learn/Rank/Equip/TryActivate/CooldownRemaining`, slots 4/5/6 by chapter.
- [x] EditMode: slot limits, passives don't use slots, rank 2 on second pick, cooldown gating, loadout swap only at camp.

### Task 7: Six slice skills
Pocket Sand, Loosen Bolt, Quiet Feet, Crossbow, Bandage, Cover Story (numbers from GDD §5).
- [x] EditMode/PlayMode tests per skill (blind duration, armed-prop cap and trigger, detection radius 2 m while crouched, crossbow reload, bandage channel + minor-wound treatment, cover-story 3 s halving).

### Task 8: Callum S0/S1 rules, Honor, witness cone
- [x] Rules S0 (challenge ≤15 m w/ 1.2 s salute, fight challenged, wait ≤3 s on Unready, fall back at 3+ engagers, Honor <40 ⇒ −25% dmg + scold); S1 (halved minor Honor loss, wait cap 2 s).
- [x] Witness: 120° / 12 m with line of sight, or an active stone; Quiet Feet narrows it.
- [x] Riposte (Strike I, 2× counter).
- [x] Tests for each rule + witness geometry.

### Task 9: Opportunity Director + Rapport ledger
- [x] Moments (Callum): Unseen assist 3, Averted cheat 4, Covered lapse 2, Wound treated after a duel 2. Penalties: Caught −3/−6, Spoiled duel −2, Friendly fire −3, Abandon −3. Ch1 offer budget 60, penalty cap 30% of offered.
- [x] EditMode: capture-rate math, cap, "uncaptured still offered", honest failure not penalised.

### Task 10: Stage evaluator
- [x] Thresholds 30/55/75; caps by check (Ch2≤S1, Ch3≤S2, door≤S3); rise ≤+1 Ch3→door; fall ≤1 per check. Exhaustive EditMode tests.

### Task 11: Wound system
- [x] ≥25% max-HP hit ⇒ wound (deterministic type from damage kind), 3+ ⇒ Crippled −30% speed, maluses per GDD table, treatment (Bandage minor, camp −1). Tests.

### Task 12: Stone system
- [x] Dormant/Active/Broken, activates on Callum's formal duel in range, witness LOS, breakable (knife), records flaw behaviour ⇒ Curator Intel 0–3. Tests.

### Task 13: Mini rigged-duel boss
- [x] Lord Ashgrave, octagon + Oath Glyph + 3 hidden archers, "no aid" terms, fixed volley signal at T+25 s; S0 orders you out and ignores volleys, S1 guards after first volley; Curator diagnosis on loss; Post-Mortem; Restore menu.

### Task 14: Campfire (two variants)
- [x] Rest (−1 wound), level-up pick, loadout swap, Stage check (slice acts as Ch2-style check, max S1), cold (S0) vs warm (S1) Callum scene, Restore Point snapshot.

### Task 15: Test harness
- [x] Determinism test: same seed + same scripted commands ⇒ identical state hash after N ticks.
- [x] Bots: Idle, Sloppy, Supportive; batch runner writes CSV to `docs/qa/balance/`.

### Final: Integrated QA
- [x] Full playthrough(s) by bots + scripted player, screenshots per room.
- [x] Report 1 — professional game developer: bugs, textures, animation, perf, UX defects; fix all blockers.
- [x] Report 2 — skeptical player: is the loop fun? Evidence-based critique + changes made + open recommendations.

---

## Execution notes — deviations from this plan (2026-10-01)

All tasks are complete; per-task evidence is in `docs/qa/QA_LOG.md`, final reports in `docs/qa/REPORT_1_GAME_DEVELOPER.md`
and `docs/qa/REPORT_2_SKEPTICAL_PLAYER.md`. Deliberate differences:

- **Camera:** max dolly 28 m (characters stay ≥100 px tall at 1080p), plus an axis-aware leash beyond what the frame can fit
  (the player's sidekick keeps the frame; the hero gets an edge marker) and a look-ahead target along the route.
- **Animation set:** no sprint (it could not be made foot-slide-free; jog is rate-matched) and no sitting clip in the CC0
  libraries (the camp uses kneel/stand). Measured planted-foot slip: jog 0.45, walk 0.37, crouch 0.56 m/s (gate 0.6).
- **Balance (from the harness, Task 15):** Ch1 bandit damage retuned, melee attack tokens, second wind on reaching the
  next room, Ashgrave 1250 HP (the duel must outlast the T+25 s signal), crouch speed 1.6.
- **Rapport:** `Withdraw` for Moments that became uncapturable (his own duel opponent); ambush Moments open at 26 m so
  scouting ahead is credited; hidden gallery archers count as Unseen-assist Moments.
- **Hazards:** made live (they were data-only); spike plates wound, tripwires stumble and raise the alarm.
- **Opening (GDD §8):** first shipped without beat 1 (the walk with earbuds, the truck, the white screen) and beat 4
  ("HERO" becoming his name), at ~10 s; completed afterwards (QA_LOG "Follow-up: the opening's missing beats").
- **Beyond the plan:** code-built UI layer (HUD, bubbles, System window, picker, end screens), the game flow with Restore
  Points, a macOS player build with an automated perf run, and an audio layer (CC0 Kenney + self-made synthesis).
