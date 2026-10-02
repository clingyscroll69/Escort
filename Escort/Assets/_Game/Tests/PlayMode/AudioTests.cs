using System.Collections;
using HS.Audio;
using HS.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public class AudioTests
    {
        GameObject _ctxGo, _ear;

        [SetUp]
        public void SetUp()
        {
            _ear = new GameObject("Listener", typeof(AudioListener));
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (AudioDirector.Instance) Object.DestroyImmediate(AudioDirector.Instance.gameObject);
            Object.DestroyImmediate(_ear);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        [Test]
        public void Every_Sound_Key_Has_A_Clip()
        {
            var a = AudioDirector.Ensure();
            foreach (var k in AudioDirector.Keys) Assert.Greater(a.ClipCount(k), 0, "missing clip for " + k);
        }

        [UnityTest]
        public IEnumerator Game_Events_Make_The_Right_Sounds_And_Repeats_Are_Rate_Limited()
        {
            var a = AudioDirector.Ensure();
            yield return null; // hooks the run context
            var ev = RunContext.Current.Events;
            ev.RaiseDamage(new DamageInfo { Tag = "sword", Kind = DamageKind.Blade, Amount = 26f }, 26f);
            ev.RaiseDamage(new DamageInfo { Tag = "sword", Kind = DamageKind.Blade, Amount = 26f }, 26f); // same frame: limited
            ev.RaiseSkillUsed("pocket_sand", null);
            ev.ProjectileFired?.Invoke(null, "crossbow");
            ev.HazardSprung?.Invoke(Vector3.zero, "Tripwire", null);
            a.OnFlow("Duel");
            CollectionAssert.AreEqual(new[] { "sword", "syn_whoosh", "cloth", "syn_twang", "bell", "j_duel" }, a.Recent);
        }
    }
}
