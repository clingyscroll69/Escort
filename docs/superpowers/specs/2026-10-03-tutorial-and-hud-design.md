# Tutorial system, skill demos and synergies, HUD/UI pass (design)

Status: approved by the owner on 2026-10-03 ("build it all"; Hero Insight stays **off** by default and the tutorial
teaches Tab). Scope: everything after the opening. The opening (walk, truck, class roll) is not touched.

## 1. Goals

1. **Learn as you go.** Every mechanic, control and HUD element is explained the first time it matters, never
   front-loaded. Nothing is taught twice; everything taught can be re-read in a Field Guide.
2. **Explain everything, give away little.** Mechanics are explained fully (what the cone is, what Honor does, how
   to disarm a trap). Strategy is only hinted ("What he doesn't see, he can't judge."). The hidden stat (Rapport,
   Stage, Moments, penalties) is never mentioned, named or quantified anywhere (GDD §2, §4.4, §11.3).
3. **Every skill pick explains itself**: what it does, how to use it, numbers per rank, how Callum's code reads it,
   a skippable live demo, and synergies with the skills you already own.
4. **A clearer, better-looking HUD**: icons instead of text-only slots, key/button glyphs, level and XP, the hero's
   current rule, wounds as chips, a pause menu.

Non-goals: new skills, new rooms, a scripted tutorial level, balance changes, changes to the opening.

## 2. Spoiler policy (applies to all copy)

- Never: "Rapport", "Stage", "S0/S1", "Moment", "capture", "points", "trust" as a quantity, numbers about the hero's
  opinion of you. A test scans all tutorial, guide, demo and synergy copy for these words.
- Mechanics: say what a thing is and does, with numbers where the game already shows them (skill values, ranges).
- Strategy: an observation or a question, never an instruction. Allowed: "A yield is sacred to him. Is it to them?"
  Not allowed: "Throw sand on the turncoat when he surrenders."
- GDD §4.7 ("tooltips describe uses, never recommendations") holds for skill text. Synergies describe how two skills
  interact; they never say "pick this".
- All copy is draft text for the owner to rewrite (as with the barks).

## 3. Architecture

New runtime folder `Assets/_Game/Tutorial/` (assembly `Game.Runtime`, namespace `HS.Tutorial`) plus UI classes in
`Assets/_Game/UI/` (namespace `HS.UI`). Nothing in the simulation changes behaviour; the tutorial only reads state,
pauses the sim for a few lessons, and draws UI.

| Unit | Purpose | Depends on |
|---|---|---|
| `TutorialProgress` | Seen lesson ids and settings (tips on/off, lesson pauses on/off, Insight default). Backed by an `ITutorialStore` (PlayerPrefs in the game, in-memory in tests). | — |
| `Lesson` + `Lessons` | Lesson data: id, category, kind (Tip / Focus / Inline), title, body (with key tokens), coach-mark target, expiry. A static C# table, like the room layouts. | — |
| `KeyGlyphs` | Resolves tokens (`{ping}`, `{skill2}`, `{insight}`…) to keyboard or gamepad labels from `GameInput`'s real bindings; follows `GameInput.UsingGamepad`. | `GameInput` |
| `TutorialDirector` | Per-run MonoBehaviour. Watches the event bus, the flow and polled state; decides when a lesson fires; throttles; marks lessons seen; completes "do this" lessons when the player does it. | RunContext, GameFlow, HUD hooks |
| `ModalGate` | Reference-counted "pause the sim / suspend gameplay input" used by focus lessons, the pause menu and the Field Guide. Restores what was there before. | SimLoop, GameInput |
| `TipView` (UI) | Toast cards (right side), the focus overlay (dim + spotlight + card), coach-mark pulses on HUD elements, world markers. | UIKit, HudView anchors |
| `PauseMenu` (UI) | Esc/Start: Resume, Field Guide, Controls, Settings, Quit. | ModalGate |
| `FieldGuide` (UI) | Tabs: Tips (seen lessons; unseen show "???"), Skills (every implemented skill with its guide, demo and synergies), Controls (keyboard and gamepad). | Lessons, SkillGuides, SkillDemoPlayer |
| `SkillGuides` | Per implemented skill: tagline, how to use it, stat lines per rank computed from `SkillDefinition` (so balance changes flow through), "How Callum sees it". | SkillCatalog |
| `SkillSynergies` | Hand-written pair notes (unordered pairs of implemented skills that actually interact) and a lookup against a kit. | — |
| `SkillDemoStage` + `SkillDemos` | The live demo: a holographic stage far below the world, puppet characters built from the real character visuals (no Agent, no sim registration), a camera rendering to a RenderTexture, and one scripted timeline per skill (rank-aware). | GameAssets, AnimDriver, Vfx |
| `SkillPicker` (rework) | Two-column System window: list (icon, family colour, rank pips, synergy badge) and detail (guide, demo viewport, synergies, Callum's view). Loadout at camp. | all of the above |
| `HudView` (rework) | Icon skill bar with keycaps, verbs strip, hero card (rule chip, Honor line, wound chips), sidekick card (level/XP, dodge recharge, sneaking / out-of-reach). | UIKit, new sprites |

### Isolation of the demos

Demo puppets are the `Visual` child of the gameplay prefabs (model + Animator + `AnimDriver`) with the
`VisualInterpolator` removed and no `Agent`, so they never enter `AgentRegistry`, `SimLoop` or any `EventBus`. The
demo runs on unscaled time, so it plays while the sim is paused (picker at the opening, Field Guide). Effects use the
presentation-only `Vfx` pool and 2D `AudioDirector` cues. A PlayMode test asserts that a full demo run raises no
event on the run's bus, registers no agent and leaves no objects behind.

The stage sits at y = −400 (nothing else is there), lit by the scene's sun, on a grid floor with a dark clear
colour. Its camera renders only while a demo viewport is visible.

### Input and pausing

- Focus lessons, the pause menu and the Field Guide go through `ModalGate`: `SimLoop.Paused = true` and the
  Gameplay action map disabled while any are open (so the key that closes a card can't also dodge or stab).
- Esc/Start opens the pause menu in the Chapter and Duel states only (not over the opening, picker or end screen).
- Hero Insight default: off. `TutorialProgress.InsightDefault` exists (Settings) but starts false.

### Persistence

PlayerPrefs keys: `hs.tut.seen` (comma-separated ids), `hs.tut.tips` (1/0, default 1), `hs.tut.pauses` (1/0,
default 1), `hs.insight` (1/0, default 0). A lesson is marked seen when it is shown, so Restore Points don't repeat
it. Settings has "Reset tutorial". AutoPlay runs (bots, harness, QA) never create a `TutorialDirector`.

## 4. Lessons

Kinds: **Tip** = toast card, non-blocking, auto-dismisses (on completion or timeout). **Focus** = the sim pauses, the
screen dims around a spotlight, a card explains; any confirm key continues (setting "Lesson pauses: off" turns
these into Tips). **Inline** = text inside a screen (picker, end screen).

Throttling: one toast at a time; at least 3 s between toasts; a queued toast whose moment has passed (expiry) is
dropped unseen and can fire again next time its situation comes up; Focus lessons pre-empt toasts and only fire in
the Chapter or Duel states with no other modal open; nothing fires for 2 s after a Focus card closes except its
chained follow-up.

Tokens like `{ping}` render as key glyphs (keyboard or gamepad, whichever was used last).

| id | kind | fires when | done when | copy (draft) |
|---|---|---|---|---|
| welcome | notice | chapter start, first time | — | System window: "TUTORIAL: no file found for class HERO's SIDEKICK. Generating one… (unofficial)" |
| move | Tip | 2.5 s into the chapter | moved 4 m | "{move} to move. Hold {walk} to walk carefully." |
| hero_rules | Focus | Callum first stops at a threshold | continue | "Sir Callum follows a code, one rule at a time. The icon over his head is the rule he's on right now — learn them and you'll know what he'll do next. He stops at every threshold before going in. That pause is yours: look ahead, get ready. You can only help him from close by." Spotlight: Callum + icon. Coach: rule chip. |
| insight | Tip | right after hero_rules | Insight toggled | "{insight}: Hero Insight spells out his current rule. Off keeps the mystery." |
| attack | Tip | first encounter starts | knife used | "{attack}: the kitchen knife. It isn't much — your tricks are the real weapons." |
| tricks | Tip | after attack (if any active skill) | a skill used | "{skills}: your tricks. Aim with the mouse; each slot shows its cooldown." Coach: skill bar. |
| dodge | Tip | first time an enemy targets you or you're hurt | dodged | "{dodge}: roll. Three charges (the pips); you're untouchable for a moment." Coach: dodge pips. |
| cone | Focus | Callum's first challenge | continue | "The pale wedge in front of him is what he sees. He's a knight, and he judges what he sees. Dirty tricks in his sight — sand in a man's eyes, a bolt in someone else's duel, striking the helpless — cost him HONOR (the gold bar). Low Honor makes his sword arm heavy. What he doesn't see, he can't judge." Spotlight: cone. Coach: Honor bar. |
| salute | Tip | after cone closes | salute finished | "He salutes before every duel. Touch his opponent before it's over and you've spoiled it." |
| unready | Tip | first wait-on-the-unready (not a surrender) | 8 s | "He won't strike a foe who can't fight back — blinded, staggered, turned away. He lowers his sword and waits (the hourglass)." |
| surrender | Tip | first surrender near him | 8 s | "He spares anyone who yields; it's in the Code. Whether they meant it is another matter." |
| caught | Tip | first time he catches you | 9 s | "He saw that. Honor falls, and for a while he watches you more closely (his cone widens). {coverHint}" (`coverHint` = "Cover Story ({skillN}) can talk him round." if owned, else "Some tricks can talk him round.") Coach: Honor bar. |
| honor_low | Tip | Honor first drops below the line | 9 s | "His Honor is low: his blows land softer until it recovers. Clearing a room steadies him." |
| spoiled | Tip | first spoiled salute | 8 s | "He takes the salute seriously. Next time, let him finish." |
| fallback | Tip | first fall-back | 8 s | "Three or more on him and he falls back to the narrows, making them come one at a time (the shield)." |
| wounds | Tip | first wound | 9 s | "A hard hit wounded him (under his health). Wounds stay until treated: Bandage handles minor ones; the camp tends one." Coach: wound chips. |
| ambush | Tip | first hidden foe springs (not flushed by you) | 8 s | "Hedges and ruins hide people. Hidden foes can be flushed out early — by someone who gets there before he does." |
| stone | Tip + marker | first chronicle stone within 18 m | 10 s | "A chronicle stone. While it glows, anything in its blue view is on record — a dirty trick it sees counts as witnessed. Three knife cuts or one bolt break it." |
| trap | Tip + marker | first armed trap within 12 m | disarmed or 10 s | "He walks his road straight over traps. You can see them: stand on one and {interact} to disarm it. Walking or crouching, you step over them." |
| prop | Tip + marker | first idle armable prop within 10 m, Loosen Bolt known | armed or 10 s | "This could come down on someone. Kneel beside it and use Loosen Bolt ({skillN}) to arm it." |
| ping | Tip | first prop armed | pinged | "Armed. {ping} on it brings it down — or let him lead a bandit underneath." |
| crouch | Tip | first threshold pause, Quiet Feet known | crouched | "{crouch}: crouch. With Quiet Feet, unaware enemies can't hear you beyond 2 m, and his cone narrows." |
| cache | Tip + marker | first unsearched cache within 12 m | searched | "Something's tucked away here. {interact} to search: exploring pays a share of every room's XP." |
| channel | Tip | first channelled action | 7 s | "Channelled tricks need you still: moving or taking a hit breaks them." |
| threats | Tip | first off-screen chevron | 7 s | "A red chevron on the edge: someone off screen is drawing on one of you." |
| hero_offscreen | Tip | first time Callum leaves the frame | 7 s | "The gold marker is Callum, off screen. He won't wait for you." |
| out_of_reach | Tip | first time > 25 m away during a fight | back in range | "Too far. From here, nothing you do helps him." |
| xp | Tip | first XP award | 8 s | "XP. Every room pays the same: for the clear, for helping, for exploring. Levels arrive at the camp." Coach: XP bar. |
| pause | Tip | 75 s into the chapter, nothing else showing | 8 s | "{pause} pauses. Everything you've learned is in the Field Guide." |
| levelup | Inline | first picker | — | "Pick any trick, any time; picking one you know raises it to rank II. Click a trick to read it and watch the simulation, click again to learn it." |
| camp | Tip | camp arrival | 12 s | "Rest tended his worst wound and patched you both up. Your level-up comes when he's done talking. Watch him by the fire — what he does here says what he makes of you." |
| loadout | Inline | first camp picker | — | "Loadout: {slots} slots for active tricks. Click one to bench it or bring it back. Passives are always on." |
| duel | Focus | the duel's terms begin | continue | "A formal duel. Callum swore to the terms: no aid. He keeps his word, and he'll hold you to it. His word binds him. It doesn't bind anyone else in this room." Spotlight: Callum and Ashgrave. |
| restore | Inline | first end screen after a loss | — | "Restore Points: start again from the chapter start or from just before the duel. Your tricks come with you." |

(The approved summary listed the camp as a freeze-frame lesson. The camp's dialogue runs on real time, so a paused
card would hide it; the camp lesson is a Tip and the duel's terms take the third Focus slot.)

## 5. Skill picks

### 5.1 Picker layout (1920×1080 reference)

A centred System window (~1560×900) over a dimmed backdrop. Header: title, picks left as pips. Left column (~460 px):
one card per implemented skill — icon, name, family colour chip, "passive" tag, rank pips, a gold ⟷ badge when it
pairs with something you own. Right column: the detail pane:

1. Name, family, type; the tagline.
2. **Demo viewport** (16:9, ~720×405) with a caption line, "SKIP ▸▸" and "REPLAY ↺" buttons (keys: S/Backspace or
   X to skip, R or Y to replay). Autoplays when a skill is selected.
3. What it does (the `uses` text), how to use it (keys), stat lines per rank (rank II highlighted when it would be
   the next pick).
4. **How Callum sees it** (from `SkillDefinition.dishonor` plus the guide's note).
5. **Synergies**: lines for pairs with skills you own ("With your Quiet Feet: …"), then dimmed "Also pairs with: …".

Bottom bar: at camp, the loadout (slot boxes with icons; click a known active to equip or bench it); the Continue
button. First-picker and first-camp inline hints sit under the header.

Click semantics stay as today (tests rely on them): rows are named `Skill_<id>`, the first click selects (reads,
plays the demo), a second click on the selected row learns it; the button is named `Continue`. Learning a skill
shows a short "NEW SYNERGY" flourish for every pair it completes with your kit.

At camp the picker opens after Callum's fireside lines (about 13 s; any confirm key skips to it), so the scene
isn't covered.

### 5.2 Skill guides (draft copy)

| skill | tagline | Callum's view |
|---|---|---|
| Pocket Sand | "A handful of grit, thrown where it hurts." | "A dirty trick if he (or a glowing stone) sees it. Sand his own opponent and he'll wait for the man to recover." |
| Loosen Bolt | "Everything on this road is held together by optimism." | "A dirty trick if he sees the collapse. Arming it isn't — it's the drop that counts." |
| Quiet Feet | "Nobody notices the help." | "Nothing to see. That's the point." |
| Crossbow | "Slow to load, hard to argue with." | "A bolt into his duel is a slight; one into a helpless man is a disgrace. Unseen, it's just a bolt." |
| Bandage | "Hold still. This will sting." | "Honest work. He holds still for it." |
| Cover Story | "'That? The wind, sir.'" | "He chooses to believe you. Mostly." |

Stat lines are computed from the `SkillDefinition` values (cooldown, valueA, valueB) per skill, e.g. Pocket Sand:
Blind 3 s / 4 s · Range 8 m · Cooldown 8 s.

### 5.3 Synergies (draft copy, unordered pairs)

| pair | note |
|---|---|
| Pocket Sand + Quiet Feet | "Crouched, you shrink his cone — a cloud thrown from the side is much harder for him to see." |
| Pocket Sand + Crossbow | "A blinded shooter can't fire back. A bolt into a blinded man is 'striking the helpless' if he sees it." |
| Pocket Sand + Cover Story | "If he does see the sand, a cover story right away softens it and steadies his Honor." |
| Pocket Sand + Loosen Bolt | "Blinded bandits stand still — under an armed prop, for instance." |
| Pocket Sand + Bandage | "Sand buys the quiet seconds a bandage needs." |
| Loosen Bolt + Quiet Feet | "Crouched, his cone narrows: a collapse at its edge may go unnoticed." |
| Loosen Bolt + Crossbow | "The collapse takes the crowd; the crossbow takes whoever walks out of the dust." |
| Loosen Bolt + Cover Story | "Seen dropping a wagonload of barrels on someone? 'Loose rocks, sir. Very old road.'" |
| Quiet Feet + Crossbow | "Shoot from a crouch: his cone is narrower, and bandits who haven't spotted you stay unaware." |
| Quiet Feet + Cover Story | "Two answers to the same problem: don't be seen, or talk your way out when you are." |
| Crossbow + Cover Story | "A bolt into his duel is only a slight — a cover story smooths it over." |
| Bandage + Cover Story | "Patch his body, mend his pride." |

Pairs not listed don't interact and show nothing ("when applicable"). Learning a second rank shows the rank II line
instead of a synergy.

### 5.4 Demos (≈ 8–12 s each, rank-aware, captions in order)

Stage: 16 × 10 m grid floor; camera at the game's 50° pitch. Overlays drawn in UI space over the viewport (captions,
icons over heads, "!" marks, key callouts, small bars); ground decals (cones, rings) are meshes on the stage.

1. **Pocket Sand.** Callum duels a thug inside his cone; a crossbowman draws on him (red aim line). The sidekick
   moves outside the cone, throws (key callout), the cloud blinds the crossbowman (stars) — "He never saw a thing."
   Then the same throw inside the cone at his opponent: "!" over Callum, Honor bar drops, hourglass — "Seen: Honor
   falls, and he waits for the blinded man."  End card: blind 3 s (II: 4 s), reveals hidden foes, range 8 m.
2. **Loosen Bolt.** Kneel at a barrel stack (channel bar) → red danger ring → a bandit walks under → ping marker →
   collapse (dust, heavy hit). "2 armed at once (II: 3)."
3. **Quiet Feet.** Crouch-walk past a dormant bandit just outside a 2 m ring: nothing. Callum's cone narrows while
   crouched. Stand up and walk: "?" then "!" over the bandit.
4. **Crossbow.** Aim line, bolt, a perch shooter drops; rank II: the bolt pierces two. Then a bolt into his duel
   inside the cone: small "!" — "a slight".
5. **Bandage.** Callum hurt with a wound chip; kneel beside him (3 s bar); heal over 6 s, the chip clears. Then a
   bandit's hit breaks a second attempt: "Moving or a hit breaks it — the cooldown isn't lost."
6. **Cover Story.** Callum scolds ("!", Honor low); the sidekick's line "A rabbit. An enormous, violent rabbit.";
   Callum: "Hm. The wind, you say." Honor refills — "Right after you're caught, it softens the fallout."

## 6. HUD and UI

### 6.1 Art (procedural, no AI imagery; `tools/make_ui_textures.py`, `tools/make_icons.py`, paths made repo-relative)

Skill icons (6), verb icons (knife, dodge, ping, crouch, interact), wound icons (5), family emblems (5), keycap
9-slice, slot frame 9-slice with bevel, corner brackets, ring, spotlight vignette, demo grid floor, hatch (locked).

### 6.2 Layout

- **Hero card (top-left):** name; HP bar with quarter ticks; Honor bar with its icon and a tick at the low line
  (pulses when low); a rule chip with his current rule icon (and the Insight label when on); wound chips.
- **Sidekick card (bottom-left):** "YOU  CALLUM's SIDEKICK" (format kept for the HERO → CALLUM swap and its tests);
  level badge and XP bar ("LEVEL UP AT CAMP" when one is banked); HP bar; dodge pips that fill while recharging;
  status chips "SNEAKING" / "CROUCHED" and "OUT OF REACH".
- **Skill bar (bottom-centre):** four slots with icons, keycaps (1–4 or RT/RB/LT/LB), radial cooldown with seconds,
  a flash when ready, rank pips, a family-colour underline; passive badges to the right (Quiet Feet lights while
  sneaking); a small verbs strip to the left (knife, dodge, ping, crouch with keycaps).
- **Prompt:** keycap chip + verb. **Channel bar:** label, bar, the key that cancels it.
- **Boss bar:** name plate with corner brackets, notched bar.
- **System window:** header strip, corner brackets; typing behaviour unchanged.
- **Tips:** toast stack on the right edge under the System window; a slim progress line shows time left; a ✓ when
  done.
- **Pause menu / Field Guide:** System-window styling, keyboard/gamepad navigable.

## 7. Testing

EditMode: lesson table integrity (unique ids, non-empty copy, tokens resolve for keyboard and gamepad); spoiler scan
over all copy; synergy table (pairs reference implemented skills, no duplicates, lookup symmetric); every implemented
skill has a guide and a demo; `TutorialProgress` (seen, reset, settings) on the memory store; `ModalGate`
ref-counting.

PlayMode: the director fires `hero_rules` at the first threshold pause and pauses the sim, continue resumes it, a seen
lesson never fires again, tips-off fires nothing, lesson-pauses-off turns Focus into Tip; the picker shows synergy
lines for owned skills and keeps the `Skill_<id>` / `Continue` click contract; the demo stage isolation test; the pause
menu pauses and suspends gameplay input; the HUD shows icons/keycaps and the level/XP; all existing tests stay green.

Visual QA: captures of the HUD in a fight, each Focus lesson, toasts, the picker with each demo mid-play, the Field
Guide tabs, the pause menu, at 1600×900; findings logged in `docs/qa/QA_LOG.md`.

## 8. Risks

- **Demo puppets vs the real Animator setup**: the visual may expect its parent agent. Mitigation: build one puppet
  early (first task of the demo phase) and capture it before writing six scripts.
- **TMP dynamic font atlases** churn on import; never commit those two font assets unless intended.
- **Copy volume**: all text is draft; keep it in tables so the owner can rewrite in one place.
- **Unity editor on the main checkout**: all work happens in this worktree with a separate headless Unity.
