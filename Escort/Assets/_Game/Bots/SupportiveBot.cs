using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using UnityEngine;

namespace HS.Bots
{
    /// <summary>
    /// Balance harness "Supportive" bot (GDD §11.4: scripted Moment capture) — a competent player's priorities:
    /// cover a lapse; sand a false surrender before the stab; flush a hedge ambush before he walks by; bandage him when he
    /// is hurt and safe; shoot cheaters with the crossbow only from where neither he nor a stone can see; disarm traps on
    /// his route; search caches when the room is quiet; at the duel, break the watching stone, then pick off the gallery
    /// from behind his back. Deterministic.
    /// </summary>
    public sealed class SupportiveBot : MonoBehaviour, ISidekickCommands
    {
        public string Intent { get; private set; } = "follow";
        RunContext _ctx;
        HeroAgent _hero;
        CallumModule _cm;
        float _now, _caughtAt = -99f, _lastHp;
        int _caughtThisRoom, _chapter = -1;
        readonly List<HazardMarker> _hazards = new List<HazardMarker>();
        readonly List<ExploreAnchor> _caches = new List<ExploreAnchor>();
        static readonly Vector3[] Ring = BuildRing();

        static Vector3[] BuildRing()
        {
            var r = new Vector3[12];
            for (int i = 0; i < r.Length; i++)
            {
                float a = i * Mathf.PI * 2f / r.Length;
                r[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            return r;
        }

        bool Bind()
        {
            var ctx = RunContext.Current;
            if (ctx == null || !(ctx.Hero is HeroAgent h)) return false;
            if (_hero == h) return true;
            _ctx = ctx;
            _hero = h;
            _cm = h.Module as CallumModule;
            if (_cm != null) _cm.Caught += (e, s) =>
            {
                _caughtAt = _now;
                _caughtThisRoom++;
            };
            ctx.Events.RoomEntered += r => _caughtThisRoom = 0;
            return true;
        }

        SidekickCommand Use(SidekickAgent self, string id, Vector3 aim, string why)
        {
            Intent = why;
            return new SidekickCommand { Skill = BotUtil.SlotOf(self, id), AimPoint = aim, HasAim = true };
        }

        SidekickCommand Go(SidekickAgent self, Vector3 goal, string why, bool careful = false, float stop = 0.6f)
        {
            Intent = why;
            var c = SidekickCommand.None;
            c.Move = BotUtil.MoveTo(self, goal, stop);
            c.Walk = careful && Geo.FlatDistance(self.Position, goal) < 3.2f;
            c.AimPoint = goal;
            c.HasAim = true;
            return c;
        }

        public SidekickCommand Next(SidekickAgent self)
        {
            if (!Bind() || !_hero.IsAlive) return SidekickCommand.None;
            _now = _ctx.SimTime;
            if (_ctx.Chapter != _chapter)
            {
                // Each chapter is a new road: the last one's traps and caches are gone.
                _chapter = _ctx.Chapter;
                _hazards.Clear();
                _caches.Clear();
            }
            if (self.IsChanneling)
            {
                Intent = "channel";
                return SidekickCommand.None;
            }
            float hpDrop = _lastHp - self.Health.Current;
            _lastHp = self.Health.Current;

            // 0) Self-preservation: something is swinging at me — dodge clear, then get behind him.
            var threat = AgentRegistry.Nearest(self.Position, 3f, a => a is EnemyAgent e && e.IsActive && e.Target == self && !e.IsRanged) as EnemyAgent;
            if (threat != null)
            {
                var c = Go(self, BotUtil.BehindHim(_hero, 6f, 0f), "evade");
                if (threat.IsAttackWindup || hpDrop > 0.5f) c.Dodge = true;
                return c;
            }
            // 1) Caught? Talk him round inside the window.
            if (_now - _caughtAt < 2.5f && BotUtil.Ready(self, "cover_story") && Geo.FlatDistance(self.Position, _hero.Position) <= 13f)
                return Use(self, "cover_story", _hero.Position, "cover story");
            // 2) A false surrender: blind him before the stab.
            // A witnessed trick costs Rapport (−3, −6 the second time in a fight): take that hit only when it matters.
            bool canCover = BotUtil.Ready(self, "cover_story");
            bool spendable = canCover || _caughtThisRoom == 0 || _hero.Health.Fraction < 0.4f;
            var tc = FindEnemy(e => e.State == EnemyState.Surrendered && e.Stats != null && e.Stats.cheapShotDamage > 0f, _hero.Position, 20f);
            if (tc != null && BotUtil.Ready(self, "pocket_sand") && (spendable || !BotUtil.Watched(_hero, tc.Position, self.Position)))
                return Geo.FlatDistance(self.Position, tc.Position) <= 7.5f ? Use(self, "pocket_sand", tc.Position, "sand the false surrender") : Go(self, tc.Position, "close on the false surrender");
            // 3) A hedge ambush ahead of him: flush it out first.
            // 3) A hedge ambush ahead: scout it while he's still far off (unseen), or flush it late if it must be done.
            var amb = FindEnemy(e => e.IsHidden && !e.Scripted && !(e.IsRanged && e.Elevated) && e.Position.z > _hero.Position.z - 2f, _hero.Position, 26f);
            if (amb != null && BotUtil.Ready(self, "pocket_sand"))
            {
                bool unseenNow = !BotUtil.Watched(_hero, amb.Position, self.Position);
                bool urgent = Geo.FlatDistance(_hero.Position, amb.Position) < 9f;
                if (unseenNow || (urgent && spendable))
                    return Geo.FlatDistance(self.Position, amb.Position) <= 7.5f ? Use(self, "pocket_sand", amb.Position, "flush the ambush") : Go(self, amb.Position, "scout the ambush", stop: 6.5f);
            }
            // 4) Bandage him when he's hurt and nobody is on him.
            // Safe = nobody on him and no shooter with a line on him (he holds still for the dressing).
            bool heroSafe = AgentRegistry.Nearest(_hero.Position, 6f, a => a is EnemyAgent e && e.IsActive) == null
                            && AgentRegistry.Nearest(_hero.Position, 22f, a => a is EnemyAgent e && e.IsActive && e.IsRanged) == null;
            if (_hero.Health.Fraction < 0.55f && heroSafe && BotUtil.Ready(self, "bandage"))
                return Geo.FlatDistance(self.Position, _hero.Position) <= 2.0f ? Use(self, "bandage", _hero.Position, "bandage") : Go(self, _hero.Position, "to bandage", stop: 1.6f);
            // 4b) The Gallery's last phase: the Duet ring first, then the Mirror's old habits.
            var mirrorCmd = MirrorPlay(self);
            if (mirrorCmd.HasValue) return mirrorCmd.Value;
            // 4c) The Bastion: a captive close by, or a sluice being worked — cut her loose, jam the wheel.
            var bastionCmd = BastionPlay(self);
            if (bastionCmd.HasValue) return bastionCmd.Value;
            // 5) Duel set piece: break the watching stone, then shoot the gallery unseen.
            if (_cm != null && _cm.NoAidTerms)
            {
                var stone = FirstStone();
                if (stone != null)
                {
                    if (Geo.FlatDistance(self.Position, stone.Position) > 1.3f) return Go(self, stone.Position, "to the stone", stop: 1.1f);
                    Intent = "break the stone";
                    return new SidekickCommand { Attack = true, AimPoint = stone.Position, HasAim = true, Skill = -1 };
                }
            }
            // 6) Unseen assist on a cheater: crossbow if we have it, else a fistful of sand (blinding = disabling).
            // Sand is the answer to a false surrender: keep it while a turncoat is still in the fight.
            bool keepSand = FindEnemy(e => e.Stats != null && e.Stats.cheapShotDamage > 0f && (e.IsActive || e.State == EnemyState.Surrendered), _hero.Position, 24f) != null;
            if (_cm != null && _cm.Challenged != null)
            {
                string tool = BotUtil.Ready(self, "crossbow") ? "crossbow" : !keepSand && BotUtil.Ready(self, "pocket_sand") ? "pocket_sand" : null;
                if (tool != null)
                {
                    var shot = PlanShot(self, tool == "crossbow" ? 9f : 6f, e => e.IsCheater && e != _cm.Challenged);
                    if (shot.HasValue)
                    {
                        var (spot, target) = shot.Value;
                        return Geo.FlatDistance(self.Position, spot) <= 1.2f ? Use(self, tool, target.Position, "unseen " + tool) : Sneak(self, Go(self, spot, "to a quiet angle"), true);
                    }
                }
            }
            // 6b) Flankers at his back: blind them unseen, or put a knife in one (he only minds his own duel).
            if (_cm != null && _cm.Challenged != null && _cm.Engagers >= 2)
            {
                if (!keepSand && BotUtil.Ready(self, "pocket_sand"))
                {
                    var shot = PlanShot(self, 6f, e => !e.IsRanged && e != _cm.Challenged && e.Target == _hero);
                    if (shot.HasValue)
                    {
                        var (spot, target) = shot.Value;
                        return Geo.FlatDistance(self.Position, spot) <= 1.2f ? Use(self, "pocket_sand", target.Position, "blind a flanker") : Go(self, spot, "to a flanker's back");
                    }
                }
                if (self.Health.Fraction > 0.5f)
                {
                    var fl = FindEnemy(e => e.IsActive && !e.IsRanged && e != _cm.Challenged && e.Target == _hero, self.Position, 9f);
                    if (fl != null)
                    {
                        var back = fl.Position - Geo.DirTo(fl.Position, _hero.Position) * 1.3f;
                        if (Geo.FlatDistance(self.Position, fl.Position) <= 1.6f)
                        {
                            Intent = "knife a flanker";
                            return new SidekickCommand { Attack = true, AimPoint = fl.Position, HasAim = true, Skill = -1 };
                        }
                        return Go(self, back, "behind a flanker", stop: 0.4f);
                    }
                }
            }
            // 7) Traps on his route.
            bool inFight = _cm != null && (_cm.Challenged != null || _cm.Engagers > 0);
            if (!inFight)
            {
                var hz = NextHazard();
                if (hz != null)
                {
                    float d = Geo.FlatDistance(self.Position, hz.InteractPosition);
                    if (d <= 1.9f) return new SidekickCommand { Interact = true, Skill = -1, AimPoint = hz.InteractPosition, HasAim = true };
                    return Go(self, hz.InteractPosition, "to disarm", careful: true, stop: 1.5f);
                }
                // 8) Caches, when the room is quiet.
                var cache = NextCache(self);
                if (cache != null)
                {
                    float d = Geo.FlatDistance(self.Position, cache.InteractPosition);
                    if (d <= 1.9f) return new SidekickCommand { Interact = true, Skill = -1, AimPoint = cache.InteractPosition, HasAim = true };
                    return Go(self, cache.InteractPosition, "to search", stop: 1.4f);
                }
            }
            // default: behind his back, out of his cone.
            var follow = Go(self, BotUtil.BehindHim(_hero, 6.5f, 2.5f), "follow", careful: true, stop: 1.2f);
            return Sneak(self, follow, inFight);
        }

        SidekickCommand Ping(Vector3 at, string why)
        {
            Intent = why;
            return new SidekickCommand { Ping = true, AimPoint = at, HasAim = true, Skill = -1 };
        }

        /// <summary>
        /// The Mirror: inside the Link ring, the capstone (or, with none, a ping on it); a collapse when it holds a niche
        /// under an armed prop; otherwise an Etiquette Reset whenever its cooldown allows (he answers it from the second
        /// stage of trust on; the copy never looks her way).
        /// </summary>
        SidekickCommand? MirrorPlay(SidekickAgent self)
        {
            var mirror = FindEnemy(e => e.Brain is HS.Boss.MirrorBrain, _hero.Position, 60f);
            if (mirror == null) return null;
            var brain = (HS.Boss.MirrorBrain)mirror.Brain;
            var link = _ctx.Get<HS.Skills.Impl.ILinkWindow>();
            var skills = self.GetComponent<HS.Skills.SidekickSkills>();
            var cap = skills != null ? skills.System.Capstone : null;
            if (link != null && link.Open)
            {
                if (cap != null && cap.Ready)
                {
                    Intent = "the Duet";
                    return new SidekickCommand { Skill = HS.Skills.SkillSystem.CapstoneSlot, AimPoint = mirror.Position, HasAim = true };
                }
                return Ping(mirror.Position, "the Duet (ping)");
            }
            if (brain.InNiche)
                foreach (var p in HS.Skills.ArmableProp.Instances)
                    if (p != null && p.State == HS.Skills.ArmableProp.PropState.Armed && Geo.FlatDistance(p.ImpactPoint, mirror.Position) <= p.Anchor.ImpactRadius)
                        return Ping(p.ImpactPoint, "drop it on the Mirror");
            if (brain.Current == HS.Boss.MirrorBrain.Mode.Fight && brain.EtiquetteCooldown <= 0f && HS.Boss.MirrorCounters.EtiquetteHeard(_hero.Stage)
                && Geo.FlatDistance(self.Position, mirror.Position) <= 20f)
                return Ping(mirror.Position, "etiquette reset");
            return null;
        }

        SidekickCommand? BastionPlay(SidekickAgent self)
        {
            foreach (var h in HS.Enemies.Hostage.All)
            {
                if (h == null || !h.Held || Geo.FlatDistance(h.Position, _hero.Position) > 16f) continue;
                if (Geo.FlatDistance(self.Position, h.Position) <= 1.6f) return new SidekickCommand { Interact = true, Skill = -1, AimPoint = h.Position, HasAim = true };
                return Go(self, h.Position, "to the captive", stop: 1.2f);
            }
            foreach (var w in SluiceWheel.All)
            {
                if (w == null || !w.isActiveAndEnabled || w.Done || w.Working == 0 || Geo.FlatDistance(w.transform.position, _hero.Position) > 30f) continue;
                if (Melee.CanReach(self, w.InteractPosition))
                {
                    if (Geo.FlatDistance(self.Position, w.InteractPosition) <= 1.9f) return new SidekickCommand { Interact = true, Skill = -1, AimPoint = w.InteractPosition, HasAim = true };
                    return Go(self, w.InteractPosition, "to the sluice", stop: 1.5f);
                }
                // The wheel is up on the walk, out of her hands from here: a bolt for whoever is turning it.
                var crewman = FindEnemy(e => e.Brain != null && e.Stats != null && e.Stats.crew && w.AtWheel(e), w.transform.position, 6f);
                if (crewman != null && BotUtil.Ready(self, "crossbow")) return Use(self, "crossbow", crewman.Position, "a bolt for the sluice crew");
            }
            return null;
        }

        /// <summary>With Quiet Feet, a fight is spent crouched (his cone narrows, bandits overlook you).</summary>
        static SidekickCommand Sneak(SidekickAgent self, SidekickCommand c, bool inFight)
        {
            bool want = self.HasQuietFeet && inFight;
            if (want != self.Crouched) c.CrouchToggle = true;
            return c;
        }

        /// <summary>FindObjectsByType order is not guaranteed: sort by position so choices are deterministic.</summary>
        static List<T> Sorted<T>(T[] items) where T : Component
        {
            var l = new List<T>(items);
            l.Sort((a, b) =>
            {
                var pa = a.transform.position;
                var pb = b.transform.position;
                int c = pa.z.CompareTo(pb.z);
                return c != 0 ? c : pa.x.CompareTo(pb.x);
            });
            return l;
        }

        static EnemyAgent FindEnemy(System.Func<EnemyAgent, bool> pred, Vector3 near, float range)
        {
            EnemyAgent best = null;
            float bestD = range * range;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || !e.gameObject.activeInHierarchy || !pred(e)) continue;
                float d = Geo.FlatSqrDistance(e.Position, near);
                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }
            return best;
        }

        ChronicleStone FirstStone()
        {
            foreach (var s in ChronicleStone.All)
                if (s != null && s.IsAlive && s.State != ChronicleStone.StoneState.Broken && Geo.FlatDistance(s.Position, _hero.Position) < 30f) return s;
            return null;
        }

        /// <summary>A target he can't see, a spot he can't see, a clean line that doesn't pass through him.</summary>
        (Vector3 spot, EnemyAgent target)? PlanShot(SidekickAgent self, float standOff, System.Func<EnemyAgent, bool> wanted)
        {
            (Vector3, EnemyAgent)? best = null;
            float bestCost = float.MaxValue;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || !e.IsActive || e.IsHidden || !wanted(e)) continue;
                if (Geo.FlatDistance(e.Position, _hero.Position) > 26f) continue;
                if (e.Status.Has(StatusType.Blinded)) continue; // already handled
                var aimAt = e.Position + Vector3.up * (e.Elevated ? 2.2f : 0f);
                var deedAt = new Vector3(e.Position.x, self.Position.y, e.Position.z); // where the game will judge it
                foreach (var dir in Ring)
                {
                    var spot = e.Position + dir * standOff;
                    spot.y = self.Position.y;
                    if (BotUtil.Watched(_hero, aimAt, spot) || BotUtil.Watched(_hero, deedAt, spot)) continue;
                    if (DistToSegment(_hero.Position, spot, e.Position) < 1.6f) continue; // never through him
                    if (!WitnessCone.HasLineOfSight(spot + Vector3.up * 1.3f, aimAt + Vector3.up * 1.0f)) continue;
                    float cost = Geo.FlatDistance(self.Position, spot);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = (spot, e);
                    }
                }
            }
            return best;
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = Geo.Flat(b - a);
            var ap = Geo.Flat(p - a);
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (ap - ab * t).magnitude;
        }

        HazardMarker NextHazard()
        {
            if (_hazards.Count == 0) _hazards.AddRange(Sorted(FindObjectsByType<HazardMarker>(FindObjectsSortMode.None)));
            HazardMarker best = null;
            float bestZ = float.MaxValue;
            foreach (var h in _hazards)
            {
                if (h == null || !h.isActiveAndEnabled || h.State != HazardMarker.HazardState.Armed) continue;
                var p = h.transform.position;
                if (p.z < _hero.Position.z - 1f || p.z > _hero.Position.z + 20f || Mathf.Abs(p.x - _hero.Position.x) > 8f) continue;
                if (p.z < bestZ)
                {
                    bestZ = p.z;
                    best = h;
                }
            }
            return best;
        }

        ExploreAnchor NextCache(SidekickAgent self)
        {
            if (_caches.Count == 0) _caches.AddRange(Sorted(FindObjectsByType<ExploreAnchor>(FindObjectsSortMode.None)));
            foreach (var c in _caches)
            {
                if (c == null || !c.isActiveAndEnabled || c.Searched) continue;
                // Only a short detour, and only ahead of or beside him — never double back while he walks on.
                if (Geo.FlatDistance(c.transform.position, _hero.Position) > 14f || c.transform.position.z < _hero.Position.z - 4f) continue;
                if (AgentRegistry.Nearest(c.transform.position, 12f, a => a is EnemyAgent e && e.IsActive) != null) continue;
                return c;
            }
            return null;
        }
    }
}
