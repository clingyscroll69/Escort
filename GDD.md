# HERO'S SIDEKICK: Game Design Document v0.1

Working title. Alternate: *Unlisted*.
Status: pre-production. Owner: creative director / QA (the builder). Production: Unity 6 (URP), built by an agentic AI coding tool via an MCP bridge.

**Conventions.** All numbers are starting values to be tuned by the balance harness (section 11.4); none are final. Systems are data-driven (ScriptableObjects or JSON) so the agent can tune without rewriting code. **No random hit chances or crits anywhere in gameplay.** The only randomness is the per-run seed used to assemble levels.

---

## 1. Vision

You died to a truck. The System gave you a class and then glitched. You are the **Hero's Sidekick**: unlisted, unseen, and the only reason the Hero survives.

Five chapters, one hero, one final boss: **the Curator**, a shapeshifter who has watched the hero through scrying stones and diagnosed his fatal flaw. The Curator built his final room around that flaw. He never accounted for you.

**Pillars**
1. **You are the unlisted one.** The System, the stones and the Curator can't see you. That is your weakness and your secret weapon.
2. **Each hero is a different dance.** Four heroes, four different ways of working together, each with different verbs, problems and moments. Not reskins.
3. **The hero changes because of you.** Growth of the hero's power is fixed. Whether he grows as a person is decided by your support.
4. **The boss read him.** The final room exploits the flaw, and raw power cannot fix a flaw.
5. **Fair, learnable loss.** The stat is hidden but its effects are always visible, and every loss teaches the next attempt.

**Reel hook.** The status window rolls HERO. A red error box appears and red glyphs cycle. It settles on "HERO's SIDEKICK". Cut to a dragon charge, with you behind the hero holding a bucket.

---

## 2. Format and technical targets

| Item | Decision |
|---|---|
| Engine | Unity 6 (URP), C#. Mono backend for Windows builds (IL2CPP to Windows isn't supported from macOS). |
| Platforms | Windows (primary), macOS (dev and bonus), WebGL (slice demo only, under 50MB, memory under 500MB). |
| Camera | Fixed-angle third-person (about 50°), both characters kept in frame. Cinemachine. |
| Input | Keyboard and mouse, plus gamepad. Unity Input System. |
| Performance | 60 fps on M-series Macs and mid-range Windows laptops. |
| Determinism | Seeded run seed for level assembly only. Fixed damage values, deterministic AI. |
| Art direction | Stylized low-poly "toy diorama" environments with **full 3D humanoid characters** on one shared Humanoid rig. Base family: Quaternius Universal Base Characters plus Universal Animation Library (CC0), modified through headless Blender scripts (proportions, outfits, colors, props). Extra clips from Mixamo where its terms allow. A toon shader and shared palette unify the look. Avoid AI-generated character meshes (Steam disclosure). |
| UI | Diegetic "System window", with red glitch states for errors. No numeric Rapport anywhere. |
| Saves | Checkpoint at each chapter start, storing snapshots of skills and Rapport. |

---

## 3. Game structure

A run is one campaign: **5 chapters, about 45–55 minutes**, ending in the boss. Levels are procedurally assembled from hand-authored room modules using the run seed.

| Ch | Name | Emphasis | Length | Hero can solo |
|---|---|---|---|---|
| 1 | The Old Road | Combat, traps, ambushes | 8–10 min | ~70% |
| 2 | Whisperwood | Attrition (wounds, hunger), ambushes | 10–12 min | ~50% |
| 3 | Catacombs of Ends | Information, seals, social | 10–12 min | ~30% |
| 4 | The Sunken Bastion | Adaptation (nemesis squads), split threats, environment | 10–12 min | ~10% |
| 5 | The Gallery | Approach plus boss | 5 + 7 min | 0% |

"Hero can solo" means the fraction of encounters the hero clears with at most one serious wound while the sidekick does nothing. It is measured by the balance harness (11.4).

- **Modules per chapter:** about 5 (combat arena, trap corridor, ambush, seal or puzzle, social, set piece), plus a campfire.
- **Hero death ends the run.** The game then offers **Restore Points** (chapter starts).
- **Sidekick death (decided).** Until chapter 2 is cleared, the sidekick's HP reaching 0 **ends the run by design** (red glyphs: "SIDEKICK: DECEASED. NO RECALL AVAILABLE."), then the Restore Point menu. After chapter 2 the hero learns **Recall** (see 4.2) and death becomes recoverable.
- **Hardcore mode** has no Restore Points (bonus cosmetic).
- **Hero unlock.** The first run is always Callum (tutorial). After that, all four heroes are selectable through a System slot-machine window.

---

## 4. Core systems

### 4.1 Sidekick

- **Base stats.** HP 80 (+10 per level), move speed 6.0 m/s, dodge roll (3 charges, 0.3s invulnerability). Baseline weapon is a kitchen knife (8 damage), so non-combat builds can still poke.
- **Leveling.** 13 levels across chapters 1–4. Levels come from fixed XP pots per room, split 50% clear, 25% assist and 25% exploration, so **pacing is identical regardless of playstyle**. Each level grants one pick (12 picks, plus one capstone).
- **Skill soup.** Pick any non-capstone skill at any level-up. No trees, no prerequisites. A second pick of the same skill is rank 2.
- **Loadout.** Active skills need slots: 4 at start, 5 from chapter 2, 6 from chapter 3. Passives don't use slots. Loadouts can be swapped only at camps.
- **Support range.** About 25m around the hero. Beyond it you earn no presence credit.
- **Ping.** A free baseline verb that marks a target or place. Heroes react per their own rules, and Stage changes how much they listen.

### 4.2 Hero

- **Power tiers.** Raw power grows on a fixed schedule: damage ×2.5 per chapter (about ×39 by chapter 5) and HP ×2 per chapter. No randomness and no choices.
- **Signature skills.** Three per hero (Strike, Stance, Finisher), unlocked on a fixed schedule: Ch1 Strike I, Ch2 Stance I, Ch3 Finisher I, Ch4 upgrades Strike II and Stance II, Ch5 Finisher II.
- **Behavior.** Each hero runs a prioritized rule list (behavior tree), with icons over his head showing the current rule. The rule list changes at each Rapport Stage (section 4.4).
- **Route.** The hero walks an authored route graph per room and pauses at thresholds (doors, arenas, bridges), so his path is predictable. His speed is slightly above your walk and below your sprint.
- **Wounds.** Wounds persist and carry maluses until treated.

| Wound | Effect |
|---|---|
| Sprained ankle | −15% speed |
| Cracked ribs | −20% max HP |
| Sword-arm strain | −20% damage |
| Fever | HP drain until treated |
| Concussion | Rule reactions delayed +0.5s |

  - A hit of 25% of his max HP or more causes a wound. Environmental hazards can too.
  - 3+ wounds means Crippled (−30% speed).
  - Treatment: Bandage (minor), Splint & Stitch (serious), camp (removes 1 automatically), meals (remove 1 more).
- **Hero HP 0** means game over, unless Second Wind (a capstone) revives him within 3s.
- **Recall (hero skill, learned at the end of chapter 2; decided).** Learned in the chapter 2 campfire scene (the first Rapport check), so his first reaction is an S0 or S1 line. From then on, when your HP hits 0 you are **Downed** for 20s and the hero tries to Recall you. 2 uses per chapter. If he can't or won't act before the timer ends, you respawn at the last door, he takes +1 wound, and the room's remaining Moments are forfeited. Both the dialogue and the mechanics vary with his current Stage, evaluated at time of use (mechanical variation approved):

| Stage | Channel | Conditions | Line |
|---|---|---|---|
| S0 | 8s | Won't start until no hostiles within 10m; +1 wound | "This is so useless. Get up." |
| S1 | 5s | Starts with 2 or fewer hostiles nearby | "...Fine. Don't make it a habit." |
| S2 | 3s | Breaks off combat unless his Finisher is active; no wound | "Up. I've got you." |
| S3 | 1.5s | Sprints to you even mid-fight; you rise at 50% HP | "I'm not losing you. Not today." |

  You rise at 30% HP otherwise. Per-hero versions: **Callum** ("Squire's Recall") won't leave an active challenge until the duel ends or the target is Unready (S0–S1). **Ansel** ("Extraction Protocol") adds it as a plan step, so at S0 it feeds his Replan Spiral. **Bram** ("Shoulder Carry") hauls you over his shoulder and keeps charging. **Vaughn** ("Curtain Call") turns it into a performance and needs Crowd 25 or more at S0–S1 ("Nobody's watching.").

### 4.3 Pressures, stones and adaptation

**Heroes are loud.** The Hero class radiates presence: monsters sense it, wards react to it, and delicate people and mechanisms react badly. Heroes can't sneak, pick locks or haggle. You are unlisted and easy to overlook. This is the lore reason the quiet jobs are yours.

Pressures (see the chapter table) grow in mix, not just strength: attrition, information, social, environment, adaptation, split threats, traps and ambushes.

**Chronicle stones** are scrying stones found in every chapter. They record and relay deeds to taverns. States: Dormant, Active, Broken. Each hero treats them differently (see his module). They never show you.

**Curator Intel** (0–3, hidden) rises when active stones record a hero's flaw-revealing behavior. It scales the size and quality of chapter 4's nemesis squads. It does not block the boss. Ways to lower it:
- Smoke Bomb blocks a stone's view.
- Forgery plants false footage (once per chapter, −1 Intel).
- Breaking a stone.

**Scouts.** A disguised Curator scout (tell: a dull chronicle-stone pendant) appears in chapters 2–4 (*Mr. Quill* the merchant, *the Prisoner*, *Darian Wren* the rival hero). Pinging a scout within 3s of first sight, or using Read the Room, makes him flee. That gives −1 Intel and an early dossier fragment.

### 4.4 Rapport (the hidden stat)

**Definition.** Rapport measures how well you supported the hero **on his own terms**. It is computed from **Moments** that each hero's Dance generates, and it is never shown as a number.

**Moments (Opportunities).**
- Each has: an id, a weight, a time window, hero-specific data, and capture methods from **at least 3 skill families**.
- The Opportunity Director offers them per chapter: Ch1 60 points, Ch2 90, Ch3 100, Ch4 110, Ch5 approach 40. Uncaptured Moments still count as offered.
- Kills by you count only when they serve the Dance.

**Capture rate** = (earned − penalties) / offered so far. Penalties are capped per chapter at −30% of that chapter's offered points.

**Negative assets** (the only way to lose Rapport):

| Action | Penalty |
|---|---|
| Friendly fire above 10% of hero HP | −2 to −3 |
| Abandon: you are over 20m away while he's under 40% HP in combat | −3 |
| Hero-specific betrayals (see modules) | −2 to −6 |

An honest failed attempt to help is **not** a penalty. Neglect earns zero, not a negative.

**Stages** are evaluated at three checks: end of chapter 2, end of chapter 3, and the boss door.

| Stage | Capture rate | Cap by check |
|---|---|---|
| S0 | below 30% | any |
| S1 | 30% or more | max S1 at Ch2 check |
| S2 | 55% or more | max S2 at Ch3 check |
| S3 | 75% or more | only at the door |

- Stage can rise by **at most +1 between the chapter 3 check and the door** (recovery cap).
- Stage can fall **by at most one per check** if penalties push the rate below the threshold.
- Each stage changes the hero's rule set, barks and campfire scenes.

**Fairness safeguards** (see 11.3): visible effects, attributable losses, foreshadowing, a gradient boss, Restore Points, and valid measurement.

**Boss stage gradient** (win chance for a competent player):

| Stage at door | Beat the boss |
|---|---|
| S0 | Effectively closed (<1%) |
| S1 | Very hard (about 5–10%, narrow path) |
| S2 | Hard but fair (about 25–30%) |
| S3 | Target about 50–60% |

### 4.5 The Curator

One entity with one tell (a dull pendant) who wears different personas. In each run he becomes the persona that exploits the current hero's flaw.

- **Dossier scrap at the end of chapter 3** (texts in section 8).
- **Boss structure.** Phase 0 (diagnosis monologue), Phase 1 (the flaw trap), Phase 2 (the persona fight), Phase 3 (the **Mirror**, see 4.5a).
- **Blind spot.** He can only mimic people he has a dossier on, so he can't mimic or predict you.

### 4.5a The Mirror (Phase 3)

**Purpose.** The final exam and the receipt. The Curator's whole method is that a person is their flaws. The Mirror is his proof, and your hero's growth is the refutation. Reached only if the Phase 1 trap is survived and the Phase 2 persona is beaten.

**Flow**
1. **Unmasking (about 10s, scripted).** The persona dissolves. Curator: "You are not the first I have read. You are the first I could not finish." A desaturated, glitch-edged copy of the hero forms, wearing the dull pendant. It has the hero's chapter 5 stats and runs his **Stage 0 rule set**, with the S0 rule icons visible above its head. Power is equal, so power can't decide the fight.
2. **Duel of Habits (90–120s).** The Mirror **ignores you entirely**: no dossier entry, you're scenery to its targeting. Its HP bar equals about 20s of the hero's chapter 5 sustained damage. There is no invulnerability and no tags. Exploit its S0 blind spots to trigger **Habit Breaks** (stall or stagger windows of 4–8s with +100% damage taken). Without Breaks, it fights at equal power and beats the hero.
3. **Duet Finisher.** At under 25% HP the Mirror "reaches for the code" (visible tell). The hero glances at you and a **Link ring** appears for 1.0s. You fire your capstone inside the window while the hero's Finisher winds up. A miss resets the window every 10s, and each miss costs the hero a wound (the Mirror's riposte).
4. **Aftermath.** The pendant glows for the first time, recording two figures. The hero speaks his "we" line. The System window reads `STATUS: LISTED`.

**Stage integration (shared-rule counters).** For every S0 rule ID the hero **still shares at the door**, the Mirror gets a scripted counter-move. An S3 hero shares none, so the Mirror just fights its old self. S2 shares one or two, S1 more. This produces the gradient without special-casing.

**Phase 3 win chance given you reach it:** S3 about 75%, S2 about 60%, S1 about 35%.

**Per-hero Mirror**

| Hero | Mirror | Habit Breaks |
|---|---|---|
| Callum | The Unbroken Code | (1) **Etiquette Reset:** re-challenging makes the Mirror salute back and wait up to 3s (free hits). (2) **Chokepoint trap:** decoys make it pin itself to a wall, then collapse it with Loosen Bolt. (3) **Witnessed dishonor:** Callum's new "cheat a cheater" play triggers the Mirror's own Honor penalty (−25% damage, 2s stagger). |
| Ansel | The Plan Made Flesh | (1) **Scout window:** it assesses for 3s each time the Gallery shifts. (2) **Plan break:** break a visible checklist step to trigger its Replan Spiral. (3) **Spiral peak:** three consecutive breaks means a 32s stall. |
| Bram | The Charge | (1) **Decoy charge:** it charges a decoy into the Gallery's hazards. (2) **Commit-lock:** it can't turn for 1.5s mid-charge, so time Bram's Opening Surge to the end of the charge. (3) **Loud:** its aggro radius wakes the Curator's own wards, which punish it (aim them with Read Runes). |
| Vaughn | The Empty Mask | (1) **Steal the audience:** all stones start facing the Mirror (Crowd 100 versus Vaughn's 0). Angle or repair stones, or block views with Smoke Bomb. (2) **Stage fright:** Mirror Crowd at 0 for 10s means −40% damage and fumbles. (3) **Showboat punish:** interrupt its long windups with Shoulder Check or Buckler for a 6s stagger. |

**Shared-rule counter examples:** a Callum still waiting on Unready enemies at S2 gets fed Feints (fake staggers). An Ansel still smashing stones at S2 is baited with decoy stones. A Bram still charging without a ready check is baited with decoys.

**Capstone Duet forms** (every capstone works, so any build can finish):

| Capstone | Duet form |
|---|---|
| Domino Effect | Armed props collapse on the Mirror (needs 2 or more armed) |
| Second Wind | The hero's Finisher can't be interrupted |
| Crossfire | Bonus damage scaled by your attack stats |
| Killing Blow | Execute threshold at 15% HP |
| Hold Please | Freezes the Mirror 4s, doubling the Link window |
| Silent Partner | Hero heals 25%, window widened |
| Bond Strike | Damage scales with Stage |
| Forged Papers | 3s false target the Mirror attacks |

**Build cost.** The Mirror is `HeroBrain(S0)` plus a mirror skin, an "ignore sidekick" flag, three Habit Break triggers, a shared-rule counter table and Link-window logic. Rough estimate (my guess): about 8–12h per hero plus about 15h shared.

**Risks:** the hero's S3 AI must be robust against the Mirror, and the Link window has to read clearly. Keep Habit Breaks player-driven so it doesn't become a scripted prompt sequence.

### 4.6 Campfire

After each chapter:
- Rest (one wound removed).
- Cook (Field Kitchen).
- Swap loadout and level-up picks.
- A hero scene, in a warm, neutral or cold variant depending on Stage and recent penalties.
- Checks at the end of Ch2 and Ch3 trigger a visible behavior change. At the end of Ch2 the hero also learns **Recall** (4.2).

### 4.7 Level-up UI

A System window shows all 40 skills with family filters, rank, cooldown and use notes. Tooltips describe **uses**, never recommendations.

---

## 5. Skill catalogue (40 + 8)

[A] active, [P] passive. Numbers are rank 1 to rank 2 starting values.

### Fixer
| Skill | Type | Effect |
|---|---|---|
| Pocket Sand | A | Thrown, blinds 3s to 4s; also reveals invisible. CD 8s. |
| Loosen Bolt | A | Arm a prop (2 to 3 armed at once). Triggers on ping or when the hero passes. Heavy area damage. |
| Quiet Feet | P | Crouch-walk: unaware enemies can't detect you beyond 2m; reduces the hero's witness cone. |
| Bait & Switch | A | Decoy: enemies within 8m retarget for 4s to 6s. |
| Grease | A | Slick patch that staggers enemies; the hero slips too (R2: hero-safe toggle). |
| Rope Trick | A | Tether: trip or pull enemies, cross gaps, rescue the hero from pits. |
| Lockpick | A | Open locks; R2 disarms traps. |
| Caltrops | A | Area denial; slows 40%; R2 adds bleed. |

### Handler
| Skill | Type | Effect |
|---|---|---|
| Signal Codes | P | Pings carry a second meaning (hold, advance, fall back); longer range. |
| Pep Talk | A | +15% hero damage and speed for 8s. CD 25s. |
| Cover Story | A | Clears shame, doubt or tarnish states; if used within 3s of a Caught penalty, halves it. |
| Pull Back | A | Yanks the hero out of a hazard. Partial at S0. Using it against a committed action is a penalty if repeated. |
| Hold That Thought | A | Delays the hero's next ability by 2s to 4s. |
| Cue Card | A | Queue up to 3 orders. |
| Flattery | A | Draws the hero's attention to a target; small morale bonus. |
| Ready Call | A | Declares "ready": bonus scales with your active prep (armed props, buffs). |

### Provisioner
| Skill | Type | Effect |
|---|---|---|
| Potion Belt | P | Carry 3 to 5 potions; craft. |
| Bandage | A | 3s channel heal over time. |
| Field Kitchen | A | At camp: cook meals from foraged ingredients for run-long buffs. |
| Repair Kit | A | Fixes gear and bridges. |
| Spare Sword | A | Hands the hero a slashing, piercing or blunt weapon for 20s (soft bonuses, never immunity). |
| Smoke Bomb | A | Breaks aggro and blocks a stone's view. |
| Heavy Pack | P | +2 item capacity. |
| Splint & Stitch | A | 6s channel treats a serious wound (uses supplies). |

### Scholar
| Skill | Type | Effect |
|---|---|---|
| Read the Room | A | Shows enemy intent and attack timing for 6s; reveals scouts. |
| Map Sketch | A | Shows the hero's path for 10s and reveals hidden doors and traps. |
| Read Runes | A | Opens seals and wards. |
| Bestiary | P | After 3 encounters, weak points flagged (soft damage bonus). |
| Translate | A | Parley with parleyable creatures or people (some heroes hate it). |
| Forecast | P | Warns 1.5s earlier of ambushes and hazards. |
| Prepared Notes | A | Pre-solve one puzzle or seal per chapter (2 uses). |
| Forgery | A/P | Detect forgeries (P); forge passes or false stone footage (A). |

### Combat
| Skill | Type | Effect |
|---|---|---|
| Sling | A | Cheap ranged, small stagger, infinite ammo. |
| Crossbow | A | Slow reload, high damage; R2 pierces. |
| Throwing Knives | A | 5 charges, bleed. |
| Dagger Flurry | A | Melee burst, dodge-cancel. |
| Backstab | P | +100% from behind vs unaware enemies or ones engaged with the hero. |
| Bomb Bag | A | Area damage. Hits the hero too. |
| Buckler | A | Block and parry; R2 intercepts projectiles aimed at the hero within 2m. |
| Shoulder Check | A | Knockback and stagger. |

### Capstones (choose 1, revealed at the end of chapter 4)
1. **Domino Effect:** chain-triggers every armed prop in the room.
2. **Second Wind:** revives the hero once per chapter.
3. **Crossfire:** synchronized attack: your own volley joins the hero's Finisher, scaling with your attack stats.
4. **Killing Blow:** a huge personal execute.
5. **Hold Please:** freezes the room for 4s except you.
6. **Silent Partner:** passive regeneration and wound-healing for the hero.
7. **Bond Strike:** joint attack that scales with the hero's Stage.
8. **Forged Papers:** bypasses one social or seal obstacle per chapter.

---

## 6. Heroes

Every hero module defines: belief, rule sets by stage, signature skills, stone behavior, Moments, negative assets, the chapter 4 nemesis squad, campfire beats, barks and the boss.

**Anti-best-build rule.** Each hero has at least 4 recurring problems, and each problem can be solved by at least 3 skill families. Hero-synergy effects are capped at +25%. QA target: no skill above a 70% pick rate per hero, and at least 3 distinct builds with clear rates within 10%.

### 6.1 Sir Callum the Honorable (starter)

*Belief:* a fight is only worth winning if it's fair.

| Stage | Rules |
|---|---|
| S0 | (1) Fight the challenged target. (2) Challenge the nearest hostile within 15m (1.2s salute). (3) Wait up to 3s if the target is Unready (sleeping, fleeing, surrendered, staggered, turned away). (4) Fall back to a chokepoint if 3+ enemies engage him. (5) **Honor:** witnessing dishonor (your sabotage in his 120° cone within 12m, or an active stone) drops Honor; below 40 he deals −25% damage and scolds you. |
| S1 | Honor loss from minor assists is halved. Waiting cap 2s. |
| S2 | **Look Away:** when you use Cover Story or ping "cover", he turns his back for 3s (an Unseen Window with witness checks suspended). Waiting cap 1s against dirty enemies. |
| S3 | **Fair to Cheat a Cheater:** against flagged cheaters he has no Honor penalties and asks for help ("A hand, friend?"), opening Duet Windows where your hits during his Finisher add damage. |

*Signature skills:* Strike **Riposte** (parry counter for 2×, later 3×); Stance **Unyielding** (−30% damage from the challenged enemy, later −40%); Finisher **Judgment** (3s charge, huge single-target hit).

*Stones:* stream only formal duels. An active stone in view counts as a witness, so cover or destroy it to deny that.

| Moment | Weight |
|---|---|
| Unseen assist (kill or disable a cheater or archer during a duel, unwitnessed) | 3 |
| Averted cheat (stopped an ambush or fake surrender) | 4 |
| Covered lapse (Cover Story removed a penalty) | 2 |
| Chosen blindness (S2+, he deliberately looked away) | 2 |
| Duet strike (S3) | 3 |
| Wound treated after a duel | 2 |

*Negative assets:* **Caught** (he sees you or a stone relays you cheating) −3, −6 if twice in the same fight. **Spoiled duel** (you stagger or kill his target before the salute finishes) −2. Friendly fire −3. Abandon −3.

*Nemesis squad (Ch4):* "Rigged Gauntlet": fake surrenders, archers behind hostages, challenge-baiters who stall for an ambush.

*Campfire beats:* Ch1 he recites the code. Ch2 "Did you see that archer fall? Fortune favors the just." Ch3 the S0/S1 version credits luck; the S2 version says "I was looking at the sky." Ch4 he asks you to stand where he can't see. Ch5 "Whatever comes, I will not lie about who helped me."

*Boss: Lord Ashgrave, the Rigged Duel.*
- **Room:** an octagon arena with an Oath Glyph circle and a gallery of hidden archers (6).
- **Trap:** formal terms ("no aid"), then at T+25s the archers fire volleys.
- **Gradient:**
  - S0: he orders you out of the circle and doesn't flinch at volleys. You can sneak back in with Quiet Feet and take out archers unseen, but it's very hard.
  - S2: he doesn't bar you but won't acknowledge help.
  - S3: "A rigged duel binds no one." He voids the terms.

*Builds:* **Shadow** (Fixer plus Crossbow), **Quartermaster** (Provisioner plus Scholar), **Second Blade** (Combat plus Handler).

### 6.2 Dr. Ansel Voss, the Overprepared

*Belief:* luck is a failure of preparation.

**Behavior.** A visible Plan checklist (3–5 icons) hangs over his head. At room entry he scouts for 3s, then builds a deterministic plan from the room template. Steps run in order. If a step's precondition fails, he stalls (the **Replan Spiral**, below).

**Why overpreparedness hurts.**
1. **Burn:** he spends potions and smoke vials at room start as "contingencies", leaving nothing for crises.
2. **Spiral:** a failed plan doesn't reset, it gets bigger. Stall time goes 4s, 8s, 16s (cap 32s from Ch3). The S2 cap is 8s and S3 removes it.
3. **Exhaustion:** he stays up rewriting plans. Fatigue tiers: −10% speed; +1 spiral step on failure; then 2s microsleeps every 60s. Only a persuaded rest at camp clears it.
4. **Overloaded:** about 40 items slow him (−15%) and make him louder.
5. **Stone paranoia.** He **proactively destroys chronicle stones** within 25m (an added step at the top of his plan, only when no enemy is within 6m), afraid the boss will read him. That costs time and exposure. It keeps Curator Intel low, but the **absence of footage is itself the diagnosis**: the Curator infers a man who fears being read has overprepared.

| Stage | Rules |
|---|---|
| S0 | As above. |
| S1 | Leaves slack in plans; slower spiral growth; adds an Improv slot. |
| S2 | Assigns you steps (your icon appears in the checklist). Accepts off-book calls. Spiral cap 8s. Spares stones you point out. |
| S3 | **Adapt:** no spiral. He rests. He stops smashing stones ("Let it watch. Let it guess wrong."). He trusts you on watch. |

*Signature skills:* Strike **Opening Gambit** (prepared opener), Stance **Contingency** (auto-negates the first ambush hit), Finisher **The Master Plan** (multi-step combo; a broken step cuts damage).

| Moment | Weight |
|---|---|
| Step completed for him (item delivered, lure placed) | 3 |
| Broken step repaired within the stall window | 4 |
| **Off-book call that worked** (your action within 2s of a step failure that recovered it) | 5 |
| Got him to rest at camp | 4 |
| Restocked after Burn | 2 |

*Negative assets:* **Sabotaged step** (your action invalidated a step without recovery) −3. **Blew the plan** (Grease or Bomb hit him or derailed it) −4. Friendly fire −3. Abandon −3.

*Nemesis squad (Ch4):* "The Improvisers": enemies that change formation every 10s.

*Campfire beats:* Ch1 he rewrites the plan by lamplight. Ch2 he offers you a step. Ch3 the S0 version says "Contingency Nine, as planned." Ch4 he adds your icon to the checklist. Ch5 (S3) he sleeps while you keep watch.

*Boss: The Unwritten.*
- **Room:** the layout rewrites every 15s.
- **Trap:** a forged page in Ansel's own handwriting, a flawless plan with **no line for you** (the Curator doesn't know you exist). At low Stage he follows it, leaves you outside and walks into the kill path.
- **Gradient:**
  - S2: he adds you as a step, but the forged path still needs an off-book call to survive.
  - S3: "This isn't my plan. My plan would have a line for you."

*Builds:* **Fixer-Combat** (keep the room clear of surprises), **Scholar-Handler** (repair the plan), **Provisioner-Packrat** (restock and rest).

### 6.3 Bram Hale, the Brash

*Belief:* hesitation is how you lose.

| Stage | Rules |
|---|---|
| S0 | (1) Charge the nearest hostile within 25m, ignoring cues. (2) No retreat. (3) **Loud:** aggro radius +50%. |
| S1 | A 1s hesitation window if you ping "wait". |
| S2 | **Ready check** at thresholds (doors, arenas, bridges): a prompt with a 6s timer. "Ready" grants an **Opening Surge** scaled by your active prep. "Wait" lets you prepare but starts a 30s reinforcement clock. |
| S3 | He asks "You ready?" at every threshold and won't cross until you answer. |

*Signature skills:* Strike **Haymaker** (big cleave), Stance **Berserk Momentum** (damage stacks per kill, lost on retreat), Finisher **Breakthrough** (charge through a line).

*Stones:* his charges wake them automatically, the chaotic footage raises Curator Intel quickly.

| Moment | Weight |
|---|---|
| Save (interrupted a serious hit via Buckler, Pull Back, Bait & Switch) | 3 |
| Flank cleared | 2 |
| Honest ready (Ready with real prep, or Wait then Ready) | 3 |
| Rescue from a trap or hazard | 3 |
| Opening Surge captured | 3 |

*Negative assets:* **False Ready** (you said ready without prep) −3. **Tripwire** (your traps hit him) −2 each. Abandon −3.

*Nemesis squad (Ch4):* "The Trapsmiths": killzone corridors and baited chokepoints.

*Campfire beats:* Ch1 he brags. Ch2 he sharpens his sword and asks "You good?" Ch3 he is quiet after a close call. Ch4 he looks back at you before the door. Ch5 "You ready?"

*Boss: Warlord Kethra at the end of the Promenade.*
- **Room:** a corridor of about 60m with spike plates, crossbow nests and collapsing arches. Kethra stands at the end with open arms.
- **Gradient:**
  - S0: Bram charges in the first second and dies in about 8s.
  - S1: the 1s hesitation lets you scout and disarm nests (a narrow path).
  - S2: a ready check at the gate, with reinforcements after 30s.
  - S3: he waits until you say go.

*Builds:* **Bodyguard** (Buckler, Bait & Switch, Bandage), **Scout** (Read the Room, Forecast, Map Sketch), **Trapper** (Loosen Bolt, Caltrops, Crossbow).

### 6.4 Vaughn Starfall, the Star

*Belief:* if nobody saw it, did it happen?

**Crowd** (0–100) drives his power: ×0.5 at 0, ×1.0 at 50, ×1.5 at 100. Sources: onlookers (villagers, rescued captives, travelers) +10 each within 15m, active stones facing him +25 each, non-engaged enemies watching +5 each.

| Stage | Rules |
|---|---|
| S0 | (1) Pose for 2s at a stone or vista when Crowd is under 50 and one is within 20m. (2) Showboat: long-windup flashy combos when Crowd is 60 or more. (3) **Stage fright:** Crowd 0 for over 10s in combat means −40% damage and fumbles. |
| S1 | Calls you "my stagehand". Accepts Applause cues. |
| S2 | In an empty room he performs for you: your **Watching stance** (standing still and facing him) counts as +20 Crowd, but you can't act. |
| S3 | **One witness is enough:** you count +60 Crowd within 10m with no stance needed. |

*Signature skills:* Strike **Flourish** (chain damage scales with Crowd), Stance **Spotlight** (taunt while posing, −25% damage), Finisher **Encore** (repeat the last ability free; bonus at Crowd 60+).

*Stones:* he is the only hero who actively engages them. He seeks them out, polishes and angles them.

| Moment | Weight |
|---|---|
| Crowd raised (recruited onlookers, repaired or angled stones) | 3 |
| Spotlight protected (covered a Finisher wind-up) | 3 |
| Applause cue at a key beat | 2 |
| Witness given (S2+) | 3 |
| Stone tended | 2 |

*Negative assets:* **Stone smashed** while he uses it −3. **Civilians harmed** −4. **Emptied the room** (you scared away onlookers) −2. Friendly fire −3. Abandon −3.

*Nemesis squad (Ch4):* "The Silencers": hecklers who reduce Crowd, and dark-corridor ambushers who douse lanterns and break stones.

*Campfire beats:* Ch1 he rehearses. Ch2 he introduces you as "my people". Ch3 an empty clearing feels wrong to him. Ch4 he performs a line just for you. Ch5 "One is enough."

*Boss: The Hollow King in the Empty Hall.*
- **Room:** doors sealed, no stones, dark.
- **Trap:** Crowd stays 0, so his power is ×0.5 with fumbles.
- **Gradient:**
  - S0 and S1: he leaves the fight looking for an audience.
  - S2: he performs for you, and you lose your actions while watching.
  - S3: your presence alone is enough.

*Builds:* **Stagehand** (Flattery, Pep Talk, Forgery), **Protector** (Buckler, Smoke Bomb, Bandage), **Lighting Crew** (Loosen Bolt, Read the Room, Rope Trick).

---

## 7. Boss summary

| Hero | Persona and room | Trap | Stage that beats it |
|---|---|---|---|
| Callum | Lord Ashgrave, Rigged Duel | "No aid" terms plus hidden archers | S3 voids the terms |
| Ansel | The Unwritten | Forged plan with no line for you | S3 rejects it |
| Bram | Warlord Kethra, the Promenade | Kill-zone corridor | S3 waits for your word |
| Vaughn | The Hollow King | Empty, dark hall | S3: one witness is enough |

All bosses share Phase 3, the Mirror: the Curator becomes a Stage 0 copy of your hero, and the fight ends with a Duet Finisher.

---

## 8. Narrative and cutscenes

**Opening cutscene (about 40s, UI and audio only):**
1. The walk, earbuds and a royalty-free song. A horn rises, the song cuts, white screen.
2. The status window: `CLASS:` rolls through *Barista, Tax Auditor, Dark Lord, Hero* and settles on **HERO**. Player thought: "YES. I finally get to be the hero."
3. A **red error box** appears. **Red glyphs** cycle through it. They settle with **'s SIDEKICK** appended: **HERO's SIDEKICK**.
4. When you first meet the hero, "HERO" quietly swaps for his name.
5. Later runs use a 3-second version.

**Dossier scraps (end of chapter 3):**
- *Callum:* "SUBJECT: CALLUM, called 'the Honorable'. Observed: salutes before striking. Waits for the fallen to rise. Conclusion: will keep a bargain after it is broken."
- *Ansel:* "SUBJECT: ANSEL VOSS. Footage: none. Every stone within a mile of him is dust. A man who fears being read has already written what he will do. Prepare a page for him."
- *Bram:* "SUBJECT: BRAM HALE. Footage: abundant. Charges the first thing that looks back. Give him something to look back."
- *Vaughn:* "SUBJECT: VAUGHN STARFALL. Footage: abundant, posed. Take the audience. Keep the stage."

**Boss diagnosis monologues** (Phase 0, 3–4 lines each, tone: calm, collector-like): the Curator names the flaw precisely, as above, and ends each with a line that shows he has no entry for you.

**Endings.** Win: each hero says his "we" line, and the System window reads `CLASS: [NAME]'s SIDEKICK. STATUS: LISTED`, then offers a class re-roll the player can decline. Loss: `HERO: [NAME]. DECEASED` in red glyphs, the Curator's diagnosis replays, then the Post-Mortem (11.3).

**Bark framework.** Each hero has about 20 rule triggers with 3 escalation tiers (S0–S1, S2, S3) and 3 variants each, about 60 barks per hero. **Write them yourself or rewrite AI drafts heavily**, since shipped AI-written text likely needs a Steam disclosure. Tone rule: the hero is sincere and competent, and the joke is a rule meeting a situation, never stupidity.

**Tone rules.** The flaw is an over-held virtue. You never mock the hero. His arc is acknowledging you: early on he credits luck, by the end he says "we."

---

## 9. UI and audio

- **HUD:** hero rule icons, per-hero meters (Honor, Plan checklist, ready prompt, Crowd), your skill bar (4–6 slots), wounds on the hero's portrait. No Rapport or Stage display.
- **Accessibility:** a **Hero Insight** toggle (shows a plain label for hero state, at the cost of mystery), a longer ready-check timer, colorblind-safe icons, gamepad support.
- **Audio:** royalty-free or self-made music and SFX. Verify licenses, and disclose any AI-generated audio on Steam.

---

## 10. AI production plan

- **AI builds well:** behavior trees, systems, UI, room assembly, procedural animation, shaders, tests, the balance harness.
- **AI builds poorly:** complex character animation and hand-crafted levels. With full 3D humanoids this is the project's biggest production risk, so characters use pre-made rigs and animation libraries, and rooms are modular prefabs.
- **Characters:** the Quaternius CC0 kit (6 base models, 120+ animations on one humanoid rig), modified with headless Blender (bpy scripts or a Blender MCP server) for proportions, outfits, colors and props, then exported to FBX or glTF for Unity. Mixamo adds clips where its terms allow (royalty-free, but files can't be redistributed standalone, and Adobe account access has been inconsistent). Paid packs (Synty, about $30–$150 each) are too expensive for a ₹5,000 budget. The Curator's personas are four model variants plus a dissolve shader. The Mirror is the hero's model plus a glitch shader.
- **Bespoke animations** (Callum's salute, Ansel writing, Vaughn's pose, arming a trap, bandaging) may not exist in these libraries. Source them, or use simple procedural upper-body overlays plus effects. Whether an agent can author good new animation in Blender is unverified.
- **Assets:** props from CC0 kits plus optional AI-generated props (Meshy or Tripo, disclose if shipped). Verify every license.
- **Architecture** (suggested folders under `Assets/_Game/`): `Core`, `Hero`, `Sidekick`, `Skills`, `Rapport`, `Stones`, `Curator`, `Rooms`, `UI`, `Data`. Systems: `HeroBrain` (behavior tree), `SkillSystem`, `OpportunityDirector`, `RapportLedger`, `StageEvaluator`, `InjurySystem`, `StoneSystem`, `CuratorDirector`, `CampfireDirector`, `SaveRestore`, `Telemetry`.
- **Usage caps:** prefer data tables and small tasks, and keep this document in the repo so the agent can read it.

---

## 11. Build plan

### 11.1 Milestones (hour estimates are my guesses)

| Milestone | Scope | Hours |
|---|---|---|
| M0 | Toolchain (Unity 6 plus MCP plus agent), project scaffold, style frames | 15–20 |
| M0b | Character pipeline spike: 1 hero plus the sidekick, 12 animations in Unity, unified look | 15–25 |
| M1 (slice) | Callum, 3 rooms, a mini rigged-duel boss, 6 skills, Stage 0 to 1 | 20–30 |
| M2 | Callum full (Ch1–5), Rapport engine, stones, Curator pipeline | 80–120 |
| M3 | Bram | 50–70 |
| M4 | Ansel | 60–90 |
| M5 | Vaughn | 60–90 |
| M6 | Polish, balance, QA, builds | 80 |
| Total | | about 390–580 (about 20–29 weeks at 20h/week; my guess, up from 350–500 after the full-3D change) |

### 11.2 Slice spec (the 20-hour test)

- Hero: Callum. One chapter of 3 rooms plus a mini rigged-duel (3 archers, fixed signal).
- Skills: Pocket Sand, Loosen Bolt, Quiet Feet, Crossbow, Bandage, Cover Story.
- Rapport: Moments and penalties for Callum, Stage 0 versus Stage 1 only.
- One campfire scene in two variants.
- **Gate criteria:**
  1. Playtesters retell a moment unprompted.
  2. They can tell Stage 0 from Stage 1 by behavior.
  3. The mini-boss loss feels attributable.
  4. At least 2 builds are viable.
  5. Characters read clearly at the fixed camera and animate acceptably (no obvious foot-sliding).

If it fails, pivot or kill (see the earlier Escort fallback).

### 11.3 Fairness contract (hidden-stat)

1. **Hidden number, visible behavior.** Every Stage change shows in rule icons, barks and campfire scenes. *Test:* testers identify Stage 0 versus Stage 2 from 30-second clips.
2. **A slope, not a cliff** (the gradient in 4.4).
3. **Attributable losses.** The Curator's diagnosis replays, then a Post-Mortem lists captured and missed Moments (qualitative).
4. **Foreshadowing ladder.** Ch2 hints, ch3 dossier, ch4 scout, the hero's own line at the door.
5. **Restore economics.** After a loss, restore to a chapter start (skills kept, Rapport restored to that snapshot). About 12–15 minutes to replay chapter 4 and the approach.
6. **Valid measurement.** Moments come from the hero's own behavior, missed ones recur, 25–30% slack above the Stage 3 threshold, and Rapport only drops through negative assets.
7. **Neglect is punished, not build choice.** Combat skills are legitimate.

**QA checks:** at least 80% of testers can say why the hero died, fewer than 20% call a loss unfair, and at least 60% retry within 2 minutes.

### 11.4 Balance harness

A headless batch mode runs 1,000 seeds per hero with bot sidekicks:
- **Idle bot:** measures solo-clear rates (target 70/50/30/10/0 by chapter).
- **Sloppy bot:** moves near the hero, uses skills randomly, never aims. **Target: 90% reach Ch3, 50% reach Ch4, 10% clear Ch4.**
- **Combat-only bot:** high clear rate, near-zero boss wins.
- **Supportive bot:** scripted Moment capture, boss win target about 35–60% at Stage 3.

### 11.5 First agent tasks (with acceptance tests)

1. Project scaffold, Input System, Cinemachine, fixed-angle camera keeping two targets in frame.
2. Import the base character and animation library, set up the Humanoid avatar and retargeting, and build a locomotion controller (walk, run, dodge, hit reaction).
3. Sidekick controller (move, dodge, baseline knife, leash range).
4. HeroBrain framework (prioritized rules, icon display, route graph).
5. Room module prefab format and seeded assembler.
6. Skill system (ScriptableObject data, slots, cooldowns).
7. Six slice skills.
8. Callum's S0 and S1 rule sets, Honor, witness cone.
9. Opportunity Director and Rapport ledger (Moments, penalties, caps).
10. Stage evaluator with checks and caps.
11. Wound system.
12. Stone system (states, witness logic).
13. Mini rigged-duel boss.
14. Campfire scene with two variants.
15. Test harness: determinism tests, sloppy-bot runs.

---

## 12. Release and business

- **Launch path:** itch.io (paid, about $5.99–$8.99, 10% default share, free to list) plus a WebGL slice demo. **Steam later**, after earnings cover the $100 fee (about ₹9,600 at about ₹96 per $1). Steam's $100 is recoupable after $1,000 revenue. Payout tax rules (itch.io withholding, Steam W-8BEN) need a CA check. I haven't verified India-specific treatment.
- **All four heroes launch together.**
- **Budget:** under ₹500 (itch.io tax interview fee), no API costs, everything else free. Steam's fee waits for revenue.
- **Discovery:** reels of the glitching cutscene and hero bits, r/litrpg and similar communities, streamers, and later Steam Next Fest. Avoid Dungeon Crawler Carl branding and phrasing.
- **AI disclosure:** code assistance needs none. Shipped AI-generated text, art or audio likely does.

---

## 13. Risks and open questions

**Risks**
- **Scope:** four heroes at launch is about 4× content. Build order is Callum first.
- **Character pipeline:** full 3D humanoids are AI's weak area. Mixed asset sources can look inconsistent, retargeting can produce foot-sliding, and bespoke animations may not exist. Mitigate with one base family, a unifying shader, the M0b spike, and cutting bespoke animations to poses plus effects if needed.
- **Hidden-stat comprehension:** if testers can't explain a loss, the game fails (the 11.3 tests).
- **Behavior-tree reliability:** mitigate with data-driven rules and the test harness.
- **Hero readability:** icons and dotted paths must be legible. Test early.
- **Boss reuse:** mitigated by personas and Mirror.
- **Emotional payoff:** depends on barks and campfire writing, which is yours.
- **Return-per-hour:** total hours are high for a modest-priced indie game.

**Open questions**
1. Hero select flow (random roll gag versus direct pick after the first run).
2. Price point.
3. Whether Ansel's stone-smashing step is fun or just a chore.

**Decided**
- Full 3D humanoid characters (not floating-limb).
- Sidekick death before the end of chapter 2 ends the run, by design.
- Recall is learned at the end of chapter 2, with Stage-dependent reactions and mechanics.
