# Chapter 2: Whisperwood Implementation Plan (Plan 2 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chapter 2 becomes its own place with its own pressure: a forest of five modules (attrition and ambushes), hunger and rations, snares, sleeping poachers, Mr. Quill the scout, Callum's Stance I, four new sidekick skills, and — learned at its campfire — Recall, so from chapter 3 on the sidekick is Downed instead of dead.

**Architecture:** Mechanics land as small units next to the systems they extend (hazards, enemies, hero module, sidekick) and are tested headless before any art exists. The forest rooms are authored in code by a new editor builder (`WhisperwoodRooms`) on the existing `RoomKit`, registered in `GameAssets`, and `ChapterDef.Whisperwood()` switches to `ModuleChapter = 2`. New characters come from the existing recipe pipeline (headless Blender → recolour → `CharacterPrefabBuilder` → `GameplayPrefabBuilder`).

**Tech Stack:** Unity 6000.3 (URP), C#, NUnit; Blender 5.2 headless + Python/PIL for characters, icons and ground textures.

**Spec:** `docs/superpowers/specs/2026-10-04-callum-campaign-design.md` (§2 hunger and recovery; §3.2 `Hunger`, `Scout`, `Downed`+`Recall`; §4 Chapter 2; §6 skills; §7 art; §8 HUD and lessons).

## Global Constraints

- Everything in Plan 1's Global Constraints still holds (tiers, determinism, no soft-locks, draft copy, spoiler policy).
- Hunger: chapters 2–4. 0–100, drains over ~6 minutes of road (100 / 360 s) only while the flow is on the road; at 0 **Starving**: −20% damage and no between-room recovery. Feeding: hold Interact next to him, 1.5 s, out of combat, costs one ration; refills him and heals 10% of max HP. The sidekick carries up to 3 rations. Camp refills hunger.
- Recall (GDD §4.2 table, Callum's "Squire's Recall"): learned at the chapter 2 campfire; from then on the sidekick at 0 HP is **Downed** for 20 s. Callum's Recall per Stage — S0: 8 s channel, starts only when no hostile is within 10 m, he takes a wound; S1: 5 s, starts with 2 or fewer hostiles within 10 m; S2: 3 s, breaks off combat unless his Finisher is active, no wound; S3: 1.5 s, sprints even mid-fight, she rises at 50%. Otherwise she rises at 30%. At S0–S1 he won't leave an active challenge until the duel ends or the target is Unready. 2 uses per chapter. If he can't or won't before 20 s: she respawns at the room's entrance, he takes a wound, and the open Moments close.
- Mr. Quill (GDD §4.3 scouts): pinging him within 3 s of first sight, or Read the Room while he's in range, makes him flee: −1 Curator Intel and a dossier fragment. Otherwise he reports at the campfire: +1 Intel (2 clips relayed).
- Skills follow GDD §5 text; every new skill gets a guide, an icon, synergy notes where they interact, and spoiler-clean copy. Live demos come in Plan 6: `SkillDemos.Pending` lists them and the picker shows the card without a demo.

All paths are relative to `Escort/Assets/_Game/` unless they start with `docs/`, `tools/` or `Escort/`. Test commands run from the worktree root.

---

### Task 1: Snares and sleeping enemies

**Files:**
- Modify: `Rooms/HazardMarker.cs` (`HazardKind.Snare`: trips 1.6 s, 6% damage, always a sprained ankle on him; disarm "Snare cut.")
- Modify: `Hero/HeroAgent.cs` (`OnHurt`: a `"snare"` tag wounds with `SprainedAnkle`)
- Modify: `Enemies/EnemyAgent.cs` (`StartsAsleep`: `Sleeping` status until challenged, struck, or the hero passes within 3 m; then a 1.6 s get-up)
- Modify: `Rooms/EncounterDirector.cs` (pass `SpawnMarker.Sleeping`)
- Modify: `Hero/Callum/CallumModule.cs` (`StartChallenge` wakes sleepers within 8 m: a knight's challenge is loud)
- Test: `Tests/PlayMode/WhisperwoodTests.cs`

**Interfaces:**
- Produces: `HazardKind.Snare`; `EnemyAgent.StartsAsleep`, `EnemyAgent.Asleep`, `EnemyAgent.Wake(string why)`.

- [ ] **Step 1: Failing tests** — `Snare_Trips_Him_And_Sprains_His_Ankle` (hero walks across an armed snare: `Staggered` for ≥1.5 s, `Wounds.All` contains `SprainedAnkle`, damage = round(6% of max)); `A_Careful_Sidekick_Steps_Over_A_Snare`; `Sleepers_Wake_When_He_Challenges_Them` (a sleeping thug is `IsHelpless(hero)` and Unready; after `StartChallenge` on a nearby enemy it is awake within 2 s); `A_Knife_In_A_Sleeper_Is_Major_If_Seen` (sidekick stabs a sleeper in his cone → Honor −20).
- [ ] **Step 2:** `tools/unity-tests.sh PlayMode WhisperwoodTests` → compile errors.
- [ ] **Step 3: Implement.** Snare springs like a plate (radius trigger) with `DamageInfo.Make(null, victim, round(max*0.06), DamageKind.Trap, "snare", 1.6f)`; its visual is a rope loop (`Rope_1`) on two stakes. `HeroAgent.OnHurt`: `Wounds.Add(d.Tag == "snare" ? WoundType.SprainedAnkle : WoundSet.TypeFor(d.Kind))`. `EnemyAgent`: in `Start`, if `StartsAsleep` apply `Sleeping` (∞) and play `"sleep"` (fall back to `"kneel"` if the animator lacks it); in `OnSimTick` Dormant branch wake when the hero is within 3 m; `Wake()` clears `Sleeping`, plays `"getup"`, sets a 1.6 s `_joinT`; any damage wakes (after the event, so the blow is judged on a sleeper).
- [ ] **Step 4:** tests pass; `CallumTests|RapportTests` unchanged.
- [ ] **Step 5:** commit "Whisperwood: snares and sleeping enemies".

---

### Task 2: Hunger and rations

**Files:**
- Create: `Hero/Hunger.cs` (plain C#: `Value`, `Starving`, `Enabled`, `Tick(dt)`, `Feed()`, `Refill()`, `DrainPerSecond = 100/360`)
- Create: `Sidekick/Rations.cs` (count 0–3, `Take()`, `Give(n)`)
- Create: `Hero/FeedInteraction.cs` (an `IInteractable` on the hero: "Feed him (ration)", 1.5 s, needs a ration and no fight — `CallumModule.Challenged == null && Engagers == 0`)
- Modify: `Hero/HeroAgent.cs` (owns `Hunger`; ticks it), `Hero/Callum/CallumModule.cs` (`OutgoingDamage` × 0.8 while Starving; recovery 0 while Starving), `Rooms/ExploreAnchor.cs` (`Rations` field: searching gives that many), `Flow/GameFlow.cs` (enable per `CampaignSchedule.For(ch).Hunger`, pause off the road; snapshot/restore hunger + rations), `Flow/CampfireDirector.cs` (refill), `Flow/RunState.cs` (`Hunger`, `Rations`)
- Modify: `UI/HudView.cs` (hero card: a "HUNGRY" chip under 30, "STARVING" at 0; sidekick card: "RATIONS n" chip when n > 0 or hunger is on)
- Test: `Tests/EditMode/HungerTests.cs`, `Tests/PlayMode/WhisperwoodTests.cs`

**Interfaces:**
- Produces: `HeroAgent.Hunger` (`HS.Hero.Hunger`), `SidekickAgent.Rations` (`HS.Sidekick.Rations`), `ExploreAnchor.Rations`.

- [ ] **Step 1: Failing tests** — EditMode: drains 100 → 0 in 360 s, clamps, `Starving` at 0, `Feed` refills, disabled hunger never drains. PlayMode: `Starving_Callum_Hits_For_80_Percent`; `Feeding_Takes_A_Ration_And_Needs_A_Lull` (can't feed while he duels; can after; ration count drops; hunger 100); `A_Forage_Cache_Gives_A_Ration`; `Restore_Brings_Back_Hunger_And_Rations`.
- [ ] **Step 2:** run → compile errors.
- [ ] **Step 3: Implement** as listed. Chips use `UIKit.Chip` like the wound chips; colours `UIKit.Gold` (hungry) and `UIKit.Danger` (starving).
- [ ] **Step 4:** tests pass.
- [ ] **Step 5:** commit "Whisperwood: hunger, rations and feeding".

---

### Task 3: Stance I, Unyielding

**Files:** Modify `Hero/Callum/CallumModule.cs` (`ModifyIncoming`: a blow from his challenged opponent × 0.7 with `StanceI`, × 0.6 with `StanceII`); Test `Tests/PlayMode/CampaignTests.cs`.

- [ ] **Step 1:** failing test `Unyielding_Softens_His_Opponents_Blows` (chapter 2 unlocks: 100 from the challenged foe → 70 × tier; from another foe → 100 × tier; chapter 4: 60).
- [ ] **Steps 2–4:** implement in `ModifyIncoming` before the arrow guard: `if (d.Source != null && d.Source == Challenged && (Unlocks & (Signature.StanceI | Signature.StanceII)) != 0) amount *= (Unlocks & Signature.StanceII) != 0 ? 0.6f : 0.7f;` (keep the arrow logic working on the modified amount).
- [ ] **Step 5:** commit "Callum: Stance I and II (Unyielding)".

---

### Task 4: Downed and Recall

**Files:**
- Create: `Hero/Callum/RecallRule.cs` (`callum_recall` rule: top priority; per-Stage numbers from args `[channel, maxHostiles, wound(0/1), riseFraction, breaksDuel(0/1)]`)
- Create: `Sidekick/Downed.cs` (plain C#: 20 s timer, `Begin`, `Tick`, `Rise(fraction)`, `TimedOut`)
- Modify: `Sidekick/SidekickAgent.cs` (when `CanBeDowned`, HP 0 → Downed instead of dead: `IsDowned`, no input, untargetable, `Revive`); `Core/Agent.cs` if `IsAlive` must stay true while downed
- Modify: `Enemies/EnemyAgent.cs` (`ChooseTarget` skips a downed sidekick)
- Modify: `Flow/GameFlow.cs` (`RecallLearned`, `RecallUsesLeft` (2 per chapter); end-of-run only when she dies without being downable; a timed-out Downed respawns her at the current room's entrance at 30%, wounds him, closes open Moments via `OpportunityDirector.EndOfFight("downed")`), `Flow/RunState.cs` (`RecallLearned`), `Flow/CampfireDirector.cs` (chapter 2: Callum's line by Stage; GDD: his first reaction is an S0 or S1 line), `Editor/Builders/GameplayPrefabBuilder.cs` (`EnsureCallumRuleSet` puts `callum_recall` first in every Stage list, with S2/S3 lists authored as copies of S1 until Plan 3)
- Modify: `UI/HudView.cs` (sidekick card: "DOWNED 14 s" chip; "RECALL ×2" chip once learned)
- Test: `Tests/PlayMode/RecallTests.cs`

**Interfaces:**
- Produces: `SidekickAgent.IsDowned`, `SidekickAgent.CanBeDowned`, `SidekickAgent.DownedRemaining`, `SidekickAgent.DownedEvent`; `GameFlow.RecallLearned`, `GameFlow.RecallUsesLeft`; rule id `callum_recall`.

- [ ] **Step 1: Failing tests** — `Before_Recall_Is_Learned_Death_Ends_The_Run` (chapter 2 flow: HP 0 → outcome `sidekick_died`); `After_Recall_She_Is_Downed_Not_Dead` (chapter 3 flow with `RecallLearned`); `S2_Callum_Recalls_In_3_Seconds_Without_A_Wound`; `S0_Callum_Waits_For_A_Lull_And_Takes_A_Wound` (a hostile within 10 m keeps him from starting); `Timing_Out_Respawns_Her_At_The_Room_Entrance_And_Wounds_Him`; `Two_Recalls_Per_Chapter`.
- [ ] **Step 2:** run → compile errors.
- [ ] **Step 3: Implement.** Rule `CanRun`: sidekick downed, uses left, the Stage condition holds (hostiles within 10 m ≤ maxHostiles; at S0–S1 not while `Challenged` is set and not Unready), and the downed timer has > channel left. `Tick`: move to her at combat speed (sprint at S3), then channel (hold still; `Presenter.PlayAction("kneel")`), then `sk.Revive(rise)`, wound if args say so, bark the Stage line ("This is so useless. Get up." / "...Fine. Don't make it a habit." / "Up. I've got you." / "I'm not losing you. Not today."), `GameFlow.RecallUsesLeft--`.
- [ ] **Step 4:** `tools/unity-tests.sh PlayMode "RecallTests|CampaignTests|CallumTests|SidekickTests"` pass; rebuild prefabs: `tools/unity-tests.sh method HS.EditorTools.GameplayPrefabBuilder.BuildAll`.
- [ ] **Step 5:** commit "Recall: Downed instead of dead after chapter 2; Callum's Recall by Stage".

---

### Task 5: Four skills — Splint & Stitch, Pull Back, Sling, Read the Room

**Files:**
- Create: `Skills/Impl/WhisperwoodSkills.cs` (`SplintAndStitchSkill`, `PullBackSkill`, `SlingSkill`, `ReadTheRoomSkill`)
- Create: `UI/IntentMarkers.cs` (world labels over enemies while Read the Room is up: "CHEAP SHOT", "AMBUSH", "AIMING", "HEAVY", plus a marker on hidden ones; scouts flagged "PENDANT")
- Modify: `Skills/SidekickSkills.cs` (register), `Editor/Builders/SkillDataBuilder.cs` (numbers, `implemented`), `Skills/SkillGuides.cs`, `Skills/SkillSynergies.cs`, `Tutorial/Demo/SkillDemos.cs` (`Pending`), `Tests/PlayMode/SkillDemoTests.cs` (skip pending), `tools/make_icons.py` (4 icons), `Rapport/CallumDance.cs` (wound Moment offered for a serious wound when she carries Splint & Stitch)
- Test: `Tests/PlayMode/WhisperwoodSkillTests.cs`, existing `TutorialDataTests` cover guides/icons/synergies

Numbers (rank 1 / rank 2), uses text per GDD §5:
| Skill | Family | CD | A | B | Conduct |
|---|---|---|---|---|---|
| splint_and_stitch | Provisioner | 30 / 24 | channel 6 / 4.5 s | uses per chapter 2 / 3 | above board |
| pull_back | Handler | 18 / 14 | reach 10 / 14 m | pull 4 / 5 m | above board |
| sling | Combat | 1.4 / 1.1 | damage 9 / 13 (×tier) | range 16 / 18 m | minor in his duel |
| read_the_room | Scholar | 20 / 16 | lasts 6 / 8 s | radius 20 / 26 m | above board |

- [ ] **Step 1: Failing tests** — `Splint_And_Stitch_Treats_A_Serious_Wound_And_Spends_A_Use`; `Pull_Back_Yanks_Him_Out_Of_A_Snare_And_Clears_The_Trip`; `Sling_Staggers_And_Scales_With_The_Chapter`; `Read_The_Room_Reveals_Intent_And_Hidden_Foes_Without_Springing_Them`.
- [ ] **Step 2:** run → compile errors.
- [ ] **Step 3: Implement**, run `tools/unity-tests.sh method HS.EditorTools.SkillDataBuilder.Build` and `python3 tools/make_icons.py`.
- [ ] **Step 4:** `tools/unity-tests.sh PlayMode "WhisperwoodSkillTests|SkillDemoTests|SliceSkillTests"` and `tools/unity-tests.sh EditMode TutorialData` pass.
- [ ] **Step 5:** commit "Skills: Splint & Stitch, Pull Back, Sling, Read the Room".

---

### Task 6: Scouts (Mr. Quill)

**Files:**
- Create: `Curator/Scout.cs` (a neutral `Agent` with the dull pendant; `FirstSeenAt`, `Unmasked`, `Fled`; ping inside 3 s of first sight or `ReadTheRoom` in range → flee, `CuratorIntel.Unmask()`, a dossier fragment notice; interact once: "a ration for the road"; at camp, not unmasked → `Intel.Relay(2)`)
- Create: `Curator/Dossier.cs` (fragments collected this run; text per scout; snapshot)
- Modify: `Rooms/CuratorIntel.cs` (`Unmasked` count subtracts like `Forged`), `Rooms/SpawnMarker.cs` usage (archetype `"scout_quill"` spawns a `Scout` instead of an enemy), `Rooms/EncounterDirector.cs`, `Flow/CampfireDirector.cs` (report), `Flow/RunState.cs` (`Dossier`)
- Test: `Tests/PlayMode/ScoutTests.cs`

- [ ] **Step 1: Failing tests** — `Ping_Within_3s_Of_First_Sight_Unmasks_Him`; `A_Late_Ping_Is_Shrugged_Off`; `Read_The_Room_Unmasks_Him_Any_Time`; `Unmasked_He_Flees_And_Intel_Drops_A_Level`; `Unspotted_He_Reports_At_The_Campfire`; `He_Gives_One_Ration`.
- [ ] **Steps 2–4:** implement; tests pass.
- [ ] **Step 5:** commit "Curator: scouts — Mr. Quill and the dull pendant".

---

### Task 7: The Whisperwood cast (characters)

**Files:**
- Modify: `tools/blender/recipes.json` (`Poacher`: Male_Ranger, T_Ranger_3, moss/brown recolour, crossbow; `Woodsman`: Male_Peasant, bark-brown, axe, beard, scale 1.12; `Quill`: Male_Peasant, T_Peasant_2, plum coat, Long hair, `chest: Pendant`)
- Modify: `Core/Tuning.cs` (`EnemyStats`: `poacher` — ranged cheater, 45 HP, 14 dmg, range 18, aim 1.0, reload 3.8; `woodsman` — brute variant, 150 HP, heavy 60 every 4th; `fern_ambusher` — ambusher variant, 60 HP, ambush 44; all chapter-1 baselines that the tier scales)
- Modify: `Editor/Builders/GameplayPrefabBuilder.cs` (enemy list + a `Scout_quill` prefab), `Editor/Builders/GameAssetsBuilder.cs` (registry)
- Output: `Art/Characters/Poacher.fbx`, `Woodsman.fbx`, `Quill.fbx`, textures, `Prefabs/Characters/*_Visual.prefab`, `Prefabs/Gameplay/Enemy_poacher.prefab` …

- [ ] **Step 1:** add the recipes; run `/Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/build_characters.py -- tools/blender/recipes.json Escort/Assets/_Game/Art/Characters build_art/previews <Name>` for each; `python3 tools/recolor_textures.py`; look at the previews.
- [ ] **Step 2:** Unity: `tools/unity-tests.sh method HS.EditorTools.CharacterPrefabBuilder.BuildAll`, then `GameplayPrefabBuilder.BuildAll`, then `GameAssetsBuilder.Build`.
- [ ] **Step 3:** a lineup capture (`Tools/HS/QA/Lineup` builder) to check silhouettes read at the game camera; `FootSlideTests` still pass.
- [ ] **Step 4:** commit "Whisperwood cast: poacher, woodsman, Mr. Quill".

---

### Task 8: The Whisperwood rooms

**Files:**
- Create: `Editor/Builders/WhisperwoodRooms.cs` (five modules on `RoomKit`, `Chapter = 2`, ground `T_Ground_Forest`)
- Create: `Rooms/BogZone.cs` (slows agents inside: hero ×0.6, others ×0.8; a dark wet decal)
- Modify: `tools/make_ground_textures.py` (forest floor, bog), `Editor/Builders/GameAssetsBuilder.cs` (modules), `Rooms/RoomAssembler.cs` (`Whisperwood()` → `ModuleChapter = 2`, slots below), `Tests/EditMode/RoomAssemblerTests.cs` (library per chapter)
- Test: `Tests/PlayMode/ChapterBuildTests.cs` (`Whisperwood_Builds_Four_Rooms_With_Quill`), `Tests/PlayMode/CampaignTests.cs` (idle bot finishes chapter 2 without a soft-lock)

Modules (each ~40–46 m long, one threshold, route and encounter like chapter 1's):
| Id | Kind | Content |
|---|---|---|
| `snare_line` | TrapCorridor | Three snares on the path, two poachers on tree perches (wood platforms, as the gallery perches), a forage cache. |
| `fern_hollow` | Ambush | Ferns and twisted trees; three fern ambushers hidden (variant 1 adds a poacher); a forage cache. |
| `mire_crossing` | SetPiece | Two bog patches over the path; a woodsman and two thugs across; a fallen tree to arm. |
| `quills_glade` | Social | A clearing with Mr. Quill's cart (scout spawn), a stone that watches the road, no fight in variant 0; a thug pair in variant 1. |
| `poacher_camp` | Combat | Two sleeping poachers by a fire, a woodsman, a turncoat; a log pile to arm; a stone. |

`ChapterDef.Whisperwood()` slots: `[Ambush, Combat]`, `[Social]`, `[TrapCorridor, SetPiece]`, `[Combat, Ambush, SetPiece]`.

- [ ] **Step 1:** failing build test (`quills_glade` not found).
- [ ] **Step 2:** write the builder; generate textures; run `tools/unity-tests.sh method HS.EditorTools.WhisperwoodRooms.BuildAll` then `GameAssetsBuilder.Build`.
- [ ] **Step 3:** tests pass; idle bot reaches the camp or dies (never times out) on seeds 1–5.
- [ ] **Step 4:** QA captures of each module (`CampaignQaCaptures` gains `Whisperwood_Rooms`) — look at every image; fix readability (trees blocking the camera side, bog contrast, perches).
- [ ] **Step 5:** commit "Whisperwood: five forest modules".

---

### Task 9: Lessons and the HUD

**Files:** `Tutorial/Lessons.cs` (`hunger`, `feed`, `snare`, `sleeper`, `scout`, `downed`), `Tutorial/LessonTriggers.cs` (triggers; re-scan hazards and caches after each chapter build — they are cached per chapter), tests in `Tests/EditMode/TutorialDataTests.cs` (copy scan) and `Tests/PlayMode/TutorialTests.cs` (`Hunger_Lesson_Fires_When_He_First_Gets_Hungry`).

- [ ] Steps: failing tests → lessons + triggers → pass → commit "Tutorial: chapter 2 lessons".

---

### Task 10: Look at it, measure it, ship it

- [ ] `CATEGORY=QA tools/unity-tests.sh PlayMode CampaignQaCaptures` → inspect `campaign_ch2*.png`.
- [ ] Harness: `{"seeds":"1-10","bots":"idle,supportive","chapters":"2"}` → idle solo rate near 50% (GDD §3); record in `docs/qa/balance/`. Tune `EnemyStats` / snare damage if far off.
- [ ] Full suites; QA_LOG entry; GDD §3 *Built* note; push; merge `main` into the branch if it moved; fast-forward and push `main`; tell the owner.
