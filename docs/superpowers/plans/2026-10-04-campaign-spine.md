# Campaign Spine Implementation Plan (Plan 1 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the one-chapter slice into a five-chapter campaign loop (Ch1 → camp → Ch2 → … → Ch5 → door → boss) with per-chapter power tiers, slots, XP, Stage checks, themes and Restore Points, playable end to end on temporary rooms.

**Architecture:** One scene, rebuilt chapter by chapter. `ChapterBootstrap` splits into a once-per-run part (the pair, the ledger, stones, UI, camera) and a per-chapter part (rooms, route, encounters, theme). `GameFlow` loops chapters using a static `CampaignSchedule`; `ChapterTier` scales combat so hits-to-kill stay constant. Chapters 2–5 borrow chapter 1's modules (`ChapterDef.ModuleChapter = 1`) until Plans 2–5 build their own.

**Tech Stack:** Unity 6000.3 (URP), C#, NUnit via Unity Test Framework, headless runs with `tools/unity-tests.sh`.

**Spec:** `docs/superpowers/specs/2026-10-04-callum-campaign-design.md` (§2 decisions, §3.1 loop, §3.2 units, §10 step 1).

## Global Constraints

- Callum only. Power: Callum damage ×2.5 and HP ×2 per chapter; enemies HP ×2.5 and damage-to-hero ×2 per chapter; sidekick outgoing damage ×2.5 per chapter; status durations never scale.
- Slots 4 / 5 / 6 / 6 / 6 by chapter. Stage checks: end of Ch2 (max S1), end of Ch3 (max S2), the door (max S3, +1 at most). Ch1 and Ch4 campfires have no check.
- Signature unlocks: Ch1 Strike I, Ch2 Stance I, Ch3 Finisher I, Ch4 Strike II + Stance II, Ch5 Finisher II.
- Between-room recovery: Ch1 50% of max HP, Ch2+ 30%.
- XP: a thorough player reaches level 3 / 6 / 9 / 13 at the ends of chapters 1–4.
- Restore Points: every chapter start reached this run, plus "before the Gallery door".
- No soft-locks; deterministic sim (no `Random`, no wall-clock in gameplay).
- All copy is draft text for the owner; never name Rapport, Stage, Moments or points in player-facing text.
- Every test command runs from the worktree root `/Users/sapnagoel/Documents/coding/Game/.claude/worktrees/callum-campaign`. Baseline before this plan: EditMode 94/94; PlayMode 104/110 (pre-existing failures: `CameraTests.FixedAngleCamera_KeepsBothTargetsInFrame`, `HarnessTests.*` ×2, `OpeningTests.The_Opening_Hands_Over_To_A_Playable_Chapter`, `RapportTests.A_Witnessed_Assist_Is_Caught_Not_Credited`, `StoneTests.An_Active_Stone_Witnesses_What_Callum_Cannot_See`). A task is green when it adds no new failures.

All paths below are relative to `Escort/Assets/_Game/` unless they start with `docs/`, `tools/` or `Escort/`.

---

### Task 1: The schedule and the tiers (pure data)

**Files:**
- Create: `Core/ChapterTier.cs`, `Hero/Signature.cs`, `Flow/CampaignSchedule.cs`
- Modify: `Core/Tuning.cs` (CallumTuning.Damage/MaxHp use ChapterTier; add `riposteMultiplierII`)
- Test: `Tests/EditMode/CampaignScheduleTests.cs`

**Interfaces:**
- Produces: `HS.Core.ChapterTier` (`HeroDamage(int)`, `HeroHp(int)`, `EnemyHp(int)`, `EnemyDamageToHero(int)`, `SidekickDamage(int)`, `Clamp(int)`); `HS.Hero.Signature` flags; `HS.Flow.ChapterRules` and `HS.Flow.CampaignSchedule` (`For(int)`, `RoomPot(int chapter, int modulePot)`, `LevelTarget(int)`, `Chapters`).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/EditMode/CampaignScheduleTests.cs
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Rapport;
using HS.Rooms;
using HS.Skills;
using NUnit.Framework;

namespace HS.Tests
{
    public class CampaignScheduleTests
    {
        [Test]
        public void Tiers_Follow_The_GDD_And_Keep_Hits_To_Kill_Constant()
        {
            Assert.AreEqual(1f, ChapterTier.HeroDamage(1), 1e-4f);
            Assert.AreEqual(39.0625f, ChapterTier.HeroDamage(5), 1e-3f, "x2.5 per chapter: about x39 by chapter 5");
            Assert.AreEqual(16f, ChapterTier.HeroHp(5), 1e-4f);
            for (int ch = 1; ch <= 5; ch++)
            {
                Assert.AreEqual(ChapterTier.HeroDamage(ch), ChapterTier.EnemyHp(ch), 1e-3f, "his hits-to-kill stay constant");
                Assert.AreEqual(ChapterTier.HeroHp(ch), ChapterTier.EnemyDamageToHero(ch), 1e-3f, "his hits-to-die stay constant");
                Assert.AreEqual(ChapterTier.EnemyHp(ch), ChapterTier.SidekickDamage(ch), 1e-3f, "her hits-to-kill stay constant");
            }
            Assert.AreEqual(5, ChapterTier.Clamp(9));
            Assert.AreEqual(1, ChapterTier.Clamp(0));
        }

        [Test]
        public void Callum_Tuning_Reads_The_Tier()
        {
            var t = new CallumTuning();
            Assert.AreEqual(26f * 6.25f, t.Damage(3), 1e-3f);
            Assert.AreEqual(260f * 4f, t.MaxHp(3), 1e-3f);
        }

        [Test]
        public void Signature_Unlocks_Accumulate()
        {
            Assert.AreEqual(Signature.StrikeI, CampaignSchedule.For(1).Unlocks);
            Assert.AreEqual(Signature.StrikeI | Signature.StanceI, CampaignSchedule.For(2).Unlocks);
            Assert.AreEqual(Signature.StrikeI | Signature.StanceI | Signature.FinisherI, CampaignSchedule.For(3).Unlocks);
            Assert.IsTrue((CampaignSchedule.For(4).Unlocks & (Signature.StrikeII | Signature.StanceII)) == (Signature.StrikeII | Signature.StanceII));
            Assert.IsTrue((CampaignSchedule.For(5).Unlocks & Signature.FinisherII) != 0);
        }

        [Test]
        public void Slots_Checks_And_Camps_By_Chapter()
        {
            int[] slots = { 4, 5, 6, 6, 6 };
            for (int ch = 1; ch <= 5; ch++)
            {
                Assert.AreEqual(slots[ch - 1], CampaignSchedule.For(ch).Slots);
                Assert.AreEqual(SkillSystem.SlotsForChapter(ch), CampaignSchedule.For(ch).Slots, "one source of truth");
            }
            Assert.IsNull(CampaignSchedule.For(1).CampCheck, "no check at the end of chapter 1");
            Assert.AreEqual(StageCheck.Chapter2, CampaignSchedule.For(2).CampCheck);
            Assert.AreEqual(StageCheck.Chapter3, CampaignSchedule.For(3).CampCheck);
            Assert.IsNull(CampaignSchedule.For(4).CampCheck);
            Assert.IsFalse(CampaignSchedule.For(5).HasCamp, "chapter 5 ends at the door");
            Assert.IsTrue(CampaignSchedule.For(2).LearnsRecall);
            Assert.IsTrue(CampaignSchedule.For(4).CapstoneReveal);
            Assert.AreEqual(0.5f, CampaignSchedule.For(1).Recovery, 1e-4f);
            Assert.AreEqual(0.3f, CampaignSchedule.For(3).Recovery, 1e-4f);
            Assert.AreEqual("Whisperwood", CampaignSchedule.For(2).Name);
        }

        [Test]
        public void Full_Pots_Reach_Level_3_6_9_13()
        {
            int xp = 0;
            for (int ch = 1; ch <= 4; ch++)
            {
                int rooms = ChapterDef.For(ch).Slots.Count;
                for (int r = 0; r < rooms; r++) xp += CampaignSchedule.RoomPot(ch, 100);
                Assert.AreEqual(CampaignSchedule.LevelTarget(ch), HS.Flow.XpTracker.LevelFor(xp), $"end of chapter {ch} ({xp} xp)");
            }
            Assert.AreEqual(0, CampaignSchedule.RoomPot(5, 100), "no levels in the Gallery");
        }
    }
}
```

- [ ] **Step 2: Run the tests and see them fail**

Run: `tools/unity-tests.sh EditMode CampaignScheduleTests`
Expected: COMPILE ERRORS naming `ChapterTier`, `CampaignSchedule`, `Signature`, `ChapterDef.For`.

- [ ] **Step 3: Implement**

```csharp
// Core/ChapterTier.cs
using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Per-chapter combat scaling (campaign spec §2). Callum: damage ×2.5 and HP ×2 per chapter (GDD §4.2). Enemies: HP
    /// ×2.5 and damage-to-hero ×2, so his hits-to-kill and hits-to-die stay constant; the sidekick's outgoing damage ×2.5
    /// so hers do too. Status durations never scale. Difficulty grows from the pressure mix, not the numbers.
    /// </summary>
    public static class ChapterTier
    {
        public const int First = 1, Last = 5;
        public static int Clamp(int chapter) => Mathf.Clamp(chapter, First, Last);
        public static float HeroDamage(int chapter) => Mathf.Pow(2.5f, Clamp(chapter) - 1);
        public static float HeroHp(int chapter) => Mathf.Pow(2f, Clamp(chapter) - 1);
        public static float EnemyHp(int chapter) => HeroDamage(chapter);
        public static float EnemyDamageToHero(int chapter) => HeroHp(chapter);
        public static float SidekickDamage(int chapter) => HeroDamage(chapter);
        public static int Current => RunContext.Current != null ? Clamp(RunContext.Current.Chapter) : First;
    }
}
```

```csharp
// Hero/Signature.cs
namespace HS.Hero
{
    /// <summary>Signature skills unlocked on the fixed schedule (GDD §4.2): Ch1 Strike I, Ch2 Stance I, Ch3 Finisher I,
    /// Ch4 Strike II + Stance II, Ch5 Finisher II.</summary>
    [System.Flags]
    public enum Signature { None = 0, StrikeI = 1, StanceI = 2, FinisherI = 4, StrikeII = 8, StanceII = 16, FinisherII = 32 }
}
```

```csharp
// Flow/CampaignSchedule.cs
using HS.Hero;
using HS.Rapport;
using UnityEngine;

namespace HS.Flow
{
    /// <summary>What a chapter is, mechanically (campaign spec §3.2).</summary>
    public sealed class ChapterRules
    {
        public int Chapter;
        public string Name;
        public int Slots;
        public Signature Unlocks;
        /// <summary>The hidden Stage check at this chapter's campfire (null: none).</summary>
        public StageCheck? CampCheck;
        /// <summary>False for the Gallery: it ends at the door, not a campfire.</summary>
        public bool HasCamp = true;
        public bool LearnsRecall, CapstoneReveal, Hunger;
        /// <summary>Between-room recovery, a fraction of his max HP (wounds stay).</summary>
        public float Recovery;
        /// <summary>Multiplier on each room module's XP pot.</summary>
        public float XpFactor;
    }

    /// <summary>
    /// The campaign's fixed schedule (GDD §3, §4.1, §4.2, §4.4, §4.6). Pure data: GameFlow, the campfire and the tests read
    /// it; nothing else decides chapter numbers.
    /// </summary>
    public static class CampaignSchedule
    {
        public const int Chapters = 5;
        const Signature Ch1 = Signature.StrikeI;
        const Signature Ch2 = Ch1 | Signature.StanceI;
        const Signature Ch3 = Ch2 | Signature.FinisherI;
        const Signature Ch4 = Ch3 | Signature.StrikeII | Signature.StanceII;
        const Signature Ch5 = Ch4 | Signature.FinisherII;

        static readonly ChapterRules[] Table =
        {
            new ChapterRules { Chapter = 1, Name = "The Old Road", Slots = 4, Unlocks = Ch1, Recovery = 0.5f, XpFactor = 1.0f },
            new ChapterRules { Chapter = 2, Name = "Whisperwood", Slots = 5, Unlocks = Ch2, CampCheck = StageCheck.Chapter2, LearnsRecall = true, Hunger = true, Recovery = 0.3f, XpFactor = 1.8f },
            new ChapterRules { Chapter = 3, Name = "Catacombs of Ends", Slots = 6, Unlocks = Ch3, CampCheck = StageCheck.Chapter3, Hunger = true, Recovery = 0.3f, XpFactor = 2.4f },
            new ChapterRules { Chapter = 4, Name = "The Sunken Bastion", Slots = 6, Unlocks = Ch4, CapstoneReveal = true, Hunger = true, Recovery = 0.3f, XpFactor = 4.7f },
            new ChapterRules { Chapter = 5, Name = "The Gallery", Slots = 6, Unlocks = Ch5, HasCamp = false, Recovery = 0.3f, XpFactor = 0f },
        };

        public static ChapterRules For(int chapter) => Table[Mathf.Clamp(chapter, 1, Chapters) - 1];

        /// <summary>A room's XP pot in this chapter (GDD §4.1: fixed pots, 13 levels across chapters 1–4).</summary>
        public static int RoomPot(int chapter, int modulePot) => Mathf.RoundToInt(modulePot * For(chapter).XpFactor);

        /// <summary>The level a thorough player has at the end of a chapter.</summary>
        public static int LevelTarget(int chapter) => chapter <= 1 ? 3 : chapter == 2 ? 6 : chapter == 3 ? 9 : 13;
    }
}
```

In `Core/Tuning.cs` (CallumTuning), replace the two scaling helpers and add the Strike II multiplier next to `riposteMultiplier`:

```csharp
        [Header("Strike I: Riposte (parry counter 2x)")]
        public float riposteCooldown = 5f;
        public float riposteMultiplier = 2f;
        [Tooltip("Strike II (chapter 4): the counter lands for 3x.")]
        public float riposteMultiplierII = 3f;
```
```csharp
        public float Damage(int chapter) => damage * ChapterTier.HeroDamage(chapter);
        public float MaxHp(int chapter) => maxHp * ChapterTier.HeroHp(chapter);
```

`ChapterDef.For` is created in Task 3; to compile Task 1 alone, add it now in `Rooms/RoomAssembler.cs` as:

```csharp
        public static ChapterDef For(int chapter) => OldRoad();
```
(Task 3 replaces the body.) With that stub, `Full_Pots_Reach_Level_3_6_9_13` fails (3 rooms in every chapter) until Task 3; that is expected and noted there.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh EditMode CampaignScheduleTests`
Expected: 4 pass; `Full_Pots_Reach_Level_3_6_9_13` fails at "end of chapter 2" (fixed in Task 3).

- [ ] **Step 5: Commit**

```bash
git add Escort/Assets/_Game/Core/ChapterTier.cs* Escort/Assets/_Game/Hero/Signature.cs* Escort/Assets/_Game/Flow/CampaignSchedule.cs* Escort/Assets/_Game/Core/Tuning.cs Escort/Assets/_Game/Rooms/RoomAssembler.cs Escort/Assets/_Game/Tests/EditMode/CampaignScheduleTests.cs*
git commit -m "Campaign: the chapter schedule and combat tiers"
```
(Unity generates `.meta` files on the first headless run; include them.)

---

### Task 2: Tiers in combat; Callum takes his chapter

**Files:**
- Modify: `Enemies/EnemyAgent.cs` (`Configure`, `ModifyIncomingDamage`)
- Modify: `Hero/HeroAgent.cs` (`ModifyIncomingDamage`)
- Modify: `Hero/Callum/CallumModule.cs` (`ApplyChapter`, `Unlocks`, `Recovery`, Riposte multiplier, `OnRoomEntered`)
- Test: `Tests/PlayMode/CampaignTests.cs`

**Interfaces:**
- Consumes: `ChapterTier`, `Signature`, `CampaignSchedule.For(int)`.
- Produces: `CallumModule.ApplyChapter(int chapter, Signature unlocks, float recovery)`, `CallumModule.Unlocks`, `CallumModule.Recovery`, `CallumModule.RiposteMultiplier`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/PlayMode/CampaignTests.cs
using System.Collections;
using HS.Core;
using HS.Enemies;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The campaign spine: tiers, chapter rebuilds, the chapter loop, restore points.</summary>
    public class CampaignTests
    {
        [TearDown]
        public void TearDown() => TestUi.TearDownAll();

        static RunContext Ctx(int chapter)
        {
            var ctx = new GameObject("RunContext").AddComponent<RunContext>();
            ctx.Chapter = chapter;
            SimLoop.Ensure().Paused = true;
            return ctx;
        }

        [UnityTest]
        public IEnumerator Enemies_And_The_Pair_Scale_With_The_Chapter()
        {
            var ctx = Ctx(3);
            var assets = GameAssets.Load();
            var thug = Object.Instantiate(assets.Enemy("thug"), new Vector3(0f, 0.05f, 5f), Quaternion.identity).GetComponent<EnemyAgent>();
            var hero = Object.Instantiate(assets.hero, new Vector3(0f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
            var sk = Object.Instantiate(assets.sidekick, new Vector3(2f, 0.05f, 0f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = hero;
            ctx.Sidekick = sk;
            yield return null;
            Assert.AreEqual(80f * 6.25f, thug.Health.Max, 0.01f, "enemy HP x2.5 per chapter");
            float before = hero.Health.Current;
            hero.TakeDamage(DamageInfo.Make(thug, hero, 10f, DamageKind.Melee, "melee"));
            Assert.AreEqual(40f, before - hero.Health.Current, 0.01f, "enemy damage to him x2 per chapter");
            before = thug.Health.Current;
            thug.TakeDamage(DamageInfo.Make(sk, thug, 8f, DamageKind.Knife, "knife"));
            Assert.AreEqual(50f, before - thug.Health.Current, 0.01f, "her damage x2.5 per chapter");
            float skBefore = sk.Health.Current;
            sk.TakeDamage(DamageInfo.Make(thug, sk, 10f, DamageKind.Melee, "melee"));
            Assert.AreEqual(10f, skBefore - sk.Health.Current, 0.01f, "her own HP grows with levels, not chapters");
        }

        [UnityTest]
        public IEnumerator Callum_Takes_His_Chapter()
        {
            Ctx(4);
            var hero = Object.Instantiate(GameAssets.Load().hero, Vector3.zero, Quaternion.identity).GetComponent<HeroAgent>();
            yield return null;
            var cm = (CallumModule)hero.Module;
            var rules = CampaignSchedule.For(4);
            cm.ApplyChapter(4, rules.Unlocks, rules.Recovery);
            Assert.AreEqual(260f * 8f, hero.Health.Max, 0.01f);
            Assert.AreEqual(hero.Health.Max, hero.Health.Current, 0.01f, "a new chapter starts rested");
            Assert.AreEqual(3f, cm.RiposteMultiplier, 1e-4f, "Strike II");
            Assert.AreEqual(0.3f, cm.Recovery, 1e-4f);
            cm.ApplyChapter(1, CampaignSchedule.For(1).Unlocks, 0.5f);
            Assert.AreEqual(2f, cm.RiposteMultiplier, 1e-4f, "Strike I");
        }
    }
}
```

- [ ] **Step 2: Run and see them fail**

Run: `tools/unity-tests.sh PlayMode CampaignTests`
Expected: COMPILE ERRORS (`ApplyChapter`, `RiposteMultiplier`, `Recovery`).

- [ ] **Step 3: Implement**

`Enemies/EnemyAgent.cs`, in `Configure`, replace the HP line:
```csharp
            float hp = Stats.maxHp * ChapterTier.EnemyHp(Ctx != null ? Ctx.Chapter : 1);
```
and end `ModifyIncomingDamage` with the sidekick's tier:
```csharp
            if (State == EnemyState.Dormant) Activate();
            return d.FromSidekick ? d.Amount * ChapterTier.SidekickDamage(Ctx != null ? Ctx.Chapter : 1) : d.Amount;
```

`Hero/HeroAgent.cs`, replace `ModifyIncomingDamage`:
```csharp
        protected override float ModifyIncomingDamage(DamageInfo d)
        {
            // Enemies hit harder each chapter (x2, matching his HP), so the danger of a blow stays the same (campaign spec §2).
            // Hazards already scale with his max HP; fever is his own.
            if (d.Source != null && d.Source.Faction == Faction.Hostile) d.Amount *= ChapterTier.EnemyDamageToHero(Ctx != null ? Ctx.Chapter : 1);
            return Module != null ? Module.ModifyIncoming(d) : d.Amount;
        }
```

`Hero/Callum/CallumModule.cs`:
- Add fields/properties near `WoundDamageMul`:
```csharp
        /// <summary>Signature skills unlocked so far (GDD §4.2 schedule; set per chapter by the campaign).</summary>
        public Signature Unlocks { get; private set; } = Signature.StrikeI;
        /// <summary>Between-room recovery, a fraction of max HP (Ch1 50%, Ch2+ 30%).</summary>
        public float Recovery { get; private set; }
        public float RiposteMultiplier => (Unlocks & Signature.StrikeII) != 0 ? T.riposteMultiplierII : T.riposteMultiplier;
```
- In `Bind`, after `Honor = T.honorMax;`: `Recovery = T.secondWind;`
- Add:
```csharp
        /// <summary>A new chapter (campaign): his power tier, his signature skills, the road's attrition. Starts rested.</summary>
        public void ApplyChapter(int chapter, Signature unlocks, float recovery)
        {
            Unlocks = unlocks;
            Recovery = recovery;
            Hero.SetMaxHp(T.MaxHp(chapter), true);
            _lastRoom = -1; // room indices start again at 0
        }
```
- In `OnRoomEntered`, use `Recovery` instead of `T.secondWind`:
```csharp
            if (room > _lastRoom && _lastRoom >= 0 && Hero.IsAlive) Hero.Health.Heal(Hero.Health.Max * Recovery);
```
- In `OnAttackResolving`, replace `* T.riposteMultiplier` with `* RiposteMultiplier`.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh PlayMode CampaignTests` → Expected: 2 passed.
Run: `tools/unity-tests.sh PlayMode CallumTests` → Expected: no new failures (chapter 1 is ×1 everywhere).

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: enemies, the sidekick and Callum take the chapter's tier"
```

---

### Task 3: Chapter definitions, the module library by chapter, chapter themes

**Files:**
- Modify: `Rooms/RoomAssembler.cs` (`ChapterDef` fields and the five chapters; `ModuleInfo.Chapter`)
- Modify: `Rooms/RoomModule.cs` (`public int Chapter = 1;`)
- Modify: `Rooms/ChapterBuilder.cs` (library filter; campfire for non-final chapters, boss for the final one; deactivate before destroy)
- Create: `Presentation/ChapterTheme.cs`
- Modify: `Tests/PlayMode/ChapterBuildTests.cs` (boss only in the Gallery)
- Test: `Tests/EditMode/RoomAssemblerTests.cs` (new cases), `Tests/PlayMode/CampaignTests.cs` (theme)

**Interfaces:**
- Produces: `ChapterDef.For(int)`, `ChapterDef.Whisperwood/Catacombs/SunkenBastion/Gallery()`, `ChapterDef.Theme`, `ChapterDef.ModuleChapter`, `ChapterDef.Final`; `ChapterBuilder.Library(int moduleChapter)`; `HS.Presentation.ChapterTheme.Apply(string id)`, `ChapterTheme.ForgetSceneDefaults()`.

- [ ] **Step 1: Write the failing tests**

Append to `Tests/EditMode/RoomAssemblerTests.cs` (inside the class):
```csharp
        [Test]
        public void Every_Chapter_Plans_Deterministically_From_Its_Library()
        {
            for (int ch = 1; ch <= 5; ch++)
            {
                var def = ChapterDef.For(ch);
                Assert.AreEqual(ch, def.Chapter);
                var a = RoomAssembler.Plan(42, def, Library());
                var b = RoomAssembler.Plan(42, def, Library());
                Assert.AreEqual(def.Slots.Count, a.Rooms.Count, def.Name);
                Assert.AreEqual(a.Signature, b.Signature, def.Name);
            }
            Assert.AreEqual(new[] { 3, 4, 4, 4, 2 }, new[] { 1, 2, 3, 4, 5 }.Select(c => ChapterDef.For(c).Slots.Count).ToArray());
            Assert.IsTrue(ChapterDef.For(5).Final);
            Assert.IsFalse(ChapterDef.For(4).Final);
        }

        [Test]
        public void Chapters_With_The_Same_Seed_Differ()
        {
            int differ = 0;
            for (int s = 0; s < 10; s++)
                if (RoomAssembler.Plan(s, ChapterDef.For(1), Library()).Signature != RoomAssembler.Plan(s, ChapterDef.For(2), Library()).Signature) differ++;
            Assert.GreaterOrEqual(differ, 8, "the chapter number is mixed into the seed");
        }
```

Append to `Tests/PlayMode/CampaignTests.cs`:
```csharp
        [UnityTest]
        public IEnumerator A_Chapter_Theme_Changes_The_Light_And_The_Old_Road_Restores_The_Scene()
        {
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
            var sun = new GameObject("TestSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            RenderSettings.sun = sun;
            sun.color = Color.white;
            RenderSettings.fog = false;
            RenderSettings.fogColor = Color.magenta;
            yield return null;
            HS.Presentation.ChapterTheme.Apply("catacombs");
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreNotEqual(Color.white, sun.color);
            HS.Presentation.ChapterTheme.Apply("old_road");
            Assert.IsFalse(RenderSettings.fog, "chapter 1 is the scene as authored");
            Assert.AreEqual(Color.magenta, RenderSettings.fogColor);
            Assert.AreEqual(Color.white, sun.color);
        }
```

Change `Tests/PlayMode/ChapterBuildTests.cs`, in `Seeded_Chapter_Is_Continuous_And_Deterministic`, the two lines `Assert.NotNull(b.Campfire); Assert.NotNull(b.Boss);` to:
```csharp
                Assert.NotNull(b.Campfire, "the Old Road ends at its campfire");
                Assert.IsNull(b.Boss, "the boss is the Gallery's");
```
and add a test:
```csharp
        [UnityTest]
        public IEnumerator The_Gallery_Builds_The_Boss_Arena_And_No_Campfire()
        {
            var b = MakeBuilder();
            b.Build(3, ChapterDef.For(5));
            yield return null;
            Assert.AreEqual(2, b.Rooms.Count);
            Assert.NotNull(b.Boss);
            Assert.IsNull(b.Campfire);
            Object.Destroy(b.gameObject);
        }
```

- [ ] **Step 2: Run and see them fail**

Run: `tools/unity-tests.sh EditMode RoomAssemblerTests` → Expected: compile errors (`Final`) or failures (3 rooms everywhere).

- [ ] **Step 3: Implement**

`Rooms/RoomAssembler.cs` — `ModuleInfo` gains `public int Chapter = 1;`. Replace `ChapterDef`:
```csharp
    public sealed class ChapterDef
    {
        public int Chapter;
        public string Name;
        /// <summary>ChapterTheme id (light, fog, ambience).</summary>
        public string Theme = "old_road";
        /// <summary>Which chapter's modules it draws from. Until a chapter's own rooms exist it borrows chapter 1's.</summary>
        public int ModuleChapter = 1;
        /// <summary>The Gallery: the door and the boss follow its rooms; no campfire.</summary>
        public bool Final;
        public List<ChapterSlot> Slots = new List<ChapterSlot>();

        public static ChapterDef For(int chapter) => chapter switch
        {
            2 => Whisperwood(),
            3 => Catacombs(),
            4 => SunkenBastion(),
            5 => Gallery(),
            _ => OldRoad(),
        };

        static ChapterSlot Any() => new ChapterSlot(RoomKind.Combat, RoomKind.Ambush, RoomKind.TrapCorridor);

        /// <summary>Chapter 1 "The Old Road": 3 rooms (GDD §11.2) — combat, traps, ambushes.</summary>
        public static ChapterDef OldRoad() => new ChapterDef
        {
            Chapter = 1, Name = "The Old Road", Theme = "old_road",
            Slots =
            {
                new ChapterSlot(RoomKind.Ambush, RoomKind.Combat),
                new ChapterSlot(RoomKind.TrapCorridor, RoomKind.Combat, RoomKind.Ambush),
                new ChapterSlot(RoomKind.Combat, RoomKind.Ambush, RoomKind.TrapCorridor),
            },
        };

        // Chapters 2–5: their rooms come with Plans 2–5. Until then they borrow the Old Road's modules under their own light.
        public static ChapterDef Whisperwood() => new ChapterDef
        {
            Chapter = 2, Name = "Whisperwood", Theme = "whisperwood", ModuleChapter = 1,
            Slots = { new ChapterSlot(RoomKind.Ambush, RoomKind.Combat), Any(), Any(), Any() },
        };

        public static ChapterDef Catacombs() => new ChapterDef
        {
            Chapter = 3, Name = "Catacombs of Ends", Theme = "catacombs", ModuleChapter = 1,
            Slots = { new ChapterSlot(RoomKind.TrapCorridor, RoomKind.Combat), Any(), Any(), Any() },
        };

        public static ChapterDef SunkenBastion() => new ChapterDef
        {
            Chapter = 4, Name = "The Sunken Bastion", Theme = "sunken_bastion", ModuleChapter = 1,
            Slots = { new ChapterSlot(RoomKind.Combat, RoomKind.Ambush), Any(), Any(), Any() },
        };

        public static ChapterDef Gallery() => new ChapterDef
        {
            Chapter = 5, Name = "The Gallery", Theme = "gallery", ModuleChapter = 1, Final = true,
            Slots = { Any(), Any() },
        };
    }
```

`Rooms/RoomModule.cs`, after `XpPot`:
```csharp
        [Tooltip("Which chapter's library this module belongs to.")]
        public int Chapter = 1;
```

`Rooms/ChapterBuilder.cs`:
```csharp
        public List<ModuleInfo> Library(int moduleChapter) => ModulePrefabs.Where(p => p != null).Select(p => p.GetComponent<RoomModule>())
            .Where(m => m.Chapter == moduleChapter)
            .Select(m => new ModuleInfo { Id = m.ModuleId, Kind = m.Kind, Variants = m.VariantCount, Chapter = m.Chapter }).ToList();

        public ChapterDef Def { get; private set; }
```
In `Build`: `chapter ??= ChapterDef.OldRoad(); Def = chapter; Plan = RoomAssembler.Plan(seed, chapter, Library(chapter.ModuleChapter));` — replace `if (CampfirePrefab != null)` with `if (CampfirePrefab != null && !chapter.Final)` and `if (BossPrefab != null)` with `if (BossPrefab != null && chapter.Final)`. In `Clear`, deactivate before destroying so registries (stones, interactables, hazards) drop them now, not at the end of the frame:
```csharp
                var c = transform.GetChild(i).gameObject;
                c.SetActive(false);
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
```

```csharp
// Presentation/ChapterTheme.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HS.Presentation
{
    /// <summary>
    /// A chapter's light (campaign spec §7): sun colour, intensity and angle, trilight ambient, linear fog. "old_road" is
    /// the scene as authored (captured on the first Apply in a scene), so chapter 1 is exactly what it was.
    /// </summary>
    public sealed class ChapterTheme
    {
        public string Id;
        public Color Sun = Color.white;
        public float SunIntensity = 1.35f;
        public Vector3 SunEuler = new Vector3(50f, -30f, 0f);
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public bool Fog = true;
        public Color FogColor;
        public float FogStart = 45f, FogEnd = 140f;

        static ChapterTheme _scene;
        static int _sceneHandle = -1;

        static readonly Dictionary<string, ChapterTheme> Themes = new Dictionary<string, ChapterTheme>
        {
            ["whisperwood"] = new ChapterTheme
            {
                Id = "whisperwood", Sun = new Color(0.82f, 0.9f, 0.8f), SunIntensity = 0.95f, SunEuler = new Vector3(46f, 20f, 0f),
                AmbientSky = new Color(0.46f, 0.56f, 0.5f), AmbientEquator = new Color(0.36f, 0.42f, 0.34f), AmbientGround = new Color(0.2f, 0.22f, 0.17f),
                FogColor = new Color(0.52f, 0.6f, 0.55f), FogStart = 22f, FogEnd = 85f,
            },
            ["catacombs"] = new ChapterTheme
            {
                Id = "catacombs", Sun = new Color(0.62f, 0.56f, 0.72f), SunIntensity = 0.38f, SunEuler = new Vector3(62f, -10f, 0f),
                AmbientSky = new Color(0.22f, 0.2f, 0.26f), AmbientEquator = new Color(0.16f, 0.14f, 0.16f), AmbientGround = new Color(0.08f, 0.07f, 0.07f),
                FogColor = new Color(0.07f, 0.06f, 0.09f), FogStart = 26f, FogEnd = 70f,
            },
            ["sunken_bastion"] = new ChapterTheme
            {
                Id = "sunken_bastion", Sun = new Color(0.74f, 0.83f, 0.96f), SunIntensity = 0.85f, SunEuler = new Vector3(40f, -50f, 0f),
                AmbientSky = new Color(0.42f, 0.5f, 0.62f), AmbientEquator = new Color(0.34f, 0.38f, 0.42f), AmbientGround = new Color(0.16f, 0.18f, 0.2f),
                FogColor = new Color(0.48f, 0.55f, 0.63f), FogStart = 30f, FogEnd = 100f,
            },
            ["gallery"] = new ChapterTheme
            {
                Id = "gallery", Sun = new Color(1f, 0.94f, 0.82f), SunIntensity = 1.15f, SunEuler = new Vector3(55f, 10f, 0f),
                AmbientSky = new Color(0.7f, 0.66f, 0.6f), AmbientEquator = new Color(0.56f, 0.52f, 0.46f), AmbientGround = new Color(0.3f, 0.27f, 0.24f),
                Fog = false, FogColor = new Color(0.8f, 0.76f, 0.7f),
            },
        };

        public static void ForgetSceneDefaults()
        {
            _scene = null;
            _sceneHandle = -1;
        }

        public static void Apply(string id)
        {
            int handle = SceneManager.GetActiveScene().handle;
            if (_scene == null || _sceneHandle != handle)
            {
                _scene = Capture();
                _sceneHandle = handle;
            }
            var t = id != null && Themes.TryGetValue(id, out var theme) ? theme : _scene;
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = t.Sun;
                sun.intensity = t.SunIntensity;
                sun.transform.rotation = Quaternion.Euler(t.SunEuler);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.AmbientSky;
            RenderSettings.ambientEquatorColor = t.AmbientEquator;
            RenderSettings.ambientGroundColor = t.AmbientGround;
            RenderSettings.fog = t.Fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = t.FogColor;
            RenderSettings.fogStartDistance = t.FogStart;
            RenderSettings.fogEndDistance = t.FogEnd;
        }

        static ChapterTheme Capture()
        {
            var sun = RenderSettings.sun;
            return new ChapterTheme
            {
                Id = "old_road",
                Sun = sun != null ? sun.color : Color.white,
                SunIntensity = sun != null ? sun.intensity : 1.35f,
                SunEuler = sun != null ? sun.transform.rotation.eulerAngles : new Vector3(50f, -30f, 0f),
                AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor, AmbientGround = RenderSettings.ambientGroundColor,
                Fog = RenderSettings.fog, FogColor = RenderSettings.fogColor, FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
            };
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh EditMode` → Expected: no failures (Task 1's level test now passes too).
Run: `tools/unity-tests.sh PlayMode "ChapterBuildTests|CampaignTests"` → Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: five chapter definitions, modules by chapter, chapter themes"
```

---

### Task 4: ChapterBootstrap builds once per run, rebuilds per chapter

**Files:**
- Modify: `Flow/ChapterBootstrap.cs` (split into `BuildRun`, `BuildChapter`, `TeardownChapter`; `Build()` keeps its meaning for QA scenes)
- Modify: `Rooms/RoomStreamer.cs` (`Reset()`)
- Test: `Tests/PlayMode/CampaignTests.cs`

**Interfaces:**
- Consumes: `ChapterDef.For`, `ChapterTheme.Apply`, `ChapterBuilder.Build(int, ChapterDef)`.
- Produces: `ChapterBootstrap.BuildRun()`, `ChapterBootstrap.BuildChapter(int chapter, int seed)`, `ChapterBootstrap.TeardownChapter()`, `ChapterBootstrap.Def` (the built `ChapterDef`).

- [ ] **Step 1: Write the failing test**

Append to `Tests/PlayMode/CampaignTests.cs`:
```csharp
        [UnityTest]
        public IEnumerator Chapters_Rebuild_In_Place_And_The_Pair_Carries_Over()
        {
            Ctx(1);
            var boot = new GameObject("GameFlow").AddComponent<ChapterBootstrap>();
            boot.AutoBuild = false;
            boot.Seed = 5;
            boot.BuildRun();
            boot.BuildChapter(1, 5);
            yield return null;
            var hero = boot.Hero;
            var sk = boot.Sidekick;
            Assert.AreEqual(3, boot.Chapter.Rooms.Count);
            hero.Motor.Teleport(new Vector3(0f, 0.05f, 60f));
            boot.TeardownChapter();
            boot.BuildChapter(2, 5);
            yield return null;
            Assert.AreSame(hero, boot.Hero, "the same Callum");
            Assert.AreSame(sk, boot.Sidekick);
            Assert.AreEqual(2, RunContext.Current.Chapter);
            Assert.AreEqual(2, boot.Director.Ledger.Chapter, "the ledger books chapter 2's offers");
            Assert.AreEqual(4, boot.Chapter.Rooms.Count);
            Assert.AreEqual("whisperwood", boot.Def.Theme);
            Assert.Less(hero.Position.z, 1f, "he starts at the new road's start");
            Assert.Greater(hero.Route.Nodes.Count, 12);
            Assert.AreEqual(1, Object.FindObjectsByType<HS.UI.HudView>(FindObjectsSortMode.None).Length, "one HUD for the whole run");
            foreach (var e in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
                CollectionAssert.Contains(new System.Collections.Generic.List<EnemyAgent>(boot.Encounters.AllEnemies), e, "no chapter 1 bandit survives the rebuild");
        }
```

- [ ] **Step 2: Run and see it fail**

Run: `tools/unity-tests.sh PlayMode CampaignTests` → Expected: compile errors (`BuildRun`, `BuildChapter`, `TeardownChapter`, `Def`).

- [ ] **Step 3: Implement**

Replace the body of `ChapterBootstrap` (keep its fields and properties) with:
```csharp
        public ChapterDef Def { get; private set; }
        RoomStreamer _streamer;

        void Start()
        {
            if (AutoBuild) Build();
        }

        /// <summary>QA scenes: the run and chapter 1, as before.</summary>
        public void Build()
        {
            BuildRun();
            BuildChapter(RunContext.Current != null && RunContext.Current.Chapter > 1 ? RunContext.Current.Chapter : 1, Seed);
        }

        /// <summary>Once per run: the pair, the ledger, the stones, the UI, the camera. They outlive every chapter.</summary>
        public void BuildRun()
        {
            var assets = GameAssets.Load();
            var ctx = RunContext.Current;
            ctx.Seed = Seed;
            ProjectileSystem.Ensure();
            Chapter = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            Chapter.ModulePrefabs = assets.roomModules;
            Chapter.StartCapPrefab = assets.startCap;
            Chapter.EndCapPrefab = assets.endCap;
            Chapter.CampfirePrefab = assets.campfire;
            Chapter.BossPrefab = assets.boss;

            Hero = Instantiate(assets.hero, new Vector3(0f, 0.05f, -1.5f), Quaternion.identity).GetComponent<HeroAgent>();
            Sidekick = Instantiate(assets.sidekick, new Vector3(-1.6f, 0.05f, -1.9f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = Hero;
            ctx.Sidekick = Sidekick;
            Hero.ApplyStage(HeroStage);
            if (SidekickBot)
            {
                var pc = Sidekick.GetComponent<PlayerCommands>();
                if (pc != null) pc.enabled = false;
                Sidekick.Commands = Sidekick.gameObject.AddComponent<HS.Bots.FollowBot>();
            }
            Director = HS.Rapport.OpportunityDirector.Create(ctx, Hero);
            Stones = StoneSystem.Create(ctx, Hero);
            var skills = Sidekick.GetComponent<SidekickSkills>();
            if (skills != null) skills.StartingSkills = StartingSkills;

            Camera = CameraRig.Build(Hero.transform.Find("CamTarget") ?? Hero.transform, Sidekick.transform.Find("CamTarget") ?? Sidekick.transform, ctx.Tuning.camera);
            Camera.TravelDirection = () =>
            {
                if (Hero == null || Hero.Route.AtEnd) return Vector3.forward;
                var d = Hero.Route.Current.Position - Hero.Position;
                d.y = 0f;
                return d.sqrMagnitude > 1f ? d.normalized : Vector3.forward;
            };
            HS.Audio.AudioDirector.Ensure().OnFlow("Chapter");
            var ui = HS.UI.UIRoot.Ensure();
            Hud = HS.UI.HudView.Create(ui);
            Hud.InsightOn = QaInsight;
            HS.UI.BarkView.Create(ui);
            HS.UI.SystemWindow.Create(ui);
            HS.UI.ThreatIndicators.Create(ui);
            HS.UI.HitFeedback.Create(ui, Hud);
            _streamer = gameObject.AddComponent<RoomStreamer>();
            _streamer.Chapter = Chapter;
            _streamer.Focus = Hero.transform;
        }

        /// <summary>A chapter: its rooms, theme, route and encounters. The pair is placed at the road's start.</summary>
        public void BuildChapter(int chapter, int seed)
        {
            var assets = GameAssets.Load();
            var ctx = RunContext.Current;
            ctx.Chapter = chapter;
            ctx.Seed = seed;
            if (Director != null) Director.Ledger.Chapter = chapter;
            Def = ChapterDef.For(chapter);
            Chapter.Build(seed, Def);
            ChapterTheme.Apply(Def.Theme);
            Hero.Motor.Teleport(new Vector3(0f, 0.05f, -1.5f));
            Hero.Motor.FaceInstant(Vector3.forward);
            Sidekick.Motor.Teleport(new Vector3(-1.6f, 0.05f, -1.9f));
            Sidekick.Motor.FaceInstant(Vector3.forward);
            Hero.Route.SetNodes(Chapter.ChapterRoute());
            Encounters = new GameObject("Encounters").AddComponent<EncounterDirector>();
            Encounters.Chapter = Chapter;
            Encounters.EnemyPrefab = assets.Enemy;
            Encounters.SpawnAll();
            _streamer?.Reset();
        }

        /// <summary>Between chapters, at black: everything of the old road goes, now (not at the end of the frame).</summary>
        public void TeardownChapter()
        {
            if (Encounters != null)
            {
                Encounters.gameObject.SetActive(false);
                Destroy(Encounters.gameObject);
                Encounters = null;
            }
            foreach (var p in FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) p.ClearAll();
            Chapter.Clear();
        }
```
Add `using HS.Presentation;` (already imported) and `using HS.Rooms;` (already imported).

`Rooms/RoomStreamer.cs`: add `public void Reset() => _last = -99;`.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh PlayMode "CampaignTests|ChapterBuildTests|CampfireTests|RiggedDuelTests|CallumTests"` → Expected: no new failures.

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: the run is built once; chapters are rebuilt in place"
```

---

### Task 5: XP pots by chapter

**Files:**
- Modify: `Flow/XpTracker.cs`
- Test: `Tests/EditMode/CampaignScheduleTests.cs`

**Interfaces:**
- Produces: `XpTracker.BeginChapter(System.Func<int, int> potForRoom)`.

- [ ] **Step 1: Write the failing test**

Append to `CampaignScheduleTests`:
```csharp
        [Test]
        public void Xp_Pots_Count_Each_Chapter_Afresh()
        {
            var go = new UnityEngine.GameObject("RunContext");
            try
            {
                var ctx = go.AddComponent<RunContext>();
                ctx.Init();
                var xp = new XpTracker();
                xp.Bind(ctx);
                xp.BeginChapter(r => 100);
                ctx.Events.RoomEntered(0);
                ctx.Events.RoomCleared(0);
                Assert.AreEqual(50, xp.Xp, "half the pot for the clear");
                xp.BeginChapter(r => 180);
                ctx.Events.RoomEntered(0);
                ctx.Events.RoomCleared(0);
                Assert.AreEqual(140, xp.Xp, "room 0 of the next chapter pays its own pot");
                ctx.Events.Explored(0, "cache");
                Assert.AreEqual(185, xp.Xp, "a quarter for exploring");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (SimLoop.Instance) UnityEngine.Object.DestroyImmediate(SimLoop.Instance.gameObject);
            }
        }
```

- [ ] **Step 2: Run and see it fail**

Run: `tools/unity-tests.sh EditMode CampaignScheduleTests` → Expected: compile error (`BeginChapter`).

- [ ] **Step 3: Implement** (in `Flow/XpTracker.cs`)

```csharp
        System.Func<int, int> _pot = r => PotPerRoom;

        /// <summary>A new chapter: room indices start again at 0, and its rooms pay this chapter's pots.</summary>
        public void BeginChapter(System.Func<int, int> potForRoom)
        {
            _cleared.Clear();
            _assisted.Clear();
            _explored.Clear();
            _currentRoom = -1;
            _pot = potForRoom ?? (r => PotPerRoom);
        }
```
Change the three award calls in `Bind` to pass shares: `Award(_cleared, r, 0.5f, "clear")`, `Award(_assisted, _currentRoom, 0.25f, "assist")` (both places), `Award(_explored, r >= 0 ? r : _currentRoom, 0.25f, "explore")`, and `Award`:
```csharp
        void Award(HashSet<int> set, int room, float share, string reason)
        {
            if (room < 0 || room >= 100 || !set.Add(room)) return;
            int amount = Mathf.RoundToInt(_pot(room) * share);
            if (amount <= 0) return;
            Xp += amount;
            Awarded?.Invoke(amount, reason);
        }
```
Add `using UnityEngine;` at the top.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh EditMode` → Expected: no failures.

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: XP pots per chapter"
```

---

### Task 6: Five and six skill slots (input, glyphs, HUD)

**Files:**
- Modify: `Core/GameInput.cs` (6 skill actions)
- Modify: `Tutorial/KeyGlyphs.cs` (`"skills"` label)
- Modify: `UI/HudView.cs` (6 slots built; only `SlotCount` shown; bar width follows)
- Test: `Tests/PlayMode/UiTests.cs`

**Interfaces:**
- Produces: `GameInput.Skills` length 6; `HudView.VisibleSlots`.

- [ ] **Step 1: Write the failing test** (append to `UiTests`, which already uses `TestUi`)

```csharp
        [UnityTest]
        public IEnumerator The_Skill_Bar_Grows_To_Six_Slots()
        {
            var flow = TestUi.StartChapterAsPlayer("pocket_sand");
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.Chapter, 5f, "the road");
            var skills = flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>();
            Assert.AreEqual(4, flow.Chapter.Hud.VisibleSlots);
            skills.System.SetChapter(3);
            yield return null;
            Assert.AreEqual(6, flow.Chapter.Hud.VisibleSlots);
            Assert.AreEqual(6, HS.Core.GameInput.Instance.Skills.Length);
            Assert.AreEqual("6", HS.Tutorial.KeyGlyphs.Label("skill6", HS.Tutorial.GlyphDevice.Keyboard));
        }
```

- [ ] **Step 2: Run and see it fail**

Run: `tools/unity-tests.sh PlayMode UiTests` → Expected: compile error (`VisibleSlots`).

- [ ] **Step 3: Implement**

`Core/GameInput.cs`: `public readonly InputAction[] Skills = new InputAction[6];` and
```csharp
            string[] keys = { "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6" };
            string[] pads = { "<Gamepad>/rightTrigger", "<Gamepad>/rightShoulder", "<Gamepad>/leftTrigger", "<Gamepad>/leftShoulder", "<Gamepad>/dpad/left", "<Gamepad>/dpad/right" };
            for (int i = 0; i < Skills.Length; i++)
```
Update the class summary comment to "1-6 skills … RT/RB/LT/LB, D-pad ←/→ skills 1-6". (`Navigate` keeps `<Gamepad>/dpad` for UI; gameplay and UI maps are never enabled together.)

`Tutorial/KeyGlyphs.cs`: `case "skills": return pad ? "RT RB LT LB ◄ ►" : "1–6";`

`UI/HudView.cs`: build six slots; show `SlotCount` of them:
```csharp
        const int MaxSlots = 6;
        int _shownSlots = -1;
        public int VisibleSlots => _shownSlots;

        void BuildSkillBar(RectTransform root)
        {
            float barW = 4 * SlotSize + 3 * SlotGap;
            _skillsRt = At(root, "Skills", new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(barW, SlotSize + 24f));
            _skillsRt.pivot = new Vector2(0.5f, 0f);
            _skillsRt.anchoredPosition = new Vector2(0f, 20f);
            _slots = new Slot[MaxSlots];
            for (int i = 0; i < MaxSlots; i++) _slots[i] = BuildSlot(_skillsRt, i);
            ShowSlots(4);
            // ... the verbs code continues unchanged, but position the verbs from a field:
        }

        RectTransform _verbsRt;

        void ShowSlots(int n)
        {
            if (n == _shownSlots) return;
            _shownSlots = n;
            float barW = n * SlotSize + (n - 1) * SlotGap;
            _skillsRt.sizeDelta = new Vector2(barW, _skillsRt.sizeDelta.y);
            for (int i = 0; i < _slots.Length; i++) _slots[i].Root.gameObject.SetActive(i < n);
            if (_verbsRt != null) _verbsRt.anchoredPosition = new Vector2(-barW * 0.5f - BarGap, 20f);
        }
```
In `BuildSkillBar`, assign `_verbsRt = vr;` after creating `vr`. `Slot` needs a `Root` (`RectTransform`) field: set it in `BuildSlot` to the slot's tile rect (the rect `BuildSlot` creates with `At(...)`). In the per-frame update (where `UpdateSlot` is called for each slot), before the loop: `ShowSlots(skills != null ? skills.System.SlotCount : 4);`. Slot positions in `BuildSlot` are left-anchored at `i * (SlotSize + SlotGap)`, so they stay put as the bar widens.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh PlayMode "UiTests|TutorialTests|HitFeedbackTests"` → Expected: no new failures.

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: five and six skill slots (keys 5-6, D-pad left/right)"
```

---

### Task 7: The campfire, per chapter

**Files:**
- Create: `Flow/CampfireScenes.cs`
- Modify: `Flow/CampfireDirector.cs` (`Options`, `Variant`, the check from options, lines from `CampfireScenes`, the continue label)
- Test: `Tests/PlayMode/CampfireTests.cs`

**Interfaces:**
- Consumes: `StageEvaluator.Evaluate`, `CampaignSchedule`.
- Produces: `CampfireVariant { Cold, Neutral, Warm }`, `CampfireScenes.VariantFor(int chapter, Stage stage, bool penalisedThisChapter)`, `CampfireScenes.Lines(int chapter, CampfireVariant v, bool sawDishonour)`, `CampfireDirector.Options`, `CampfireDirector.Begin(RoomModule, HeroAgent, SidekickAgent, Options)`, `CampfireDirector.Variant`.

- [ ] **Step 1: Write the failing tests** (append to `CampfireTests`)

```csharp
        CampfireDirector CampAt(HeroAgent hero, SidekickAgent sk, RoomModule room, int chapter, float earned, float offered)
        {
            var dir = OpportunityDirector.Create(Ctx, hero);
            _extra.Add(dir.gameObject);
            dir.Ledger.Chapter = chapter;
            for (int i = 0; i < offered; i++)
            {
                var m = dir.Ledger.Offer("unseen_assist", 1f);
                if (i < earned) dir.Ledger.Capture(m);
            }
            var c = new GameObject("Campfire").AddComponent<CampfireDirector>();
            _extra.Add(c.gameObject);
            c.AutoSceneSeconds = 0.05f;
            var rules = CampaignSchedule.For(chapter);
            c.Begin(room, hero, sk, new CampfireDirector.Options { Chapter = chapter, Check = rules.CampCheck, AutoPicks = true });
            return c;
        }

        [UnityTest]
        public IEnumerator Chapter_1_Has_No_Check_Chapter_3_Allows_S2()
        {
            var (hero, sk, room) = Stage();
            yield return null;
            var c1 = CampAt(hero, sk, room, 1, 9f, 10f);
            Assert.AreEqual(HS.Core.Stage.S0, hero.Stage, "no Stage check at the end of chapter 1");
            Assert.AreEqual(CampfireVariant.Neutral, c1.Variant);
            TearDown();
            SetUp();
            (hero, sk, room) = Stage();
            yield return null;
            hero.ApplyStage(HS.Core.Stage.S1);
            var c3 = CampAt(hero, sk, room, 3, 6f, 10f);
            Assert.AreEqual(HS.Core.Stage.S2, hero.Stage, "60% at the chapter 3 check: S2");
            Assert.AreEqual(CampfireVariant.Warm, c3.Variant);
        }

        [Test]
        public void Every_Chapter_And_Variant_Has_Lines_Without_Spoilers()
        {
            foreach (int ch in new[] { 1, 2, 3, 4 })
            foreach (CampfireVariant v in System.Enum.GetValues(typeof(CampfireVariant)))
            {
                var lines = CampfireScenes.Lines(ch, v, false);
                Assert.GreaterOrEqual(lines.Length, 3, $"ch{ch} {v}");
                foreach (var l in lines)
                {
                    StringAssert.Contains(l.Contains("~") ? "~" : "|", l);
                    foreach (var banned in new[] { "Rapport", "Stage", "Moment", "points" }) StringAssert.DoesNotContain(banned, l);
                }
            }
        }
```

- [ ] **Step 2: Run and see them fail**

Run: `tools/unity-tests.sh PlayMode CampfireTests` → Expected: compile errors.

- [ ] **Step 3: Implement**

```csharp
// Flow/CampfireScenes.cs
using HS.Core;

namespace HS.Flow
{
    public enum CampfireVariant { Cold, Neutral, Warm }

    /// <summary>
    /// The fireside scene per chapter and variant (GDD §4.6: warm, neutral or cold by Stage and recent penalties; §6.1
    /// campfire beats). Lines: "speaker|text" for a bark, "sidekick~text" for her thought. Draft copy for the owner.
    /// </summary>
    public static class CampfireScenes
    {
        public static CampfireVariant VariantFor(int chapter, Stage stage, bool penalisedThisChapter)
        {
            switch (chapter)
            {
                case 1: return penalisedThisChapter ? CampfireVariant.Cold : CampfireVariant.Neutral;
                case 2: return stage >= Stage.S1 ? CampfireVariant.Warm : CampfireVariant.Cold;
                default: return stage >= Stage.S2 ? CampfireVariant.Warm : stage == Stage.S1 ? CampfireVariant.Neutral : CampfireVariant.Cold;
            }
        }

        const string Code = "\"Strike the ready. Spare the yielded. Never the back.\"";

        public static string[] Lines(int chapter, CampfireVariant v, bool sawDishonour)
        {
            switch (chapter)
            {
                case 1:
                    return v == CampfireVariant.Cold
                        ? new[] { "callum|We camp here. I'll take the first watch.", "callum|" + Code + " The Code. Learn it.", sawDishonour ? "callum|And keep your sand in your pockets." : "callum|Fortune favoured us today.", "sidekick~He hasn't looked at me once." }
                        : new[] { "callum|We camp here. The road was kind today.", "callum|" + Code + " My father's words. Learn them; they'll keep you alive.", "callum|Fortune favoured us.", "sidekick~Fortune. Sure." };
                case 2:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Sit. The fire's big enough for two.", "callum|" + Code + " Tonight they feel lighter.", "callum|That archer in the trees fell before he could loose. ...You were near him, weren't you.", "sidekick~Was that almost a thank-you?" }
                        : new[] { "callum|Did you see that archer fall? Fortune favours the just.", "callum|The wood is hungry. So am I. Keep the rations coming.", "callum|First watch is mine.", "sidekick~Fortune, again." };
                case 3:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Down there, when the robber went down behind me...", "callum|I was looking at the sky.", "callum|Some things a knight does not need to see. Sit closer; the crypt is cold.", "sidekick~He looked away. On purpose." }
                        : v == CampfireVariant.Neutral
                            ? new[] { "callum|The dead keep strange company. We were lucky in that vault.", "callum|Lucky more than once, now that I count.", "callum|Get some sleep. I'll wake you.", "sidekick~He's counting." }
                            : new[] { "callum|Luck carried us through those tombs.", "callum|" + Code + " That is what carried us. That, and luck.", "callum|I'll take the watch.", "sidekick~It wasn't luck." };
                default:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Tomorrow, the Gallery.", "callum|Stand where I can't see you. Whatever you do there... I'd rather not have to judge it.", "callum|And come back.", "sidekick~He's asking me to cheat. Kind of." }
                        : v == CampfireVariant.Neutral
                            ? new[] { "callum|Tomorrow, the Gallery.", "callum|Whoever waits there has studied me. I can feel it.", "callum|Stay close. Not too close.", "sidekick~Close. Not too close. Got it." }
                            : new[] { "callum|Tomorrow, the Gallery. Single combat, I expect.", "callum|Stay out of it. Whatever happens.", "callum|" + Code, "sidekick~He still thinks this is a fair fight." };
            }
        }
    }
}
```

`Flow/CampfireDirector.cs`:
```csharp
        public sealed class Options
        {
            public int Chapter = 2;
            /// <summary>The hidden Stage check this campfire runs (null: none).</summary>
            public HS.Rapport.StageCheck? Check = HS.Rapport.StageCheck.Chapter2;
            public int Picks;
            public bool AutoPicks;
            public string[] AutoPickIds = new string[0];
            public string ContinueLabel = "CONTINUE";
        }

        public CampfireVariant Variant { get; private set; }
        public bool Warm => Variant == CampfireVariant.Warm;

        /// <summary>The slice's call: its check acts as the chapter 2 one (max S1).</summary>
        public void Begin(RoomModule camp, HeroAgent hero, SidekickAgent sk, int picks, bool autoPicks, string[] autoPickIds) =>
            Begin(camp, hero, sk, new Options { Chapter = 2, Check = HS.Rapport.StageCheck.Chapter2, Picks = picks, AutoPicks = autoPicks, AutoPickIds = autoPickIds ?? new string[0] });
```
Rewrite the start of `Begin` (now `Begin(RoomModule camp, HeroAgent hero, SidekickAgent sk, Options o)`):
```csharp
            var ctx = RunContext.Current;
            _hero = hero;
            _sk = sk;
            _options = o;
            var ledger = ctx.Get<RapportLedger>();
            // 1) The hidden Stage check (only the chapters that have one).
            if (o.Check.HasValue)
            {
                float rate = ledger != null ? ledger.CaptureRate : 0f;
                hero.ApplyStage(StageEvaluator.Evaluate(hero.Stage, rate, o.Check.Value));
            }
            StageAfter = hero.Stage;
            bool penalised = ledger != null && ledger.RawPenaltiesIn(o.Chapter) > 0f;
            Variant = CampfireScenes.VariantFor(o.Chapter, StageAfter, penalised);
            var cm = hero.Module as CallumModule;
            _sawDishonour = cm != null && cm.Honor < cm.T.honorMax;
```
Then use `Variant` for placement and the fire: warm — by the fire facing the sidekick; neutral — by the fire facing the fire; cold — apart, facing the dark:
```csharp
            var heroPos = Variant == CampfireVariant.Cold ? heroSeat.position + (heroSeat.position - fire.position).normalized * 2.6f : heroSeat.position;
            ...
            var face = Variant == CampfireVariant.Warm ? Geo.DirTo(heroPos, skSeat.position)
                : Variant == CampfireVariant.Neutral ? Geo.DirTo(heroPos, fire.position) : Geo.DirTo(fire.position, heroPos);
            ...
            float fireScale = Variant == CampfireVariant.Warm ? 1.25f : Variant == CampfireVariant.Neutral ? 1f : 0.7f;
            _flame = HS.Presentation.Vfx.FireAt(fire, fireScale);
            _light.range = 5f + 4f * (fireScale - 0.7f) / 0.55f;
            _light.intensity = 1.4f + 1.8f * (fireScale - 0.7f) / 0.55f;
```
Lines: `_lines = CampfireScenes.Lines(o.Chapter, Variant, _sawDishonour);` Picks: replace `picks`, `autoPicks`, `autoPickIds` with `o.Picks`, `o.AutoPicks`, `o.AutoPickIds`. In `SkipScene`, the picker's continue label becomes `_options.ContinueLabel`. Add the field `Options _options;`.

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh PlayMode CampfireTests` → Expected: all pass (the slice tests use the old overload).

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: a campfire per chapter (checks at 2 and 3, cold/neutral/warm scenes)"
```

---

### Task 8: GameFlow runs the campaign; Restore Points per chapter

**Files:**
- Modify: `Flow/RunState.cs` (chapter-indexed starts, the door, `Resolve`)
- Modify: `Flow/GameFlow.cs` (the loop; `StartChapter`, `StartStage`, `StopAfterChapter`, `CurrentChapter`; the door; restore buttons)
- Modify: `Tests/PlayMode/UiQaCaptures.cs` (`Duel_Lesson_And_Boss_Bar` resumes from the door)
- Test: `Tests/PlayMode/CampaignTests.cs`, `Tests/EditMode/CampaignScheduleTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `GameFlow.StartChapter`, `GameFlow.StartStage`, `GameFlow.StopAfterChapter`, `GameFlow.CurrentChapter`, `GameFlow.AtDoor`; `RunState.Point.Chapter`, `RunState.SetChapterStart(int, Point)`, `RunState.ChapterStartOf(int)`, `RunState.Door`, `RunState.Resolve(string)`, `RunState.ForgetAfter(int)`; Outcome `"chapter_done"`.

- [ ] **Step 1: Write the failing tests**

EditMode (append to `CampaignScheduleTests`):
```csharp
        [Test]
        public void Restore_Points_Resolve_By_Name()
        {
            RunState.Clear();
            var p2 = new RunState.Point();
            var p3 = new RunState.Point();
            var door = new RunState.Point { Chapter = 5 };
            RunState.SetChapterStart(2, p2);
            RunState.SetChapterStart(3, p3);
            RunState.Door = door;
            Assert.AreSame(p3, RunState.ChapterStart, "the latest chapter start");
            Assert.AreSame(p2, RunState.Resolve("chapter:2"));
            Assert.AreSame(p3, RunState.Resolve("chapter"));
            Assert.AreSame(door, RunState.Resolve("door"));
            Assert.AreEqual(3, p3.Chapter);
            RunState.ForgetAfter(2);
            Assert.IsNull(RunState.ChapterStartOf(3), "restoring to chapter 2 forgets the later timeline");
            Assert.IsNull(RunState.Door);
            RunState.Clear();
        }
```

PlayMode (append to `CampaignTests`):
```csharp
        static GameFlow Flow(int startChapter = 1, int stopAfter = 0)
        {
            var go = new GameObject("GameFlow");
            go.SetActive(false);
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            flow.Fast = true;
            flow.StartChapter = startChapter;
            flow.StopAfterChapter = stopAfter;
            go.SetActive(true);
            return flow;
        }

        /// <summary>Skip the road: no bandits, the hero at its end (exercises the loop, not the fights).</summary>
        static void SkipRoad(GameFlow flow)
        {
            foreach (var e in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            var hero = flow.Chapter.Hero;
            hero.Route.SetNodes(new System.Collections.Generic.List<RouteNode>());
            hero.Motor.Teleport(new Vector3(0f, 0.05f, flow.Chapter.Chapter.ChapterLength - 2f));
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator The_Campaign_Runs_Chapter_To_Chapter_Into_The_Gallery()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow();
            yield return null;
            flow.Chapter.Hero.Health.Invulnerable = true;
            var seen = new System.Collections.Generic.List<int>();
            for (int ch = 1; ch <= 5; ch++)
            {
                yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.Chapter && flow.CurrentChapter == ch, 20f, "chapter " + ch);
                seen.Add(flow.CurrentChapter);
                var skills = flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>();
                Assert.AreEqual(CampaignSchedule.For(ch).Slots, skills.System.SlotCount, "slots in chapter " + ch);
                Assert.AreEqual(260f * Mathf.Pow(2f, ch - 1), flow.Chapter.Hero.Health.BaseMax, 0.01f, "his HP tier in chapter " + ch);
                Assert.IsNotNull(RunState.ChapterStartOf(ch), "a Restore Point at chapter " + ch);
                SkipRoad(flow);
            }
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.Duel, 20f, "the door, then the boss");
            Assert.IsNotNull(RunState.Door, "a Restore Point before the door");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, seen);
            flow.Duel.Ashgrave.TakeDamage(DamageInfo.Make(flow.Chapter.Hero, flow.Duel.Ashgrave, 1e9f, DamageKind.Blade, "sword"));
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 10f, "the end");
            Assert.AreEqual("won", flow.Outcome);
        }

        [UnityTest]
        public IEnumerator Starting_At_Chapter_4_Skips_The_Opening_With_A_Fitting_Kit()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(4);
            yield return null;
            Assert.AreEqual(GameFlow.State.Chapter, flow.Current);
            Assert.AreEqual(4, RunContext.Current.Chapter);
            Assert.AreEqual(CampaignSchedule.LevelTarget(3), flow.Chapter.Sidekick.Level);
            Assert.AreEqual(6, flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>().System.SlotCount);
            Assert.AreEqual(260f * 8f, flow.Chapter.Hero.Health.BaseMax, 0.01f);
            Assert.IsTrue(RenderSettings.fog);
        }

        [UnityTest]
        public IEnumerator A_Chapter_3_Restore_Point_Brings_Back_Chapter_3()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(3);
            yield return null;
            var point = RunState.ChapterStartOf(3);
            Assert.IsNotNull(point);
            Assert.AreEqual(3, point.Chapter);
            var hero = flow.Chapter.Hero;
            hero.ApplyStage(HS.Core.Stage.S2);
            var restored = flow.Snapshot();
            Assert.AreEqual(3, restored.Chapter);
            flow.Apply(point);
            Assert.AreEqual(HS.Core.Stage.S0, hero.Stage);
            Assert.AreEqual(6, flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>().System.SlotCount);
        }

        [UnityTest]
        public IEnumerator Stop_After_Chapter_Ends_The_Run_At_Its_Campfire()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(1, 1);
            yield return null;
            SkipRoad(flow);
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 20f, "the end of chapter 1");
            Assert.AreEqual("chapter_done", flow.Outcome);
        }
```

- [ ] **Step 2: Run and see them fail**

Run: `tools/unity-tests.sh EditMode CampaignScheduleTests` and `tools/unity-tests.sh PlayMode CampaignTests` → Expected: compile errors (`SetChapterStart`, `StartChapter`, …).

- [ ] **Step 3: Implement `RunState`**

```csharp
    public static class RunState
    {
        public sealed class Point
        {
            public int Chapter = 1;
            public int Seed;
            public int Level;
            public int Xp;
            public List<(string id, int rank)> Skills = new List<(string, int)>();
            public List<string> Loadout = new List<string>();
            public Stage HeroStage;
            public HS.Rapport.RapportLedger.State Ledger;
            public (int relayed, int forged) Intel;
            public float HeroHp = -1f;
            public List<HS.Hero.WoundType> Wounds = new List<HS.Hero.WoundType>();
        }

        static readonly Point[] Starts = new Point[6]; // [1..5]
        /// <summary>Before the Gallery door (after the chapter 5 approach).</summary>
        public static Point Door;
        /// <summary>Set before reloading the scene: "chapter:N", "chapter" (the latest), or "door".</summary>
        public static string Resume;
        public static int Runs;

        public static Point ChapterStartOf(int chapter) => chapter >= 1 && chapter <= 5 ? Starts[chapter] : null;

        public static void SetChapterStart(int chapter, Point p)
        {
            p.Chapter = chapter;
            Starts[UnityEngine.Mathf.Clamp(chapter, 1, 5)] = p;
        }

        /// <summary>The latest chapter start reached (tests and the tutorial's resume set it directly).</summary>
        public static Point ChapterStart
        {
            get
            {
                for (int c = 5; c >= 1; c--) if (Starts[c] != null) return Starts[c];
                return null;
            }
            set
            {
                if (value != null) SetChapterStart(value.Chapter, value);
            }
        }

        public static Point Resolve(string resume)
        {
            if (resume == "door") return Door;
            if (resume == "chapter") return ChapterStart;
            if (resume != null && resume.StartsWith("chapter:") && int.TryParse(resume.Substring(8), out int ch)) return ChapterStartOf(ch);
            return null;
        }

        /// <summary>Restoring to chapter N: the later timeline (its chapter starts, the door) is gone.</summary>
        public static void ForgetAfter(int chapter)
        {
            for (int c = chapter + 1; c <= 5; c++) Starts[c] = null;
            Door = null;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            Runs = 0;
        }

        public static void Clear()
        {
            for (int c = 0; c < Starts.Length; c++) Starts[c] = null;
            Door = null;
            Resume = null;
        }
    }
```

- [ ] **Step 4: Implement `GameFlow`**

New fields and properties:
```csharp
        [Tooltip("QA / Tools/HS/Play from chapter: start the run here with a preset kit (skips the opening).")]
        public int StartChapter = 1;
        public Stage StartStage = Stage.S0;
        [Tooltip("Harness: 0 plays the whole campaign; N ends the run at chapter N's campfire (outcome chapter_done).")]
        public int StopAfterChapter;
        public int CurrentChapter => _ctx != null ? _ctx.Chapter : 1;
        public bool AtDoor { get; private set; }
        ChapterRules Rules => CampaignSchedule.For(CurrentChapter);
```

`Start()` (after `ReadCommandLine` / `ModalGate.Clear()`):
```csharp
            _ctx = RunContext.Current;
#if UNITY_EDITOR
            int fromMenu = UnityEditor.SessionState.GetInt(PlayFromChapterKey, 0);
            if (fromMenu > 0)
            {
                UnityEditor.SessionState.EraseInt(PlayFromChapterKey);
                StartChapter = fromMenu;
            }
#endif
            string resume = RunState.Resume;
            RunState.Resume = null;
            var point = RunState.Resolve(resume);
            Chapter = gameObject.AddComponent<ChapterBootstrap>();
            Chapter.AutoBuild = false;
            Chapter.Seed = point != null ? point.Seed : Seed;
            Chapter.SidekickBot = false;
            Chapter.StartingSkills = new string[0];
            Xp = new XpTracker();
            Xp.Bind(_ctx);
            _ctx.Register(Xp);
            _ctx.Register(this);
            Chapter.BuildRun();
            PrepareChapter(point != null ? point.Chapter : Mathf.Clamp(StartChapter, 1, CampaignSchedule.Chapters));
            ScreenFade.Ensure(UIRoot.Ensure());
            // ... the AutoPlay bot, tutorial, pause menu, damage/isolation hooks, RunState.Runs++ — unchanged ...
            if (point != null)
            {
                Chapter.Hud.MeetHero("CALLUM", true);
                Apply(point);
                if (resume == "door") StartDuel();
                else
                {
                    RunState.ForgetAfter(point.Chapter);
                    EnterChapter(false);
                }
                return;
            }
            RunState.Clear();
            if (CurrentChapter > 1)
            {
                ApplyPreset();
                EnterChapter(true);
                return;
            }
            BeginOpening();
```
(Remove the old `Xp = new XpTracker(); …` lines further down, now created above; remove `Chapter.Build()`.)

```csharp
        public const string PlayFromChapterKey = "hs.playFromChapter";

        /// <summary>Build chapter N in place and set everything the schedule says about it.</summary>
        void PrepareChapter(int ch)
        {
            var rules = CampaignSchedule.For(ch);
            Chapter.BuildChapter(ch, Chapter.Seed);
            Sk.GetComponent<SidekickSkills>().System.SetChapter(ch);
            if (Hero.Module is HS.Hero.Callum.CallumModule cm) cm.ApplyChapter(ch, rules.Unlocks, rules.Recovery);
            var rooms = Chapter.Chapter.Rooms;
            Xp.BeginChapter(r => CampaignSchedule.RoomPot(ch, rooms[Mathf.Clamp(r, 0, rooms.Count - 1)].XpPot));
            AtDoor = false;
        }

        /// <summary>A run started past chapter 1 (QA, "Play from chapter N"): the kit a thorough player would have by now.</summary>
        void ApplyPreset()
        {
            var skills = Sk.GetComponent<SidekickSkills>();
            skills.System.AtCamp = true;
            var ids = AutoPlay ? OpeningPicks.Concat(CampPicks) : HS.Bots.BotFactory.Build("supportive").opening.Concat(HS.Bots.BotFactory.Build("supportive").camp);
            foreach (var id in ids) skills.Learn(id);
            skills.System.AtCamp = false;
            int level = CampaignSchedule.LevelTarget(CurrentChapter - 1);
            Sk.SetLevel(level, true);
            Xp.Restore(XpTracker.Thresholds[Mathf.Clamp(level - 2, 0, XpTracker.Thresholds.Length - 1)]);
            Hero.ApplyStage(StartStage);
            Chapter.Hud.MeetHero("CALLUM", true);
        }
```
(add `using System.Linq;`).

`EnterChapter`:
```csharp
        void EnterChapter(bool snapshot)
        {
            if (snapshot) RunState.SetChapterStart(CurrentChapter, Snapshot());
            _levelAtChapterStart = Sk.Level;
            SetState(State.Chapter);
            SimLoop.Instance.Paused = false;
            _ctx.Events.RaiseNotice($"PARTY: SIR CALLUM <size=80%>(Hero)</size>  ·  YOU <size=80%>(HERO's SIDEKICK)</size>\nQUEST: {Rules.Name}.");
            if (snapshot && CurrentChapter == 1 && StartChapter <= 1) StartCoroutine(MeetCallum());
        }
```

`Update`:
```csharp
            if (Current == State.Chapter && !AtDoor)
            {
                if (!Hero.IsAlive || !Sk.IsAlive)
                {
                    EndRun(false, CuratorDiagnosis.ForRoad(LastHitTag, !Sk.IsAlive), !Sk.IsAlive);
                    return;
                }
                if (Hero.Route.AtEnd && Hero.Position.z > Chapter.Chapter.ChapterLength - 6f)
                {
                    if (Chapter.Def.Final) StartCoroutine(ToDoor());
                    else ToCamp();
                }
            }
```

`ToCamp` (replace the camp creation part):
```csharp
                int levelNow = XpTracker.LevelFor(Xp.Xp);
                int cap = CampaignSchedule.LevelTarget(4);
                int picks = Sk.Level >= cap ? 0 : Mathf.Min(cap - _levelAtChapterStart, Mathf.Max(1, levelNow - _levelAtChapterStart));
                Sk.SetLevel(_levelAtChapterStart + picks, true);
                Camp = new GameObject("Campfire").AddComponent<CampfireDirector>();
                if (Fast) Camp.AutoSceneSeconds = 0f;
                int ch = CurrentChapter;
                Camp.Finished += () =>
                {
                    if (StopAfterChapter == ch)
                    {
                        EndRun(true, new List<string> { $"CHAPTER {ch}: {Rules.Name.ToUpperInvariant()} — CLEARED." }, false, null, "chapter_done");
                        return;
                    }
                    Transition(NextChapter);
                };
                var next = CampaignSchedule.For(ch + 1);
                Camp.Begin(Chapter.Chapter.Campfire, Hero, Sk, new CampfireDirector.Options
                {
                    Chapter = ch, Check = Rules.CampCheck, Picks = picks, AutoPicks = AutoPlay, AutoPickIds = CampPicks,
                    ContinueLabel = $"CONTINUE  »  {next.Name.ToUpperInvariant()}",
                });
                SimLoop.Instance.Paused = false;
```

```csharp
        void NextChapter()
        {
            if (Camp != null) Destroy(Camp.gameObject);
            Camp = null;
            Chapter.TeardownChapter();
            PrepareChapter(CurrentChapter + 1);
            EnterChapter(true);
        }

        static readonly string[] DoorLines =
        {
            "Stay behind me. This is my fight.",
            "Whatever is behind this door... keep your distance. And your eyes open.",
            "If I don't look back in there, it isn't because I've forgotten you.",
            "Whatever comes, I will not lie about who helped me.",
        };

        /// <summary>The Gallery door (GDD §11.3.4: "the hero's own line at the door"): the last Stage check, a Restore Point.</summary>
        System.Collections.IEnumerator ToDoor()
        {
            AtDoor = true;
            var ledger = _ctx.Get<RapportLedger>();
            Hero.ApplyStage(StageEvaluator.Evaluate(Hero.Stage, ledger != null ? ledger.CaptureRate : 0f, StageCheck.Door));
            _ctx.Events.RaiseBark("callum", DoorLines[(int)Hero.Stage], 3.6f, 3);
            if (!Fast) yield return new WaitForSeconds(3.8f);
            RunState.Door = Snapshot();
            Hero.Route.SetNodes(new List<RouteNode>());
            Transition(StartDuel);
        }
```
In `StartDuel`, keep the body; it already uses `Chapter.Chapter.Boss` (built for the Gallery). Its `Won` branch becomes:
```csharp
                    EndRun(true, new List<string> { "CHAPTER 5: THE GALLERY — CLEARED." }, false, quote);
```

`EndRun` gains an outcome override and the restore buttons:
```csharp
        void EndRun(bool won, List<string> diagnosis, bool sidekickDied, string quote = null, string outcome = null)
        {
            if (Current == State.End) return;
            SetState(State.End);
            Outcome = outcome ?? (won ? "won" : sidekickDied ? "sidekick_died" : "hero_died");
            ...
                Title = outcome == "chapter_done" ? diagnosis[0] : won ? "THE GALLERY — WON" : sidekickDied ? "SIDEKICK: DECEASED. NO RECALL AVAILABLE." : "HERO: CALLUM. DECEASED",
            ...
            if (won) m.Buttons.Add(("PLAY AGAIN · NEW ROAD", PlayAgain));
            else
            {
                if (RunState.Door != null) m.Buttons.Add(("RESTORE · BEFORE THE DOOR", () => Restore("door")));
                for (int c = CurrentChapter; c >= 1; c--)
                {
                    int chapter = c;
                    if (RunState.ChapterStartOf(chapter) != null)
                        m.Buttons.Add(($"RESTORE · CHAPTER {chapter} START", () => Restore("chapter:" + chapter)));
                }
            }
```

`Snapshot()` sets `Chapter = CurrentChapter` in the `Point` initializer.

`ClearEnemies` stays as is (called at `ToCamp`).

Update `UiQaCaptures.Duel_Lesson_And_Boss_Bar`: build the point with `Chapter = 5`, set `RunState.SetChapterStart(5, point); RunState.Door = point; RunState.Resume = "door";` (replacing the `ChapterStart`/`Campfire`/`"campfire"` lines), and the camp picker label line to `"CONTINUE  »  WHISPERWOOD"`.

- [ ] **Step 5: Run the tests**

Run: `tools/unity-tests.sh EditMode` → Expected: no failures.
Run: `tools/unity-tests.sh PlayMode` → Expected: the new `CampaignTests` pass; no failures beyond the baseline six (the two `HarnessTests` may now pass or fail differently — Task 9 settles them).

- [ ] **Step 6: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: GameFlow runs five chapters, the Gallery door and the boss; Restore Points per chapter"
```

---

### Task 9: Everything that assumed one chapter

**Files:**
- Modify: `UI/PauseMenu.cs` (subtitle: the chapter's name; "The Gallery" in the boss)
- Modify: `Tutorial/Lessons.cs` (the "restore" lesson text)
- Modify: `QA/BalanceHarness.cs` (`StartChapter`, `StopAfterChapter`, a `Chapter` column)
- Modify: `Tests/PlayMode/HarnessTests.cs` (runs chapter 1 only, as the slice did)

**Interfaces:**
- Consumes: `GameFlow.StartChapter`, `GameFlow.StopAfterChapter`, `GameFlow.CurrentChapter`.
- Produces: `BalanceHarness.StartChapter`, `BalanceHarness.StopAfterChapter`, `BalanceHarness.Row.Chapter`.

- [ ] **Step 1: Change the harness test first** — in `HarnessTests.Harness(...)` set `h.StopAfterChapter = 1;` and in `A_Full_Run_Per_Bot_Completes_And_Writes_A_Row` assert `Assert.AreNotEqual("timeout", row.Outcome, …)` (unchanged) plus `Assert.AreEqual(1, row.Chapter);`.

- [ ] **Step 2: Run and see it fail**

Run: `tools/unity-tests.sh PlayMode HarnessTests` → Expected: compile error (`StopAfterChapter`, `Row.Chapter`).

- [ ] **Step 3: Implement**

`QA/BalanceHarness.cs`: fields `public int StartChapter = 1; public int StopAfterChapter = 1;`, `Row.Chapter` (int; the chapter reached), set before `flowGo.SetActive(true)`: `flow.StartChapter = StartChapter; flow.StopAfterChapter = StopAfterChapter;`; after the loop `row.Chapter = flow.CurrentChapter;`; CSV header gains `chapter` after `reached` (and the row value); `ReadArgs` reads `"chapters"` as `"1"` or `"2-4"` into `StartChapter`/`StopAfterChapter`. In `Summary`, the "reached camp" column counts `Outcome == "chapter_done"` too.

`UI/PauseMenu.cs` line 116:
```csharp
            _sub.text = (_flow == null ? "The Old Road" : _flow.Current == GameFlow.State.Duel ? "The Gallery" : HS.Flow.CampaignSchedule.For(_flow.CurrentChapter).Name)
                        + (sk != null ? $"  ·  Callum's sidekick  ·  level {sk.Level}" : "");
```

`Tutorial/Lessons.cs`, the "restore" lesson body:
```
"Restore Points: start again from any chapter start you have reached, or from just before the Gallery door. Your tricks come with you."
```

- [ ] **Step 4: Run the tests**

Run: `tools/unity-tests.sh PlayMode "HarnessTests|TutorialTests|UiTests"` and `tools/unity-tests.sh EditMode TutorialData` → Expected: no new failures (the harness tests may still fail on their pre-existing determinism problem; record the result).

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: pause menu, restore lesson and balance harness know about chapters"
```

---

### Task 10: Tools/HS/Play from chapter N

**Files:**
- Create: `Editor/PlayFromChapterMenu.cs`
- Test: `Tests/PlayMode/CampaignTests.cs`

**Interfaces:**
- Consumes: `GameFlow.PlayFromChapterKey`.

- [ ] **Step 1: Write the failing test**

```csharp
        [UnityTest]
        public IEnumerator The_Play_From_Chapter_Menu_Starts_There_Once()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, 3);
#endif
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow();
            yield return null;
            Assert.AreEqual(3, flow.CurrentChapter);
#if UNITY_EDITOR
            Assert.AreEqual(0, UnityEditor.SessionState.GetInt(GameFlow.PlayFromChapterKey, 0), "consumed: the next Play starts at the opening");
#endif
        }
```

- [ ] **Step 2: Run** → passes already if Task 8 read the key; if it fails, fix `GameFlow.Start`. Run: `tools/unity-tests.sh PlayMode CampaignTests`.

- [ ] **Step 3: Implement the menu**

```csharp
// Editor/PlayFromChapterMenu.cs
using UnityEditor;
using UnityEditor.SceneManagement;

namespace HS.EditorTools
{
    /// <summary>Owner's shortcut: press Play straight into chapter N with the kit a thorough player would have.</summary>
    public static class PlayFromChapterMenu
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("Tools/HS/Play from chapter/2 · Whisperwood")] static void Ch2() => Play(2);
        [MenuItem("Tools/HS/Play from chapter/3 · Catacombs of Ends")] static void Ch3() => Play(3);
        [MenuItem("Tools/HS/Play from chapter/4 · The Sunken Bastion")] static void Ch4() => Play(4);
        [MenuItem("Tools/HS/Play from chapter/5 · The Gallery")] static void Ch5() => Play(5);

        static void Play(int chapter)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorSceneManager.GetActiveScene().path != MainScene) EditorSceneManager.OpenScene(MainScene);
            SessionState.SetInt(HS.Flow.GameFlow.PlayFromChapterKey, chapter);
            EditorApplication.isPlaying = true;
        }
    }
}
```

- [ ] **Step 4: Compile check**

Run: `tools/unity-tests.sh EditMode ScriptFileTests` → Expected: passes (compiles the editor assembly).

- [ ] **Step 5: Commit**

```bash
git add -A Escort/Assets/_Game
git commit -m "Campaign: Tools/HS/Play from chapter N"
```

---

### Task 11: Look at it — chapter themes captured, full suites green

**Files:**
- Create: `Tests/PlayMode/CampaignQaCaptures.cs` (category `QA`)
- Output: `docs/qa/shots/campaign_ch1.png` … `campaign_ch5.png`

- [ ] **Step 1: Write the capture scenario**

```csharp
using System.Collections;
using HS.Core;
using HS.Flow;
using HS.QA;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HS.Tests
{
    [Category("QA")]
    public class CampaignQaCaptures
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";

        [TearDown]
        public void TearDown() => TestUi.TearDownAll();

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Each_Chapter_Under_Its_Light()
        {
            for (int ch = 1; ch <= 5; ch++)
            {
#if UNITY_EDITOR
                UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, ch);
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                var flow = Object.FindAnyObjectByType<GameFlow>();
                HS.Tutorial.TutorialProgress.TipsEnabled = false;
                yield return new WaitForSecondsRealtime(3f);
                QaCapture.Capture(Camera.main, "campaign_ch" + ch, 1600, 900);
                Assert.AreEqual(ch, flow.CurrentChapter);
            }
        }
    }
}
```
If `ch == 1` with the key set skips the opening only for `ch > 1`, set `RunState.Runs = 1` before loading so chapter 1 gets the 3-second opening, and wait 8 s for it instead of 3.

- [ ] **Step 2: Run the captures and look at every image**

Run: `CATEGORY=QA tools/unity-tests.sh PlayMode CampaignQaCaptures` → then open each `docs/qa/shots/campaign_ch*.png` with the Read tool. Expected: chapter 1 unchanged; Whisperwood green-grey haze; Catacombs dark; Bastion cold blue; Gallery bright. Characters readable in all five. Adjust `ChapterTheme` values if any is unreadable (Catacombs is the risk: raise `SunIntensity` or ambient until the sidekick and hero read), re-run, re-check.

- [ ] **Step 3: Full suites**

Run: `tools/unity-tests.sh EditMode` and `tools/unity-tests.sh PlayMode` → Expected: EditMode all pass; PlayMode no failures beyond the six baseline ones (fewer is fine).

- [ ] **Step 4: Commit**

```bash
git add -A Escort/Assets/_Game docs/qa/shots/campaign_ch*.png
git commit -m "Campaign: QA captures of the five chapter themes"
```

---

### Task 12: Docs, push, and the owner's editor

**Files:**
- Modify: `GDD.md` (§3: a *Built* note under the chapter table), `docs/qa/QA_LOG.md` (a "Campaign spine" entry: what changed, test totals, the capture paths, the known temporary rooms)

- [ ] **Step 1: Write the docs** — GDD §3 note:
```
*Built (campaign spine, 2026-10-04):* the run loops all five chapters (power tiers, slots, XP pots, Stage checks at the
Ch2/Ch3 campfires and the door, Restore Points at every chapter start and the door). Chapters 2–5 use the Old Road's
rooms under their own light until their modules land. See docs/superpowers/specs/2026-10-04-callum-campaign-design.md.
```
QA_LOG entry: the headline change, the test totals from Task 11, the five capture paths, and "temporary: chapters 2–5 reuse chapter 1 rooms; the boss is still the slice's rigged duel (3 archers)".

- [ ] **Step 2: Commit and push the branch**

```bash
git add GDD.md docs/qa/QA_LOG.md
git commit -m "Docs: campaign spine"
git push
```

- [ ] **Step 3: Fast-forward `main` for the owner's editor**

In `/Users/sapnagoel/Documents/coding/Game` (the main checkout, which has the owner's uncommitted camera edits in `Escort/Assets/_Game/Core/Tuning.cs`, `Presentation/CameraRig.cs`, `Tests/PlayMode/CameraTests.cs`, `docs/qa/QA_LOG.md`, two font assets, `ProjectSettings.asset`, plus untracked `Escort/Assets/Resources/`):
1. `git status --short` and `git diff --stat` — note the dirty files.
2. Find overlap: `git diff --name-only main claude/callum-campaign` ∩ dirty files (expected: `Core/Tuning.cs`, `docs/qa/QA_LOG.md`).
3. `git stash push -m "callum-campaign-ff-<timestamp>" -- <overlapping files>`; record the SHA from `git stash list --format='%H %gs'`.
4. `git merge --ff-only claude/callum-campaign`.
5. `git stash apply <sha>`; resolve if needed so both the camera edit and the branch's changes are present; confirm `git diff` of those files shows exactly the owner's original hunks; then `git stash drop` the entry found by its tag.
6. `git push origin main`.
7. Check the editor: `python3 tools/u.py status`. If it is in Play mode, tell the owner to stop and press Play again.
```

---

## Self-review notes

- Spec §2 decisions: power scaling (Tasks 1–2), XP (1, 5), between-room recovery (2), slots (6), Restore Points (8), Ch1 campfire without a check (7), duel out of chapter 1 (8). Hunger, ping "cover", the capstone slot: later plans (2, 3, 4).
- Spec §3.1: the loop (4, 8), "Play from chapter N" (10). §3.2 units in this plan: `CampaignSchedule`, `ChapterTier`, `ChapterDef`, `ChapterTheme`, `RoomModule.Chapter`, `CampfireScenes`. The rest belong to Plans 2–5.
- Names used across tasks: `ChapterBootstrap.Def`, `BuildRun`, `BuildChapter(int,int)`, `TeardownChapter`; `CampaignSchedule.For/RoomPot/LevelTarget`; `CallumModule.ApplyChapter(int, Signature, float)`; `RunState.SetChapterStart/ChapterStartOf/Door/Resolve/ForgetAfter`; `GameFlow.StartChapter/StopAfterChapter/CurrentChapter/PlayFromChapterKey/AtDoor`.
