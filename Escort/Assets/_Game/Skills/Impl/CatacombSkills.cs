using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Rooms;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>Read Runes (Scholar): kneel at a rune seal and read it open (channel A s, reach B m). Above board.</summary>
    public sealed class ReadRunesSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            if (SealDoor.NearestClosed(c.User.Position, s.Def.B(s.Rank), SealKind.Rune) != null) return true;
            reason = SealDoor.NearestClosed(c.User.Position, s.Def.B(s.Rank)) != null ? "That's a lock, not a ward." : "No runes here to read.";
            return false;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            if (!(c.User is SidekickAgent sk)) return false;
            var seal = SealDoor.NearestClosed(sk.Position, s.Def.B(s.Rank), SealKind.Rune);
            if (seal == null) return false;
            var dir = Geo.DirTo(sk.Position, seal.transform.position);
            if (dir != Vector3.zero) sk.Motor.FaceInstant(dir);
            var state = s;
            sk.StartChannel("Reading the runes", s.Def.A(s.Rank), "kneel", true,
                onComplete: () => seal.Open("runes"),
                onCancel: () => state.CooldownRemaining = 0f);
            return true;
        }
    }

    /// <summary>Lockpick (Fixer): open an iron gate (channel A s). Rank 2 also cuts a revealed trap from B m, at once.</summary>
    public sealed class LockpickSkill : ISkillBehaviour
    {
        const float Reach = 3f;

        static HazardMarker NearestTrap(Agent user, float range)
        {
            HazardMarker best = null;
            float bestD = range;
            foreach (var h in HazardMarker.All)
            {
                if (h == null || !h.isActiveAndEnabled || h.State != HazardMarker.HazardState.Armed || !h.Visible) continue;
                float d = Geo.FlatDistance(h.transform.position, user.Position);
                if (d <= bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            if (SealDoor.NearestClosed(c.User.Position, Reach, SealKind.Gate) != null) return true;
            if (s.Rank >= 2 && NearestTrap(c.User, s.Def.B(s.Rank)) != null) return true;
            reason = SealDoor.NearestClosed(c.User.Position, Reach) != null ? "No keyhole. That's warded." : "Nothing here to pick.";
            return false;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            if (!(c.User is SidekickAgent sk)) return false;
            var gate = SealDoor.NearestClosed(sk.Position, Reach, SealKind.Gate);
            if (gate != null)
            {
                var dir = Geo.DirTo(sk.Position, gate.transform.position);
                if (dir != Vector3.zero) sk.Motor.FaceInstant(dir);
                var state = s;
                sk.StartChannel("Picking the lock", s.Def.A(s.Rank), "kneel", true,
                    onComplete: () => gate.Open("lockpick"),
                    onCancel: () => state.CooldownRemaining = 0f);
                return true;
            }
            var trap = s.Rank >= 2 ? NearestTrap(sk, s.Def.B(s.Rank)) : null;
            if (trap == null) return false;
            sk.Presenter?.PlayAction("throw", 0.4f);
            trap.Interact(sk);
            return true;
        }
    }

    /// <summary>Map Sketch (Scholar): for A s, his path shows as a dotted line; hidden plates and caches within B m are found.</summary>
    public sealed class MapSketchSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            user.Presenter?.PlayAction("interact", 1f);
            int found = HazardMarker.RevealAround(user.Position, s.Def.B(s.Rank));
            foreach (var cache in Object.FindObjectsByType<ExploreAnchor>(FindObjectsSortMode.None))
                if (!cache.Searched && Geo.FlatDistance(cache.transform.position, user.Position) <= s.Def.B(s.Rank))
                    Vfx.Burst(VfxKind.Glint, cache.transform.position + Vector3.up * 1f, 0.8f);
            var hero = c.Run != null ? c.Run.Hero as HeroAgent : null;
            if (hero != null) PathSketch.Show(hero, s.Def.A(s.Rank));
            c.Run?.Events.RaiseThought(found > 0 ? $"There — {found} plate{(found > 1 ? "s" : "")} he'd have walked onto." : "The way ahead, on paper.");
            return true;
        }
    }

    /// <summary>The dotted line of his route ahead (Map Sketch). Presentation only.</summary>
    public sealed class PathSketch : MonoBehaviour
    {
        HeroAgent _hero;
        float _until;
        LineRenderer _line;
        static PathSketch _current;

        public static PathSketch Current => _current;
        public bool Showing => _line != null && _line.enabled;

        public static void Show(HeroAgent hero, float seconds)
        {
            if (_current == null)
            {
                _current = new GameObject("PathSketch").AddComponent<PathSketch>();
                _current._line = _current.gameObject.AddComponent<LineRenderer>();
                var l = _current._line;
                l.widthMultiplier = 0.18f;
                l.material = new Material(Shader.Find("Sprites/Default"));
                l.startColor = l.endColor = new Color(0.48f, 0.9f, 1f, 0.8f);
                l.textureMode = LineTextureMode.Tile;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _current._hero = hero;
            _current._until = Time.time + seconds;
            _current._line.enabled = true;
        }

        void LateUpdate()
        {
            if (_hero == null || Time.time > _until)
            {
                _line.enabled = false;
                return;
            }
            var pts = new List<Vector3> { _hero.Position + Vector3.up * 0.15f };
            var nodes = _hero.Route.Nodes;
            for (int i = Mathf.Max(0, _hero.Route.Index); i < nodes.Count && pts.Count < 24; i++) pts.Add(nodes[i].Position + Vector3.up * 0.15f);
            _line.positionCount = pts.Count;
            _line.SetPositions(pts.ToArray());
            // dashes: fade alternate segments by colour gradient is overkill; a gently pulsing alpha reads as "sketch"
            var col = new Color(0.48f, 0.9f, 1f, 0.55f + 0.25f * Mathf.Sin(Time.time * 6f));
            _line.startColor = _line.endColor = col;
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }

    /// <summary>Buckler (Combat): raise it for A s — a blow from the front is parried (the attacker reels 1 s); rank 2 also
    /// catches shots aimed at him while she stands within B m of him.</summary>
    public sealed class BucklerSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User is SidekickAgent sk && !sk.Blocking;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            if (!(c.User is SidekickAgent sk)) return false;
            var dir = Geo.DirTo(sk.Position, c.AimPoint);
            if (dir != Vector3.zero) sk.Motor.FaceInstant(dir);
            sk.RaiseBuckler(s.Def.A(s.Rank), s.Rank >= 2 ? s.Def.B(s.Rank) : 0f);
            return true;
        }

        /// <summary>The shield that takes a shot meant for the hero (rank 2), or null.</summary>
        public static SidekickAgent GuardFor(Agent victim)
        {
            if (!(victim is HeroAgent)) return null;
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as SidekickAgent : null;
            if (sk == null || !sk.Blocking || sk.BucklerCoverRange <= 0f) return null;
            return Geo.FlatDistance(sk.Position, victim.Position) <= sk.BucklerCoverRange ? sk : null;
        }
    }
}
