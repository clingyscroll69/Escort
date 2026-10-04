# Callum's full campaign: chapters 2–5 and the final boss (design)

Status: approved by the owner on 2026-10-04 ("Full campaign, lean mechanics"; the rigged duel moves to chapter 5;
work on a branch, pushed regularly, `main` fast-forwarded whenever a chapter becomes playable). GDD milestone M2
("Callum full Ch1–5"). Hero: Callum only. Bram, Ansel and Vaughn stay M3–M5.

## 1. Goals

1. **A complete run.** Opening → five chapters → the Gallery boss → an ending, about 45–55 minutes (GDD §3), with a
   Restore Point at every chapter start and before the Gallery door.
2. **Each chapter has its own pressure** (GDD §3 table), in a lean but real form: chapter 2 attrition, chapter 3
   information, seals and social, chapter 4 adaptation and split threats, chapter 5 the approach and the boss.
3. **The hidden stat matters end to end.** Stage checks at the end of chapter 2 (max S1), the end of chapter 3
   (max S2) and the door (max S3, at most +1 since the last check). Callum's S2 and S3 rule sets exist and are visibly
   different. The boss gradient (GDD §4.4) comes out of his rules, not special cases.
4. **The full final boss** (GDD §4.5, §4.5a, §6.1): Phase 0 diagnosis, Phase 1 the rigged duel with 6 hidden
   archers, Phase 2 Ashgrave without the pretence, Phase 3 the Mirror with Habit Breaks, shared-rule counters and
   the Duet Finisher, then the ending.
5. **No soft-locks.** Every obstacle a sidekick skill solves (seals, the sluice, hostages) also has a slower or
   costlier way for the hero through, so idle-bot and balance-harness runs always finish.

**Non-goals (this pass):** the other three heroes; the remaining 22 skills and 4 capstones; Field Kitchen and Forgery
footage; hardcore mode; hero select; live demos for the new skills (they come in the polish phase; until then the
picker shows the skill card without a demo); Steam/itch builds.

## 2. Decisions this spec makes (the GDD is silent or the slice differs)

| Topic | Decision | Why |
|---|---|---|
| Ch1 ending | The rigged duel leaves chapter 1. Chapter 1 ends at its campfire and the road goes on into chapter 2. | The trap stays fresh for the finale (owner's choice). |
| Ch1 campfire | No Stage check (checks are Ch2, Ch3, door, GDD §4.4). The scene picks cold/neutral from recent penalties. | GDD §4.6. |
| Power scaling | Callum: damage ×2.5 and HP ×2 per chapter (GDD §4.2). Enemies: HP ×2.5 and damage-to-hero ×2 per chapter, so hits-to-kill and hits-to-die against him stay constant. The sidekick's outgoing damage also ×2.5 per chapter (her HP grows with levels, +10 each). Status durations (blind, stagger, slow) never scale. | Without this, enemies are paper by chapter 3 and the sidekick's damage is irrelevant by chapter 4 (GDD §11.3.7: "combat skills are legitimate"). Difficulty grows from the pressure mix, as the GDD intends. |
| XP | Each room's pot comes from its module (`RoomModule.XpPot`, already a field) times a per-chapter factor, tuned so a thorough player reaches level 13 at the end of chapter 4 (≈ L3, L6, L9, L13). | GDD §4.1: 13 levels across Ch1–4, fixed pots. |
| Hunger | Chapters 2–4. A meter on Callum (0–100) that drains over about 6 minutes of road. At 0 he is **Starving**: −20% damage, no between-room recovery. You feed him a ration (hold Interact next to him, 1.5 s, out of combat). Rations come from forage caches and Mr. Quill; you carry up to 3. Camp refills him. | GDD Ch2 "attrition (wounds, hunger)" in a lean form; Field Kitchen stays later. |
| Between-room recovery | Ch1 50% of max HP, Ch2+ 30%, 0% while Starving. | Attrition. |
| Ping "cover" | At S2+, a ping on Callum himself means "cover" (no Signal Codes needed). | GDD §6.1 S2 "ping cover" without a skill gate. |
| Capstone slot | The capstone has its own key and slot; it never takes one of the 4–6 active slots. | Every build must reach the Duet Finisher. |
| Restore Points | Every chapter start reached this run, plus "before the Gallery door". Skills, level, Rapport, Intel, wounds restored to that snapshot. | GDD §11.3.5; the boss is long. |

## 3. Architecture

### 3.1 The campaign loop (one scene, rebuilt chapter by chapter)

`GameFlow` becomes a campaign: `Opening → (Chapter N → Camp N) × 4 → Chapter 5 approach → Door → Boss → End`.
The hero, the sidekick, `RunContext`, the ledger, the stone system and the skills are created once and survive every
chapter. Between chapters, at black:

1. `ChapterBootstrap.Teardown()` destroys the chapter's rooms, encounters, projectiles, props and stones.
2. `RunContext.Chapter` advances; the ledger's `Chapter` advances.
3. `CampaignSchedule.ApplyChapter(hero, sidekick, chapter)`: power tier, signature unlocks, slot count, hunger on/off,
   between-room recovery.
4. `ChapterBootstrap.BuildChapter(chapter, seed)` builds the next chapter from its `ChapterDef` and `ChapterTheme`,
   places the pair at its start and sets the hero's route.
5. A Restore Point snapshot is taken.

QA and tests can start a run at any chapter (`GameFlow.StartChapter`, plus a preset build and Stage) so later chapters
are testable without playing the earlier ones. An editor menu, **Tools/HS/Play from chapter N**, does the same for the
owner.

### 3.2 New and changed units

| Unit | Purpose |
|---|---|
| `CampaignSchedule` (Flow) | Static table per chapter: Callum's damage/HP multipliers, unlocks (Ch1 Strike I, Ch2 Stance I, Ch3 Finisher I, Ch4 Strike II + Stance II, Ch5 Finisher II), slots (4/5/6/6/6), which campfire runs which Stage check, XP factor, hunger, recovery. Pure C#, EditMode-tested. |
| `ChapterTier` (Core) | Enemy HP/damage multipliers and the sidekick's damage multiplier for the current chapter. Used by `EnemyAgent`, `Combat`, the skills. |
| `ChapterDef` (Rooms, extended) | Chapter number, name, slots (allowed room kinds), the module library filter, theme id, approach/boss flags. `OldRoad()`, `Whisperwood()`, `Catacombs()`, `SunkenBastion()`, `Gallery()`. |
| `ChapterTheme` (Presentation) | Sun colour/intensity/angle, ambient, fog, post-FX weights, music and ambience cues, ground materials. Applied on build. |
| `RoomModule.Chapter` | Which chapter a module belongs to; `ChapterBuilder` filters the library by it. |
| `CampfireScenes` (Flow) | Per chapter × variant (cold / neutral / warm) line sets (draft copy). `CampfireDirector` takes the check and the variant from the schedule instead of hard-coding the slice's. |
| `Hunger` (Hero) | Meter, Starving state, feed interaction, HUD chip. |
| `SealDoor` (Rooms) | Blocks a threshold. Solutions: Read Runes (channel), Lockpick (iron gates), a seal key from a cache, or the hero breaks it after 8 s (wards pulse: a wound, and 2 guardians wake). |
| `HiddenHazard` (Rooms) | A `HazardMarker` that is invisible until revealed (sidekick within 2.5 m, Map Sketch, Pocket Sand, Forecast). |
| `Scout` (Curator) | Shared scout behaviour: the dull-pendant tell, "first sight", the 3 s ping window, Read the Room reveal, flee (−1 Intel and a dossier fragment), or report at chapter end (+1 Intel). Mr. Quill, the Prisoner, Darian Wren are data on top. |
| `Dossier` (Curator) | Fragments from unmasked scouts and the chapter 3 scrap (GDD §8 text); shown at camp and in the Field Guide. |
| `NemesisSquad` (Rooms) | Spawn markers with `MinIntel`; chapter 4 rooms spawn more and better cheats as Curator Intel rises (0–3). |
| `SluiceWheel` (Rooms) | Chapter 4 split threat: a crew cranks it over 40 s; done, the lower floor floods (hero −30% speed, environment damage). Stop it by killing or blinding the crew, or jamming the wheel (Interact 3 s). |
| `Hostage` (Enemies) | A civilian an archer shelters behind. Callum won't strike through a hostage; your flank frees the shot. Harming the hostage is a Major dishonour. |
| `Downed` + `Recall` (Sidekick, Hero) | From chapter 3 on (learned at the chapter 2 campfire). Your HP 0 → Downed 20 s; Callum's Recall per Stage (GDD §4.2 table, Callum's "Squire's Recall": he won't leave an active challenge at S0–S1 until the duel ends or the target is Unready). 2 uses per chapter. Failing it: you respawn at the last door, he takes a wound, the room's open Moments close. |
| `CallumRules` (extended) | S2: **Look Away** (Cover Story or ping "cover": he turns his back 3 s, witness checks suspended; "Chosen blindness" Moment), waiting cap 1 s against dirty enemies. S3: **Fair to Cheat a Cheater** (no Honor loss against flagged cheaters; "A hand, friend?" opens Duet Windows during his Finisher; "Duet strike" Moment). |
| `CallumModule` (extended) | Stance (Unyielding −30% / −40% from the challenged enemy), Finisher (Judgment: 3 s charge, a huge single hit; a hit of 10%+ of his HP during the charge breaks it; Finisher II charges in 2 s), Strike II (Riposte 3×), `ApplyChapter`. |
| `GalleryBoss` (Boss) | Replaces the slice's `RiggedDuelDirector` as the boss director (§5); reuses its terms, archers, glyph and cause-of-death code. |
| `MirrorKnight` (Boss) | The Mirror: an `EnemyAgent` driven by a mirror-side copy of Callum's S0 rules through a new controller hook on `EnemyAgent`. |
| `DuetFinisher` (Boss) | The Link ring, the capstone duet forms, the miss/riposte loop. |
| `Capstones` (Skills) | Domino Effect, Crossfire, Hold Please, Silent Partner: normal use plus each one's Duet form. |

## 4. The chapters

All copy (barks, campfire scenes, notes, the dossier, the diagnosis) is draft text for the owner to rewrite (GDD §8).
Rooms are authored in code by editor builders (as `RoomBuilder` does for chapter 1), one builder per chapter. Indoor
chapters are **open-topped dioramas**: walls only, low or cut away on the camera side, so the fixed 50° camera always
reads the floor.

### Chapter 1: The Old Road (exists; changed)
- Unchanged rooms (3 of the 4 modules).
- The duel is removed. The campfire is chapter 1's end: rest, picks, the Code recital (cold if he caught you this
  chapter, neutral otherwise). No Stage check.

### Chapter 2: Whisperwood (attrition, ambushes) — 4 rooms from 5 modules, about 10–12 minutes
| Module | Kind | Content |
|---|---|---|
| Snare Line | TrapCorridor | Snares (new hazard kind: roots the victim 2 s; on him, a sprained ankle). Poacher shooters in the trees. |
| Fern Hollow | Ambush | Three-plus ambushers in the ferns; a forage cache off the path. |
| Mire Crossing | SetPiece | Bog patches slow everyone (him more: heavy armour); a brute and thugs wait across. |
| Quill's Glade | Social (always present) | **Mr. Quill** the merchant (scout): sells rations and bandage supplies; dull pendant. |
| Poacher Camp | Combat | Sleeping poachers (Unready: he waits), a brute, a turncoat. |

- Enemies: **poacher** (ranged, cheater, perches in trees), woodsman (brute variant), fern ambusher (ambusher variant).
- Callum gains **Stance I, Unyielding**. Hunger starts. Between-room recovery drops to 30%.
- Skills added: **Splint & Stitch** (Provisioner; treats a serious wound, 6 s channel), **Pull Back** (Handler; yanks
  him out of a snare, bog or hazard), **Sling** (Combat), **Read the Room** (Scholar; intent and timing for 6 s,
  reveals scouts).
- **Campfire:** Stage check (max S1). He learns **Recall** (his first reaction is an S0 or S1 line). GDD beat: "Did
  you see that archer fall? Fortune favors the just." (S0) / a version that half-notices (S1).

### Chapter 3: Catacombs of Ends (information, seals, social) — 4 rooms from 5 modules
| Module | Kind | Content |
|---|---|---|
| Sealed Vault | Seal (always present) | A rune seal on the exit; ward guardians. |
| Dark Gallery | TrapCorridor | Hidden pressure plates in the dark; you see them close up, he never does. |
| Crypt of Sleepers | Combat | Sleeping cultists (Unready), tomb robbers (fake surrender), alcove archers. |
| The Prisoner's Cell | Social (always present) | **The Prisoner** (scout) begs to be freed. Callum wants to free him. |
| Bone Bridge | SetPiece | A narrow bridge over a pit; shield-bearers shove him toward the edge (a fall: a serious wound and he climbs back). Pull Back saves him; Shoulder Check or Buckler stop the shove. |

- Enemies: **cultist** (starts asleep), **tomb robber** (fake surrender), **ward guardian** (heavy, wakes when a seal
  is smashed), alcove archer (archer variant).
- Callum gains **Finisher I, Judgment**. Slots go to 6.
- Skills added: **Read Runes** (Scholar), **Lockpick** (Fixer; R2 disarms traps), **Map Sketch** (Scholar; shows his
  path and reveals hidden traps and doors for 10 s), **Buckler** (Combat; block and parry; R2 intercepts projectiles
  aimed at him within 2 m).
- **Campfire:** Stage check (max S2). At S2 his rules visibly change (Look Away; wait cap 1 s against dirty enemies).
  The **dossier scrap** (GDD §8, Callum's) is found and read. Beat: S0/S1 credits luck; S2 "I was looking at the sky."

### Chapter 4: The Sunken Bastion (adaptation, split threats, environment) — 4 rooms from 5 modules
| Module | Kind | Content |
|---|---|---|
| Flooded Gate | Combat | Bastion soldiers in water to the knee (slow); a stone over the gate. |
| Sluice Works | SetPiece (always present) | **Split threat:** he duels on the lower floor while a crew cranks the sluice above. |
| Hostage Court | Ambush (nemesis) | Archers behind hostages; fake surrenders. |
| Baiters' Causeway | Ambush (nemesis) | Challenge-baiters accept, then back away and stall while ambushers close from the water. |
| Wren's Rampart | Social (always present) | **Darian Wren**, a rival hero (scout), offers to fight beside Callum. |

- **Nemesis squad "Rigged Gauntlet"** (GDD §6.1): base squad at Intel 0; each Intel level adds one cheat (a turncoat,
  an extra hostage archer, a baiter) and the archers reload 15% faster.
- Callum gains **Strike II** (Riposte 3×) and **Stance II** (−40%).
- Skills added: **Bait & Switch** (Fixer; decoy, enemies within 8 m retarget for 4–6 s), **Smoke Bomb**
  (Provisioner; breaks aggro, blocks a stone's view), **Pep Talk** (Handler; +15% damage and speed for 8 s),
  **Shoulder Check** (Combat; knockback and stagger).
- **Campfire:** no Stage check. **Capstone reveal**: choose one of the four implemented capstones. Beat: he asks you to
  stand where he can't see (S2+); colder versions below.

### Chapter 5: The Gallery (approach + boss)
- **Approach** (2 fixed rooms, about 5 minutes, Moment budget 40): *Hall of Exhibits* (stones on plinths replay
  Callum's earlier chapters; gallery wardens) and *The Long Gallery* (active stones everywhere, so being unseen means
  smoke, angles and patience).
- **The Door:** Callum's own line (S0: "Stay behind me. This is my fight." … S3: "Whatever comes, I will not lie about
  who helped me."), then the door check (max S3, +1 at most). A Restore Point.
- **The boss:** §5.

## 5. The final boss: the Gallery (Lord Ashgrave → the Mirror)

Arena: the slice's octagon (the Oath Glyph circle), enlarged gallery with **6** hidden archer posts, wall alcoves for
armed props (Loosen Bolt) and two chokepoint niches for the Mirror.

| Phase | What happens | Ends when |
|---|---|---|
| 0. Diagnosis | A figure with the dull pendant speaks 3–4 calm lines naming Callum's flaw and one that shows he has no entry for you, then dissolves into Lord Ashgrave. | Scripted, about 12 s. |
| 1. The flaw trap | The terms ("no aid"), Callum's answer by Stage (S0: orders you out of the circle; S1: "stay near the edge"; S2: doesn't bar you, won't acknowledge help; S3: "A rigged duel binds no one." and voids the terms). The formal duel. At T+25 s the 6 archers stand and volley. | Ashgrave falls to 60% HP. |
| 2. The persona fight | Ashgrave drops the pretence: faster, heavier combos, calls the remaining archers. | Ashgrave at 0 HP. |
| 3. The Mirror | Unmasking (scripted, about 10 s): "You are not the first I have read. You are the first I could not finish." A desaturated, glitch-edged Callum with the pendant, chapter 5 stats, S0 rule icons over its head. **It ignores you entirely.** Its HP equals about 20 s of Callum's chapter 5 sustained damage. | Duet Finisher lands, or Callum falls. |
| Aftermath | The pendant glows for the first time, recording two figures. Callum's "we" line. `CLASS: CALLUM's SIDEKICK. STATUS: LISTED`. | — |

**Habit Breaks** (each a 4–8 s stall or stagger with +100% damage taken; player-driven, never a prompt sequence):
1. **Etiquette Reset.** Ping the Mirror: Callum (S1+) re-challenges, and the Mirror must salute back and wait up to
   3 s. 12 s cooldown.
2. **Chokepoint trap.** Decoys (Bait & Switch) or three-plus engagers make it fall back into a niche; an armed prop
   there (Loosen Bolt) collapses on it: 6 s stagger.
3. **Witnessed dishonour.** At S3 Callum's "cheat a cheater" feint, done in the Mirror's view, triggers the Mirror's
   own Honor penalty: −25% damage and a 2 s stagger.

**Shared-rule counters** (GDD §4.5a): every S0 rule Callum still has at the door gives the Mirror a counter.
| Rule still shared | Stages | Counter |
|---|---|---|
| Wait on the Unready (cap 3 / 2 / 1 s vs dirty) | S0, S1, S2 | **Feint:** the Mirror fakes a stagger; Callum waits; the Mirror's free heavy blow. |
| Honor (witnessed help costs Honor) | S0, S1 | **Goad:** the Mirror turns your Habit Breaks against him; each one he witnesses costs him Honor. |
| No aid (terms bind him) | S0 | **Terms:** at the Duet Window he refuses your help ("No aid."): the window never opens. |
S3 shares none. This produces the gradient (S0 effectively closed; S1 very hard; S2 hard but fair; S3 the target
50–60%) without special cases, and the balance harness measures it.

**Duet Finisher.** Under 25% HP the Mirror "reaches for the code" (visible tell). Callum glances at you and a **Link
ring** appears around you for 1.0 s while his Finisher winds up. Fire your capstone inside it:
| Capstone | Duet form |
|---|---|
| Domino Effect | Every armed prop collapses on the Mirror (needs 2+ armed). |
| Crossfire | Bonus damage scaled by your attack skills' ranks. |
| Hold Please | The Mirror freezes 4 s; the Link window doubles. |
| Silent Partner | Callum heals 25%; the window widens to 1.6 s. |
A miss: the Mirror's riposte wounds Callum, and the window comes back in 10 s.

**Loss:** the Curator's diagnosis replays (by phase and cause), then the Post-Mortem, then Restore (before the door,
or any chapter start reached).

## 6. Skills (12 more + 4 capstones; 22 + 4 stay for later)

| Family | Added now | Chapter that needs it |
|---|---|---|
| Fixer | Lockpick, Bait & Switch | 3 (gates, trap disarm), 4–5 (decoys, Mirror) |
| Handler | Pull Back, Pep Talk | 2 (snares, bog), 4 |
| Provisioner | Splint & Stitch, Smoke Bomb | 2 (serious wounds), 4–5 (stones) |
| Scholar | Read the Room, Read Runes, Map Sketch | 2 (scouts), 3 (seals, hidden traps) |
| Combat | Sling, Buckler, Shoulder Check | 2, 3 (hostage and gallery volleys), 4 |
| Capstone | Domino Effect, Crossfire, Hold Please, Silent Partner | 4 (reveal), 5 (Duet) |

Each gets: behaviour, numbers from `SkillDefinition` (rank 1/2), a `SkillGuides` entry, "how Callum's code reads it",
synergy notes with the skills it actually interacts with, and tutorial copy that passes the spoiler scan. The
picker offers only implemented skills, as now. Every obstacle stays solvable by at least three families (GDD §6
anti-best-build rule).

## 7. Art and audio

- **Characters** from the headless-Blender recipe pipeline (`tools/blender/recipes.json`, Quaternius outfits; no
  AI-generated meshes): poacher, woodsman, cultist, tomb robber, ward guardian, bastion soldier, gallery warden,
  hostage, Mr. Quill, the Prisoner, Darian Wren, the Curator. Variants that only need a colour change reuse a body with
  a recolour recipe.
- **The Mirror:** Callum's visual with a glitch/desaturate shader and the pendant.
- **Environments** from the Nature, Village (MegaKit) and FantasyProps kits already in `ThirdParty/Quaternius`, plus
  generated ground textures (`tools/make_ground_textures.py`): forest floor and moss, crypt flagstone, wet stone and
  water, gallery marble. Water is a simple animated plane shader.
- **Lighting per chapter** (`ChapterTheme`): Whisperwood green-grey fog; Catacombs dark, warm candle and torch pools;
  Bastion cold blue, wet; Gallery pale marble and gold.
- **Audio:** per-chapter music and ambience from the Kenney packs already in the repo; the boss reuses the duel cues.

## 8. HUD and tutorial

- Hero card: a hunger chip, the Finisher charge, the Stance icon. Sidekick card: rations, Recall uses, the Downed
  timer, the capstone slot. The Link ring is world-space around the sidekick.
- New lessons, each the first time it matters: hunger and feeding, seals, hidden plates, scouts and the pendant,
  Downed and Recall, the capstone, the Link ring. Same spoiler policy as the tutorial spec (no Rapport words; a test
  scans the copy).

## 9. Testing

- **EditMode:** every `ChapterDef` assembles deterministically from its library with every slot fillable; the
  `CampaignSchedule` and `ChapterTier` tables; XP pots reach the level targets; which campfire runs which check; Recall
  per Stage; nemesis squad size per Intel; Mirror counters per Stage; the ledger's chapter budgets.
- **PlayMode:** each chapter builds, and both the follow bot and the idle bot reach its end or die (no soft-lock) in
  `Fast` mode; chapter transitions keep skills, level, ledger, Intel and wounds; Restore to chapter N start and before
  the door; each seal solution and the smash fallback; hidden plates reveal; scouts flee on a ping inside 3 s and report
  otherwise; hunger and feeding; Downed and Recall by Stage; each new skill; boss phases advance; the Mirror never
  targets the sidekick; Duet hit and miss; the ending.
- **Balance harness:** runs all five chapters with the idle, sloppy, combat-only and supportive bots; reports solo-clear
  rates per chapter (targets 70/50/30/10/0) and boss wins by Stage; results go to `docs/qa/balance/`.
- **Visual QA:** captures of each chapter's rooms and the boss phases in `docs/qa/shots/`; the QA log records each
  phase.

## 10. Build order

Each step ends with tests passing, a commit, and a push of the branch. `main` is fast-forwarded and pushed whenever a
step leaves the game playable (owner plays `main` in the editor).

1. **Campaign spine.** The chapter loop, `CampaignSchedule`, `ChapterTier`, per-chapter XP, slots, Stage checks at the
   right campfires, Restore Points per chapter, "Play from chapter N", duel removed from chapter 1. Chapters 2–5
   temporarily reuse chapter 1 modules with their themes, so the run is playable end to end immediately.
2. **Chapter 2: Whisperwood** + Downed/Recall + its four skills.
3. **Chapter 3: Catacombs** + S2 rules + dossier + its four skills.
4. **Chapter 4: Sunken Bastion** + nemesis squad + split threat + its four skills + capstones.
5. **Chapter 5: The Gallery** + the full boss + S3 rules + the ending.
6. **Balance and polish:** the harness across five chapters, demos for the new skills, QA captures, docs.

## 11. Risks

- **Scope.** This is the GDD's largest milestone (estimated 80–120 h there). The build order keeps the game playable
  after every step, so stopping early still leaves a complete (if thinner) campaign.
- **Mirror AI.** Reusing Callum's rules on the enemy side needs an enemy controller hook; it must stay deterministic.
- **Readability indoors.** Open-topped interiors must read at the fixed camera; QA captures check each room.
- **Harness time.** Five chapters per run makes 1,000-seed batches long; the harness reports per-chapter rates from
  isolated chapter runs as well as full runs.
