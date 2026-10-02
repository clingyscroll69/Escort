using System.Collections;
using HS.Core;
using HS.Enemies;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public sealed class DummyHero : Agent, IWounded
    {
        public override Faction Faction => Faction.Hero;
        public int Wounds = 1;
        public int WoundCount => Wounds;
        public bool HasMinorWound => Wounds > 0;
        public bool TreatMinorWound()
        {
            if (Wounds <= 0) return false;
            Wounds--;
            return true;
        }
        protected override void Awake()
        {
            base.Awake();
            Health = new Health(260f);
        }
        protected override void OnSimTick(float dt) => Motor.Move(Vector3.zero, 50f, dt);
    }

    public class SliceSkillTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;

        [SetUp]
        public void SetUp()
        {
            _ground = SidekickTests.Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
            ProjectileSystem.Ensure();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            foreach (var p in Object.FindObjectsByType<ArmableProp>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            foreach (var w in GameObject.FindGameObjectsWithTag("Untagged")) if (w.name == "TestWall") Object.Destroy(w);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject); // deferred Destroy lets the next test register into a dying loop
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) MakeSidekick(Vector3 pos, params string[] learn)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(pos, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            foreach (var id in learn) Assert.IsTrue(skills.Learn(id), "learn " + id);
            return (sk, skills, cmd);
        }

        EnemyAgent Enemy(Vector3 pos, string arch = "thug", float yaw = 180f)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            e.Archetype = arch;
            e.Configure(arch);
            return e;
        }

        static SidekickCommand Cast(int slot, Vector3 aim) => new SidekickCommand { Skill = slot, AimPoint = aim, HasAim = true };

        [UnityTest]
        public IEnumerator PocketSand_Blinds_In_Radius_Reveals_Hidden_And_Is_Dirty()
        {
            var (sk, skills, cmd) = MakeSidekick(Vector3.zero, "pocket_sand");
            var near = Enemy(new Vector3(0f, 0f, 5f));
            var near2 = Enemy(new Vector3(1.2f, 0f, 5.6f));
            var hidden = Enemy(new Vector3(-1.2f, 0f, 5f), "ambusher");
            var far = Enemy(new Vector3(0f, 0f, 12f));
            SabotageEvent? seen = null;
            Ctx.Events.Sabotage += e => seen = e;
            yield return null; // Start() runs: ambusher hides
            Assert.IsTrue(hidden.IsHidden);
            cmd.Current = Cast(0, new Vector3(0f, 0f, 5f));
            Loop.StepMany(30); // 0.5 s: throw lands at 0.3 s
            Assert.IsTrue(near.Status.Has(StatusType.Blinded) && near2.Status.Has(StatusType.Blinded), "blinds enemies in the cloud");
            Assert.AreEqual(3f - 0.2f, near.Status.Remaining(StatusType.Blinded), 0.05f, "rank 1: 3 s");
            Assert.IsFalse(far.Status.Has(StatusType.Blinded), "cloud is local");
            Assert.IsFalse(hidden.IsHidden, "reveals hidden things");
            Assert.IsTrue(seen.HasValue && seen.Value.Severity == SabotageSeverity.Major && seen.Value.Tag == "pocket_sand");
            // Rank 2: 4 s blind
            skills.Learn("pocket_sand");
            Loop.StepMany(Mathf.CeilToInt(8f / SimLoop.Dt) + 2); // +2 ticks: 480 × (1/60) leaves float residue on an 8 s cooldown
            Assert.IsFalse(near.Status.Has(StatusType.Blinded), "first blind expired");
            cmd.Current = Cast(0, new Vector3(0f, 0f, 5f)); // range stays 8 m at rank 2
            Loop.StepMany(22); // throw lands ~0.3 s after release (±1 tick of float clock drift)
            Assert.AreEqual(4f - 3f * SimLoop.Dt, near.Status.Remaining(StatusType.Blinded), 0.04f, "rank 2: 4 s");
        }

        ArmableProp Prop(Vector3 pos, Vector3 impactOffset, float radius = 2.5f)
        {
            var go = new GameObject("TestProp");
            go.transform.position = pos;
            var anchor = go.AddComponent<ArmableAnchor>();
            anchor.ImpactOffset = impactOffset;
            anchor.ImpactRadius = radius;
            anchor.Damage = 90f;
            return go.AddComponent<ArmableProp>();
        }

        [UnityTest]
        public IEnumerator LoosenBolt_Arms_Caps_At_Two_And_Collapses_On_Ping()
        {
            var (sk, skills, cmd) = MakeSidekick(Vector3.zero, "loosen_bolt");
            var p1 = Prop(new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 4f));
            var p2 = Prop(new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var p3 = Prop(new Vector3(-2f, 0f, 0f), new Vector3(-4f, 0f, 0f));
            var victim = Enemy(new Vector3(0f, 0f, 5.5f));
            yield return null;
            cmd.Current = Cast(0, Vector3.zero);
            Loop.StepMany(Mathf.CeilToInt(1.6f / SimLoop.Dt));
            Assert.AreEqual(ArmableProp.PropState.Armed, p1.State, "1.5 s channel arms the nearest prop");
            Assert.AreEqual(1, ArmableProp.ArmedCount());
            p2.Arm();
            Loop.StepMany(70); // cooldown 1 s
            cmd.Current = Cast(0, Vector3.zero);
            Loop.StepMany(2);
            Assert.IsFalse(sk.IsChanneling, "rank 1 caps armed props at 2");
            Assert.AreEqual(ArmableProp.PropState.Idle, p3.State);
            cmd.Current = new SidekickCommand { Ping = true, AimPoint = new Vector3(0f, 0f, 5.5f), HasAim = true, Skill = -1 };
            Loop.StepMany(40); // 0.5 s fall
            Assert.AreEqual(ArmableProp.PropState.Spent, p1.State);
            Assert.IsFalse(victim.IsAlive, "90 heavy damage in the impact zone kills a thug (80 HP)");
        }

        [UnityTest]
        public IEnumerator LoosenBolt_Triggers_When_Hero_Passes_With_Enemy_Underneath()
        {
            var (sk, skills, cmd) = MakeSidekick(new Vector3(8f, 0f, 0f), "loosen_bolt");
            var hero = SidekickTests.Spawn<DummyHero>(new Vector3(0f, 0f, 0.5f)); // 5.5 m from the impact point
            Ctx.Hero = hero;
            var p = Prop(new Vector3(-3f, 0f, 6f), new Vector3(3f, 0f, 0f));
            var e = Enemy(new Vector3(0f, 0f, 6f));
            yield return null;
            p.Arm();
            Loop.StepMany(60);
            Assert.AreEqual(ArmableProp.PropState.Spent, p.State, "hero near (5–7 m) but clear, enemy in zone → collapse");
            Assert.AreEqual(260f, hero.Health.Current, "hero outside the zone takes nothing");
            Assert.Less(e.Health.Current, e.Health.Max);
        }

        [UnityTest]
        public IEnumerator QuietFeet_Limits_Detection_To_Two_Metres_While_Crouched()
        {
            var e = Enemy(Vector3.zero, "thug", 0f); // facing +Z
            var (sk, skills, cmd) = MakeSidekick(new Vector3(0f, 0f, 3f));
            yield return null;
            cmd.Current = new SidekickCommand { CrouchToggle = true, Skill = -1 };
            Loop.Step();
            Assert.IsTrue(sk.Crouched);
            Assert.IsTrue(e.CanDetect(sk), "without Quiet Feet a crouching sidekick at 3 m in view is seen");
            skills.Learn("quiet_feet");
            Assert.IsTrue(sk.IsSneaking);
            Assert.IsFalse(e.CanDetect(sk), "Quiet Feet: not detected beyond 2 m");
            sk.Motor.Teleport(new Vector3(0f, 0.05f, 1.6f));
            Assert.IsTrue(e.CanDetect(sk), "within 2 m still detected");
            skills.Learn("quiet_feet");
            sk.Motor.Teleport(new Vector3(0f, 0.05f, 1.8f));
            Assert.IsFalse(e.CanDetect(sk), "rank 2: 1.5 m");
        }

        [UnityTest]
        public IEnumerator Crossbow_Hits_For_38_Rank2_Pierces_And_Walls_Block()
        {
            var (sk, skills, cmd) = MakeSidekick(Vector3.zero, "crossbow");
            var a = Enemy(new Vector3(0f, 0f, 6f));
            var b = Enemy(new Vector3(0f, 0f, 8f));
            yield return null;
            cmd.Current = Cast(0, new Vector3(0f, 0f, 12f));
            Loop.StepMany(40);
            Assert.AreEqual(80f - 38f, a.Health.Current, 0.01f, "rank 1: 38");
            Assert.AreEqual(80f, b.Health.Current, 0.01f, "rank 1 does not pierce");
            skills.Learn("crossbow");
            Loop.StepMany(Mathf.CeilToInt(3.6f / SimLoop.Dt));
            cmd.Current = Cast(0, new Vector3(0f, 0f, 12f));
            Loop.StepMany(40);
            Assert.IsFalse(a.IsAlive, "rank 2: 44 finishes the first");
            Assert.AreEqual(80f - 44f, b.Health.Current, 0.01f, "rank 2 pierces to the second");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TestWall";
            wall.transform.position = new Vector3(0f, 1f, 7f);
            wall.transform.localScale = new Vector3(4f, 3f, 0.4f);
            Physics.SyncTransforms();
            Loop.StepMany(Mathf.CeilToInt(3.1f / SimLoop.Dt));
            cmd.Current = Cast(0, new Vector3(0f, 0f, 12f));
            Loop.StepMany(40);
            Assert.AreEqual(80f - 44f, b.Health.Current, 0.01f, "cover blocks bolts");
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator Bandage_Channels_Heals_Over_Time_And_Treats_A_Minor_Wound()
        {
            var hero = SidekickTests.Spawn<DummyHero>(new Vector3(1.5f, 0f, 0f));
            Ctx.Hero = hero;
            var (sk, skills, cmd) = MakeSidekick(Vector3.zero, "bandage");
            yield return null;
            hero.TakeDamage(DamageInfo.Make(null, hero, 160f, DamageKind.Melee, "test"));
            Assert.AreEqual(100f, hero.Health.Current, 0.01f);
            cmd.Current = Cast(0, hero.Position);
            Loop.StepMany(Mathf.CeilToInt(3.05f / SimLoop.Dt));
            Assert.AreEqual(0, hero.Wounds, "treats a minor wound on completion");
            Loop.StepMany(Mathf.CeilToInt(6.1f / SimLoop.Dt));
            Assert.AreEqual(140f, hero.Health.Current, 0.6f, "40 over 6 s");
            // Cancel by moving: cooldown refunded
            Loop.StepMany(Mathf.CeilToInt(12.1f / SimLoop.Dt));
            hero.TakeDamage(DamageInfo.Make(null, hero, 20f, DamageKind.Melee, "test"));
            cmd.Current = Cast(0, hero.Position);
            Loop.StepMany(20);
            Assert.IsTrue(sk.IsChanneling);
            cmd.Current = new SidekickCommand { Move = Vector3.left, Skill = -1 };
            Loop.StepMany(2);
            Assert.IsFalse(sk.IsChanneling, "moving cancels");
            Assert.IsTrue(skills.System.Get("bandage").Ready, "an interrupted bandage refunds its cooldown");
        }

        [UnityTest]
        public IEnumerator CoverStory_Talks_The_Hero_Round_Only_Within_Earshot()
        {
            var hero = SidekickTests.Spawn<DummyHero>(new Vector3(5f, 0f, 0f));
            Ctx.Hero = hero;
            var (sk, skills, cmd) = MakeSidekick(Vector3.zero, "cover_story");
            float restore = -1f, window = -1f;
            Ctx.Events.CoverStory += (r, w) => { restore = r; window = w; };
            yield return null;
            cmd.Current = Cast(0, hero.Position);
            Loop.Step();
            Assert.AreEqual(12f, restore, 0.01f);
            Assert.AreEqual(3f, window, 0.01f, "rank 1: halves a Caught penalty within 3 s");
            hero.Motor.Teleport(new Vector3(20f, 0.05f, 0f));
            Loop.StepMany(Mathf.CeilToInt(20.1f / SimLoop.Dt));
            restore = -1f;
            string thought = null;
            Ctx.Events.ThoughtPopup += t => thought = t;
            cmd.Current = Cast(0, hero.Position);
            Loop.Step();
            Assert.AreEqual(-1f, restore, "out of earshot: no effect");
            Assert.IsNotNull(thought, "the player is told why");
        }
    }
}
