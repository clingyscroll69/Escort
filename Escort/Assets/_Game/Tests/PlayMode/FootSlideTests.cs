using System.Collections;
using HS.Core;
using HS.Presentation;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>
    /// Slice gate 5: "animate acceptably (no obvious foot-sliding)". Runs the real sidekick prefab and measures the
    /// ground speed of the planted (lower) foot. A perfectly planted stride reads ~0 m/s; a 2x speed mismatch at
    /// 6 m/s would read ~3 m/s.
    /// </summary>
    public class FootSlideTests
    {
        GameObject _ground, _ctx, _sk;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            Object.Destroy(_sk);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctx);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject); // deferred Destroy lets the next test register into a dying loop
        }

        /// <summary>Result: (planted-foot along-travel skate speed m/s, body speed m/s).</summary>
        IEnumerator Measure(float moveMagnitude, bool walk, bool crouch, System.Action<float, float> result)
        {
#if UNITY_EDITOR
            // Every frame advances game time by exactly 1/60 s, however slowly the editor renders: the simulation and
            // the animators step identically, so the measurement no longer depends on the machine's frame rate.
            Time.captureDeltaTime = 1f / 60f;
            _ground = SidekickTests.Ground();
            _ctx = new GameObject("RunContext");
            _ctx.AddComponent<RunContext>();
            var loop = SimLoop.Ensure();
            loop.Paused = true; // stepped by hand below: one sim tick per (captured) 1/60 s frame — lockstep with animation
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Sidekick.prefab");
            Assert.NotNull(prefab, "Sidekick prefab missing (Tools/HS/Build/Gameplay Prefabs)");
            _sk = Object.Instantiate(prefab, new Vector3(0f, 0.05f, -20f), Quaternion.identity);
            Object.Destroy(_sk.GetComponent<PlayerCommands>());
            var agent = _sk.GetComponent<SidekickAgent>();
            var cmd = new ScriptedCommands { Current = new SidekickCommand { Move = Vector3.forward * moveMagnitude, Walk = walk, CrouchToggle = crouch, Skill = -1 } };
            agent.Commands = cmd;
            var anim = _sk.GetComponentInChildren<AnimDriver>().Animator;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera in the test scene
            var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            for (int f = 0; f < 48; f++) // 0.8 s: reach speed, blend settles
            {
                loop.Step();
                yield return null;
            }
            // Sample at a fixed 30 Hz (every 2nd frame) so sub-millimetre per-frame jitter doesn't read as speed;
            // 30 Hz is well above what a player perceives as sliding.
            var samples = new System.Collections.Generic.List<(Vector3 l, Vector3 r, Vector3 body, float dt)>();
            float window = 1f / 30f;
            for (int f = 0; f < 180; f++)
            {
                loop.Step();
                yield return new WaitForEndOfFrame();
                if (f % 2 == 1) samples.Add((lf.position, rf.position, agent.Position, window));
            }
            float minL = float.MaxValue, minR = float.MaxValue;
            foreach (var s in samples) { minL = Mathf.Min(minL, s.l.y); minR = Mathf.Min(minR, s.r.y); }
            float sum = 0f, bodySum = 0f, signed = 0f, lateral = 0f;
            int n = 0, m = 0;
            var along = new System.Collections.Generic.List<float>();
            for (int i = 1; i < samples.Count; i++)
            {
                var a = samples[i - 1];
                var b = samples[i];
                bodySum += Geo.Flat(b.body - a.body).magnitude / b.dt;
                m++;
                // An editor stall stretches a window and smears the pose: skip it rather than read it as sliding.
                if (b.dt > window * 2f) continue;
                if (b.l.y < minL + 0.04f && a.l.y < minL + 0.04f) { var d = (b.l - a.l) / b.dt; sum += Geo.Flat(d).magnitude; signed += d.z; lateral += Mathf.Abs(d.x); along.Add(d.z); n++; }
                if (b.r.y < minR + 0.04f && a.r.y < minR + 0.04f) { var d = (b.r - a.r) / b.dt; sum += Geo.Flat(d).magnitude; signed += d.z; lateral += Mathf.Abs(d.x); along.Add(d.z); n++; }
            }
            along.Sort();
            float median = along.Count > 0 ? along[along.Count / 2] : 99f;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            Debug.Log($"[FootSlide] params Speed={anim.GetFloat("Speed"):F2} CrouchRate={anim.GetFloat("CrouchRate"):F2} LocoRate={anim.GetFloat("LocoRate"):F2} Crouch={anim.GetBool("Crouch")} state={(st.IsName("CrouchLocomotion") ? "Crouch" : st.IsName("Locomotion") ? "Loco" : st.fullPathHash.ToString())} speedMul={st.speedMultiplier:F2} len={st.length:F2}");
            Debug.Log($"[FootSlide] contact windows {n}/{samples.Count * 2} signedZ mean {(n > 0 ? signed / n : 0):F2} median {median:F2} lateralX {(n > 0 ? lateral / n : 0):F2}");
            // Perceptible artefact = skating along the travel direction (median: robust to a stray frame);
            // lateral is dominated by swing-phase crossing.
            result(Mathf.Abs(median), bodySum / Mathf.Max(1, m));
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Jog_PlantedFoot_Does_Not_Slide()
        {
            float foot = -1f, body = -1f;
            yield return Measure(1f, false, false, (f, b) => { foot = f; body = b; });
            Debug.Log($"[FootSlide] jog: body {body:F2} m/s, planted foot {foot:F2} m/s");
            Assert.Greater(body, 5f);
            Assert.Less(foot, 0.6f, "planted foot skates along travel while jogging");
        }

        [UnityTest]
        public IEnumerator Walk_PlantedFoot_Does_Not_Slide()
        {
            float foot = -1f, body = -1f;
            yield return Measure(1f, true, false, (f, b) => { foot = f; body = b; });
            Debug.Log($"[FootSlide] walk: body {body:F2} m/s, planted foot {foot:F2} m/s");
            Assert.Less(foot, 0.6f, "planted foot skates along travel while walking");
        }

        [UnityTest]
        public IEnumerator Crouch_PlantedFoot_Does_Not_Slide()
        {
            float foot = -1f, body = -1f;
            yield return Measure(1f, false, true, (f, b) => { foot = f; body = b; });
            Debug.Log($"[FootSlide] crouch: body {body:F2} m/s, planted foot {foot:F2} m/s");
            Assert.Less(foot, 0.6f, "planted foot skates along travel while crouch-walking");
        }
    }
}
