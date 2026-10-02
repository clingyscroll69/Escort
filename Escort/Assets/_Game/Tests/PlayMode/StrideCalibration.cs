using System.Collections;
using HS.Core;
using HS.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Diagnostic: in-place stride speed of each locomotion threshold on a real character.</summary>
    public class StrideCalibration
    {
        [UnityTest]
        public IEnumerator Measure_InPlace_Stride_Speeds()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/Sidekick_Visual.prefab");
            var go = Object.Instantiate(prefab);
            var anim = go.GetComponent<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var driver = go.GetComponent<AnimDriver>();
            driver.enabled = false; // drive the parameter directly
            var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Debug.Log($"[Stride] hipsY={hips.position.y:F3} humanScale={anim.humanScale:F3}");
            foreach (var (label, speed, crouch, rate) in new[] { ("jog", 5.45f, false, 1f), ("crouch_r1", 0.66f, true, 1f), ("crouch_r3", 2.0f, true, 3f) })
            {
                anim.SetFloat("Speed", speed);
                anim.SetBool("Crouch", crouch);
                anim.SetFloat("CrouchRate", rate);
                anim.SetFloat("LocoRate", 1f);
                yield return new WaitForSeconds(0.8f);
                float minL = float.MaxValue, minR = float.MaxValue;
                var s = new System.Collections.Generic.List<(Vector3 l, Vector3 r, float dt)>();
                for (float t = 0; t < 2f; t += Time.deltaTime)
                {
                    yield return new WaitForEndOfFrame();
                    s.Add((lf.position, rf.position, Mathf.Max(1e-4f, Time.deltaTime)));
                }
                foreach (var x in s) { minL = Mathf.Min(minL, x.l.y); minR = Mathf.Min(minR, x.r.y); }
                float sum = 0; int n = 0;
                for (int i = 1; i < s.Count; i++)
                {
                    if (s[i].l.y < minL + 0.035f && s[i - 1].l.y < minL + 0.035f) { sum += Geo.Flat(s[i].l - s[i - 1].l).magnitude / s[i].dt; n++; }
                    if (s[i].r.y < minR + 0.035f && s[i - 1].r.y < minR + 0.035f) { sum += Geo.Flat(s[i].r - s[i - 1].r).magnitude / s[i].dt; n++; }
                }
                Debug.Log($"[Stride] {label}: param {speed:F3} → contact-foot ground speed {(n > 0 ? sum / n : 0):F3} m/s (frames {n})");
            }
            Object.Destroy(go);
#endif
            yield break;
        }
    }
}
