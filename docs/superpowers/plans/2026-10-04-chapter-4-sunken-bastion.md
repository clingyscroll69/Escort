# Chapter 4: The Sunken Bastion Implementation Plan (Plan 4 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chapter 4 becomes a flooded fortress with its own pressures — adaptation (the nemesis squad grows with Curator Intel), a split threat (the sluice), hostages, wading — plus Callum's Strike II and Stance II, Darian Wren the scout, four skills (Bait & Switch, Smoke Bomb, Pep Talk, Shoulder Check) and the capstone reveal at its campfire (Domino Effect, Crossfire, Hold Please, Silent Partner, each with its Duet form for Plan 5).

**Architecture:** As Plans 2–3: mechanics as small units next to the systems they extend, then a `BastionRooms` editor builder and `ChapterDef.SunkenBastion()` switched to `ModuleChapter = 4`. Two changes of method, both because the build machine for this plan has no Unity editor:
1. **Cast by alias.** A new archetype needs no new prefab: `CastFactory` spawns the registered prefab if there is one, otherwise the prefab of an existing body (`EnemyStats.body`) tinted by `EnemyStats.tint` (Toon `_BaseColor` through a property block). `GameplayPrefabBuilder` still lists every archetype, so a rebuild registers real prefabs and the alias is never used.
2. **Rooms fall back.** Until `Tools/HS/Build/Rooms · Sunken Bastion` has run, `ChapterBuilder` finds no chapter 4 modules and assembles the chapter from the Old Road's (as the spine did), under the Bastion's light. `Tools/HS/Build/Chapters 4–5 (everything)` runs every builder these plans add.

**Tech Stack:** Unity 6000.3 (URP), C#, NUnit; Python/PIL for icons and ground textures.

**Spec:** `docs/superpowers/specs/2026-10-04-callum-campaign-design.md` (§4 Chapter 4; §3.2 `NemesisSquad`, `SluiceWheel`, `Hostage`, `Capstones`; §6; §7; §8).

## Global Constraints

- Plans 1–3's constraints hold (tiers, determinism, no soft-locks, draft copy, the spoiler scan).
- **Wading** (Flooded Gate, the sluice's lower floor once flooded): a `BogZone` in water mode — him ×0.75, everyone else ×0.85; flooded, him ×0.7.
- **Nemesis squad "Rigged Gauntlet"** (GDD §6.1): a `SpawnMarker` carries `MinIntel` (0–3); a room spawns the markers whose `MinIntel` ≤ the Curator's Intel level when the chapter is built. Base squad at Intel 0; each level adds one cheat (a turncoat, an extra hostage archer, a baiter). Bastion archers reload 15% faster per Intel level (×0.85ⁿ).
- **Hostage:** a neutral civilian bound to the archer standing within 2.2 m behind her. While she shields him, Callum will not challenge that archer (he names it), and the archer never holds the room gate (he is the sidekick's problem, like a perch). She is freed by: Interact beside her (1.2 s), the captor's death, or the captor being staggered or blinded (she bolts). Any harm she takes from the sidekick is a Major dishonour if witnessed and closes her Moment. Freeing a held hostage is the *Averted cheat* Moment (4).
- **Challenge-baiters:** accept his challenge, then back away for 6 s (never swinging) while hidden *drowned ambushers* wait in the water along the causeway; the hero walking past springs them (the existing ambush rule).
- **The sluice (split threat):** a wheel on the upper gallery; living, unhindered crew within 2.5 m crank it (40 s of work in total, parallel work doesn't stack). Done: the lower floor floods — wading ×0.7 for him and 3% of his max HP every 3 s while he stands in it (tag `flood`: no wound). The crew is stopped by killing, blinding or staggering them, or by jamming the wheel (Interact 3 s, once). The crew never hold the gate and Callum never climbs to them (`Unchallengeable`). Never a soft-lock: the flood is a cost, not a door.
- **Darian Wren** (GDD §4.3): a scout on the rampart (`scout_wren`): Callum's visual, gold tint; ping within 3 s or Read the Room unmasks him (−1 Intel, a fragment); a ration as a gift.
- **Skills** (rank 1 / rank 2; conduct):

| Skill | Family | CD | A | B | Conduct |
|---|---|---|---|---|---|
| bait_and_switch | Fixer | 16 / 13 | lure 4 / 6 s | radius 8 / 8 m | above board (never lures his duel opponent) |
| smoke_bomb | Provisioner | 20 / 16 | lasts 6 / 8 s | radius 3 / 3.6 m | above board |
| pep_talk | Handler | 25 / 20 | +15% / +20% damage and speed | 8 / 10 s | above board (within 14 m) |
| shoulder_check | Combat | 7 / 6 | knockback 3 / 4 m | stagger 1.2 / 1.6 s | a slight if seen in his duel |

  Smoke blocks every line of sight through it (Callum's, a stone's, a shooter's) and wipes the aggro of anyone inside it. A decoy is a target for enemies within B m for A s (not for his duel opponent); the Mirror (Plan 5) counts it as two engagers.
- **Capstones** (revealed at the chapter 4 campfire, one for the run, own key: X / R3; never a loadout slot):

| Capstone | Type | Use | Duet form (Plan 5) |
|---|---|---|---|
| domino_effect | Active, CD 45 s | Arms up to 2 idle props within 12 m, then brings down every armed prop within 40 m | Every armed prop collapses on the Mirror (needs 2+) |
| crossfire | Active, CD 30 s | A volley of three bolts (20 + 10 × your attack ranks each); if he is charging Judgment, it hits his target and adds to his blow | Bonus damage by your attack ranks |
| hold_please | Active, CD 60 s | Everyone but you is held for 4 s (stunned) | The Mirror held 4 s; the Link window doubles (2 s) |
| silent_partner | Passive | Within 12 m of you he regains 0.6% of his max HP a second, and every 45 s one minor wound is tended | He heals 25%; the window widens to 1.6 s |

- All copy is draft text for the owner. Lessons never name the hidden stat.

All paths are relative to `Escort/Assets/_Game/` unless they start with `docs/`, `tools/` or `Escort/`.

---

### Task 1: Cast by alias, tints, the enemy controller hook
**Files:** `Core/Tuning.cs` (`EnemyStats.body`, `tint`, `baiter`, `crew`; archetypes `bastion_soldier` 110 HP honest, `baiter` turncoat-like with `baiter`, `drowned_ambusher` ambusher-like, `bastion_archer` archer-like ground shooter, `sluice_crew` thug-like with `crew`), `Core/CastFactory.cs` (prefab or alias + tint; neutral bodies; hero-body copies), `Enemies/EnemyAgent.cs` (`Controller` hook: `IEnemyController.Tick` runs instead of the default AI when it returns true; `Unchallengeable`; `ReloadMul`; `FlaggedCheater`; `SwapStats`; `ConsumeParry`; tint on start; baiter backpedal), `Rooms/EncounterDirector.cs` (spawns through `CastFactory`; `MinIntel`; hostages; reload by Intel), `Rooms/SpawnMarker.cs` (`MinIntel`), `Hero/Callum/CallumModule.cs` (skips the unchallengeable and the shielded).
**Tests:** `BastionTests` — an unregistered archetype spawns on its body and keeps its own stats; a baiter backs away when challenged; the controller hook replaces the AI.

### Task 2: Wading, the sluice, hostages, the nemesis squad
**Files:** `Rooms/BogZone.cs` (`Water` mode: its own bark; `Flooded` gating), `Rooms/SluiceWheel.cs`, `Enemies/Hostage.cs`, `Rooms/NemesisSquad.cs` (pure: `Level(intel)`, `ReloadMul(intel)`, `Spawns(markerMinIntel, intel)`), `Hero/HeroAgent.cs` (`flood` damage never wounds), `Rapport/CallumDance.cs` (hostage Moment), `Rapport/PostMortem.cs` (hostage lines).
**Tests:** `BastionTests` — the crew cranks the wheel to a flood in 40 s; a jammed wheel never floods; blinded crew stop; the flood slows and hurts him without wounds; Callum won't challenge a shielded archer; freeing the hostage frees the shot and is a Moment; EditMode `NemesisSquadTests` — markers by Intel, reload by Intel.

### Task 3: Bait & Switch, Smoke Bomb, Pep Talk, Shoulder Check
**Files:** `Skills/Impl/BastionSkills.cs` (`BaitAndSwitchSkill` + `Decoy` agent, `SmokeBombSkill` + `SmokeCloud`, `PepTalkSkill`, `ShoulderCheckSkill`), `Hero/WitnessCone.cs` (smoke blocks sight), `Enemies/EnemyAgent.cs` (decoys in `ChooseTarget`; smoke blinds shooters), `Hero/HeroAgent.cs` (Pep Talk buff), `Skills/SidekickSkills.cs`, `Data/Skills/*.asset` and `Editor/Builders/SkillDataBuilder.cs` (numbers, implemented), `Skills/SkillGuides.cs`, `Skills/SkillSynergies.cs`, `Tutorial/Demo/SkillDemos.cs` (`Pending`), `tools/make_icons.py` (icons).
**Tests:** `BastionSkillTests` — a decoy pulls bandits but not his opponent; smoke hides a deed from him and from a stone; Pep Talk raises his damage 15% for 8 s; Shoulder Check knocks back and staggers.

### Task 4: The capstones and their key
**Files:** `Skills/SkillSystem.cs` (`Capstone`, `CapstoneSlot`, `TryActivateCapstone`), `Skills/Impl/Capstones.cs`, `Core/GameInput.cs` (`Capstone`: X, R3), `Sidekick/PlayerCommands.cs`, `Skills/SidekickSkills.cs`, `Tutorial/KeyGlyphs.cs` (`capstone`), `UI/HudView.cs` (capstone slot), `UI/CapstonePicker.cs`, `Flow/CampfireDirector.cs` (the reveal after the level-up; AutoPlay picks), `Flow/GameFlow.cs` (preset kit at chapter 5 includes one), data assets, guides.
**Tests:** `CapstoneTests` — one capstone for the run, never in a slot; Domino brings down armed props; Crossfire joins Judgment; Hold Please holds everyone else 4 s; Silent Partner mends him near her; the chapter 4 campfire offers the four.

### Task 5: Darian Wren
**Files:** `Curator/Scout.cs` (Wren already scripted; gift a ration), `Core/CastFactory.cs` (scout bodies by id: Wren wears Callum's visual, gold).
**Tests:** `ScoutTests` — Wren unmasks on an early ping.

### Task 6: The Bastion rooms
`Editor/Builders/BastionRooms.cs`, five modules (`flooded_gate` Combat; `sluice_works` SetPiece — always 3rd; `hostage_court` Ambush; `baiters_causeway` Ambush; `wrens_rampart` Social — always 2nd), wet stone, water and rampart textures (`tools/make_ground_textures.py`), open tops with low near walls, bastion caps. `ChapterDef.SunkenBastion()` → `ModuleChapter = 4`, slots `[Combat, Ambush]`, `[Social]`, `[SetPiece]`, `[Ambush, Combat]`, caps `bastion_start/end`. Registry (`GameAssetsBuilder`), `ChapterBuildTests.Bastion_Builds_Four_Rooms_With_Wren_And_The_Sluice`, a fallback test (no modules → the Old Road's).

### Task 7: Lessons, HUD, ship
Lessons: `wading`, `sluice`, `hostage`, `baiter`, `smoke`, `capstone`. Hero card: PEP TALK chip; sidekick card: the capstone slot. Bots: the supportive bot jams the wheel, frees hostages and fires its capstone at the Link ring (Plan 5). Docs: GDD §3 *Built* note, QA log. Full compile check.
