# Chapter 5: The Gallery Implementation Plan (Plan 5 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The run's last chapter: a two-room approach through the Curator's gallery, the door, and the full final boss — Phase 0 the diagnosis, Phase 1 the rigged duel with six hidden archers, Phase 2 Ashgrave without the pretence, Phase 3 the Mirror (Habit Breaks, shared-rule counters, the Duet Finisher) — then the ending. Callum's S3 rules (Fair to Cheat a Cheater, Duet Windows) land here.

**Architecture:** `GalleryBoss` (Boss) replaces `RiggedDuelDirector` in the campaign (the slice's director stays for the QA duel scene and its tests); it reuses the terms, archers, glyph and cause-of-death approach. The Mirror is an `EnemyAgent` wearing Callum's visual (desaturated, glitch-edged) driven through the `IEnemyController` hook (Plan 4) by `MirrorBrain`, a mirror-side copy of Callum's S0 rules. `MirrorCounters` and `DuetFinisher` are small units, the counters pure data. The arena is a new `gallery_arena` module from the room builder; until it is built, the boss runs in the slice's `rigged_duel` arena (three archers; niches and alcove props are placed at runtime).

**Tech Stack:** Unity 6000.3 (URP), C#, NUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-callum-campaign-design.md` (§4 Chapter 5; §5; §3.2 `GalleryBoss`, `MirrorKnight`, `DuetFinisher`, `CallumRules` S3).

## Global Constraints

- Plans 1–4's constraints hold.
- **S3, Fair to Cheat a Cheater** (GDD §6.1): a deed he witnesses against a flagged cheater costs no Honor and is never *Caught*. When he begins Judgment on a cheater he asks "A hand, friend?" — a Duet Window for the charge: each hit of hers on that target adds +25% to the blow (up to +100%); the first is the *Duet strike* Moment (3).
- **Phase 0, diagnosis** (12 s, scripted): the Curator (Ashgrave's body, grey, the pendant glinting) names the flaw in three lines and adds one that shows he has no entry for her; he dissolves into Lord Ashgrave.
- **Phase 1, the flaw trap:** the terms by Stage — S0 orders her out of the circle; S1 "stay near the edge"; S2 does not bar her and, under the terms, will not acknowledge help (no Honor loss, no *Caught*); S3 "A rigged duel binds no one." and voids them. The signal comes at T+25 s, or at once if Ashgrave falls to 60% first: six archers (three in the fallback arena) stand and loose. Ends when Ashgrave is at ≤60% and the signal has come.
- **Phase 2, the persona:** Ashgrave drops the pretence (`ashgrave_unmasked`: quicker windup, a heavy every third blow, a flagged cheater); every archer still hidden stands. Ends when he falls.
- **Phase 3, the Mirror:** unmasking (10 s): "You are not the first I have read. You are the first I could not finish." The archers melt away; the Mirror forms: Callum's chapter 5 numbers mirrored (`mirror`: HP = about 20 s of his chapter 5 sustained damage; his blows on it ×0.6, as his Stance II; a Riposte of its own), S0 rule icons over its head, and **it never targets the sidekick**. Its HP cannot fall below 25% except to the Duet.
- **Habit Breaks** (each makes the Mirror take +100% damage for the break; player-driven):
  1. *Etiquette Reset* — ping the Mirror: Callum (S1+) re-challenges; the Mirror salutes back (1.2 s) and waits (3 s). 12 s cooldown. At S0 he is too busy fighting to listen.
  2. *Chokepoint trap* — three or more engagers (Callum counts one, a decoy within 4.6 m two) send it to the nearer niche to hold for 8 s; an armed prop collapsing on it there (Loosen Bolt, Domino Effect): a 6 s stagger.
  3. *Witnessed dishonour* — S3 only: the same ping, and Callum strikes during its return salute ("Cheat a cheater."): the Mirror's own Honor breaks — −25% damage (to −50%) and a 2 s stagger.
- **Shared-rule counters** (one per S0 rule he still has at the door):

| Counter | Stages | Effect |
|---|---|---|
| Feint | S0, S1, S2 (he still waits on the Unready) | Every 14 s it feigns a stagger (1.2 s); if he waits, a free heavy blow lands, unparried |
| Goad | S0, S1 (Honor) | Each Habit Break he sees costs him Major Honor |
| Terms | S0 (no aid) | At the Duet he refuses: "No aid." The window never opens |

- **Duet Finisher:** at 25% it "reaches for the code" (1.5 s tell); then Callum glances at her and a Link ring circles her for 1.0 s (Hold Please 2.0 s, Silent Partner 1.6 s) while his Judgment charges. Her capstone inside the ring (with none ready — not chosen, or still resting — a ping on the Mirror) lands the Duet: its form (Plan 4 table), then Judgment takes whatever the Mirror has left. A miss: its riposte (12% of his max HP, a wound), and the ring returns 10 s later.
- **Aftermath:** the pendant glows for the first time; his "we" line by Stage; the System window: `CLASS: CALLUM's SIDEKICK. STATUS: LISTED`, and a class re-roll she can decline.
- **Loss:** the Curator's diagnosis by phase and cause, the Post-Mortem, Restore (before the door, or any chapter start).
- **The approach** (2 fixed rooms, Moment budget 40): *Hall of Exhibits* (Combat: plinth stones that wake for his duels, exhibits that replay his road, gallery wardens and a marksman) and *The Long Gallery* (TrapCorridor: stones that never sleep and sweep their gaze, so unseen means smoke, angles and patience; wardens, marksmen on balconies, a steward who yields falsely).

---

### Task 1: S3 — Fair to Cheat a Cheater and Duet Windows
**Files:** `Hero/Callum/CallumModule.cs` (`Witnessed` skips cheaters at S3; `DuetWindow`; Judgment's bonus), `Hero/Callum/CallumRules.cs` (Finisher icon `duet` at S3 on a cheater), `Rapport/CallumDance.cs` (*Duet strike*), `Data/Callum_RuleSet.asset` + `GameplayPrefabBuilder.EnsureCallumRuleSet` (S3 list), `tools/make_icons.py` (`duet`).
**Tests:** `CallumTests` — S3 sees sand on a cheater without a word; S2 does not; her hits during a Duet Window add to Judgment and capture the Moment.

### Task 2: The Mirror
**Files:** `Boss/MirrorBrain.cs` (`IEnemyController`: salute, fight, Riposte, wait, fall back, feint, reach for the code, breaks; rule icons), `Boss/MirrorCounters.cs` (pure), `Core/CastFactory.cs` (`Mirror`: the hero's visual on an enemy body), `Core/Tuning.cs` (`mirror`, `ashgrave_unmasked`, the gallery cast).
**Tests:** EditMode `MirrorCountersTests`; PlayMode `GalleryBossTests` — the Mirror never targets her; a ping stalls it at S1, not at S0; S3's ping breaks its Honor; a collapse in a niche staggers it 6 s.

### Task 3: The Duet Finisher
**Files:** `Boss/DuetFinisher.cs` (Link ring, window by capstone, hit/miss, riposte), `Skills/Impl/Capstones.cs` (duet forms), `Skills/SidekickSkills.cs` (capstone fires notify the Duet).
**Tests:** `GalleryBossTests` — a capstone in the ring finishes the Mirror; outside it, a wound and the ring returns in 10 s; S0 never opens it.

### Task 4: GalleryBoss and the flow
**Files:** `Boss/GalleryBoss.cs`, `Boss/CuratorDiagnosis.cs` (phase lines; "we" lines), `Flow/GameFlow.cs` (`Duel` is the GalleryBoss; boss bar names; the ending; re-roll), `UI/EndScreen.cs` (stays), `Audio/AudioDirector.cs` (`Mirror` cue), `Tutorial/LessonTriggers.cs`, `QA/BalanceHarness.cs`, `Tests/PlayMode/CampaignTests.cs`, `Tests/PlayMode/UiQaCaptures.cs`.
**Tests:** `GalleryBossTests` — phases advance 0 → 1 → 2 → 3 → aftermath; S3 voids the terms; the campaign ends `won` after the Mirror.

### Task 5: The Gallery rooms and the arena
`Editor/Builders/GalleryRooms.cs` (`hall_of_exhibits`, `long_gallery`, `gallery_arena`: R 14 m octagon, six perches, two niches with alcove props, the Curator's plinth; marble textures), `Rooms/StoneAnchor.cs` + `Rooms/ChronicleStone.cs` (`AlwaysActive`, `Sweep`), `ChapterDef.Gallery()` → `ModuleChapter = 5`, slots `[Combat]`, `[TrapCorridor]`; `GameAssets.gallery`; registry; `ChapterBuildTests`.

### Task 6: Lessons, ship
Lessons: `mirror`, `habit_break`, `link_ring`, `duet_window` (S3 behaviour shown, never named as a Stage). Bots fire the capstone at the ring and ping the Mirror on cooldown. GDD §3 *Built* note, QA log. Full compile check.
