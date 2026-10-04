using System;
using System.Collections.Generic;
using HS.Core;
using HS.Hero;
using HS.Presentation;
using UnityEngine;

namespace HS.Rooms
{
    public enum SealKind { Rune, Gate }

    /// <summary>
    /// A seal across the way on (Catacombs, campaign spec §4). A rune seal opens to Read Runes; an iron gate to Lockpick;
    /// either to the seal key hidden in the room. Never a soft-lock: once Callum has stood at it 8 s, he breaks it — the ward
    /// pulses (a serious wound for him) and the guardians it kept wake (their markers sit under "Guardians").
    /// </summary>
    public sealed class SealDoor : MonoBehaviour, ISimTickable, IDynamicVisual
    {
        public SealKind Kind = SealKind.Rune;
        public float Width = 4.4f;
        public float BreakAfter = 8f;
        public float HeroReach = 3.4f;
        public float WardDamageFraction = 0.1f;

        public bool IsOpen { get; private set; }
        public string OpenedBy { get; private set; }
        public float HeroWait { get; private set; }
        public int TickOrder => TickOrders.Physics + 5;
        public event Action<SealDoor, string> Opened;

        public static readonly List<SealDoor> All = new List<SealDoor>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        BoxCollider _block;
        Transform _visual;

        void Awake()
        {
            _block = GetComponent<BoxCollider>();
            if (_block == null)
            {
                _block = gameObject.AddComponent<BoxCollider>();
                _block.center = new Vector3(0f, 1.5f, 0f);
                _block.size = new Vector3(Width, 3f, 0.6f);
            }
            _visual = transform.Find("Visual");
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            SimLoop.Register(this);
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            SimLoop.Unregister(this);
            All.Remove(this);
        }

        /// <summary>The side the party comes from (local −Z): where she stands to work it.</summary>
        public Vector3 WorkPosition => transform.position - transform.forward * 1.1f;

        public static SealDoor NearestClosed(Vector3 p, float range, SealKind? kind = null)
        {
            SealDoor best = null;
            float bestD = range;
            foreach (var s in All)
            {
                if (s == null || s.IsOpen || (kind.HasValue && s.Kind != kind.Value)) continue;
                float d = Geo.FlatDistance(s.WorkPosition, p);
                if (d <= bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        public void Open(string how)
        {
            if (IsOpen) return;
            IsOpen = true;
            OpenedBy = how;
            if (_block != null) _block.enabled = false;
            if (_visual != null)
            {
                // The slab sinks into the floor / the gate swings aside.
                if (Kind == SealKind.Rune) _visual.localPosition += Vector3.down * 3.2f;
                else _visual.localRotation = Quaternion.Euler(0f, 100f, 0f) * _visual.localRotation;
            }
            Vfx.Burst(how == "broken" ? VfxKind.Dust : VfxKind.Glint, transform.position + Vector3.up * 1.2f, 1.2f);
            var ctx = RunContext.Current;
            ctx?.Events.RaiseNotice(how == "broken" ? "The ward breaks with a scream of light." : Kind == SealKind.Rune ? "The runes go dark. The way is open." : "The gate swings open.");
            Opened?.Invoke(this, how);
        }

        public void SimTick(float dt)
        {
            if (IsOpen) return;
            var hero = RunContext.Current != null ? RunContext.Current.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive) return;
            bool atIt = Geo.FlatDistance(hero.Position, transform.position) <= HeroReach && transform.InverseTransformPoint(hero.Position).z < 0.5f;
            HeroWait = atIt ? HeroWait + dt : 0f;
            if (HeroWait >= BreakAfter) Break(hero);
        }

        /// <summary>Heroes are loud (GDD §4.3): he puts his shoulder to it, and the ward answers.</summary>
        void Break(HeroAgent hero)
        {
            hero.TakeDamage(DamageInfo.Make(null, hero, Mathf.Round(hero.Health.Max * WardDamageFraction), DamageKind.Environment, "ward", 0.8f));
            if (hero.IsAlive) hero.Wounds.Add(WoundType.CrackedRibs);
            if (hero.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(BreakLines, 2);
            Open("broken");
            var markers = new List<SpawnMarker>();
            var guardians = transform.Find("Guardians");
            if (guardians != null) markers.AddRange(guardians.GetComponentsInChildren<SpawnMarker>(true));
            var room = GetComponentInParent<RoomModule>();
            if (room != null)
                foreach (var m in room.GetComponentsInChildren<SpawnMarker>(false)) // the active variant's extra guardians
                    if (m.transform.parent != null && m.transform.parent.name == "SealGuardians") markers.Add(m);
            if (markers.Count > 0) FindAnyObjectByType<EncounterDirector>()?.SpawnLate(room, markers);
        }

        static readonly string[] BreakLines = { "Enough of this. Stand back!", "A knight does not wait on a door." };
    }
}
