using System.Collections;
using HS.Core;
using HS.Curator;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills.Impl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Curator scouts (GDD §4.3): the dull pendant, the 3 s ping window, Read the Room, the report at camp.</summary>
    public class ScoutTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        StoneSystem _stones;
        Dossier _dossier;
        SidekickAgent _sk;
        Scout _quill;

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
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            if (_stones) Object.DestroyImmediate(_stones.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator Stage()
        {
            _stones = StoneSystem.Create(Ctx, null);
            _dossier = new Dossier();
            Ctx.Register(_dossier);
            _sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(0f, 0f, -12f), 0.32f);
            _sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = _sk;
            _quill = SidekickTests.Spawn<Scout>(new Vector3(0f, 0f, 12f));
            yield return null;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));
        void PingQuill() => Ctx.Events.RaisePing(new PingInfo { Point = _quill.Position, Target = _quill, Meaning = "mark" });

        [UnityTest]
        public IEnumerator Ping_Within_3s_Of_First_Sight_Unmasks_Him()
        {
            yield return Stage();
            Loop.Step();
            Assert.Less(_quill.FirstSeenAt, 0f, "24 m away: not seen yet");
            _sk.Motor.Teleport(new Vector3(0f, 0.05f, 0f));
            Loop.Step();
            Assert.GreaterOrEqual(_quill.FirstSeenAt, 0f, "first sight");
            Seconds(2f);
            PingQuill();
            Assert.IsTrue(_quill.Unmasked);
            Assert.AreEqual(1, _stones.Intel.Forged, "-1 Intel");
            Assert.AreEqual(1, _dossier.Fragments.Count, "a page fell from his coat");
            Seconds(Scout.FleeTime + 0.2f);
            Assert.IsFalse(_quill.gameObject.activeSelf, "gone");
        }

        [UnityTest]
        public IEnumerator A_Late_Ping_Is_Shrugged_Off_But_Read_The_Room_Catches_Him()
        {
            yield return Stage();
            _sk.Motor.Teleport(new Vector3(0f, 0.05f, 0f));
            Seconds(3.5f);
            PingQuill();
            Assert.IsFalse(_quill.Unmasked, "the moment passed");
            ReadTheRoom.Begin(Ctx, _sk, 6f, 20f);
            Assert.IsTrue(_quill.Unmasked, "read: the pendant shows");
        }

        [UnityTest]
        public IEnumerator Missed_He_Reports_At_The_Campfire()
        {
            yield return Stage();
            Assert.AreEqual(0, _stones.IntelLevel);
            _quill.Report(_stones);
            Assert.AreEqual(1, _stones.IntelLevel, "+1 Intel");
            _quill.Report(_stones);
            Assert.AreEqual(1, _stones.IntelLevel, "once");
        }

        [UnityTest]
        public IEnumerator He_Gives_One_Ration()
        {
            yield return Stage();
            Assert.IsTrue(_quill.CanInteract(_sk));
            _quill.Interact(_sk);
            Assert.AreEqual(1, _sk.Rations.Count);
            Assert.IsFalse(_quill.CanInteract(_sk), "once");
        }
    }
}
