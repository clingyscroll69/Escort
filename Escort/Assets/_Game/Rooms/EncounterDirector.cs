using System;
using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Spawns and runs each room's encounters. Enemies wait visibly at their posts (ambushers hidden) so the hero's
    /// threshold pause is planning time for the sidekick. An encounter starts when the hero enters its zone or any of
    /// its enemies is provoked; the hero is held at the room's exit until every encounter there is resolved.
    /// </summary>
    public sealed class EncounterDirector : MonoBehaviour, ISimTickable
    {
        public int TickOrder => TickOrders.Director;
        public ChapterBuilder Chapter;
        public Func<string, GameObject> EnemyPrefab;

        public sealed class Encounter
        {
            public RoomModule Room;
            public EncounterZone Zone;
            public readonly List<EnemyAgent> Enemies = new List<EnemyAgent>();
            public bool Started, Resolved;
            public float StartedAt;
        }

        public readonly List<Encounter> Encounters = new List<Encounter>();
        readonly HashSet<int> _cleared = new HashSet<int>();
        readonly HashSet<int> _entered = new HashSet<int>();
        public event Action<Encounter> EncounterStarted;
        public event Action<Encounter> EncounterResolved;
        public event Action<RoomModule> RoomCleared;
        public event Action<RoomModule> RoomEntered;
        public int CurrentRoom { get; private set; } = -1;

        void OnEnable() => SimLoop.Register(this);
        void OnDisable() => SimLoop.Unregister(this);

        public bool IsCleared(int roomIndex) => _cleared.Contains(roomIndex);
        public IEnumerable<EnemyAgent> AllEnemies => Encounters.SelectMany(e => e.Enemies);

        public void SpawnAll()
        {
            Encounters.Clear();
            _cleared.Clear();
            _entered.Clear();
            foreach (var room in Chapter.Rooms) SpawnRoom(room);
            if (Chapter.Boss != null) { /* boss encounter is run by the rigged-duel director */ }
        }

        public void SpawnRoom(RoomModule room)
        {
            foreach (var zone in room.GetComponentsInChildren<EncounterZone>(false))
            {
                var enc = new Encounter { Room = room, Zone = zone };
                foreach (var m in room.GetComponentsInChildren<SpawnMarker>(false))
                {
                    if (m.Group != zone.Group) continue;
                    var prefab = EnemyPrefab?.Invoke(m.Archetype);
                    if (prefab == null)
                    {
                        Debug.LogWarning("[Encounter] no prefab for " + m.Archetype);
                        continue;
                    }
                    var go = Instantiate(prefab, m.transform.position + Vector3.up * 0.05f, m.transform.rotation, transform);
                    go.name = $"{room.name}_{m.Archetype}_{enc.Enemies.Count}";
                    var e = go.GetComponent<EnemyAgent>();
                    e.AgentId = go.name;
                    e.Archetype = m.Archetype;
                    e.Group = room.RoomIndex * 10 + zone.Group;
                    e.StartsHidden = m.Hidden;
                    e.StartsAsleep = m.Sleeping;
                    e.Elevated = m.Elevated;
                    e.JoinDelay = m.Delay;
                    enc.Enemies.Add(e);
                }
                Encounters.Add(enc);
            }
            if (!Encounters.Any(e => e.Room == room)) _cleared.Add(room.RoomIndex);
        }

        public void SimTick(float dt)
        {
            var ctx = RunContext.Current;
            var hero = ctx != null ? ctx.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive || Chapter == null) return;
            int roomNow = Chapter.RoomIndexAt(hero.Position.z);
            if (hero.Position.x < 150f && roomNow != CurrentRoom)
            {
                CurrentRoom = roomNow;
                if (_entered.Add(roomNow))
                {
                    RoomEntered?.Invoke(Chapter.Rooms[roomNow]);
                    ctx.Events.RoomEntered?.Invoke(roomNow);
                }
            }
            foreach (var enc in Encounters)
            {
                if (enc.Resolved) continue;
                if (!enc.Started)
                {
                    bool heroIn = Geo.FlatDistance(hero.Position, enc.Zone.transform.position) <= enc.Zone.Radius;
                    bool provoked = enc.Enemies.Any(e => e.State != EnemyState.Dormant && e.State != EnemyState.Hidden);
                    if (heroIn || provoked) BeginEncounter(enc, ctx);
                }
                else if (!enc.Enemies.All(Resolved) && enc.Enemies.All(e => Resolved(e) || e.IsHidden))
                {
                    // Nobody left to hide behind: hedge ambushers come out swinging rather than wait forever (no soft-lock).
                    foreach (var e in enc.Enemies)
                        if (e != null && e.IsHidden) e.Reveal(true);
                }
                else if (enc.Enemies.All(Resolved))
                {
                    enc.Resolved = true;
                    EncounterResolved?.Invoke(enc);
                    if (Encounters.Where(x => x.Room == enc.Room).All(x => x.Resolved) && _cleared.Add(enc.Room.RoomIndex))
                    {
                        RoomCleared?.Invoke(enc.Room);
                        ctx.Events.RoomCleared?.Invoke(enc.Room.RoomIndex);
                    }
                }
            }
            // Room gate: don't walk into the next room while bandits he can reach are still on their feet here.
            // Shooters holding a perch don't hold him up (they're the sidekick's problem; he walks on under fire).
            if (!hero.Route.AtEnd)
            {
                int nextRoom = hero.Route.Current.RoomIndex;
                hero.Route.Held = nextRoom > roomNow && !_cleared.Contains(roomNow) && HasReachableHostiles(roomNow);
            }
        }

        public bool HasReachableHostiles(int roomIndex)
        {
            foreach (var enc in Encounters)
            {
                if (enc.Resolved || enc.Room.RoomIndex != roomIndex) continue;
                foreach (var e in enc.Enemies)
                    if (!Resolved(e) && !(e.Elevated && e.IsRanged)) return true;
            }
            return false;
        }

        static bool Resolved(EnemyAgent e) =>
            e == null || !e.gameObject.activeInHierarchy || !e.IsAlive || e.State == EnemyState.Spared || e.State == EnemyState.Fleeing;

        void BeginEncounter(Encounter enc, RunContext ctx)
        {
            enc.Started = true;
            enc.StartedAt = ctx.SimTime;
            foreach (var e in enc.Enemies)
                if (e != null && e.State == EnemyState.Dormant) e.Activate();
            EncounterStarted?.Invoke(enc);
        }
    }
}
