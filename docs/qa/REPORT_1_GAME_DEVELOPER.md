# QA Report 1 — Professional game developer review

**Build under test:** Hero's Sidekick, M1 vertical slice (GDD §11.2): Callum, the Old Road (3 seeded rooms from 4 modules × 2
variants), campfire (cold S0 / warm S1), Lord Ashgrave's rigged duel, 6 skills, Rapport/Stage S0→S1.
**Engine:** Unity 6000.3.25f1, URP 17.3. **Machine:** Apple M1, 16 GB, macOS 15.6.1.
**Date:** 2026-10-01. **Evidence:** `docs/qa/QA_LOG.md` (per-task log), `docs/qa/shots/` (384 captures), `docs/qa/balance/`
(harness CSV + summary), `docs/qa/perf_player.json` (player-build frame times), test results via Unity Test Framework.

## 1. Verdict

**Ship-quality for a vertical slice on macOS: yes, with the open issues in §6.** It builds clean, runs start-to-finish in the
player with zero exceptions, holds ~168 fps at 1080p on an M1, and is deterministic. All blocker and critical defects found
during QA are fixed and most are pinned by regression tests. What remains are balance/design risks (owned by the designer), a
few art/animation rough edges, and release hygiene.

## 2. How it was tested

| Layer | What | Result |
|---|---|---|
| Unit / EditMode | 71 tests — determinism RNG, rule priority, room assembler, skill soup, Rapport ledger maths, Stage evaluator (exhaustive properties over every Stage × check × 209 rates), Post-Mortem wording, script/file hygiene | **71/71 pass** |
| Integration / PlayMode | 83 tests on real prefabs — camera, sidekick, foot-slide (deterministic lockstep), chapter build, 6 skills, Callum's rules, Rapport judge (12), wounds + hazards, stones, rigged duel incl. balance slope, campfire + restore points, harness determinism, audio, the opening (walk → truck → white, class roll, replay version, skip, no-audio fallback, HERO → CALLUM) | **83/83 pass** |
| Determinism | Same seed + same bot ⇒ identical FNV state hash after the same ticks; different seed ⇒ different hash | pass |
| Balance harness | 60 full-slice runs (6 bots × 10 seeds) at 40 sim ticks/frame | §5 |
| Visual QA | Event-triggered + periodic captures of complete runs (`ChapterWatch`), menus via real-time captures | 384 captures reviewed |
| Player build | macOS standalone, automated full run (`-hs-autoplay handler -hs-seed 2 -hs-perf … -hs-quit`) | won, 0 exceptions |

## 3. Stability, build and performance

* **Build:** `Builds/macOS/HerosSidekick.app`, 179 MB, ~20 s incremental (188 s clean). The 3 remaining build-log "errors" are
  from the third-party MCP Unity tooling package (a test file without a `.meta`), not the game. Symbol upload to Unity Cloud
  was failing (project not cloud-linked) and was disabled.
* **Runtime errors:** 0 exceptions in the player log of a complete run; the editor console is clean (a parameterised `Start()`
  that Unity flagged on every reload was found during the build check and renamed; the codebase was swept for others).
* **Frame time (player, 1920×1080 windowed, full automated run):** avg **6.06 ms (165 fps)**, p95 7.8 ms, p99 8.4 ms, worst
  in-play frame **40 ms**. Measure with the Unity editor idle: a compile or test run in the editor during a player run
  showed up as 0.3–0.9 s stalls that a clean re-run did not reproduce.
  Startup has two one-off hitches (2.2 s scene build, 0.55 s first-render shader compile) — hidden behind the opening's black
  pre-roll. Audio first-use loads used to cause 0.3–1.2 s mid-fight hitches; all clips now load at startup.
* **The opening, live in the player** (`docs/qa/perf_opening.json`): avg 6.50 ms (154 fps), p99 8.3 ms, worst frame
  15 ms. Its masked UI used to compile shader variants on its first visible frame (a 316 ms stall as the walk began);
  it is now drawn invisibly during the black pre-roll, so only the two startup hitches remain.
* **Static batching:** scenery is batched; anything that moves (falling props, traps, stones) is excluded via `IDynamicVisual`
  (regression-tested: 0 of 24 dynamic renderers frozen).

## 4. Defects found and fixed (selected — full list in QA_LOG.md)

| Sev | Defect | Root cause → fix |
|---|---|---|
| Major (spec gap) | The opening skipped GDD §8 beat 1 (the walk with earbuds, the horn, the song cutting, white) and beat 4 ("HERO" becoming his name), and ran ~10 s instead of ~40 s; found after the first report | Built both, UI and audio only as specified: a phone lock screen that walks, a self-made song cut on the same sample as the street and the truck, headlights, white; the HUD's HERO's SIDEKICK glitches into CALLUM's SIDEKICK when he introduces himself (7 tests) |
| Blocker | Physics results depended on frame timing (flaky test exposed an enemy falling through a resized floor) | Sim ticks in `Update`; collider transforms only synced when a physics step happened → `Physics.SyncTransforms()` at the start of every tick |
| Blocker | Loosen-Bolt props dealt damage without visibly falling; traps/stones couldn't change in real chapters | Every moving part was frozen into the chapter's static batch → scenery-only batching |
| Blocker | Hedge ambushers off the route never triggered → the room could never clear → the hero was held at the exit forever | 6.2 m trigger + lunge; once all visible enemies are down, hidden ones come out |
| Critical | Every staggering sidekick hit read as "striking the helpless" (Major dishonour) | Damage event fired after the hit's own stagger was applied → publish first |
| Critical | Turncoat's fake surrender looped (two 66-damage stabs in one fight) | Once per turncoat |
| Critical | NullReferenceException once the chapter's 60-point Moment budget was spent | Null-safe judge + test |
| Critical | Arrows from off-screen archers / perch shooters were unattributable; the hero could leave the frame entirely | Off-screen threat chevrons; axis-aware camera leash with a hero edge marker |
| Major | Callum thrashed between fight and fall-back every few ticks; waited politely after each of his own sword hits | Committed retreat + "holding the narrows"; statuses record their source |
| Major | 4 bandits swinging at once (~45 DPS vs 260 HP) | Melee attack tokens (2 swingers, the rest circle and rotate in) |
| Major | Bandaging a walking hero always failed; System window typewriter threw on tagged text; UI covered the road ahead | Hero holds still for dressing; bounds-checked typewriter; System window moved top-right |
| Minor | Bubbles hid the duel opponent; slot numbers over names; one-click permanent picks; camp fire off-screen | Bubbles beside heads; slot layout; read-then-learn; camp framing |
| Test infra | Real-time foot-slide test flaked at 14 fps after long editor sessions | Lockstep sim + animation via `Time.captureDeltaTime`; identical readings every run |

## 5. Balance harness (docs/qa/balance/summary.md)

| bot (build) | reached duel | S1 at camp | duel won | rooms passed |
|---|---|---|---|---|
| idle (encounters isolated) | 3/10 | 0 | 0 | 65% (GDD target ~70%) |
| sloppy | 2/10 | 0 | 0 | 71% |
| supportive (sand + bandage) | 10/10 | 0 | 0 | 100% |
| fixer (quiet feet + sand) | 9/10 | 1 | 1 | 97% |
| handler (sand + cover story) | 6/10 | 4 | **4** | 87% |
| shadow (quiet feet + crossbow) | 2/10 | 0 | 0 | 70% |

Every S1 duel was won; every S0 duel was lost. Deterministic duel slope (tests): S0 alone loses to the volleys (Ashgrave on
19%), S1 alone loses with Ashgrave on 2 HP, S1 + one archer silenced wins, S0 needs all three.

## 6. Open issues

| # | Sev | Area | Issue | Recommendation |
|---|---|---|---|---|
| O1 | Major | Design/balance | S1 is reliably reachable only with Cover Story (Handler); Fixer reached it once in 10; sand+bandage and crossbow builds never did. GDD gate "≥ 2 viable builds" is only narrowly met. | Designer call: e.g. halve *Caught* when the deed averted a cheat; more unseen-capturable Moments; re-measure with the harness. |
| O2 | Major | Difficulty | Sloppy play reaches the camp 2/10; S0 duels: 0 wins in 27 (final batch). First-time players will likely meet an S0 duel and lose. | Intended ("S0 effectively closed"), but pair it with stronger teaching (O5) and make the Restore Point the obvious next step. |
| O3 | Minor | Art | Campfire seat logs read as long planks; the start-cap banner shows as a bare pole from above; arena near-walls are vertically squashed meshes; no rest/sit poses | Swap to log/bench props; a banner with cloth facing the camera; author a low-wall piece. |
| O4 | Minor | Animation | No foot IK (planted-foot slip 0.37–0.56 m/s, under the 0.6 gate but visible when crouched up close); Callum keeps his sword drawn at camp; the turncoat's surrender pose can hide behind Callum | Foot IK or root-motion locomotion; sheathe at camp; surrender pose offset. |
| O5 | Minor | UX | No tutorial or controls screen (only skill keys and [E] are shown); Hero Insight (Tab) is undiscoverable; the rule icon can sit over the duel opponent | First-run tutorial room and controls overlay; Insight on for the first run. |
| O6 | Minor | Audio | Functional but sparse: no footsteps or voice; synthesized pads as music; ambience only on the road | Composer/stock music pass; footsteps on animation events. |
| O7 | Minor | Release hygiene | QA components (`ChapterWatch`, `BalanceHarness`, `PerfProbe`, bots) compile into the player assembly; the Burst "DoNotShip" folder sits next to the app; the MCP package is in the project | Move QA/bots behind a define or a separate asmdef; strip the MCP package from release branches. |
| O8 | Info | Platforms | Windows and WebGL build modules aren't installed, so neither is verified (GDD launch path includes a WebGL demo) | Install the modules; run the same autoplay perf script on each. |
| O9 | Info | Content | All barks, camp lines and Curator monologues are AI-drafted placeholders | Owner rewrite (GDD §8: shipped AI-written text likely needs a Steam disclosure). |
| O10 | Info | Rapport | The 60-point chapter offer budget saturated in a few long runs, so late Moments weren't offered | Allocate the budget per room so each room's Moments are always on the table. |

## 7. GDD §11.2 slice gate — technical read

| Gate | Status |
|---|---|
| 1. Testers retell a moment unprompted | Needs humans. Strong candidates exist (sanding the fake surrender a second before the stab; the rigged duel's volley). |
| 2. Stage 0 vs Stage 1 tellable by behaviour | Yes: S1 waits 2 s not 3, spares the turncoat before the stab, raises his guard after the first volley, says different terms and victory lines, and the camp is warm (`shots/final2_h2_23_t083.png`) vs cold (`shots/flow2_22_camp.png`). |
| 3. The mini-boss loss feels attributable | Yes on paper: the Curator names the flaw ("He agreed to my terms… never once looked up at the gallery") and the Post-Mortem lists each missed Moment in words (`shots/flow2_27_end.png`). Needs human confirmation. |
| 4. At least 2 builds viable | Borderline (O1). |
| 5. Characters read clearly and animate acceptably | Yes: unified toon look with outlines; foot slip under threshold (jog 0.45, walk 0.37, crouch 0.56 m/s). |
