using System.Collections;
using System.Linq;
using HS.Core;
using HS.Enemies;
using HS.Sidekick;
using HS.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Hits on enemies read clearly: a white flash, a damage number coloured by who struck, an overhead HP bar.</summary>
    public class HitFeedbackTests
    {
        HitFeedback _fb;
        GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            _ground = SidekickTests.Ground();
            new GameObject("RunContext").AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
            var cam = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            cam.transform.SetPositionAndRotation(new Vector3(0f, 7f, -9f), Quaternion.Euler(35f, 0f, 0f));
            _fb = HitFeedback.Create(UIRoot.Ensure(), null);
        }

        [TearDown]
        public void TearDown()
        {
            TestUi.TearDownAll();
            Object.Destroy(_ground);
        }

        static IEnumerator Thug(Vector3 pos, System.Action<EnemyAgent> got, bool hidden = false)
        {
            var e = Object.Instantiate(GameAssets.Load().Enemy("thug"), pos + Vector3.up * 0.05f, Quaternion.identity).GetComponent<EnemyAgent>();
            e.StartsHidden = hidden;
            yield return null; // Start: configure from tuning (and hide a lurker)
            got(e);
        }

        static Renderer ToonRenderer(EnemyAgent e) =>
            e.GetComponentsInChildren<Renderer>().First(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_FlashAmount"));

        [UnityTest]
        public IEnumerator A_Hit_Flashes_The_Enemy_Shows_The_Damage_And_A_Health_Bar()
        {
            EnemyAgent thug = null;
            yield return Thug(Vector3.zero, e => thug = e);
            float max = thug.Health.Max;
            var body = ToonRenderer(thug);
            Assert.IsFalse(body.HasPropertyBlock(), "no flash before the hit");

            thug.TakeDamage(DamageInfo.Make(null, thug, 20f, DamageKind.Melee, "test"));

            var mpb = new MaterialPropertyBlock();
            body.GetPropertyBlock(mpb);
            Assert.Greater(mpb.GetFloat("_FlashAmount"), 0.5f, "the body flashes white on the hit");
            var number = _fb.Numbers.Single();
            Assert.AreEqual("20", number.text);
            Assert.AreEqual(HitFeedback.OtherHit, (Color)number.color, "a hit with no attacker (a trap) is grey");
            yield return null;
            Assert.IsTrue(_fb.BarShown(thug), "a wounded enemy gets an overhead bar");
            Assert.AreEqual((max - 20f) / max, _fb.BarValue(thug), 1e-3f);
            var head = Camera.main.WorldToScreenPoint(thug.Position + Vector3.up * thug.Controller.height);
            var bar = RectTransformUtility.WorldToScreenPoint(null, GameObject.Find("EnemyHp").transform.position);
            Assert.AreEqual(head.x, bar.x, 2f, "the bar is centred over the head");
            Assert.That(bar.y - head.y, Is.InRange(4f, 40f), "and just above it");

            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(_fb.Flashing(thug));
            Assert.IsFalse(body.HasPropertyBlock(), "the flash cleans up after itself (back on the SRP Batcher)");

            yield return new WaitForSeconds(0.8f);
            Assert.IsEmpty(_fb.Numbers, "the number floats away");
            Assert.IsTrue(_fb.BarShown(thug), "the bar stays while the enemy is wounded");
        }

        [UnityTest]
        public IEnumerator Numbers_Say_Who_Struck_And_Only_Enemies_Get_Them()
        {
            EnemyAgent thug = null;
            yield return Thug(Vector3.zero, e => thug = e);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(-2f, 0f, 0f));
            yield return null;

            thug.TakeDamage(DamageInfo.Make(sk, thug, 12.4f, DamageKind.Knife, "knife"));
            var n = _fb.Numbers.Last();
            Assert.AreEqual("12", n.text);
            Assert.AreEqual(HitFeedback.SidekickHit, (Color)n.color, "the sidekick's hits are ochre");

            sk.TakeDamage(DamageInfo.Make(thug, sk, 10f, DamageKind.Melee, "punch"));
            Assert.AreEqual(1, _fb.Numbers.Count(), "hits on the sidekick (the HUD shows those) make no number");
        }

        [UnityTest]
        public IEnumerator Hidden_Enemies_Stay_Hidden_And_The_Bar_Goes_With_The_Dead()
        {
            EnemyAgent lurker = null, thug = null;
            yield return Thug(new Vector3(3f, 0f, 2f), e => lurker = e, hidden: true);
            yield return Thug(Vector3.zero, e => thug = e);
            Assert.IsTrue(lurker.IsHidden);

            lurker.TakeDamage(DamageInfo.Make(null, lurker, 5f, DamageKind.Trap, "tripwire"));
            yield return null;
            Assert.IsEmpty(_fb.Numbers, "a hit on a hidden enemy gives nothing away");
            Assert.IsFalse(_fb.BarShown(lurker));

            thug.TakeDamage(DamageInfo.Make(null, thug, 9999f, DamageKind.Heavy, "test"));
            Assert.IsFalse(thug.IsAlive);
            yield return null;
            Assert.IsTrue(_fb.BarShown(thug), "the bar holds a moment to show the killing blow");
            yield return new WaitForSeconds(1.3f);
            Assert.IsFalse(_fb.BarShown(thug), "then it is gone with the body");
        }
    }
}
