using System;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// The Sunken Bastion's split threat (campaign spec §3.2, §4): while Callum duels on the lower floor, a crew on the
    /// gallery above cranks the sluice. Living, unhindered crew at the wheel turn it (40 s of work; more hands don't make it
    /// faster). Opened, the lower floor floods: he wades (×0.7) and the cold takes 3% of his max HP every 3 s while he
    /// stands in it (no wounds). The crew stop when killed, blinded, staggered or busy with the sidekick; the wheel stops
    /// for good when she jams it (Interact, 3 s). The crew are out of his reach (they never hold the room gate), so the
    /// flood is a cost, never a locked door.
    /// </summary>
    public sealed class SluiceWheel : MonoBehaviour, IInteractable, ISimTickable, IDynamicVisual
    {
        public float WorkSeconds = 40f;
        public float CrewRadius = 2.5f;
        public float JamTime = 3f;
        public float FloodDamageFraction = 0.03f;
        public float FloodTick = 3f;
        [Tooltip("The lower floor's water (a BogZone in water mode); inactive until the sluice opens. A child of the wheel so it is never static-batched.")]
        public BogZone Flood;
        [Tooltip("How far crew are recruited from.")]
        public float CrewRange = 16f;

        public float Progress { get; private set; }
        public bool Jammed { get; private set; }
        public bool Flooded { get; private set; }
        /// <summary>Crew at the wheel this tick.</summary>
        public int Working { get; private set; }
        public bool Done => Jammed || Flooded;
        public int TickOrder => TickOrders.Director - 5;
        public event Action<SluiceWheel> Opened, WasJammed;

        public static readonly List<SluiceWheel> All = new List<SluiceWheel>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        Transform _visual, _water;
        float _floodT, _waterY0, _rise;
        bool _half, _near;
        CrewBrain _brain;
        readonly List<EnemyAgent> _crew = new List<EnemyAgent>();

        void Awake()
        {
            _visual = transform.Find("Visual");
            _brain = new CrewBrain(this);
            if (Flood != null)
            {
                _water = Flood.transform.Find("Water");
                if (_water != null) _waterY0 = _water.localPosition.y;
                if (Application.isPlaying) Flood.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (!All.Contains(this)) All.Add(this);
            SimLoop.Register(this);
            Interactables.Register(this);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            All.Remove(this);
            SimLoop.Unregister(this);
            Interactables.Unregister(this);
        }

        /// <summary>Where a crewman stands to work it (spread round the wheel by his place in the crew).</summary>
        public Vector3 WorkSpot(EnemyAgent e)
        {
            int i = Mathf.Max(0, _crew.IndexOf(e));
            float a = (i * 137f + 30f) * Mathf.Deg2Rad;
            return transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CrewRadius * 0.6f;
        }

        public bool AtWheel(EnemyAgent e) => Geo.FlatDistance(e.Position, transform.position) <= CrewRadius;

        static bool Hindered(EnemyAgent e) =>
            e.Status.Incapacitated || e.Status.Has(StatusType.Blinded) || e.TimeSinceSidekickHurtMe < CrewBrain.Grudge;

        public void SimTick(float dt)
        {
            Recruit();
            Working = 0;
            foreach (var e in _crew)
                if (e != null && e.IsAlive && e.gameObject.activeInHierarchy && e.State == EnemyState.Engaged && !Hindered(e) && AtWheel(e)) Working++;
            if (!Done && Working > 0)
            {
                Progress = Mathf.Min(1f, Progress + dt / Mathf.Max(1f, WorkSeconds));
                if (!_half && Progress >= 0.5f)
                {
                    _half = true;
                    RunContext.Current?.Events.RaiseNotice("The sluice wheel groans. The gates above are half open.");
                }
                if (!_near && Progress >= 0.85f)
                {
                    _near = true;
                    RunContext.Current?.Events.RaiseNotice("Water spills over the sluice lip. It's nearly open.");
                }
                if (Progress >= 1f) Open();
            }
            if (Flooded) TickFlood(dt);
        }

        void Recruit()
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || e.Brain != null || e.Stats == null || !e.Stats.crew) continue;
                if (Geo.FlatDistance(e.Position, transform.position) > CrewRange) continue;
                e.Brain = _brain;
                e.Unchallengeable = true;
                if (!_crew.Contains(e)) _crew.Add(e);
            }
        }

        void Open()
        {
            if (Flooded) return;
            Flooded = true;
            if (Flood != null) Flood.gameObject.SetActive(true);
            var ctx = RunContext.Current;
            ctx?.Events.RaiseNotice("THE SLUICE OPENS. The lower floor floods.");
            if (ctx != null && ctx.Hero is HeroAgent h && h.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(FloodLines, 2);
            HS.Audio.AudioDirector.Instance?.Play("collapse", transform.position, 1f, 0.5f, 0f);
            Opened?.Invoke(this);
        }

        void TickFlood(float dt)
        {
            var hero = RunContext.Current != null ? RunContext.Current.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive || Flood == null || !Flood.Contains(hero.Position))
            {
                _floodT = 0f;
                return;
            }
            _floodT += dt;
            if (_floodT < FloodTick) return;
            _floodT -= FloodTick;
            hero.TakeDamage(DamageInfo.Make(null, hero, Mathf.Round(hero.Health.Max * FloodDamageFraction), DamageKind.Environment, "flood"));
        }

        void LateUpdate()
        {
            if (_visual != null && !Done && Working > 0)
                _visual.localRotation = Quaternion.Euler(0f, 0f, -40f * Time.deltaTime) * _visual.localRotation;
            if (_water != null && Flooded && _rise < 1f)
            {
                _rise = Mathf.MoveTowards(_rise, 1f, Time.deltaTime / 2f);
                var p = _water.localPosition;
                p.y = Mathf.Lerp(_waterY0 - 0.45f, _waterY0, _rise);
                _water.localPosition = p;
            }
        }

        // ------------------------------------------------------------------ IInteractable: jam it
        public Vector3 InteractPosition => transform.position;
        public string Prompt => "Jam the sluice wheel";
        public float InteractDuration => JamTime;
        public bool CanInteract(Agent who) => who is SidekickAgent && !Done;

        public void Interact(Agent who)
        {
            if (Done) return;
            Jammed = true;
            Vfx.Burst(VfxKind.Sparks, transform.position + Vector3.up * 1.2f, 1f);
            var ctx = RunContext.Current;
            ctx?.Events.RaiseNotice("A bar through the spokes. That wheel isn't turning again.");
            ctx?.Events.RaiseSkillUsed("jam_sluice", who);
            WasJammed?.Invoke(this);
        }

        static readonly string[] FloodLines = { "The floor's flooding! Fight on — it's only water.", "Cold. Very cold. Onward." };

        /// <summary>
        /// The crew's job comes first: walk to the wheel and turn it. Hurt by the sidekick, a crewman answers her (the default
        /// fight) for a few seconds, then goes back to work. Once the wheel is done they stand idle by it — out of his reach.
        /// </summary>
        sealed class CrewBrain : IEnemyController
        {
            public const float Grudge = 3.5f;
            readonly SluiceWheel _wheel;
            readonly Dictionary<EnemyAgent, float> _turnT = new Dictionary<EnemyAgent, float>();

            public CrewBrain(SluiceWheel wheel) => _wheel = wheel;

            public bool Tick(EnemyAgent e, float dt)
            {
                if (_wheel == null || !_wheel.isActiveAndEnabled) return false;
                if (e.State != EnemyState.Engaged) return false; // dormant until the encounter starts; hidden; yielding
                if (e.Status.Incapacitated || e.Status.Has(StatusType.Blinded)) return false; // stumbling: the default handles it
                if (e.TimeSinceSidekickHurtMe < Grudge) return false; // she hurt him: he answers her
                var spot = _wheel.WorkSpot(e);
                float d = Geo.FlatDistance(e.Position, spot);
                if (d > 0.5f && !(_wheel.Done && _wheel.AtWheel(e)))
                {
                    var dir = Geo.DirTo(e.Position, spot);
                    e.Motor.Move(dir * e.Stats.speed, 30f, dt);
                    e.Motor.FaceDirection(dir, 400f, dt);
                    e.Presenter?.SetLocomotion(e.Motor.Speed, false);
                    return true;
                }
                e.Motor.Move(Vector3.zero, 30f, dt);
                e.Motor.FaceDirection(Geo.DirTo(e.Position, _wheel.transform.position), 400f, dt);
                e.Presenter?.SetLocomotion(0f, false);
                if (_wheel.Done) return true;
                _turnT.TryGetValue(e, out float t);
                t -= dt;
                if (t <= 0f)
                {
                    t = 2f;
                    e.Presenter?.PlayAction("interact", 1.8f);
                }
                _turnT[e] = t;
                return true;
            }
        }
    }
}
