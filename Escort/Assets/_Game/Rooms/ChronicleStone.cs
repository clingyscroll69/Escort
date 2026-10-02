using System;
using System.Collections.Generic;
using HS.Core;
using HS.Presentation;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Chronicle stone (GDD §4.3): a scrying stone that records deeds and relays them to the taverns. States Dormant,
    /// Active, Broken. While Active it sees 200° / 14 m with line of sight (its view is drawn on the ground so the player
    /// can play around it); it never records the sidekick herself (you're unlisted) — but a dirty deed in its view becomes
    /// part of the hero's story. Breakable: three knife cuts or one crossbow bolt. Which events wake it and what it
    /// records is the hero's stone policy (<see cref="StoneSystem"/>).
    /// </summary>
    [RequireComponent(typeof(StoneAnchor))]
    public sealed class ChronicleStone : Agent, HS.Sidekick.IBreakable, IDynamicVisual
    {
        public enum StoneState { Dormant, Active, Broken }

        public static readonly List<ChronicleStone> All = new List<ChronicleStone>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public override Faction Faction => Faction.Neutral;
        public override int TickOrder => TickOrders.Status;
        public override bool Immovable => true;

        public float MaxHp = 24f;
        public float EyeHeight = 1.25f;
        public StoneState State { get; private set; } = StoneState.Dormant;
        /// <summary>Flaw clips this stone holds that have not been relayed yet (lost if it breaks).</summary>
        public int PendingClips;
        public event Action<ChronicleStone, StoneState> StateChanged;

        StoneAnchor _anchor;
        float _linger;
        Renderer[] _renderers;
        int[] _gemIndex;
        MaterialPropertyBlock _mpb;
        Transform _model;
        float _pulse;

        public float ViewRange => _anchor != null ? _anchor.ViewRange : 14f;
        public float ViewAngle => _anchor != null ? _anchor.ViewAngle : 200f;
        public Vector3 Eye => Position + Vector3.up * EyeHeight;

        protected override void Awake()
        {
            base.Awake();
            Health = new Health(MaxHp);
            _anchor = GetComponent<StoneAnchor>();
            _mpb = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _gemIndex = new int[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _gemIndex[i] = -1;
                var mats = _renderers[i].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null && mats[m].name.IndexOf("Gem", StringComparison.OrdinalIgnoreCase) >= 0) _gemIndex[i] = m;
            }
            if (transform.childCount > 0) _model = transform.GetChild(0);
            var cone = GetComponent<WitnessConeView>();
            if (cone == null) cone = gameObject.AddComponent<WitnessConeView>();
            cone.Base = new Color(0.55f, 0.85f, 1f, 0.11f);
            cone.Emphasised = new Color(0.55f, 0.85f, 1f, 0.22f);
            cone.Angle = () => ViewAngle;
            cone.Range = () => ViewRange;
            cone.Visible = () => State == StoneState.Active;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!All.Contains(this)) All.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            All.Remove(this);
        }

        /// <summary>Can this stone see the point right now (Active, in view, line of sight)?</summary>
        public bool Sees(Vector3 point)
        {
            if (State != StoneState.Active) return false;
            if (Geo.FlatDistance(Position, point) > ViewRange) return false;
            if (Geo.AngleTo(Position, Forward, point) > ViewAngle * 0.5f) return false;
            return HS.Hero.WitnessCone.HasLineOfSight(Eye, point + Vector3.up * 1.0f);
        }

        /// <summary>Could it see the point if it were awake (for waking decisions)?</summary>
        public bool InView(Vector3 point)
        {
            if (State == StoneState.Broken) return false;
            if (Geo.FlatDistance(Position, point) > ViewRange) return false;
            if (Geo.AngleTo(Position, Forward, point) > ViewAngle * 0.5f) return false;
            return HS.Hero.WitnessCone.HasLineOfSight(Eye, point + Vector3.up * 1.0f);
        }

        /// <summary>Wake (or stay awake) for at least <paramref name="seconds"/>.</summary>
        public void Wake(float seconds)
        {
            if (State == StoneState.Broken) return;
            _linger = Mathf.Max(_linger, seconds);
            if (State != StoneState.Active)
            {
                SetState(StoneState.Active);
                HS.Audio.AudioDirector.Instance?.Play("chime", Position, 0.5f, 0.5f, 0f);
            }
        }

        void SetState(StoneState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(this, s);
        }

        protected override void OnSimTick(float dt)
        {
            Motor.Move(Vector3.zero, 100f, dt);
            if (State != StoneState.Active) return;
            _linger -= dt;
            if (_linger <= 0f) SetState(StoneState.Dormant);
        }

        protected override void OnHurt(DamageInfo d, float applied)
        {
            Vfx.Burst(VfxKind.Sparks, Position + Vector3.up * 1.0f, 0.5f);
        }

        protected override void OnDied(DamageInfo d)
        {
            base.OnDied(d);
            PendingClips = 0; // what it saw dies with it
            SetState(StoneState.Broken);
            Vfx.Burst(VfxKind.Dust, Position + Vector3.up * 0.6f, 1.2f);
            if (_model != null)
            {
                _model.localRotation = Quaternion.Euler(16f, 0f, 9f) * _model.localRotation;
                _model.localPosition += Vector3.down * 0.18f;
            }
            RunContext.Current?.Events.RaiseThought("That's one less pair of eyes.");
        }

        void LateUpdate()
        {
            if (_renderers == null) return;
            _pulse += Time.deltaTime;
            float glow = State == StoneState.Active ? 1.6f + 0.9f * Mathf.Sin(_pulse * 5f) : State == StoneState.Dormant ? 0.15f : 0f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null || r is ParticleSystemRenderer || r.gameObject.name == "WitnessCone") continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Desaturate", State == StoneState.Broken ? 0.85f : 0f);
                _mpb.SetColor("_EmissionColor", _gemIndex[i] < 0 ? Color.black : new Color(0.35f, 0.85f, 1f) * glow);
                if (_gemIndex[i] >= 0) r.SetPropertyBlock(_mpb, _gemIndex[i]);
                else r.SetPropertyBlock(_mpb);
            }
        }
    }
}
