using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Skills
{
    public struct ProjectileSpec
    {
        public Agent Owner;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float Damage;
        public DamageKind Kind;
        public string Tag;
        public float MaxRange;
        public bool Pierce;
        public float Stagger;
        public Func<Agent, bool> HitsFaction;
    }

    /// <summary>
    /// Deterministic projectiles (bolts/arrows) stepped in the sim: segment-vs-agent tests against the registry and a
    /// linecast against level geometry. Visuals are pooled transforms interpolated in LateUpdate.
    /// </summary>
    public sealed class ProjectileSystem : MonoBehaviour, ISimTickable
    {
        public static ProjectileSystem Instance { get; private set; }
        public int TickOrder => TickOrders.Projectiles;
        public GameObject BoltVisualPrefab;
        public event Action<ProjectileSpec, Agent, Vector3> Hit;   // (spec, victim or null, point)

        sealed class Live
        {
            public ProjectileSpec Spec;
            public Vector3 Pos, PrevPos;
            public float Travelled;
            public HashSet<Agent> Struck = new HashSet<Agent>();
            public Transform Visual;
            public bool Done;
        }

        readonly List<Live> _live = new List<Live>();
        readonly Stack<Transform> _pool = new Stack<Transform>();
        int _envMask = ~0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            _envMask = LayerMask.GetMask("Default");
        }

        void OnEnable() => SimLoop.Register(this);
        void OnDisable() => SimLoop.Unregister(this);

        public static ProjectileSystem Ensure()
        {
            if (Instance != null)
            {
                // A recreated sim loop (scene/test reset) must still tick the long-lived projectile system.
                if (Instance.isActiveAndEnabled && !SimLoop.Contains(Instance)) SimLoop.Register(Instance);
                return Instance;
            }
            Instance = FindAnyObjectByType<ProjectileSystem>();
            if (Instance == null) Instance = new GameObject("Projectiles").AddComponent<ProjectileSystem>();
            return Instance;
        }

        /// <summary>Drop every projectile in flight (scene transitions).</summary>
        public void ClearAll()
        {
            foreach (var p in _live)
            {
                if (p.Visual == null) continue;
                p.Visual.gameObject.SetActive(false);
                _pool.Push(p.Visual);
            }
            _live.Clear();
        }

        public void Fire(ProjectileSpec spec)
        {
            RunContext.Current?.Events.ProjectileFired?.Invoke(spec.Owner, spec.Tag);
            spec.Direction = spec.Direction.normalized;
            var p = new Live { Spec = spec, Pos = spec.Origin, PrevPos = spec.Origin };
            p.Visual = TakeVisual();
            if (p.Visual != null)
            {
                p.Visual.position = spec.Origin;
                p.Visual.rotation = Quaternion.LookRotation(spec.Direction);
                // Readability/fairness: incoming fire is warm and bright; the sidekick's own bolts are pale.
                var trail = p.Visual.GetComponent<TrailRenderer>();
                if (trail != null)
                {
                    bool friendly = spec.Owner != null && spec.Owner.Faction == Faction.Sidekick;
                    var c = friendly ? new Color(0.9f, 0.95f, 1f, 0.9f) : new Color(1f, 0.55f, 0.2f, 0.95f);
                    trail.startColor = c;
                    trail.endColor = new Color(c.r, c.g, c.b, 0f);
                    trail.Clear();
                }
            }
            _live.Add(p);
        }

        Transform TakeVisual()
        {
            if (_pool.Count > 0)
            {
                var t = _pool.Pop();
                t.gameObject.SetActive(true);
                return t;
            }
            if (BoltVisualPrefab != null) return Instantiate(BoltVisualPrefab, transform).transform;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(0.07f, 0.38f, 0.07f);
            var holder = new GameObject("Bolt").transform;
            holder.SetParent(transform);
            go.transform.SetParent(holder, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = BoltMaterial();
            var trail = holder.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = 0.14f;
            trail.endWidth = 0.0f;
            trail.minVertexDistance = 0.1f;
            trail.sharedMaterial = Resources.Load<Material>("FX/FX_Soft");
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return holder;
        }

        static Material _boltMat;
        static Material BoltMaterial()
        {
            if (_boltMat != null) return _boltMat;
            var sh = Shader.Find("HS/Toon") ?? Shader.Find("Universal Render Pipeline/Lit");
            _boltMat = new Material(sh) { color = new Color(0.35f, 0.25f, 0.15f) };
            if (_boltMat.HasProperty("_BaseColor")) _boltMat.SetColor("_BaseColor", new Color(0.35f, 0.25f, 0.15f));
            return _boltMat;
        }

        public void SimTick(float dt)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var p = _live[i];
                if (p.Done) continue;
                float step = p.Spec.Speed * dt;
                var from = p.Pos;
                var to = from + p.Spec.Direction * step;
                // Level geometry first (walls, rocks, barricades block shots — cover matters).
                if (Physics.Linecast(from, to, out var hit, _envMask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Agent>() == null)
                {
                    to = hit.point;
                    p.Done = true;
                    Hit?.Invoke(p.Spec, null, hit.point);
                }
                var victim = FirstAgentOnSegment(p, from, to);
                if (victim != null)
                {
                    p.Struck.Add(victim);
                    var d = DamageInfo.Make(p.Spec.Owner, victim, p.Spec.Damage, p.Spec.Kind, p.Spec.Tag, p.Spec.Stagger);
                    d.Point = to;
                    victim.TakeDamage(d);
                    Hit?.Invoke(p.Spec, victim, to);
                    if (!p.Spec.Pierce) p.Done = true;
                }
                p.PrevPos = from;
                p.Pos = to;
                p.Travelled += step;
                if (p.Travelled >= p.Spec.MaxRange) p.Done = true;
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (!_live[i].Done) continue;
                if (_live[i].Visual != null)
                {
                    _live[i].Visual.gameObject.SetActive(false);
                    _pool.Push(_live[i].Visual);
                }
                _live.RemoveAt(i);
            }
        }

        Agent FirstAgentOnSegment(Live p, Vector3 a, Vector3 b)
        {
            Agent best = null;
            float bestT = float.MaxValue;
            var all = AgentRegistry.All;
            var ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (int i = 0; i < all.Count; i++)
            {
                var ag = all[i];
                if (ag == null || !ag.IsAlive || ag == p.Spec.Owner || p.Struck.Contains(ag)) continue;
                if (p.Spec.HitsFaction != null && !p.Spec.HitsFaction(ag)) continue;
                if (ag is HS.Enemies.EnemyAgent e && e.IsHidden) continue;
                var c = ag.Position + Vector3.up * 1.0f;
                float t = Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2);
                var closest = a + ab * t;
                var diff = c - closest;
                float r = ag.Radius + 0.15f;
                if (new Vector2(diff.x, diff.z).sqrMagnitude <= r * r && Mathf.Abs(diff.y) < 1.0f && t < bestT)
                {
                    bestT = t;
                    best = ag;
                }
            }
            return best;
        }

        void LateUpdate()
        {
            float a = SimLoop.Instance != null ? SimLoop.Instance.Alpha : 1f;
            foreach (var p in _live)
                if (p.Visual != null) p.Visual.position = Vector3.Lerp(p.PrevPos, p.Pos, a);
        }

        public int LiveCount => _live.Count;
    }
}
