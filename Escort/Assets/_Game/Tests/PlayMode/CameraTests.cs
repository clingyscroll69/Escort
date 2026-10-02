using System.Collections;
using HS.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public class CameraTests
    {
        GameObject _a, _b;
        CameraRig _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig) Object.Destroy(_rig.gameObject);
            if (_rig && _rig.Camera) Object.Destroy(_rig.Camera.gameObject);
            if (_a) Object.Destroy(_a);
            if (_b) Object.Destroy(_b);
        }

        static void AssertInView(Camera cam, Vector3 world, string label)
        {
            var v = cam.WorldToViewportPoint(world);
            Assert.Greater(v.z, 0f, label + " behind camera");
            Assert.That(v.x, Is.InRange(0.04f, 0.96f), $"{label} x={v.x:F2}");
            Assert.That(v.y, Is.InRange(0.04f, 0.96f), $"{label} y={v.y:F2}");
        }

        [UnityTest]
        public IEnumerator FixedAngleCamera_KeepsBothTargetsInFrame()
        {
            _a = new GameObject("HeroTarget");
            _b = new GameObject("SidekickTarget");
            _a.transform.position = new Vector3(-8f, 0f, 0f);
            _b.transform.position = new Vector3(8f, 0f, 6f);
            _rig = CameraRig.Build(_a.transform, _b.transform);
            yield return new WaitForSeconds(2.5f);

            var cam = _rig.Camera;
            AssertInView(cam, _a.transform.position + Vector3.up, "hero@17m");
            AssertInView(cam, _b.transform.position + Vector3.up, "sidekick@17m");
            Assert.That(cam.transform.eulerAngles.x, Is.InRange(45f, 55f), "pitch ~50°");

            // Separate to the edge of support range (25 m): still both in frame.
            _a.transform.position = new Vector3(-12.5f, 0f, -2f);
            _b.transform.position = new Vector3(12.5f, 0f, 2f);
            yield return new WaitForSeconds(3f);
            AssertInView(cam, _a.transform.position + Vector3.up, "hero@25m");
            AssertInView(cam, _b.transform.position + Vector3.up, "sidekick@25m");
            Assert.That(cam.transform.eulerAngles.x, Is.InRange(45f, 55f), "pitch unchanged by framing");
        }

        [UnityTest]
        public IEnumerator Too_Far_Apart_In_Depth_The_Player_Keeps_The_Frame()
        {
            _a = new GameObject("HeroTarget");
            _b = new GameObject("SidekickTarget");
            _a.transform.position = new Vector3(0f, 0f, 24f);  // the hero far up the road
            _b.transform.position = new Vector3(0f, 0f, 0f);
            _rig = CameraRig.Build(_a.transform, _b.transform);
            yield return new WaitForSeconds(3f);
            AssertInView(_rig.Camera, _b.transform.position + Vector3.up, "sidekick (the player) stays in frame");
            Assert.IsTrue(_rig.HeroOffFrame, "the hero is flagged for the edge marker");
        }
    }
}
