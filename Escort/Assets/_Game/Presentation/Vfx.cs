using System.Collections.Generic;
using UnityEngine;

namespace HS.Presentation
{
    public enum VfxKind { Sand, Dust, Sparks, Heal, Glint, Reveal }

    /// <summary>
    /// Pooled procedural particle bursts (no authored assets besides Resources/FX materials). Presentation only.
    /// </summary>
    public static class Vfx
    {
        static readonly Dictionary<VfxKind, Stack<ParticleSystem>> Pools = new Dictionary<VfxKind, Stack<ParticleSystem>>();
        static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Pools.Clear();
            _root = null;
        }

        public static void Burst(VfxKind kind, Vector3 position, float scale = 1f)
        {
            if (!Application.isPlaying) return;
            var ps = Take(kind);
            ps.transform.position = position;
            ps.transform.localScale = Vector3.one * scale;
            ps.Clear(true);
            ps.Play(true);
            var ret = ps.gameObject.GetComponent<VfxReturn>();
            if (ret == null) ret = ps.gameObject.AddComponent<VfxReturn>();
            ret.Arm(kind, ps, 2.5f);
        }

        /// <summary>Looping campfire: flames (FX_Flame), embers (FX_Spark) and thin smoke (FX_Smoke).</summary>
        public static Transform FireAt(Transform at, float scale)
        {
            var root = new GameObject("CampfireFX").transform;
            root.SetParent(at, false);
            root.localScale = Vector3.one * scale;
            Loop(root, "Flames", "FX_Flame", 28f, 0.45f, 0.9f, 0.6f, 1.5f, 0.45f, 0.95f, new Color(1f, 0.72f, 0.35f, 0.95f), -0.3f, 0.22f);
            Loop(root, "Embers", "FX_Spark", 9f, 0.8f, 1.7f, 1.0f, 2.6f, 0.04f, 0.09f, new Color(1f, 0.6f, 0.25f, 1f), -0.35f, 0.3f);
            Loop(root, "Smoke", "FX_Smoke", 3.5f, 2.5f, 4f, 0.5f, 1.0f, 0.8f, 1.6f, new Color(0.35f, 0.33f, 0.32f, 0.28f), -0.08f, 0.25f);
            return root;
        }

        static void Loop(Transform parent, string name, string mat, float rate, float lifeMin, float lifeMax, float speedMin, float speedMax,
            float sizeMin, float sizeMax, Color color, float gravity, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 2f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.35f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, name == "Smoke" ? 2.2f : 0.35f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat(mat);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play(true);
        }

        internal static void Return(VfxKind kind, ParticleSystem ps)
        {
            if (ps == null) return;
            ps.gameObject.SetActive(false);
            if (!Pools.TryGetValue(kind, out var st)) Pools[kind] = st = new Stack<ParticleSystem>();
            st.Push(ps);
        }

        static ParticleSystem Take(VfxKind kind)
        {
            if (Pools.TryGetValue(kind, out var st) && st.Count > 0)
            {
                var p = st.Pop();
                if (p != null)
                {
                    p.gameObject.SetActive(true);
                    return p;
                }
            }
            return Create(kind);
        }

        static Material Mat(string name)
        {
            var m = Resources.Load<Material>("FX/" + name);
            if (m == null) Debug.LogWarning("[Vfx] missing Resources/FX/" + name);
            return m;
        }

        static ParticleSystem Create(VfxKind kind)
        {
            if (_root == null) _root = new GameObject("VFX").transform;
            var go = new GameObject("Vfx_" + kind);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat(kind == VfxKind.Sparks || kind == VfxKind.Glint ? "FX_Spark" : "FX_Soft");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Gradient g = new Gradient();
            switch (kind)
            {
                case VfxKind.Sand:
                    main.duration = 0.6f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f); main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                    main.startColor = new Color(0.86f, 0.74f, 0.5f, 0.85f); main.gravityModifier = 0.15f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 38) });
                    shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.9f;
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
                    break;
                case VfxKind.Dust:
                    main.duration = 0.8f; main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f); main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
                    main.startColor = new Color(0.62f, 0.56f, 0.48f, 0.9f); main.gravityModifier = -0.05f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 46) });
                    shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 1.6f; shape.rotation = new Vector3(-90f, 0f, 0f);
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
                    break;
                case VfxKind.Sparks:
                    main.duration = 0.2f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f); main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
                    main.startColor = new Color(1f, 0.85f, 0.45f, 1f); main.gravityModifier = 1.2f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
                    shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.1f;
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.2f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                    break;
                case VfxKind.Heal:
                    main.duration = 1.2f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f); main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
                    main.startColor = new Color(0.55f, 1f, 0.6f, 1f); main.gravityModifier = -0.3f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 16), new ParticleSystem.Burst(0.4f, 10) });
                    shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.5f; shape.rotation = new Vector3(-90f, 0f, 0f);
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    break;
                case VfxKind.Glint:
                case VfxKind.Reveal:
                    main.duration = 0.3f; main.startLifetime = 0.45f; main.startSpeed = 0f;
                    main.startSize = kind == VfxKind.Reveal ? 2.4f : 0.9f;
                    main.startColor = kind == VfxKind.Reveal ? new Color(1f, 0.9f, 0.6f, 0.9f) : new Color(1f, 1f, 1f, 1f);
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                    shape.enabled = false;
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1.4f));
                    break;
            }
            col.color = g;
            return ps;
        }
    }
}
