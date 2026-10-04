using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public class CallumTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        CallumModule _cm;

        [SetUp]
        public void SetUp()
        {
            _ground = SidekickTests.Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            foreach (var w in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)) if (w.name == "TestWall") Object.DestroyImmediate(w.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator MakeCallum(Vector3 pos, Stage stage = Stage.S0)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity);
            _hero = go.GetComponent<HeroAgent>();
            _cm = go.GetComponent<CallumModule>();
            Ctx.Hero = _hero;
            yield return null; // Start → rules
            _hero.ApplyStage(stage);
#else
            yield break;
#endif
        }

        EnemyAgent Enemy(Vector3 pos, string arch = "thug", bool engage = true)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.LookRotation(-pos.normalized == Vector3.zero ? Vector3.back : -new Vector3(pos.x, 0, pos.z).normalized);
            e.Archetype = arch;
            e.Configure(arch);
            if (engage) e.Activate();
            return e;
        }

        [UnityTest]
        public IEnumerator Challenges_Nearest_Within_15m_With_A_1_2s_Salute()
        {
            yield return MakeCallum(Vector3.zero);
            var far = Enemy(new Vector3(0f, 0f, 16.5f));
            far.Status.Apply(StatusType.Stunned, 99f); // keep it put
            Loop.StepMany(5);
            Assert.IsNull(_cm.Challenged, "nobody within 15 m → no challenge");
            var near = Enemy(new Vector3(1f, 0f, 9f));
            near.Status.Apply(StatusType.Stunned, 1.1f); // recovers just before the salute ends
            var mid = Enemy(new Vector3(-2f, 0f, 12f));
            mid.Status.Apply(StatusType.Stunned, 99f);
            Loop.Step();
            Loop.Step();
            Assert.AreEqual(near, _cm.Challenged, "nearest hostile");
            Assert.AreEqual("challenge", _hero.ActiveIcon);
            Assert.AreEqual(_hero, near.DuelOpponent);
            int ticks = 0;
            while (_cm.Saluting && ticks < 200) { Loop.Step(); ticks++; }
            Assert.That(ticks * SimLoop.Dt, Is.InRange(1.1f, 1.25f), "salute lasts 1.2 s");
            Loop.StepMany(3);
            Assert.AreEqual("callum_fight", _hero.ActiveRuleId);
        }

        IEnumerator WaitDuration(Stage stage, System.Action<float> result)
        {
            yield return MakeCallum(Vector3.zero, stage);
            var e = Enemy(new Vector3(0f, 0f, 3f));
            e.Status.Apply(StatusType.Stunned, 1.6f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            e.Status.Apply(StatusType.Blinded, 6f);
            Loop.Step();
            int waited = 0;
            while (_hero.ActiveRuleId == "callum_wait_unready" && waited < 1000) { Loop.Step(); waited++; }
            result(waited * SimLoop.Dt);
        }

        [UnityTest]
        public IEnumerator Waits_On_Unready_Target_S0_3s()
        {
            float t = -1f;
            yield return WaitDuration(Stage.S0, x => t = x);
            Assert.That(t, Is.InRange(2.9f, 3.1f));
        }

        [UnityTest]
        public IEnumerator Waits_On_Unready_Target_S1_2s()
        {
            float t = -1f;
            yield return WaitDuration(Stage.S1, x => t = x);
            Assert.That(t, Is.InRange(1.9f, 2.1f), "S1: waiting cap 2 s");
        }

        [UnityTest]
        public IEnumerator Turncoat_Fake_Surrender_Cheap_Shot_Wounds_At_S0()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            // bring him to surrender
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Surrendered, tc.State);
            float hp = _hero.Health.Current;
            Loop.StepMany(Mathf.CeilToInt(2.7f / SimLoop.Dt)); // the stab lands at 2.4 s, before a normal follow-up
            Assert.AreEqual(hp - 66f, _hero.Health.Current, 0.5f, "S0 waits 3 s; the stab lands at 2.4 s");
        }

        [UnityTest]
        public IEnumerator Turncoat_Is_Spared_Before_The_Stab_At_S1()
        {
            yield return MakeCallum(Vector3.zero, Stage.S1);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            float hp = _hero.Health.Current;
            Loop.StepMany(Mathf.CeilToInt(3.2f / SimLoop.Dt));
            Assert.AreEqual(EnemyState.Spared, tc.State, "S1 accepts the surrender at 2 s");
            Assert.AreEqual(hp, _hero.Health.Current, 0.01f);
        }

        [UnityTest]
        public IEnumerator Riposte_Parries_The_First_Swing_And_Counters_Double()
        {
            yield return MakeCallum(Vector3.zero);
            var e = Enemy(new Vector3(0f, 0f, 1.9f), "brute");
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            float heroHp = _hero.Health.Current, eHp = e.Health.Current;
            // run until the brute's first swing resolves
            int guard = 0;
            while (_cm.RiposteCooldown <= 0f && guard++ < 600) Loop.Step();
            Assert.Greater(_cm.RiposteCooldown, 0f, "riposte triggered");
            Assert.AreEqual(heroHp, _hero.Health.Current, 0.01f, "the parried swing does nothing");
            Assert.LessOrEqual(e.Health.Current, eHp - 52f + 0.01f, "counter for 2 × 26");
        }

        static SabotageEvent Deed(Vector3 at, SabotageSeverity sev) => new SabotageEvent { Tag = "pocket_sand", Severity = sev, Position = at, ActorPosition = at };

        [UnityTest]
        public IEnumerator Honor_Costs_Major20_Minor8_And_S1_Halves_Minor()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            Assert.AreEqual(100f, _cm.Honor);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(0f, 0f, 6f), SabotageSeverity.Major));
            Assert.AreEqual(80f, _cm.Honor, 0.01f);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(1f, 0f, 5f), SabotageSeverity.Minor));
            Assert.AreEqual(72f, _cm.Honor, 0.01f);
            _hero.ApplyStage(Stage.S1);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(-1f, 0f, 5f), SabotageSeverity.Minor));
            Assert.AreEqual(68f, _cm.Honor, 0.01f, "S1: minor assists cost half");
            bool unseen = false;
            _cm.UnseenDeed += _ => unseen = true;
            Ctx.Events.RaiseSabotage(Deed(new Vector3(0f, 0f, -6f), SabotageSeverity.Major)); // behind him
            Assert.AreEqual(68f, _cm.Honor, 0.01f, "unseen: no cost");
            Assert.IsTrue(unseen);
        }

        [UnityTest]
        public IEnumerator Low_Honor_Cuts_His_Damage_By_A_Quarter()
        {
            yield return MakeCallum(Vector3.zero);
            Assert.AreEqual(26f, _cm.OutgoingDamage(26f), 0.01f);
            _cm.SetHonor(39f);
            Assert.IsTrue(_cm.HonorLow);
            Assert.AreEqual(19.5f, _cm.OutgoingDamage(26f), 0.01f);
        }

        [UnityTest]
        public IEnumerator QuietFeet_Narrows_The_Witness_Cone_And_Walls_Block_Sight()
        {
            yield return MakeCallum(Vector3.zero);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(20f, 0f, 0f));
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var p = Quaternion.Euler(0f, 50f, 0f) * Vector3.forward * 8f; // 50° off-axis, 8 m
            Assert.IsTrue(_cm.Sees(p, false), "inside 120°/12 m");
            sk.HasQuietFeet = true;
            Assert.IsFalse(_cm.Sees(p, true), "Quiet Feet sneak: ~80°/7.2 m");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TestWall";
            wall.transform.position = new Vector3(0f, 1.5f, 4f);
            wall.transform.localScale = new Vector3(4f, 3f, 0.3f);
            Physics.SyncTransforms();
            Assert.IsFalse(_cm.Sees(new Vector3(0f, 0f, 8f), false), "no line of sight through walls");
        }

        [UnityTest]
        public IEnumerator Falls_Back_To_A_Chokepoint_At_Three_Engagers()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Route.SetNodes(new List<RouteNode>
            {
                new RouteNode { Position = new Vector3(0f, 0f, -7f), Chokepoint = true, Label = "narrows" },
                new RouteNode { Position = new Vector3(0f, 0f, 20f) },
            }, 1);
            Enemy(new Vector3(1.5f, 0f, 1.5f));
            Enemy(new Vector3(-1.5f, 0f, 1.5f));
            Enemy(new Vector3(0f, 0f, 2.2f));
            int switchesWhileRetreating = 0;
            _hero.Brain.RuleChanged += (p, n) => { if (p != null && p.Id == "callum_fallback" && _cm.FallingBack) switchesWhileRetreating++; };
            Loop.StepMany(30);
            Assert.AreEqual("callum_fallback", _hero.ActiveRuleId);
            Assert.AreEqual("fallback", _hero.ActiveIcon);
            Loop.StepMany(90);
            Assert.Less(_hero.Position.z, -2f, "retreats toward the narrows");
            Assert.AreEqual(0, switchesWhileRetreating, "the retreat is committed (no fight/fallback thrash)");
            int guard = 0;
            while (_cm.FallingBack && guard++ < 400) Loop.Step();
            Assert.IsTrue(_cm.HoldingNarrows, "arrived and holding the chokepoint");
            Assert.AreEqual(1, _hero.MeleeSlots, "single file: one attacker at a time");
        }

        [UnityTest]
        public IEnumerator His_Own_Blows_And_Parries_Do_Not_Make_The_Target_Unready()
        {
            yield return MakeCallum(Vector3.zero);
            var e = Enemy(new Vector3(0f, 0f, 2.0f), "brute");
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            bool waited = false;
            _hero.Brain.RuleChanged += (p, n) => { if (n != null && n.Id == "callum_wait_unready") waited = true; };
            Loop.StepMany(Mathf.CeilToInt(6f / SimLoop.Dt));
            Assert.IsFalse(waited, "sword staggers and ripostes are his own doing");
            Assert.Less(e.Health.Current, e.Health.Max - 52f, "and he kept fighting");
        }

        [UnityTest]
        public IEnumerator Only_Two_Bandits_Swing_At_Once_The_Rest_Circle()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 30f) } }); // no chokepoint
            var foes = new List<EnemyAgent>();
            for (int i = 0; i < 4; i++) foes.Add(Enemy(Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 2.5f));
            int maxSwinging = 0, everSwung = 0;
            var swung = new HashSet<EnemyAgent>();
            for (int k = 0; k < 900; k++)
            {
                Loop.Step();
                int swinging = 0;
                foreach (var f in foes)
                    if (f != null && f.IsAlive && f.IsAttackWindup)
                    {
                        swinging++;
                        swung.Add(f);
                    }
                maxSwinging = Mathf.Max(maxSwinging, swinging);
            }
            everSwung = swung.Count;
            Assert.LessOrEqual(maxSwinging, 2, "two attack tokens on the hero");
            Assert.GreaterOrEqual(everSwung, 3, "tokens rotate: the circlers get their turn");
        }

        [UnityTest]
        public IEnumerator He_Judges_Her_Blows_On_His_Duel_And_The_Helpless_Not_Her_Own_Fights()
        {
            yield return MakeCallum(Vector3.zero);
            var duel = Enemy(new Vector3(0f, 0f, 2.6f));
            var hers = Enemy(new Vector3(-3f, 0f, 7f));
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(-3f, 0f, 9.5f));
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var barks = new List<string>();
            Ctx.Events.Bark += b => { if (b.SpeakerId == "callum") barks.Add(b.Text); };
            hers.TakeDamage(DamageInfo.Make(sk, hers, 5f, DamageKind.Knife, "knife")); // she starts it: he turns on her
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            Assert.AreSame(duel, _cm.Challenged);
            Assert.AreSame(sk, hers.Target);
            hers.transform.rotation = Quaternion.LookRotation(Vector3.forward); // squared up with her, his back to Callum
            float honor = _cm.Honor;

            // Her own fight, in plain sight: a bolt (its own 0.3 s stagger) then a knife. Neither is his business.
            hers.TakeDamage(DamageInfo.Make(sk, hers, 5f, DamageKind.Ranged, "crossbow", 0.3f));
            hers.TakeDamage(DamageInfo.Make(sk, hers, 5f, DamageKind.Knife, "knife"));
            Assert.AreEqual(honor, _cm.Honor, 0.01f, "a fair fight of her own costs nothing");

            // A bolt into his duel: a slight (Minor), and he says so about the bolt.
            barks.Clear();
            duel.TakeDamage(DamageInfo.Make(sk, duel, 5f, DamageKind.Ranged, "crossbow", 0.3f));
            Assert.AreEqual(honor - _cm.T.honorLossMinor, _cm.Honor, 0.01f, "Minor (−8), not Major (−20)");
            var bolt = new SabotageEvent { Tag = "crossbow", Severity = SabotageSeverity.Minor, Victim = duel };
            CollectionAssert.Contains(CallumModule.CaughtLines(bolt), barks[barks.Count - 1]);

            // Knifing a blinded man in her own fight: striking the helpless (Major), and he names the blindness.
            honor = _cm.Honor;
            hers.Status.Apply(StatusType.Blinded, 3f);
            barks.Clear();
            hers.TakeDamage(DamageInfo.Make(sk, hers, 5f, DamageKind.Knife, "knife"));
            Assert.AreEqual(honor - _cm.T.honorLossMajor, _cm.Honor, 0.01f);
            var blind = new SabotageEvent { Tag = "knife", Severity = SabotageSeverity.Major, Victim = hers };
            CollectionAssert.Contains(CallumModule.CaughtLines(blind), barks[barks.Count - 1]);

            var sand = new SabotageEvent { Tag = "pocket_sand", Severity = SabotageSeverity.Major, Victim = hers };
            CollectionAssert.AreNotEqual(CallumModule.CaughtLines(sand), CallumModule.CaughtLines(bolt), "a bolt is not sand");
            CollectionAssert.AreNotEqual(CallumModule.CaughtLines(sand), CallumModule.CaughtLines(blind));
        }

        [UnityTest]
        public IEnumerator The_Turncoat_Fakes_His_Surrender_Only_Once()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Surrendered, tc.State);
            Loop.StepMany(Mathf.CeilToInt(2.7f / SimLoop.Dt)); // the stab lands; he's back to fighting
            Assert.AreEqual(EnemyState.Engaged, tc.State);
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 5f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Engaged, tc.State, "the ruse doesn't work twice");
        }

        // ------------------------------------------------------------------ S2 (campaign plan 3)

        [UnityTest]
        public IEnumerator S2_A_Ping_On_Him_Turns_His_Back_And_He_Sees_Nothing()
        {
            yield return MakeCallum(Vector3.zero, Stage.S2);
            _hero.Route.SetNodes(new List<RouteNode>());
            var dir = HS.Rapport.OpportunityDirector.Create(Ctx, _hero);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1.5f, 0f, -2f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var thug = Enemy(new Vector3(0f, 0f, 5f), "thug", engage: false);
            float honor = _cm.Honor;
            Ctx.Events.RaisePing(new PingInfo { Point = _hero.Position, Target = _hero, Meaning = "mark" });
            Assert.IsTrue(_cm.LookingAway);
            Loop.StepMany(3);
            Assert.AreEqual("callum_look_away", _hero.ActiveRuleId);
            Assert.IsTrue(dir.Ledger.Log.Exists(l => l.Kind == "offer" && l.Id == "chosen_blindness"), "the Moment is on offer");
            thug.Status.Apply(StatusType.Blinded, 3f);
            Ctx.Events.RaiseSabotage(new SabotageEvent { Tag = "pocket_sand", Severity = SabotageSeverity.Major, Position = thug.Position, ActorPosition = sk.Position, Victim = thug, Time = Ctx.SimTime });
            Assert.AreEqual(honor, _cm.Honor, 0.01f, "behind his back");
            Assert.IsTrue(dir.Ledger.Log.Exists(l => l.Kind == "capture" && l.Id == "chosen_blindness"));
            Loop.StepMany(Mathf.CeilToInt((CallumModule.LookAwayTime + 0.2f) / SimLoop.Dt));
            Assert.IsFalse(_cm.LookingAway, "three seconds, no more");
            Object.Destroy(dir.gameObject);
        }

        [UnityTest]
        public IEnumerator S1_Never_Looks_Away()
        {
            yield return MakeCallum(Vector3.zero, Stage.S1);
            Ctx.Events.RaisePing(new PingInfo { Point = _hero.Position, Target = _hero, Meaning = "mark" });
            Assert.IsFalse(_cm.LookingAway);
            Assert.IsFalse(_cm.LookAway());
        }

        [UnityTest]
        public IEnumerator S2_Waits_Only_1s_On_A_Cheater()
        {
            yield return MakeCallum(Vector3.zero, Stage.S2);
            var e = Enemy(new Vector3(0f, 0f, 3f), "turncoat");
            e.Status.Apply(StatusType.Stunned, 1.6f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            e.Status.Apply(StatusType.Blinded, 6f);
            Loop.Step();
            int waited = 0;
            while (_hero.ActiveRuleId == "callum_wait_unready" && waited < 1000) { Loop.Step(); waited++; }
            Assert.That(waited * SimLoop.Dt, Is.InRange(0.9f, 1.1f), "a cheater gets 1 s");
        }

        // ------------------------------------------------------------------ S3 (campaign plan 5)

        [UnityTest]
        public IEnumerator S3_Fair_To_Cheat_A_Cheater_And_S2_Is_Not()
        {
            foreach (var (stage, free) in new[] { (Stage.S3, true), (Stage.S2, false) })
            {
                yield return MakeCallum(Vector3.zero, stage);
                var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1.5f, 0f, 2f), 0.32f);
                sk.Commands = new ScriptedCommands();
                Ctx.Sidekick = sk;
                var cheat = Enemy(new Vector3(0f, 0f, 4f), "cultist");
                int caught = 0;
                _cm.Caught += (e, byStone) => caught++;
                float honor = _cm.Honor;
                cheat.Status.Apply(StatusType.Blinded, 3f);
                Ctx.Events.RaiseSabotage(new SabotageEvent { Tag = "pocket_sand", Severity = SabotageSeverity.Major, Position = cheat.Position, ActorPosition = sk.Position, Victim = cheat, Time = Ctx.SimTime });
                Assert.AreEqual(free ? honor : honor - _cm.T.honorLossMajor, _cm.Honor, 0.01f, stage.ToString());
                Assert.AreEqual(free ? 0 : 1, caught, stage + ": never Caught at S3");
                foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator S3_Judgment_On_A_Cheater_Asks_For_A_Hand_And_Her_Blows_Count()
        {
            yield return MakeCallum(Vector3.zero, Stage.S3);
            Ctx.Chapter = 5;
            var rules = HS.Flow.CampaignSchedule.For(5);
            _cm.ApplyChapter(5, rules.Unlocks, rules.Recovery);
            _hero.Route.SetNodes(new List<RouteNode>());
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1.5f, 0f, 1f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var cheat = Enemy(new Vector3(0f, 0f, 2.4f), "cultist");
            cheat.Health.Invulnerable = true;
            int asked = 0, struck = 0;
            _cm.DuetAsked += e => asked++;
            _cm.DuetStruck += e => struck++;
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            cheat.Health.SetCurrent(cheat.Health.Max * 0.5f);
            int guard = 0;
            while (!_cm.FinisherCharging && guard++ < 600) Loop.Step();
            Assert.IsTrue(_cm.DuetWindow, "against a cheat he asks for a hand");
            Assert.AreEqual(1, asked);
            Loop.Step();
            Assert.AreEqual("duet", _hero.ActiveIcon, "the Duet icon over his head");
            float honor = _cm.Honor;
            cheat.Health.Invulnerable = false;
            for (int i = 0; i < 6; i++) cheat.TakeDamage(DamageInfo.Make(sk, cheat, 1f, DamageKind.Knife, "knife"));
            Assert.AreEqual(6, struck);
            Assert.AreEqual(2f, _cm.DuetMultiplier, 1e-4f, "+25% a blow, to +100%");
            Assert.AreEqual(honor, _cm.Honor, 0.01f, "he asked for it");
        }
    }
}
