# Chapter 3: Catacombs of Ends Implementation Plan (Plan 3 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chapter 3 becomes an underground crypt with its own pressures — information (traps hidden in the dark), seals, and a social scout — plus Callum's Finisher I (Judgment), his S2 rules (Look Away), the dossier scrap at its campfire, and four skills (Read Runes, Lockpick, Map Sketch, Buckler).

**Architecture:** As Plan 2: small mechanics units next to the systems they extend, tested headless first; then a `CatacombRooms` editor builder (open-topped crypt dioramas with torch light), new cast through the recipe pipeline, `ChapterDef.Catacombs()` switched to `ModuleChapter = 3`.

**Tech Stack:** Unity 6000.3 (URP), C#, NUnit; Blender 5.2 headless + Python/PIL.

**Spec:** `docs/superpowers/specs/2026-10-04-callum-campaign-design.md` (§4 Chapter 3; §3.2 `SealDoor`, `HiddenHazard`, `CallumRules` S2, `CallumModule` Finisher; §5; §6).

## Global Constraints

- Plans 1–2's constraints hold.
- Seals never soft-lock: each `SealDoor` opens to Read Runes (channel), Lockpick (iron gates only), a seal key found in the room, or — after he has waited 8 s at it — Callum breaking it: the ward pulses (a serious wound for him) and two ward guardians wake.
- Hidden plates are invisible to the player until the sidekick is within 2.5 m, Map Sketch is up, or Pocket Sand lands on them; Callum never sees them.
- Judgment (Finisher I): a 3 s charge then one blow for 6× his damage; a hit of 10%+ of his max HP during the charge breaks it; 20 s between attempts; he begins it on a challenged foe under 60% HP. Finisher II (chapter 5): 2 s charge.
- S2 (GDD §6.1): **Look Away** — Cover Story, or a ping on Callum himself, makes him turn his back for 3 s: no witness checks; "Chosen blindness" Moment (2). Waiting cap 1 s against a flagged cheater (2 s otherwise).
- The dossier scrap (GDD §8, Callum's) is read at the chapter 3 campfire and kept in the run's dossier.

---

### Task 1: Hidden plates, seals, the seal key
**Files:** `Rooms/HazardMarker.cs` (`Hidden`, `Revealed`, `Reveal()`; renderers off until revealed; the sidekick within 2.5 m reveals; Pocket Sand reveals; disarm only once revealed), `Rooms/SealDoor.cs` (an `IInteractable` barrier at a threshold: `Kind` Rune / Gate; `Open(string how)`; a `BoxCollider` blocker; holds the hero's route at its threshold until open; after 8 s he breaks it: `SealBroken` → serious wound + guardians wake), `Rooms/SealKey.cs` (an `ExploreAnchor` flavour: searching it opens the room's seal), `Skills/Impl/SliceSkills.cs` (Pocket Sand reveals plates), `Rooms/EncounterDirector.cs` (guardians spawn dormant, wake on `SealBroken`).
**Tests:** `Tests/PlayMode/CatacombTests.cs` — plate hidden until she's close; sand reveals; he springs an unrevealed plate; a key opens the seal; he breaks a seal after 8 s, takes a serious wound, guardians wake; the route waits at a closed seal.

### Task 2: Judgment (Finisher I/II)
**Files:** `Hero/Callum/CallumModule.cs` (`FinisherCharging`, `FinisherProgress`, `BeginFinisher()`, break on a big hit), `Hero/Callum/CallumRules.cs` (`callum_finisher` rule between fight and wait), `Core/Tuning.cs` (`finisherMul 6`, `finisherCharge 3`, `finisherChargeII 2`, `finisherCooldown 20`, `finisherBreakFraction 0.1`, `finisherBelow 0.6`), `UI/HudView.cs` (hero card "JUDGMENT" charge chip), `Editor/Builders/GameplayPrefabBuilder.cs` (rule order).
**Tests:** `CampaignTests` — chapter 3 Callum charges Judgment on a foe under 60% and lands 6× damage; a big hit during the charge breaks it; chapter 2 never uses it.

### Task 3: S2 — Look Away and the 1 s cap
**Files:** `Hero/Callum/CallumModule.cs` (`LookingAway`, `LookAway(float)`; `Sees` returns false while looking away), `Hero/Callum/CallumRules.cs` (`callum_look_away` rule: turns his back, holds), `Rapport/CallumDance.cs` ("Chosen blindness" Moment offered when he looks away, captured by an unseen helpful deed inside the window), `Skills/Impl/SliceSkills.cs` (Cover Story at S2+ triggers it), ping on Callum at S2+ triggers it, `WaitUnreadyRule` cap from args per cheater, rule set S2/S3 lists.
**Tests:** `CallumTests` — S2 ping on him → back turned 3 s, a dirty trick in that window isn't witnessed, Moment captured; S1 ping does nothing; S2 waits 1 s on a blinded cheater, 2 s on an honest foe.

### Task 4: Read Runes, Lockpick, Map Sketch, Buckler
**Files:** `Skills/Impl/CatacombSkills.cs`, data/guides/synergies/icons/pending demos as in Plan 2.
Numbers (rank 1 / rank 2): Read Runes — CD 6/4, channel 2/1.2 s, reach 3 m; Lockpick — CD 6/4, channel 3/2 s, rank 2 also cuts a revealed trap from 2.5 m instantly; Map Sketch — CD 24/18, lasts 10/14 s, radius 30/40 m (reveals plates and shows his path as a dotted line); Buckler — CD 6/5, a 1.2/1.5 s block: frontal melee blows on her are parried (attacker staggered 1 s), rank 2 also catches projectiles aimed at him within 2 m of her.
**Tests:** `CatacombSkillTests`.

### Task 5: The Prisoner (scout) and the dossier scrap
**Files:** `Curator/Scout.cs` (lines and gift per scout id: the Prisoner asks to be freed; freeing him is the interaction; Callum calls to free him), `Curator/Dossier.cs` (`CallumScrap`), `Flow/CampfireDirector.cs` (chapter 3: the scrap is read — a System notice and a line from Callum by Stage), `Editor/Builders/GameplayPrefabBuilder.cs` (`Scout_prisoner`).
**Tests:** `ScoutTests` (prisoner unmask/free), `CampfireTests` (chapter 3 adds the scrap to the dossier).

### Task 6: The crypt cast
Recipes: `Cultist` (Male_Peasant, grey-violet robes, dagger, pale), `TombRobber` (Male_Peasant, dust-brown, dagger), `WardGuardian` (Male_Ranger, bone-white with pauldrons, axe, scale 1.15), `ShieldBearer` (Male_Ranger, iron grey, sword + shield), `Prisoner` (Male_Peasant, ragged, no weapon, pendant). `EnemyStats`: `cultist` (thug-like, 75 HP), `tomb_robber` (turncoat-like fake surrender), `ward_guardian` (190 HP, heavy 70), `shield_bearer` (130 HP, every 3rd blow a shove: 3 m knockback), `alcove_archer` (archer stats, `Archer_Visual`).

### Task 7: The crypt rooms
`Editor/Builders/CatacombRooms.cs`, five modules (`sealed_vault` Seal — always 4th; `dark_gallery` TrapCorridor; `crypt_of_sleepers` Combat; `prisoners_cell` Social — always 2nd; `bone_bridge` SetPiece), crypt floor/wall textures, torch point lights (≤ 4 per room), open tops with low near walls; `PitZone` for the bone bridge (a fall: serious wound, climbs back after 2.5 s unless Pull Back). `ChapterDef.Catacombs()` → `ModuleChapter = 3`, slots `[Combat, TrapCorridor]`, `[Social]`, `[TrapCorridor, SetPiece, Combat]`, `[Seal]`. Registry; tests; QA captures; soft-lock harness test.

### Task 8: Lessons, balance, docs, ship
Lessons: `seal`, `hidden_plate`, `finisher`, `look_away` (S2 behaviour shown, never named as a Stage), `prisoner`. Balance batch chapter 3 (idle solo target ~30%). Full suites, QA_LOG, GDD note, push, merge `main` if moved, fast-forward `main`.
