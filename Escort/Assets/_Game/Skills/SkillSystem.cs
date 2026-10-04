using System;
using System.Collections.Generic;
using System.Linq;
using HS.Core;
using UnityEngine;

namespace HS.Skills
{
    /// <summary>Runtime state of one known skill.</summary>
    public sealed class SkillState
    {
        public SkillDefinition Def;
        public int Rank = 1;
        public float CooldownRemaining;
        public string Id => Def.id;
        public bool Ready => CooldownRemaining <= 0f;
        public float CooldownFraction => Def.Cooldown(Rank) <= 0f ? 0f : Mathf.Clamp01(CooldownRemaining / Def.Cooldown(Rank));
    }

    /// <summary>Everything a skill behaviour needs to act.</summary>
    public struct SkillUseContext
    {
        public Agent User;
        public Vector3 AimPoint;
        public Agent Target;
        public RunContext Run;
    }

    /// <summary>Behaviour of an active skill (Task 7). Return false from Execute to refund the cooldown.</summary>
    public interface ISkillBehaviour
    {
        bool CanUse(in SkillUseContext c, SkillState s, out string reason);
        bool Execute(in SkillUseContext c, SkillState s);
    }

    /// <summary>
    /// GDD §4.1: skill soup (any non-capstone at any level-up, second pick = rank 2), loadout slots 4/5/6 by chapter,
    /// passives don't use slots, loadouts swap only at camps. Plain C# for unit testing.
    /// </summary>
    public sealed class SkillSystem
    {
        public const int MaxRank = 2;
        /// <summary>The command slot index of the capstone's own key (never a loadout slot).</summary>
        public const int CapstoneSlot = 6;
        public readonly Dictionary<string, SkillState> Known = new Dictionary<string, SkillState>();
        readonly List<string> _loadout = new List<string>();
        readonly Dictionary<string, ISkillBehaviour> _behaviours;

        public int SlotCount { get; private set; } = 4;
        public bool AtCamp;
        public IReadOnlyList<string> Loadout => _loadout;
        public event Action<SkillState> Activated;
        public event Action<SkillState> Learned;
        public string LastFailReason { get; private set; }

        public SkillSystem(Dictionary<string, ISkillBehaviour> behaviours = null)
        {
            _behaviours = behaviours ?? new Dictionary<string, ISkillBehaviour>();
        }

        public static int SlotsForChapter(int chapter) => chapter <= 1 ? 4 : chapter == 2 ? 5 : 6;

        public void SetChapter(int chapter)
        {
            SlotCount = SlotsForChapter(chapter);
            while (_loadout.Count > SlotCount) _loadout.RemoveAt(_loadout.Count - 1);
        }

        public int RankOf(string id) => Known.TryGetValue(id, out var s) ? s.Rank : 0;
        public bool Has(string id) => Known.ContainsKey(id);
        public SkillState Get(string id) => Known.TryGetValue(id, out var s) ? s : null;

        public SkillState InSlot(int slot) => slot >= 0 && slot < _loadout.Count && _loadout[slot] != null ? Get(_loadout[slot]) : null;

        public bool CanLearn(SkillDefinition def) => def != null && !def.capstone && RankOf(def.id) < MaxRank;

        /// <summary>Level-up pick. New actives auto-fill a free slot; otherwise they wait in reserve for camp.</summary>
        public bool Learn(SkillDefinition def)
        {
            if (!CanLearn(def)) return false;
            if (Known.TryGetValue(def.id, out var s))
            {
                s.Rank++;
            }
            else
            {
                s = new SkillState { Def = def, Rank = 1 };
                Known[def.id] = s;
                if (def.UsesSlot && _loadout.Count < SlotCount) _loadout.Add(def.id);
            }
            Learned?.Invoke(s);
            return true;
        }

        /// <summary>Capstone (revealed at end of chapter 4): one of eight, exclusive.</summary>
        public bool LearnCapstone(SkillDefinition def)
        {
            if (def == null || !def.capstone || Known.Values.Any(k => k.Def.capstone)) return false;
            Known[def.id] = new SkillState { Def = def, Rank = 1 };
            return true;
        }

        /// <summary>The run's capstone, once chosen.</summary>
        public SkillState Capstone => Known.Values.FirstOrDefault(k => k.Def.capstone);

        public IEnumerable<SkillState> Reserve => Known.Values.Where(k => k.Def.UsesSlot && !k.Def.capstone && !_loadout.Contains(k.Id));

        /// <summary>Put a known active into a slot. Swapping out an equipped skill requires a camp.</summary>
        public bool Equip(string id, int slot)
        {
            if (!Known.ContainsKey(id) || !Known[id].Def.UsesSlot || Known[id].Def.capstone || slot < 0 || slot >= SlotCount) return false;
            bool freeSlot = slot >= _loadout.Count;
            if (!freeSlot && !AtCamp) return false;
            int existing = _loadout.IndexOf(id);
            if (existing >= 0 && !AtCamp) return false;
            if (freeSlot)
            {
                if (existing >= 0) _loadout.RemoveAt(existing);
                _loadout.Add(id);
                return true;
            }
            if (existing >= 0) _loadout[existing] = _loadout[slot];
            _loadout[slot] = id;
            return true;
        }

        /// <summary>Take an active out of the loadout (camp only).</summary>
        public bool Unequip(string id)
        {
            if (!AtCamp) return false;
            return _loadout.Remove(id);
        }

        public void Tick(float dt)
        {
            foreach (var s in Known.Values)
                if (s.CooldownRemaining > 0f) s.CooldownRemaining = Mathf.Max(0f, s.CooldownRemaining - dt);
        }

        public bool TryActivate(int slot, in SkillUseContext ctx)
        {
            LastFailReason = null;
            var s = slot == CapstoneSlot ? Capstone : InSlot(slot);
            if (s == null)
            {
                LastFailReason = "empty slot";
                return false;
            }
            if (!s.Ready)
            {
                LastFailReason = "cooldown";
                return false;
            }
            if (!_behaviours.TryGetValue(s.Id, out var b))
            {
                LastFailReason = "not implemented";
                return false;
            }
            if (!b.CanUse(ctx, s, out var reason))
            {
                LastFailReason = reason;
                return false;
            }
            if (!b.Execute(ctx, s)) return false;
            s.CooldownRemaining = s.Def.Cooldown(s.Rank);
            Activated?.Invoke(s);
            return true;
        }

        /// <summary>Snapshot for Restore Points (GDD §2 Saves: skills + Rapport at chapter start).</summary>
        public List<(string id, int rank)> Snapshot() => Known.Values.Select(k => (k.Id, k.Rank)).ToList();

        public List<string> LoadoutSnapshot() => new List<string>(_loadout);

        public void Restore(List<(string id, int rank)> snap, List<string> loadout, Func<string, SkillDefinition> lookup)
        {
            Known.Clear();
            _loadout.Clear();
            foreach (var (id, rank) in snap)
            {
                var def = lookup(id);
                if (def != null) Known[id] = new SkillState { Def = def, Rank = rank };
            }
            foreach (var id in loadout)
                if (Known.ContainsKey(id) && _loadout.Count < SlotCount) _loadout.Add(id);
        }
    }
}
