using System;
using System.Collections.Generic;
using System.Linq;
using HS.Core;

namespace HS.Rooms
{
    public sealed class ModuleInfo
    {
        public string Id;
        public RoomKind Kind;
        public int Variants = 1;
        public int Chapter = 1;
    }

    [Serializable]
    public sealed class ChapterSlot
    {
        public RoomKind[] Allowed;
        public ChapterSlot(params RoomKind[] allowed) => Allowed = allowed;
    }

    public sealed class ChapterDef
    {
        public int Chapter;
        public string Name;
        /// <summary>ChapterTheme id (light, fog, ambience).</summary>
        public string Theme = "old_road";
        /// <summary>Which chapter's modules it draws from. Until a chapter's own rooms exist it borrows chapter 1's.</summary>
        public int ModuleChapter = 1;
        /// <summary>The Gallery: the door and the boss follow its rooms; no campfire.</summary>
        public bool Final;
        public List<ChapterSlot> Slots = new List<ChapterSlot>();

        public static ChapterDef For(int chapter) => chapter switch
        {
            2 => Whisperwood(),
            3 => Catacombs(),
            4 => SunkenBastion(),
            5 => Gallery(),
            _ => OldRoad(),
        };

        static ChapterSlot Any() => new ChapterSlot(RoomKind.Combat, RoomKind.Ambush, RoomKind.TrapCorridor);

        /// <summary>Chapter 1 "The Old Road": 3 rooms (GDD §11.2) — combat, traps, ambushes.</summary>
        public static ChapterDef OldRoad() => new ChapterDef
        {
            Chapter = 1, Name = "The Old Road", Theme = "old_road",
            Slots =
            {
                new ChapterSlot(RoomKind.Ambush, RoomKind.Combat),
                new ChapterSlot(RoomKind.TrapCorridor, RoomKind.Combat, RoomKind.Ambush),
                new ChapterSlot(RoomKind.Combat, RoomKind.Ambush, RoomKind.TrapCorridor),
            },
        };

        // Chapters 3–5: their rooms come with Plans 3–5. Until then they borrow the Old Road's modules under their own light.
        /// <summary>Chapter 2 "Whisperwood": attrition and ambushes — four of five forest modules, Quill's glade always.</summary>
        public static ChapterDef Whisperwood() => new ChapterDef
        {
            Chapter = 2, Name = "Whisperwood", Theme = "whisperwood", ModuleChapter = 2,
            Slots =
            {
                new ChapterSlot(RoomKind.Ambush, RoomKind.Combat),
                new ChapterSlot(RoomKind.Social),
                new ChapterSlot(RoomKind.TrapCorridor, RoomKind.SetPiece),
                new ChapterSlot(RoomKind.Combat, RoomKind.Ambush, RoomKind.SetPiece),
            },
        };

        public static ChapterDef Catacombs() => new ChapterDef
        {
            Chapter = 3, Name = "Catacombs of Ends", Theme = "catacombs", ModuleChapter = 1,
            Slots = { new ChapterSlot(RoomKind.TrapCorridor, RoomKind.Combat), Any(), Any(), Any() },
        };

        public static ChapterDef SunkenBastion() => new ChapterDef
        {
            Chapter = 4, Name = "The Sunken Bastion", Theme = "sunken_bastion", ModuleChapter = 1,
            Slots = { new ChapterSlot(RoomKind.Combat, RoomKind.Ambush), Any(), Any(), Any() },
        };

        public static ChapterDef Gallery() => new ChapterDef
        {
            Chapter = 5, Name = "The Gallery", Theme = "gallery", ModuleChapter = 1, Final = true,
            Slots = { Any(), Any() },
        };
    }

    public struct PlannedRoom
    {
        public string ModuleId;
        public int Variant;
        public RoomKind Kind;
    }

    public sealed class ChapterPlan
    {
        public int Seed;
        public int Chapter;
        public readonly List<PlannedRoom> Rooms = new List<PlannedRoom>();
        public string Signature => string.Join("|", Rooms.Select(r => $"{r.ModuleId}:{r.Variant}"));
    }

    /// <summary>
    /// Seeded level assembly (GDD §3): picks hand-authored modules and their variants per slot. The run seed is the
    /// only randomness in the game. Deterministic regardless of library order (candidates sorted by id).
    /// </summary>
    public static class RoomAssembler
    {
        public static ChapterPlan Plan(int seed, ChapterDef chapter, IReadOnlyList<ModuleInfo> library)
        {
            var rng = new DetRandom(unchecked(seed * 31 + chapter.Chapter * 7919));
            var plan = new ChapterPlan { Seed = seed, Chapter = chapter.Chapter };
            var used = new HashSet<string>();
            foreach (var slot in chapter.Slots)
            {
                var candidates = library.Where(m => slot.Allowed.Contains(m.Kind) && !used.Contains(m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
                if (candidates.Count == 0)
                    candidates = library.Where(m => slot.Allowed.Contains(m.Kind)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
                if (candidates.Count == 0) throw new InvalidOperationException($"no module for slot [{string.Join(",", slot.Allowed)}]");
                var pick = candidates[rng.Range(0, candidates.Count)];
                used.Add(pick.Id);
                plan.Rooms.Add(new PlannedRoom { ModuleId = pick.Id, Variant = rng.Range(0, Math.Max(1, pick.Variants)), Kind = pick.Kind });
            }
            return plan;
        }
    }
}
