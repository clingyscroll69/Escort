using System.Collections.Generic;
using HS.Core;
using HS.Presentation;
using HS.Rooms;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills
{
    /// <summary>
    /// Loosen Bolt target (GDD §5): kneel to arm (Interact or the skill), then it collapses when pinged or when the hero
    /// passes with enemies under it and himself clear. Heavy area damage to everyone in the impact zone — including the
    /// hero if he's careless (friendly fire). Armed impact zones are shown on the ground so the trap is readable.
    /// </summary>
    [RequireComponent(typeof(ArmableAnchor))]
    public sealed class ArmableProp : MonoBehaviour, IInteractable, ISimTickable, IDynamicVisual
    {
        public enum PropState { Idle, Armed, Falling, Spent }

        public PropState State { get; private set; }
        public int TickOrder => TickOrders.Skills;
        public ArmableAnchor Anchor { get; private set; }
        public Vector3 ImpactPoint => Anchor.transform.TransformPoint(Anchor.ImpactOffset);
        public string Prompt => "Loosen the bolts";
        public float InteractDuration => _channelTime;
        public Vector3 InteractPosition => Anchor.transform.position;

        static readonly List<ArmableProp> All = new List<ArmableProp>();
        public static IReadOnlyList<ArmableProp> Instances => All;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        float _channelTime = 1.5f;
        float _fallT;
        Transform _visual;
        Quaternion _visualStart;
        Vector3 _fallAxis;
        GameObject _ring;
        readonly List<Agent> _scratch = new List<Agent>();
        const float FallTime = 0.5f;

        void Awake()
        {
            Anchor = GetComponent<ArmableAnchor>();
            _visual = transform.Find("Visual");
            if (_visual != null) _visualStart = _visual.localRotation;
        }

        void OnEnable()
        {
            All.Add(this);
            Interactables.Register(this);
            SimLoop.Register(this);
            if (RunContext.Current != null) RunContext.Current.Events.Ping += OnPing;
        }

        void OnDisable()
        {
            All.Remove(this);
            Interactables.Unregister(this);
            SimLoop.Unregister(this);
            if (RunContext.Current != null) RunContext.Current.Events.Ping -= OnPing;
        }

        public static int ArmedCount()
        {
            int n = 0;
            foreach (var p in All) if (p.State == PropState.Armed) n++;
            return n;
        }

        static SidekickSkills SkillsOf(Agent who) => who != null ? who.GetComponent<SidekickSkills>() : null;

        public bool CanInteract(Agent who)
        {
            if (State != PropState.Idle) return false;
            var sk = SkillsOf(who);
            if (sk == null || !sk.System.Has("loosen_bolt")) return false;
            var s = sk.System.Get("loosen_bolt");
            _channelTime = s.Def.B(s.Rank);
            return ArmedCount() < Mathf.RoundToInt(s.Def.A(s.Rank));
        }

        public void Interact(Agent who) => Arm();

        public void Arm()
        {
            if (State != PropState.Idle) return;
            State = PropState.Armed;
            ShowRing(true);
            Vfx.Burst(VfxKind.Glint, InteractPosition + Vector3.up * 1f, 0.8f);
            RunContext.Current?.Events.RaiseSkillUsed("loosen_bolt_armed", RunContext.Current.Sidekick);
        }

        void ShowRing(bool on)
        {
            if (on && _ring == null)
            {
                _ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(_ring.GetComponent<Collider>());
                _ring.name = "ArmedZone";
                _ring.transform.SetParent(transform, true);
                _ring.transform.position = ImpactPoint + Vector3.up * 0.05f;
                _ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _ring.transform.localScale = Vector3.one * Anchor.ImpactRadius * 2.1f;
                var r = _ring.GetComponent<Renderer>();
                r.sharedMaterial = Resources.Load<Material>("FX/FX_Ring");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_ring != null) _ring.SetActive(on);
        }

        void OnPing(PingInfo p)
        {
            if (State != PropState.Armed) return;
            if (Geo.FlatDistance(p.Point, InteractPosition) <= 2.6f || Geo.FlatDistance(p.Point, ImpactPoint) <= Anchor.ImpactRadius + 0.5f)
                Trigger();
        }

        public void Trigger()
        {
            if (State != PropState.Armed) return;
            State = PropState.Falling;
            _fallT = 0f;
            var dir = Geo.DirTo(InteractPosition, ImpactPoint);
            if (dir == Vector3.zero) dir = transform.forward;
            _fallAxis = Vector3.Cross(Vector3.up, dir);
        }

        public void SimTick(float dt)
        {
            var ctx = RunContext.Current;
            if (State == PropState.Armed && ctx != null && ctx.Hero != null && ctx.Hero.IsAlive)
            {
                // "When the hero passes": he's near but clear, and at least one hostile stands in the zone.
                float heroD = Geo.FlatDistance(ctx.Hero.Position, ImpactPoint);
                if (heroD < 7f && heroD > Anchor.ImpactRadius + 0.6f && HostilesInZone() > 0) Trigger();
            }
            if (State != PropState.Falling) return;
            _fallT += dt;
            if (_fallT >= FallTime) Impact();
        }

        int HostilesInZone()
        {
            AgentRegistry.InRadius(ImpactPoint, Anchor.ImpactRadius, _scratch, a => a.Faction == Faction.Hostile && !(a is HS.Enemies.EnemyAgent e && e.IsHidden));
            return _scratch.Count;
        }

        void Impact()
        {
            State = PropState.Spent;
            ShowRing(false);
            var ctx = RunContext.Current;
            var sidekick = ctx != null ? ctx.Sidekick : null;
            AgentRegistry.InRadius(ImpactPoint, Anchor.ImpactRadius, _scratch);
            Agent firstVictim = null;
            foreach (var a in _scratch.ToArray())
            {
                if (a is HS.Enemies.EnemyAgent e && e.IsHidden) e.Reveal(false);
                var d = DamageInfo.Make(sidekick, a, Anchor.Damage, DamageKind.Heavy, "loosen_bolt", 1.5f);
                d.Point = ImpactPoint;
                a.TakeDamage(d);
                if (firstVictim == null && a.Faction == Faction.Hostile) firstVictim = a;
            }
            Vfx.Burst(VfxKind.Dust, ImpactPoint, Anchor.ImpactRadius / 2.2f);
            if (ctx != null)
            {
                ctx.Events.PropCollapsed?.Invoke(ImpactPoint, Anchor.Kind.ToString());
                // A collapse on an empty road harmed nobody: an honest miss, not a dirty deed.
                if (firstVictim != null)
                    ctx.Events.RaiseSabotage(new SabotageEvent
                {
                    Tag = "loosen_bolt", Severity = SabotageSeverity.Major, Position = ImpactPoint,
                    ActorPosition = sidekick != null ? sidekick.Position : ImpactPoint, Victim = firstVictim, Time = ctx.SimTime,
                });
            }
        }

        void LateUpdate()
        {
            if (_visual == null || (State != PropState.Falling && State != PropState.Spent)) return;
            float t = State == PropState.Spent ? 1f : Mathf.Clamp01(_fallT / FallTime);
            float angle = 78f * t * t;
            _visual.localRotation = Quaternion.AngleAxis(angle, transform.InverseTransformDirection(_fallAxis)) * _visualStart;
        }
    }
}
