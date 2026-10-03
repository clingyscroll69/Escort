# Tutorial, Skill Demos & Synergies, HUD Pass — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> This plan is executed inline (as the M1 slice plan was): the code lives in the files each task names rather than
> being duplicated here. Every task lists its files, the interfaces other tasks rely on (exact names and types), the
> tests to write first (with their code for the contracts), and its acceptance checks.

**Goal:** A learn-as-you-go tutorial (tips, three freeze-frame lessons, Field Guide), a skill picker that explains
every skill with a live skippable demo and synergies with your kit, and a clearer HUD with icons, key glyphs,
level/XP and a pause menu — without touching the opening.

**Architecture:** A read-only `TutorialDirector` watches the run (event bus, flow, polled state) and shows lessons
through a `TipView`; a `ModalGate` pauses the sim and suspends gameplay input for focus lessons and menus. Skill
knowledge is data (`SkillGuides`, `SkillSynergies`); demos are scripted "puppet" scenes built from the real
character visuals on an isolated stage rendered to a texture. UI stays code-built uGUI through `UIKit`.

**Tech Stack:** Unity 6000.3.25f1, URP 17.3, uGUI + TextMeshPro, Input System 1.20 (code-defined actions), Unity
Test Framework 1.6, Python 3 + Pillow for procedural sprites.

**Spec:** `docs/superpowers/specs/2026-10-03-tutorial-and-hud-design.md` (read §2 spoiler policy, §4 lessons, §5
skill picks, §6 HUD).

## Global Constraints

- The opening (`OpeningView`, `ClassRoll`, `OpeningStreet`) is not modified.
- No numeric Rapport, Stage, Moments, penalties or "trust" anywhere in player-facing text; the copy test enforces it.
- Skill text describes uses, never recommendations (GDD §4.7); synergies describe interactions.
- Hero Insight is **off** by default; the tutorial teaches Tab.
- AutoPlay runs (bots, balance harness, QA scenes) never create a `TutorialDirector` or `PauseMenu`.
- The simulation's behaviour is unchanged: the tutorial only reads state, pauses the sim (focus lessons, menus) and
  draws UI. Demos never touch `SimLoop`, `AgentRegistry` or any `EventBus`.
- Existing public UI contracts stay: `SkillPicker.Show/Pick/ToggleEquip/Continue/PicksLeft/Done`, rows named
  `Skill_<id>` with read-then-learn clicks, a button named `Continue`; `HudView.Create/MeetHero/TickSwap/SidekickLabel`
  with the label format `YOU  <NAME>'s SIDEKICK`; `UIRoot` layers (`Hud` hidden during the opening).
- All art is procedural (Pillow) — no AI imagery, no downloads. Tool scripts write relative to the repo they live in.
- Never commit TMP dynamic font atlas churn (`LiberationSans SDF - Fallback.asset`, `ShareTechMono SDF.asset`) — revert
  it before each commit.
- Copy is draft text for the owner to rewrite; it lives in tables (`Lessons`, `SkillGuides`, `SkillSynergies`).
- Unity is open on the main checkout: work only in this worktree, verify with headless Unity (`tools/unity-tests.sh`).

## File Structure

| Path (under `Escort/Assets/_Game/` unless noted) | Responsibility |
|---|---|
| `tools/unity-tests.sh` (repo root) | Headless Unity runner for this checkout: EditMode/PlayMode tests (+ filter), summary from the XML, or `-executeMethod`. |
| `tools/make_icons.py`, `tools/make_ui_textures.py` (repo root) | New icons and UI sprites; output paths relative to the repo. |
| `Editor/Import/ArtImportPostprocessor.cs` | 9-slice borders for the new UI sprites; grid texture wraps. |
| `Tutorial/TutorialProgress.cs` | `ITutorialStore`, `PlayerPrefsStore`, `MemoryStore`, `TutorialProgress` (seen ids, settings). |
| `Tutorial/KeyGlyphs.cs` | `{token}` → keyboard/gamepad labels from `GameInput` bindings; inline keycap rich text. |
| `Tutorial/Lessons.cs` | `Lesson`, enums, the lesson table (spec §4). |
| `Tutorial/ModalGate.cs` | Ref-counted sim pause + gameplay-input suspension. |
| `Tutorial/TutorialDirector.cs` | Queue, throttle, expiry, show/complete, persistence, focus via `ModalGate`. |
| `Tutorial/LessonTriggers.cs` | Every trigger in spec §4: event subscriptions + per-frame polling → `director.Offer`. |
| `Skills/SkillGuides.cs` | Taglines, how-to, stat lines per rank from `SkillDefinition`, Callum's view. |
| `Skills/SkillSynergies.cs` | Pair table + lookups against a kit. |
| `Tutorial/Demo/DemoStage.cs` | Isolated stage at y = −400: floor, camera → RenderTexture, puppet/prop factory, decals. |
| `Tutorial/Demo/Puppet.cs` | A character visual without an Agent: move/face/locomotion/actions. |
| `Tutorial/Demo/DemoScript.cs` | Seekable timeline: beats, tweens, captions; `DemoContext`; `DemoOverlay` (UI over the viewport). |
| `Tutorial/Demo/SkillDemos.cs` | The six skill demos (rank-aware). |
| `UI/UIKit.cs` | + family colours, `Icon()`, `Keycap()`, `Chip()`, `Brackets()`, new sprites. |
| `UI/HudView.cs` | Reworked cards, skill bar, verbs strip, coach anchors. |
| `UI/SystemWindow.cs` | Header strip + corner brackets (behaviour unchanged). |
| `UI/TipView.cs` | Toasts, focus overlay with spotlight, coach-mark pulses, world markers. |
| `UI/DemoViewport.cs` | RawImage + overlay + caption + skip/replay; plays a skill demo. |
| `UI/SkillPicker.cs` | Reworked two-column picker with demo, guide, synergies, loadout. |
| `UI/PauseMenu.cs` | Pause, Settings, Controls pages. |
| `UI/FieldGuide.cs` | Tips / Skills / Controls tabs. |
| `UI/EndScreen.cs` | + optional inline hint line. |
| `UI/ThreatIndicators.cs` | + `AnyThreatShown`, `HeroMarkerShown`. |
| `Flow/XpTracker.cs` | + `Awarded(int amount, string reason)` event. |
| `Flow/CampfireDirector.cs` | Picker opens after the fireside lines (skippable). |
| `Flow/GameFlow.cs` | Creates the director and pause menu for the player; end-screen hint. |
| `Core/GameAssets.cs`, `Editor/Builders/GameAssetsBuilder.cs` | + `demoProps` registry (kit barrels, crate, stone). |
| `Tests/EditMode/TutorialDataTests.cs` | Progress, glyphs, lessons, copy scan, guides, synergies, gate. |
| `Tests/PlayMode/TutorialTests.cs` | Director behaviour in a built chapter. |
| `Tests/PlayMode/SkillDemoTests.cs` | Stage isolation, every demo runs, skip/replay. |
| `Tests/PlayMode/UiTests.cs` | HUD, picker contract + synergies, pause menu, Field Guide, camp deferral. |
| `Tests/PlayMode/UiQaCaptures.cs` | `[Explicit]` screenshot scenarios → `docs/qa/shots/ui_*.png`. |

## QA gate (every task)

1. Headless compile: `tools/unity-tests.sh EditMode` shows 0 compile errors (the runner fails loudly on CS errors).
2. New tests written first and seen failing (compile failure counts as failing), then passing.
3. Full EditMode suite green each task; full PlayMode suite green at Tasks 7, 10, 13, 14, 15 and 16.
4. UI tasks: a capture read back and inspected (overlap, legibility at 1600×900, contrast, clipping).
5. Revert font-atlas churn; commit with the task name.

---

### Task 1: Tooling and art (headless runner, icons, UI sprites, import rules)

**Files:**
- Create: `tools/unity-tests.sh`
- Modify: `tools/make_icons.py` (repo-relative `OUT`; new icons), `tools/make_ui_textures.py` (repo-relative `OUT`; new sprites)
- Modify: `Escort/Assets/_Game/Editor/Import/ArtImportPostprocessor.cs` (borders table, grid wrap mode)
- Create (generated): `Resources/Icons/{skill_*, verb_*, wound_*, fam_*, blind, alert, question, check}.png`, `Resources/UI/{keycap, slot, card, corner, ring, spot, grid, hatch, diamond, line}.png`
- Test: `Escort/Assets/_Game/Tests/EditMode/TutorialDataTests.cs` (first test only)

**Interfaces — Produces:** sprite ids loadable as `Resources.Load<Sprite>("Icons/<id>")` and `("UI/<id>")`:
skills `skill_pocket_sand, skill_loosen_bolt, skill_quiet_feet, skill_crossbow, skill_bandage, skill_cover_story`;
verbs `verb_knife, verb_dodge, verb_ping, verb_crouch, verb_interact`; wounds `wound_ankle, wound_ribs, wound_arm,
wound_fever, wound_concussion`; families `fam_fixer, fam_handler, fam_provisioner, fam_scholar, fam_combat`; marks
`blind, alert, question, check`; UI `keycap (border 14), slot (22), card (24), corner, ring, spot, grid (Repeat),
hatch (Repeat), diamond, line (border 0)`.

- [ ] **Step 1: Runner.** `tools/unity-tests.sh <EditMode|PlayMode|method> [filter-or-method]` resolves the repo root
  from its own path, runs `Unity -batchmode -projectPath <root>/Escort -runTests -testPlatform <p> -testResults
  <tmp>.xml [-testFilter <f>] -logFile <tmp>.log` (or `-executeMethod <m> -quit`), then prints `passed/failed/total`,
  each failed test's name and message, and any `error CS` lines; exits non-zero on failure.
- [ ] **Step 2: Failing test.**

```csharp
[Test]
public void Tutorial_Sprites_Are_Imported()
{
    foreach (var id in new[] { "skill_pocket_sand", "skill_loosen_bolt", "skill_quiet_feet", "skill_crossbow",
                 "skill_bandage", "skill_cover_story", "verb_knife", "verb_dodge", "verb_ping", "verb_crouch",
                 "verb_interact", "wound_ankle", "wound_ribs", "wound_arm", "wound_fever", "wound_concussion",
                 "fam_fixer", "fam_handler", "fam_provisioner", "fam_scholar", "fam_combat", "blind", "alert",
                 "question", "check" })
        Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + id), "Icons/" + id);
    foreach (var (id, border) in new[] { ("keycap", 14f), ("slot", 22f), ("card", 24f) })
    {
        var s = Resources.Load<Sprite>("UI/" + id);
        Assert.IsNotNull(s, "UI/" + id);
        Assert.AreEqual(border, s.border.x, 0.01f, id + " is 9-sliced");
    }
    foreach (var id in new[] { "corner", "ring", "spot", "grid", "hatch", "diamond", "line" })
        Assert.IsNotNull(Resources.Load<Sprite>("UI/" + id), "UI/" + id);
}
```

- [ ] **Step 3: Run** `tools/unity-tests.sh EditMode Tutorial_Sprites` → FAIL (missing sprites).
- [ ] **Step 4: Generate.** Icons follow `make_icons.py`'s style (white glyph, dark dilated outline, 256²). UI sprites
  follow `make_ui_textures.py` (white, tinted at runtime; supersampled). `spot` is a 256² radial vignette with a
  soft transparent centre (spotlight edge); `grid` a 256² tileable line grid; `card` a rounded panel with a 2 px inner
  highlight; `keycap` a rounded key with a darker bottom lip; `slot` a bevelled square frame.
- [ ] **Step 5: Import rules.** Borders by name: `panel*`=20, `bar`=10, `keycap`=14, `slot`=22, `card`=24, else 0;
  `grid`/`hatch` → `TextureWrapMode.Repeat`, `SpriteMeshType.FullRect`.
- [ ] **Step 6: Run** → PASS. Inspect a contact sheet of the new icons (PIL montage to the scratchpad) for legibility at
  48 px.
- [ ] **Step 7: Commit** "Tutorial art: icons, UI sprites, headless test runner".

### Task 2: TutorialProgress (seen lessons and settings)

**Files:** Create `Tutorial/TutorialProgress.cs`; Test `Tests/EditMode/TutorialDataTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial {
  public interface ITutorialStore { string GetString(string key, string def); void SetString(string key, string value);
                                    int GetInt(string key, int def); void SetInt(string key, int value); void Save(); }
  public sealed class PlayerPrefsStore : ITutorialStore { /* PlayerPrefs */ }
  public sealed class MemoryStore : ITutorialStore { /* Dictionary */ }
  public static class TutorialProgress {
    public static ITutorialStore Store { get; set; }          // default PlayerPrefsStore; reset on SubsystemRegistration
    public static bool IsSeen(string id); public static void MarkSeen(string id);
    public static IReadOnlyCollection<string> Seen { get; }  public static void ResetSeen();
    public static bool TipsEnabled { get; set; }             // "hs.tut.tips", default true
    public static bool LessonPauses { get; set; }            // "hs.tut.pauses", default true
    public static bool InsightDefault { get; set; }          // "hs.insight", default false
    public static event System.Action Changed;
  }
}
```

- [ ] **Step 1: Failing tests** (`[SetUp] TutorialProgress.Store = new MemoryStore();`):

```csharp
[Test] public void Progress_Remembers_Seen_Lessons_Until_Reset()
{
    Assert.IsFalse(TutorialProgress.IsSeen("cone"));
    TutorialProgress.MarkSeen("cone"); TutorialProgress.MarkSeen("cone");
    Assert.IsTrue(TutorialProgress.IsSeen("cone"));
    CollectionAssert.AreEquivalent(new[] { "cone" }, TutorialProgress.Seen);
    TutorialProgress.ResetSeen();
    Assert.IsFalse(TutorialProgress.IsSeen("cone"));
}
[Test] public void Progress_Defaults_Tips_On_Pauses_On_Insight_Off()
{
    Assert.IsTrue(TutorialProgress.TipsEnabled);
    Assert.IsTrue(TutorialProgress.LessonPauses);
    Assert.IsFalse(TutorialProgress.InsightDefault);
    TutorialProgress.TipsEnabled = false;
    Assert.IsFalse(TutorialProgress.TipsEnabled);
}
[Test] public void Progress_Survives_A_New_Store_View()
{
    var store = new MemoryStore();
    TutorialProgress.Store = store; TutorialProgress.MarkSeen("move");
    TutorialProgress.Store = new MemoryStore(); Assert.IsFalse(TutorialProgress.IsSeen("move"));
    TutorialProgress.Store = store; Assert.IsTrue(TutorialProgress.IsSeen("move"), "seen ids live in the store, not a cache");
}
```

- [ ] **Step 2: Run** → FAIL (types missing). **Step 3: Implement** (seen ids stored comma-joined under
  `hs.tut.seen`; parse on read; `Changed` fires on every write). **Step 4: Run** → PASS. **Step 5: Commit.**

### Task 3: KeyGlyphs (keyboard and gamepad labels)

**Files:** Create `Tutorial/KeyGlyphs.cs`; Test `TutorialDataTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial {
  public enum GlyphDevice { Keyboard, Gamepad }
  public static class KeyGlyphs {
    public static GlyphDevice Current { get; }                         // GameInput.Instance.UsingGamepad
    public static string Label(string token, GlyphDevice d);         // null for unknown tokens
    public static string Format(string text, GlyphDevice d);         // "{ping}" → Chip("Q"); unknown {x} left as-is
    public static string Format(string text) => Format(text, Current);
    public static string Chip(string label);                         // inline keycap rich text
    public static string SlotToken(int slot);                        // 0 → "skill1"
  }
}
```

Tokens: `move, walk, aim, attack, dodge, ping, interact, crouch, insight, pause, skills, skill1..skill4, confirm, cancel`.
Keyboard labels come from the first `<Keyboard>`/`<Mouse>` binding of the action (`Q`, `LMB`, `Space`, `Shift`,
`Tab`, `Esc`, `1`); gamepad from the first `<Gamepad>` binding (`A B X Y LB RB LT RT L3 Start Select LS RS`);
composites: `move` → `WASD` / `LS`; `aim` → `Mouse` / `RS`; `skills` → `1–4` / `RT RB LT LB`; `walk` on gamepad →
`LS (lightly)`.

- [ ] **Step 1: Failing tests:**

```csharp
[TestCase("ping", "Q", "Y")] [TestCase("attack", "LMB", "X")] [TestCase("dodge", "Space", "B")]
[TestCase("interact", "E", "A")] [TestCase("crouch", "C", "L3")] [TestCase("insight", "Tab", "Select")]
[TestCase("pause", "Esc", "Start")] [TestCase("skill1", "1", "RT")] [TestCase("skill2", "2", "RB")]
[TestCase("skill4", "4", "LB")] [TestCase("move", "WASD", "LS")] [TestCase("walk", "Shift", "LS (lightly)")]
public void Glyphs_Follow_The_Real_Bindings(string token, string kb, string pad)
{
    Assert.AreEqual(kb, KeyGlyphs.Label(token, GlyphDevice.Keyboard));
    Assert.AreEqual(pad, KeyGlyphs.Label(token, GlyphDevice.Gamepad));
}
[Test] public void Format_Replaces_Known_Tokens_And_Leaves_Others()
{
    var s = KeyGlyphs.Format("Press {ping} near {nothing}.", GlyphDevice.Keyboard);
    StringAssert.Contains(KeyGlyphs.Chip("Q"), s);
    StringAssert.Contains("{nothing}", s);
    Assert.IsNull(KeyGlyphs.Label("nothing", GlyphDevice.Keyboard));
}
```

- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS). **Step 5: Commit.**

### Task 4: Lessons table and the copy rules

**Files:** Create `Tutorial/Lessons.cs`; Test `TutorialDataTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial {
  public enum LessonKind { Tip, Focus, Inline, Notice }
  public enum LessonCategory { Controls, TheHero, TheRoad, Camp }
  public enum CoachTarget { None, RuleChip, HonorBar, SkillBar, DodgePips, WoundChips, XpBar }
  public sealed class Lesson {
    public string Id, Title, Body; public LessonKind Kind; public LessonCategory Category;
    public CoachTarget Coach; public float Duration = 9f; public float Expiry = 6f; public int Priority;
  }
  public static class Lessons {
    public static IReadOnlyList<Lesson> All { get; }
    public static Lesson Get(string id);                    // null when unknown
    public static readonly string[] Forbidden;              // spoiler words (spec §2)
    public static IEnumerable<string> CopyViolations(string text); // forbidden words found (case-insensitive, whole words)
  }
}
```

Content: every row of spec §4 (ids `welcome, move, hero_rules, insight, attack, tricks, dodge, cone, salute, unready,
surrender, caught, honor_low, spoiled, fallback, wounds, ambush, stone, trap, prop, ping, crouch, cache, channel,
threats, hero_offscreen, out_of_reach, xp, pause, levelup, camp, loadout, duel, restore`). Body variables beyond key
tokens use `{coverHint}` and `{slots}`, filled by the director.

- [ ] **Step 1: Failing tests:**

```csharp
[Test] public void Lessons_Are_Unique_Complete_And_Format_On_Both_Devices()
{
    var ids = new HashSet<string>();
    foreach (var l in Lessons.All)
    {
        Assert.IsTrue(ids.Add(l.Id), "duplicate " + l.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(l.Body), l.Id);
        if (l.Kind != LessonKind.Notice && l.Kind != LessonKind.Inline) Assert.IsFalse(string.IsNullOrWhiteSpace(l.Title), l.Id);
        foreach (var d in new[] { GlyphDevice.Keyboard, GlyphDevice.Gamepad })
        {
            var s = KeyGlyphs.Format(l.Body.Replace("{coverHint}", "").Replace("{slots}", "4"), d);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(s, @"\{[a-z0-9]+\}"), $"{l.Id}: unresolved token in '{s}'");
        }
    }
    Assert.AreEqual(3, Lessons.All.Count(l => l.Kind == LessonKind.Focus), "three freeze-frame lessons: hero_rules, cone, duel");
}
[Test] public void Copy_Never_Mentions_The_Hidden_Stat()
{
    foreach (var l in Lessons.All)
        CollectionAssert.IsEmpty(Lessons.CopyViolations(l.Title + " " + l.Body), l.Id);
    CollectionAssert.IsNotEmpty(Lessons.CopyViolations("Your Rapport is at Stage 1"), "the scan works");
}
```

- [ ] **Step 2–4:** FAIL → write the table from spec §4 → PASS. **Step 5: Commit.**

### Task 5: SkillGuides and SkillSynergies

**Files:** Create `Skills/SkillGuides.cs`, `Skills/SkillSynergies.cs`; Test `TutorialDataTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Skills {
  public sealed class SkillGuide { public string Id, Tagline, HowTo, CallumView;
      public (string label, System.Func<SkillDefinition, int, string> value)[] Stats; }
  public static class SkillGuides {
    public static SkillGuide Get(string id);                         // null when none
    public static IEnumerable<SkillGuide> All { get; }
    public static List<(string label, string rank1, string rank2)> StatLines(SkillDefinition def);
    public static string IconId(string skillId) => "skill_" + skillId;
    public static string FamilyIcon(SkillFamily f);                  // "fam_fixer"...
  }
  public static class SkillSynergies {
    public readonly struct Pair { public readonly string A, B, Note; }
    public static IReadOnlyList<Pair> All { get; }
    public static string Note(string a, string b);                   // order-independent; null when none
    public static List<(string other, string note)> With(string id, IEnumerable<string> kit);    // owned partners
    public static List<(string other, string note)> Without(string id, IEnumerable<string> kit); // not owned
  }
}
```

Stat lines (from the definition, never hard-coded numbers): Pocket Sand `Blind A s`, `Range B m`, `Cooldown`; Loosen
Bolt `Armed at once A`, `Arming B s`; Quiet Feet `Heard within A m`, `His cone ×B`; Crossbow `Damage A`, `Range B
m`, `Reload`, rank 2 `Pierces`; Bandage `Heals A over 6 s`, `Channel B s`, `Cooldown`; Cover Story `Honor +A`,
`Caught window B s`, `Cooldown`. Copy from spec §5.2–5.3.

- [ ] **Step 1: Failing tests:**

```csharp
[Test] public void Every_Implemented_Skill_Has_A_Guide_With_Live_Numbers()
{
    foreach (var def in SkillCatalog.Load().Implemented)
    {
        var g = SkillGuides.Get(def.id);
        Assert.IsNotNull(g, def.id);
        Assert.IsNotEmpty(SkillGuides.StatLines(def), def.id);
        Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + SkillGuides.IconId(def.id)), def.id + " icon");
        CollectionAssert.IsEmpty(Lessons.CopyViolations(g.Tagline + " " + g.HowTo + " " + g.CallumView), def.id);
    }
    var sand = SkillCatalog.Load().Get("pocket_sand");
    var blind = SkillGuides.StatLines(sand).First(l => l.label.StartsWith("Blind"));
    Assert.AreEqual($"{sand.A(1):0.#} s", blind.rank1);
    Assert.AreEqual($"{sand.A(2):0.#} s", blind.rank2);
}
[Test] public void Synergies_Are_Symmetric_And_Only_Between_Implemented_Skills()
{
    var impl = new HashSet<string>(SkillCatalog.Load().Implemented.Select(d => d.id));
    var seen = new HashSet<string>();
    foreach (var p in SkillSynergies.All)
    {
        Assert.IsTrue(impl.Contains(p.A) && impl.Contains(p.B), $"{p.A}+{p.B}");
        Assert.AreNotEqual(p.A, p.B);
        Assert.IsTrue(seen.Add(string.CompareOrdinal(p.A, p.B) < 0 ? p.A + "|" + p.B : p.B + "|" + p.A), "duplicate pair");
        Assert.AreEqual(SkillSynergies.Note(p.A, p.B), SkillSynergies.Note(p.B, p.A));
        CollectionAssert.IsEmpty(Lessons.CopyViolations(p.Note));
    }
    var with = SkillSynergies.With("quiet_feet", new[] { "pocket_sand", "bandage" });
    CollectionAssert.AreEqual(new[] { "pocket_sand" }, with.Select(w => w.other));
    Assert.IsNull(SkillSynergies.Note("bandage", "quiet_feet"), "pairs that don't interact show nothing");
}
```

- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5: Commit.**

### Task 6: ModalGate

**Files:** Create `Tutorial/ModalGate.cs`; Test `TutorialDataTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial {
  public static class ModalGate {
    public static object Push(string reason, bool pauseSim = true, bool blockGameplay = true);
    public static void Pop(object token);          // unknown/duplicate tokens ignored
    public static int Count { get; }
    public static bool Any => Count > 0;
  }
}
```

First push that pauses remembers `SimLoop.Instance.Paused` and sets it true; when the last pausing token pops the
remembered value is restored. Same for `GameInput.Instance.Gameplay` (disable / re-enable only if it was enabled).
Statics reset on SubsystemRegistration.

- [ ] **Step 1: Failing test:**

```csharp
[Test] public void Gate_Pauses_And_Restores_What_Was_There()
{
    var loop = SimLoop.Ensure();
    try
    {
        loop.Paused = false;
        var a = ModalGate.Push("focus"); var b = ModalGate.Push("pause");
        Assert.IsTrue(loop.Paused); Assert.IsFalse(GameInput.Instance.Gameplay.enabled);
        ModalGate.Pop(a); Assert.IsTrue(loop.Paused, "still one modal open");
        ModalGate.Pop(b); ModalGate.Pop(b);
        Assert.IsFalse(loop.Paused); Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
        loop.Paused = true; var c = ModalGate.Push("x"); ModalGate.Pop(c);
        Assert.IsTrue(loop.Paused, "a sim that was already paused stays paused");
    }
    finally { Object.DestroyImmediate(loop.gameObject); }
}
```

- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5: Commit.**

### Task 7: HUD rework (cards, skill bar, verbs strip, coach anchors)

**Files:** Modify `UI/UIKit.cs`, `UI/HudView.cs`, `Flow/XpTracker.cs`, `UI/ThreatIndicators.cs`; Test
`Tests/PlayMode/UiTests.cs`; capture scenario in `Tests/PlayMode/UiQaCaptures.cs`.

**Interfaces — Consumes:** sprites (Task 1), `KeyGlyphs` (Task 3), `SkillGuides.IconId/FamilyIcon` (Task 5),
`CoachTarget` (Task 4). **Produces:**

```csharp
// UIKit
public static Color Family(SkillFamily f);       // Fixer ochre, Handler violet, Provisioner green, Scholar blue, Combat red
public static Sprite Icon(string id);            // Resources/Icons, cached
public static Sprite UISprite(string id);        // Resources/UI, cached
public static RectTransform Keycap(Transform parent, string label, float height); // keycap sprite + mono label, auto width
public static TMPro.TextMeshProUGUI Chip(Transform parent, string name, string text, Color color); // pill label
public static void Brackets(RectTransform panel, Color color, float size = 18f);  // four corner brackets
// HudView
public RectTransform CoachAnchor(HS.Tutorial.CoachTarget t);
public string SlotIcon(int slot);   // sprite name in the slot or null
public string LevelText { get; }    // "LV 2"
public float XpFill { get; }        // 0..1 toward the next level
public bool LevelBanked { get; }    // XP for a level the camp hasn't granted yet
// XpTracker
public event System.Action<int, string> Awarded;   // (amount, "clear" | "assist" | "explore")
// ThreatIndicators
public bool AnyThreatShown { get; }  public bool HeroMarkerShown { get; }
```

Layout (1920×1080 reference): hero card top-left 520×176 (`card` panel, brackets, name, HP bar with quarter ticks,
Honor bar + `honor` icon + tick at the low line, rule chip with the active rule icon and Insight label, wound chips
with `wound_*` icons); sidekick card bottom-left 470×150 (label format unchanged, `LV n` badge + XP bar + "LEVEL UP
AT CAMP" when banked, HP bar, dodge pips with recharge fill, status chips SNEAKING/CROUCHED, OUT OF REACH); skill
bar bottom-centre: 4 × 96 px `slot` frames with icon, keycap (follows device), radial cooldown + seconds, ready
flash, rank pips (`diamond`), family underline; passive badges right of the bar; verbs strip left (knife, dodge,
ping, crouch keycaps + icons); prompt = keycap + text.

- [ ] **Step 1: Failing tests (PlayMode, `UiTests`):**

```csharp
[UnityTest] public IEnumerator Hud_Shows_Icons_Keys_And_Level()
{
    var (hud, sk) = BuildHudWithPair();             // RunContext + Callum + Sidekick + XpTracker registered
    sk.GetComponent<SidekickSkills>().Learn("pocket_sand");
    yield return null;
    Assert.AreEqual("skill_pocket_sand", hud.SlotIcon(0));
    Assert.IsNull(hud.SlotIcon(1));
    Assert.AreEqual("LV 1", hud.LevelText);
    RunContext.Current.Get<XpTracker>().Restore(XpTracker.Thresholds[0]);
    yield return null;
    Assert.IsTrue(hud.LevelBanked, "XP for level 2 is banked until the camp");
    foreach (CoachTarget t in System.Enum.GetValues(typeof(CoachTarget)))
        if (t != CoachTarget.None) Assert.IsNotNull(hud.CoachAnchor(t), t.ToString());
}
```

- [ ] **Step 2: Run** `tools/unity-tests.sh PlayMode UiTests` → FAIL.
- [ ] **Step 3: Implement** (keep `MeetHero/TickSwap/SidekickLabel` and the label format).
- [ ] **Step 4: Run** → PASS; then the full PlayMode suite (the opening and HERO→CALLUM tests must stay green).
- [ ] **Step 5: Capture** `UiQaCaptures.Hud_In_A_Fight` (a built chapter at room 1 with a bot, captures
  `ui_hud_fight.png`), read it back, fix overlap/legibility. **Step 6: Commit.**

### Task 8: System window, prompt, channel and boss bar polish

**Files:** Modify `UI/SystemWindow.cs`, `UI/HudView.cs` (boss bar, channel), `UI/BarkView.cs` (top band clears the
taller hero card); Test: existing suites; capture `ui_system_window.png`, `ui_boss_bar.png`.

- [ ] **Step 1:** System window: header strip (title + small "SYSTEM" tag), corner brackets, same typing, same queue,
  same `OccupiedFromTop/OccupiedWidth` contract. Error state keeps red.
- [ ] **Step 2:** Boss bar: name plate with brackets, notches every 10%; channel bar shows `{dodge}`-style keycap of
  the move key that cancels ("move to cancel").
- [ ] **Step 3:** Run EditMode + PlayMode suites → PASS. **Step 4:** captures inspected. **Step 5: Commit.**

### Task 9: TipView (toasts, focus overlay, coach marks, world markers)

**Files:** Create `UI/TipView.cs`; Test `UiTests.cs`.

**Interfaces — Consumes:** `Lesson`, `CoachTarget`, `HudView.CoachAnchor`. **Produces:**

```csharp
public sealed class TipView : MonoBehaviour {
  public static TipView Create(UIRoot root, HudView hud);
  public void ShowToast(Lesson l, string body, float duration);   // body already formatted
  public void MarkToastDone();                                      // ✓, then fades after 1.2 s
  public void HideToast();
  public bool ToastVisible { get; }  public string ToastId { get; }
  public void ShowFocus(Lesson l, string body, System.Func<Rect?> spotlight, System.Action onContinue); // rect in Overlay-layer local coords
  public void HideFocus();
  public bool FocusVisible { get; }
  public void Coach(CoachTarget t, float seconds);
  public void Marker(System.Func<Vector3?> world, float seconds);
}
```

Toast: right edge, below the System window (`SystemWindow.OccupiedFromTop`), 440 px wide `card`, category icon,
title, body, progress line; lives in `UIRoot.Windows` (hidden during the opening). Focus: in `UIRoot.Overlay`: four
dim panels around the spotlight rect + `spot` vignette over it, a card (title, body, "{confirm} continue" footer),
blocks raycasts; confirm (UI `Confirm`, mouse click on the card's button) calls `onContinue`. Coach: a pulsing
`ring`/outline over the anchor. Marker: `ring` + `tail` arrow at the world point (clamped to screen edges).

- [ ] **Step 1: Failing tests:**

```csharp
[UnityTest] public IEnumerator Toast_Shows_Then_Times_Out()
{
    var view = TipView.Create(UIRoot.Ensure(), null);
    view.ShowToast(Lessons.Get("move"), "Move.", 0.3f);
    yield return null;
    Assert.IsTrue(view.ToastVisible); Assert.AreEqual("move", view.ToastId);
    yield return new WaitForSecondsRealtime(1.2f);
    Assert.IsFalse(view.ToastVisible);
}
[UnityTest] public IEnumerator Focus_Blocks_Clicks_And_Continues()
{
    var view = TipView.Create(UIRoot.Ensure(), null);
    bool cont = false;
    view.ShowFocus(Lessons.Get("cone"), "What he sees.", () => new Rect(-100, -100, 200, 200), () => cont = true);
    yield return null;
    Assert.IsTrue(view.FocusVisible);
    ClickName("FocusContinue");                     // helper: EventSystem raycast + click (as in OpeningTests)
    yield return null;
    Assert.IsTrue(cont); Assert.IsFalse(view.FocusVisible);
}
```

- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5:** capture `ui_toast.png`, `ui_focus.png` (static scene),
  inspect. **Step 6: Commit.**

### Task 10: TutorialDirector and LessonTriggers, wired into the game flow

**Files:** Create `Tutorial/TutorialDirector.cs`, `Tutorial/LessonTriggers.cs`; Modify `Flow/GameFlow.cs`; Test
`Tests/PlayMode/TutorialTests.cs`.

**Interfaces — Consumes:** Tasks 2–6, 9; `GameFlow.StateChanged/Current/Chapter/Duel`, `RunContext.Events`,
`CallumModule.Caught/SpoiledDuel/DuelBegan`, `HeroAgent.ActiveRuleId`, `ThreatIndicators` flags, `XpTracker.Awarded`.
**Produces:**

```csharp
public sealed class TutorialDirector : MonoBehaviour {
  public static TutorialDirector Create(HS.Flow.GameFlow flow);   // TipView + LessonTriggers
  public bool Offer(string id, System.Func<bool> doneWhen = null, System.Func<Vector3?> marker = null,
                    System.Func<Rect?> spotlight = null);          // false if seen, disabled, unknown or already queued
  public void Complete(string id);
  public void ContinueFocus();
  public string Showing { get; }         // id on screen (toast or focus) or null
  public bool FocusOpen { get; }
  public TipView View { get; }
  public event System.Action<Lesson> Shown;
}
```

Rules (spec §4): tips off → `Offer` returns false; seen → false; `LessonPauses` off → Focus shown as Tip; one toast
at a time, 3 s gap, queued tips expire after `Lesson.Expiry`; Focus only in Chapter/Duel with `!ModalGate.Any`,
pre-empts the toast (re-queued); 2 s quiet after a Focus except its chained follow-up (`hero_rules → insight`,
`cone → salute`). Marked seen when shown. `{coverHint}` and `{slots}` filled from the sidekick's loadout.
`GameFlow.Start`: when `!AutoPlay` → `TutorialDirector.Create(this)` after the chapter is built; the `welcome` notice is
a `SystemNotice` raised on entering the chapter the first time.

- [ ] **Step 1: Failing tests** (fixture: `TutorialProgress.Store = new MemoryStore()`, a `GameFlow` with
  `AutoPlay = false`, its opening skipped via `RunState.Resume = "chapter"` with a stored `RunState.ChapterStart`
  snapshot, scripted sidekick commands):

```csharp
[UnityTest] public IEnumerator First_Threshold_Pause_Freezes_On_His_Rules()
{
    var flow = StartChapterAsPlayer();
    yield return WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f);
    var dir = Object.FindAnyObjectByType<TutorialDirector>();
    yield return WaitUntil(() => dir.Showing == "hero_rules", 2f);
    Assert.IsTrue(dir.FocusOpen); Assert.IsTrue(SimLoop.Instance.Paused);
    Assert.IsFalse(GameInput.Instance.Gameplay.enabled, "the continue key can't also dodge");
    dir.ContinueFocus();
    yield return null;
    Assert.IsFalse(SimLoop.Instance.Paused); Assert.IsTrue(TutorialProgress.IsSeen("hero_rules"));
    yield return WaitUntil(() => dir.Showing == "insight", 4f);   // its chained tip
}
[UnityTest] public IEnumerator Seen_Lessons_Never_Repeat_And_Tips_Off_Is_Silent()
{
    TutorialProgress.MarkSeen("hero_rules");
    TutorialProgress.TipsEnabled = false;
    var flow = StartChapterAsPlayer();
    yield return WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f);
    yield return new WaitForSecondsRealtime(1f);
    var dir = Object.FindAnyObjectByType<TutorialDirector>();
    Assert.IsNull(dir.Showing); Assert.IsFalse(SimLoop.Instance.Paused);
}
[UnityTest] public IEnumerator Lesson_Pauses_Off_Turns_Focus_Into_A_Tip()
{
    TutorialProgress.LessonPauses = false;
    var flow = StartChapterAsPlayer();
    yield return WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f);
    var dir = Object.FindAnyObjectByType<TutorialDirector>();
    yield return WaitUntil(() => dir.Showing == "hero_rules", 2f);
    Assert.IsFalse(dir.FocusOpen); Assert.IsFalse(SimLoop.Instance.Paused);
}
[UnityTest] public IEnumerator Move_Tip_Completes_When_You_Move()
{
    var flow = StartChapterAsPlayer();
    var dir = Object.FindAnyObjectByType<TutorialDirector>();
    yield return WaitUntil(() => dir.Showing == "move", 6f);
    Scripted(flow).Current.Move = Vector3.right;              // walk 4 m
    yield return WaitUntil(() => dir.Showing != "move", 4f);
}
[UnityTest] public IEnumerator Bots_Get_No_Tutorial()
{
    var go = new GameObject("GameFlow"); var flow = go.AddComponent<GameFlow>(); flow.AutoPlay = true; flow.Fast = true;
    yield return null;
    Assert.IsNull(Object.FindAnyObjectByType<TutorialDirector>());
}
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** the director, then `LessonTriggers` for every row of spec §4
  (events: `SkillUsed` dodge/skills, `Damage` knife, `DuelStarted`, `SaluteFinished`, `WoundChanged`, `Revealed`,
  `Explored`, `HazardSprung`; Callum: `Caught`, `SpoiledDuel`; polls: rule ids, surrender, Honor low, stones/props/
  traps/caches in range, chevrons, hero marker, support range, channel, XP; flow: Chapter/Camp/Duel; duel phase
  `Terms`). **Step 4: Run** → PASS, then the full PlayMode suite. **Step 5:** capture `ui_focus_hero_rules.png` and
  `ui_focus_cone.png` from a real chapter. **Step 6: Commit.**

### Task 11: Demo stage and puppets (isolated)

**Files:** Create `Tutorial/Demo/DemoStage.cs`, `Tutorial/Demo/Puppet.cs`; Modify `Core/GameAssets.cs`,
`Editor/Builders/GameAssetsBuilder.cs` (+ run it headless to update `Resources/GameAssets.asset`); Test
`Tests/PlayMode/SkillDemoTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial.Demo {
  public sealed class DemoStage : MonoBehaviour {
    public static readonly Vector3 Origin = new Vector3(0f, -400f, 0f);
    public static DemoStage Ensure();                                 // builds floor, camera, RenderTexture (960×540)
    public RenderTexture Texture { get; }  public Camera Camera { get; }
    public bool Rendering { get; set; }                               // camera on only while a viewport shows
    public Puppet Spawn(string kind, Vector2 pos, float yaw);         // "sidekick","callum","thug","crossbowman","turncoat","brute"
    public GameObject Prop(string id, Vector2 pos, float yaw, float scale = 1f); // GameAssets.demoProps id
    public Transform Decals { get; }
    public Vector3 World(Vector2 stagePos);                           // stage XZ → world
    public bool ToViewport(Vector3 world, out Vector2 viewport01);
    public void Clear();                                              // destroys cast, props, decals
  }
  public sealed class Puppet {
    public string Kind; public Transform Root; public HS.Presentation.AnimDriver Anim;
    public Vector2 Pos { get; set; } public float Yaw { get; set; }
    public void Locomotion(float speed, bool crouched); public void Play(string action, float duration = -1f);
    public void Flag(string flag, bool on); public Vector3 Head { get; }
  }
}
// GameAssets: public List<Entry> demoProps;  public GameObject DemoProp(string id);
```

Puppets: instantiate the gameplay prefab under an inactive holder, take its `Visual` child, destroy its
`VisualInterpolator`, re-parent to a puppet root on the stage, destroy the rest, then activate. Floor: disc mesh with
`grid` on an `HS/FX` material instance (`_Color` dark cyan); camera clear colour `#0A1422`, FOV 30°, 50° pitch.

- [ ] **Step 1: Failing test:**

```csharp
[UnityTest] public IEnumerator Stage_Puppets_Are_Outside_The_Simulation()
{
    var ctx = new GameObject("RunContext").AddComponent<RunContext>();
    int events = 0; HookAllEvents(ctx.Events, () => events++);          // every EventBus field
    int agents = AgentRegistry.All.Count, ticks = SimLoop.Ensure().RegisteredCount;
    var stage = DemoStage.Ensure();
    var sk = stage.Spawn("sidekick", new Vector2(-2f, 0f), 90f);
    var cal = stage.Spawn("callum", new Vector2(2f, 0f), -90f);
    stage.Prop("barrel_stack", new Vector2(0f, 3f), 0f);
    Assert.IsNotNull(sk.Anim); Assert.IsNull(sk.Root.GetComponentInChildren<Agent>(true));
    sk.Play("throw"); cal.Locomotion(4f, false);
    stage.Rendering = true;
    yield return null; yield return null;
    Assert.AreEqual(agents, AgentRegistry.All.Count); Assert.AreEqual(ticks, SimLoop.Instance.RegisteredCount);
    Assert.AreEqual(0, events);
    stage.Clear();
    yield return null;
    Assert.AreEqual(0, stage.transform.Find("Cast").childCount);
}
```

- [ ] **Step 2–4:** FAIL → implement (+ `demoProps`: `barrel_stack` = holder + three barrels prefab built by the
  builder, `crate`, `stone`, `bush`) → PASS. **Step 5:** capture `ui_demo_stage.png` (stage RT with two puppets) and
  inspect lighting, outline, scale. **Step 6: Commit.**

### Task 12: Demo scripts, the six skill demos, DemoViewport

**Files:** Create `Tutorial/Demo/DemoScript.cs`, `Tutorial/Demo/SkillDemos.cs`, `UI/DemoViewport.cs`; Test
`SkillDemoTests.cs`.

**Interfaces — Produces:**

```csharp
namespace HS.Tutorial.Demo {
  public sealed class DemoContext {
    public DemoStage Stage; public DemoOverlay Overlay; public int Rank;
    public readonly Dictionary<string, Puppet> Cast;
  }
  public sealed class DemoScript {
    public float Length { get; }
    public DemoScript At(float t, System.Action<DemoContext> beat);                 // fires once when time passes t
    public DemoScript Over(float from, float to, System.Action<DemoContext, float> tween); // u in 0..1 each frame
    public DemoScript Caption(float t, string text);
    public DemoScript End(float t);                                                  // sets Length
    public void Run(DemoContext c, float time);                                      // seekable, idempotent
  }
  public sealed class DemoOverlay : MonoBehaviour {      // UI over the viewport
    public void Caption(string text);
    public void Mark(Puppet who, string iconId, float seconds);          // "alert","question","blind","check", rule icons
    public void Bubble(Puppet who, string text, float seconds);
    public void Bar(string id, Puppet who, float fill, Color color);     // small HP/Honor/channel bars over a head
    public void KeyCallout(Puppet who, string token, float seconds);
    public void EndCard(string title, string[] lines);
    public void Clear();
  }
  public static class SkillDemos {
    public static bool Has(string skillId);
    public static DemoScript Build(string skillId, DemoContext c);   // casts puppets/props, returns the timeline
  }
}
public sealed class DemoViewport : MonoBehaviour {
  public static DemoViewport Create(RectTransform parent, Vector2 size);
  public void Play(string skillId, int rank);  public void Skip();  public void Replay();  public void Stop();
  public bool Playing { get; }  public float Time { get; }  public string CaptionText { get; }  public bool AtEndCard { get; }
  public bool ManualClock;  public void Advance(float dt);      // tests
}
```

Demo content: spec §5.4. Ground decals: cone mesh (as `WitnessConeView`), danger ring, detection ring, aim line —
created under `Stage.Decals`. Sand/dust/heal bursts via `Vfx.Burst`; sounds via `AudioDirector.Play(name, null, …)`.

- [ ] **Step 1: Failing tests:**

```csharp
[UnityTest] public IEnumerator Every_Demo_Plays_Through_Without_Touching_The_Run([Values(1, 2)] int rank)
{
    var ctx = new GameObject("RunContext").AddComponent<RunContext>();
    int events = 0; HookAllEvents(ctx.Events, () => events++);
    int agents = AgentRegistry.All.Count;
    var vp = DemoViewport.Create(UIKit.Stretch(UIRoot.Ensure().Overlay, "Host"), new Vector2(720, 405));
    vp.ManualClock = true;
    foreach (var def in SkillCatalog.Load().Implemented)
    {
        Assert.IsTrue(SkillDemos.Has(def.id), def.id);
        vp.Play(def.id, rank);
        var captions = new HashSet<string>();
        for (int i = 0; i < 1200 && !vp.AtEndCard; i++) { vp.Advance(1f / 60f); captions.Add(vp.CaptionText); yield return null; }
        Assert.IsTrue(vp.AtEndCard, def.id + " reaches its end card");
        Assert.GreaterOrEqual(captions.Count, 3, def.id + " explains itself in steps");
    }
    vp.Stop();
    Assert.AreEqual(0, events); Assert.AreEqual(agents, AgentRegistry.All.Count);
}
[UnityTest] public IEnumerator Skip_Jumps_To_The_End_Card_And_Replay_Restarts()
{
    var vp = DemoViewport.Create(UIKit.Stretch(UIRoot.Ensure().Overlay, "Host"), new Vector2(720, 405));
    vp.ManualClock = true; vp.Play("pocket_sand", 1); vp.Advance(1f); yield return null;
    vp.Skip(); yield return null; Assert.IsTrue(vp.AtEndCard);
    vp.Replay(); yield return null; Assert.Less(vp.Time, 0.1f); Assert.IsFalse(vp.AtEndCard);
}
```

- [ ] **Step 2–4:** FAIL → implement engine, then the six scripts one at a time (capture each mid-demo:
  `ui_demo_<id>.png`, inspect) → PASS. **Step 5: Commit.**

### Task 13: SkillPicker rework

**Files:** Modify `UI/SkillPicker.cs`; Test `UiTests.cs`.

**Interfaces — Consumes:** `SkillGuides`, `SkillSynergies`, `DemoViewport`, `KeyGlyphs`, `TutorialProgress`,
`Lessons` (`levelup`, `loadout`). **Produces** (existing API unchanged, plus):

```csharp
public string Selected { get; }
public void Select(string id);                                  // read + demo (same as the first click)
public IReadOnlyList<string> SynergyPartners { get; }            // owned partners listed for Selected
public DemoViewport Demo { get; }
public event System.Action<string, string> SynergyFormed;        // (learned, partner) after a pick
```

- [ ] **Step 1: Failing tests:**

```csharp
[UnityTest] public IEnumerator Picker_Explains_Demos_And_Pairs_With_Your_Kit()
{
    var sys = new SkillSystem(SidekickSkills.SliceBehaviours());
    sys.Learn(SkillCatalog.Load().Get("pocket_sand"));
    var p = SkillPicker.Show(UIRoot.Ensure(), sys, 1, "» TEST", false, "GO");
    yield return null;
    Click("Skill_quiet_feet");                                      // first click reads
    yield return null;
    Assert.AreEqual("quiet_feet", p.Selected); Assert.IsTrue(p.Demo.Playing);
    CollectionAssert.Contains(p.SynergyPartners, "pocket_sand");
    string formed = null; p.SynergyFormed += (a, b) => formed = b;
    Click("Skill_quiet_feet");                                      // second click learns
    yield return null;
    Assert.AreEqual(1, sys.RankOf("quiet_feet")); Assert.AreEqual("pocket_sand", formed);
    Click("Continue"); yield return null;
    Assert.IsFalse(p);
}
```

Plus the existing `OpeningTests.The_Opening_Hands_Over_To_A_Playable_Chapter` (clicks `Skill_pocket_sand` ×2,
`Skill_crossbow` ×2, `Continue`) must stay green.

- [ ] **Step 2–4:** FAIL → implement layout (spec §5.1), detail pane, synergy lines + flourish, inline `levelup` /
  `loadout` hints, loadout bar at camp, gamepad navigation (explicit Up/Down between rows, Right into the detail
  buttons) → PASS + full PlayMode suite. **Step 5:** captures `ui_picker_opening.png`, `ui_picker_camp.png` inspected.
  **Step 6: Commit.**

### Task 14: Camp deferral, camp tip, end-screen restore hint

**Files:** Modify `Flow/CampfireDirector.cs`, `Flow/GameFlow.cs`, `UI/EndScreen.cs`; Test `UiTests.cs`.

**Interfaces — Produces:** `CampfireDirector.SceneDone { get; }`, `CampfireDirector.SkipScene()`,
`CampfireDirector.SceneLength` (0.6 + lines × 4.2 s); `EndScreen.Model.Hint` (string, optional).

- [ ] **Step 1: Failing test:**

```csharp
[UnityTest] public IEnumerator Camp_Picker_Waits_For_The_Fireside_Scene()
{
    var (hero, sk, camp) = StageCamp();                               // as CampfireTests.Stage()
    UIRoot.Ensure();
    var c = new GameObject("Campfire").AddComponent<CampfireDirector>();
    c.Begin(camp, hero, sk, 1, false, new string[0]);
    yield return null;
    Assert.IsNull(c.Picker, "the scene plays first");
    c.SkipScene(); yield return null;
    Assert.IsNotNull(c.Picker); Assert.IsTrue(c.SceneDone);
}
```

- [ ] **Step 2–4:** FAIL → implement (picker opens at `SceneLength` or on Confirm via `SkipScene`; a "{confirm} skip to
  the level-up" chip; `GameFlow` offers the `camp` tip on arrival; on a loss, if `restore` is unseen and tips are on,
  `m.Hint` = the lesson body and it is marked seen) → PASS + CampfireTests green. **Step 5: Commit.**

### Task 15: Pause menu, Settings, Controls, Field Guide

**Files:** Create `UI/PauseMenu.cs`, `UI/FieldGuide.cs`; Modify `Flow/GameFlow.cs` (create the pause menu when
`!AutoPlay`), `UI/HudView.cs` (Insight starts at `TutorialProgress.InsightDefault`); Test `UiTests.cs`.

**Interfaces — Produces:**

```csharp
public sealed class PauseMenu : MonoBehaviour {
  public static PauseMenu Create(UIRoot root, HS.Flow.GameFlow flow);
  public bool IsOpen { get; }  public void Open();  public void Close();   // Open is a no-op outside Chapter/Duel
}
public sealed class FieldGuide : MonoBehaviour {
  public static FieldGuide Show(UIRoot root, string tab, System.Action onClose);   // "tips" | "skills" | "controls"
  public string Tab { get; }  public void SelectTab(string tab);  public void Close();
  public IReadOnlyList<string> ListedLessons { get; }   // ids shown with their text (seen ones)
  public int LockedCount { get; }                       // shown as "???"
}
```

Pause (Esc/Start, via `GameInput.Pause`): `ModalGate.Push("pause")`; buttons RESUME, FIELD GUIDE, CONTROLS,
SETTINGS (Tips on/off, Lesson pauses on/off, Hero Insight at start on/off, Reset tutorial with a confirm), QUIT TO
TITLE? → no title screen exists: RESTART FROM CHAPTER START (uses `RunState.ChapterStart`) and QUIT. Field Guide:
tabs TIPS (grouped by category; seen ones readable, unseen "???"), SKILLS (implemented skills: guide, stats, Callum's
view, synergies, demo viewport), CONTROLS (both devices, from `KeyGlyphs`).

- [ ] **Step 1: Failing tests:**

```csharp
[UnityTest] public IEnumerator Pause_Freezes_The_Road_And_Resumes()
{
    var flow = StartChapterAsPlayer();                       // as TutorialTests
    yield return WaitUntil(() => flow.Current == GameFlow.State.Chapter, 5f);
    var menu = Object.FindAnyObjectByType<PauseMenu>();
    menu.Open(); yield return null;
    Assert.IsTrue(menu.IsOpen); Assert.IsTrue(SimLoop.Instance.Paused); Assert.IsFalse(GameInput.Instance.Gameplay.enabled);
    menu.Close(); yield return null;
    Assert.IsFalse(SimLoop.Instance.Paused); Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
}
[UnityTest] public IEnumerator Field_Guide_Lists_What_You_Have_Learned()
{
    TutorialProgress.Store = new MemoryStore(); TutorialProgress.MarkSeen("cone");
    var g = FieldGuide.Show(UIRoot.Ensure(), "tips", null); yield return null;
    CollectionAssert.Contains(g.ListedLessons, "cone");
    CollectionAssert.DoesNotContain(g.ListedLessons, "duel");
    Assert.Greater(g.LockedCount, 0);
}
```

- [ ] **Step 2–4:** FAIL → implement → PASS + full suites. **Step 5:** captures `ui_pause.png`, `ui_guide_tips.png`,
  `ui_guide_skills.png`, `ui_guide_controls.png` inspected. **Step 6: Commit.**

### Task 16: Integrated QA, docs, player build

**Files:** `Tests/PlayMode/UiQaCaptures.cs` (complete set), `docs/qa/QA_LOG.md`, `GDD.md` (§4.7 and §9: one line each
pointing at the tutorial/picker), plan checkboxes.

- [ ] **Step 1:** Full EditMode + PlayMode suites green (expect 71 + new EditMode, 83 + new PlayMode).
- [ ] **Step 2:** Run every `UiQaCaptures` scenario; read each capture; fix every overlap, clipping, contrast or
  legibility issue found; re-capture.
- [ ] **Step 3:** Player build (`tools/unity-tests.sh method HS.EditorTools.PlayerBuild.BuildMac`): 0 errors (catches
  editor-only APIs in runtime code); run it with `-hs-autoplay supportive -hs-seed 2 -hs-quit` and check the player log
  for exceptions.
- [ ] **Step 4:** QA_LOG section "Tutorial, skill demos, HUD pass" (what was built, tests, captures, defects found and
  fixed, open issues); GDD one-liners.
- [ ] **Step 5: Commit** "Tutorial + HUD pass: integrated QA and docs".

---

## Self-review

- Spec §3 units → Tasks 2–6, 9–15; §4 lessons → Tasks 4, 10, 13, 14; §5 picker/guides/synergies/demos → Tasks 5,
  11–13; §6 art/HUD → Tasks 1, 7, 8; §7 testing → every task + 16; persistence and settings → Tasks 2, 15; camp
  deferral → Task 14; Insight default off → Tasks 2, 15.
- Names used across tasks: `TutorialProgress.{IsSeen,MarkSeen,ResetSeen,TipsEnabled,LessonPauses,InsightDefault}`,
  `KeyGlyphs.{Label,Format,Chip,SlotToken}`, `Lessons.{All,Get,CopyViolations}`, `CoachTarget`, `ModalGate.{Push,Pop,Any}`,
  `TipView.{ShowToast,ShowFocus,Coach,Marker}`, `TutorialDirector.{Offer,Complete,ContinueFocus,Showing,FocusOpen}`,
  `DemoStage.{Ensure,Spawn,Prop,Clear}`, `DemoViewport.{Play,Skip,Replay,Stop,Advance}`, `SkillGuides.{Get,StatLines,IconId}`,
  `SkillSynergies.{Note,With,Without}` — consistent.

## Execution

Inline, task by task, with the QA gate after each (the session has no mandate for sub-agents; the M1 slice plan was
executed the same way).
