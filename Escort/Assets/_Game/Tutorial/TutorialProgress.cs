using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Tutorial
{
    /// <summary>Where the tutorial keeps what you've seen and your settings (PlayerPrefs in the game, memory in tests).</summary>
    public interface ITutorialStore
    {
        string GetString(string key, string def);
        void SetString(string key, string value);
        int GetInt(string key, int def);
        void SetInt(string key, int value);
        void Save();
    }

    public sealed class PlayerPrefsStore : ITutorialStore
    {
        public string GetString(string key, string def) => PlayerPrefs.GetString(key, def);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public int GetInt(string key, int def) => PlayerPrefs.GetInt(key, def);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    public sealed class MemoryStore : ITutorialStore
    {
        readonly Dictionary<string, string> _s = new Dictionary<string, string>();
        readonly Dictionary<string, int> _i = new Dictionary<string, int>();
        public string GetString(string key, string def) => _s.TryGetValue(key, out var v) ? v : def;
        public void SetString(string key, string value) => _s[key] = value;
        public int GetInt(string key, int def) => _i.TryGetValue(key, out var v) ? v : def;
        public void SetInt(string key, int value) => _i[key] = value;
        public void Save() { }
    }

    /// <summary>
    /// What the player has been taught (a lesson is marked seen when it is shown, so Restore Points never repeat it) and
    /// the tutorial settings: tips on/off, whether big lessons pause the game, and whether Hero Insight starts on.
    /// </summary>
    public static class TutorialProgress
    {
        const string SeenKey = "hs.tut.seen", TipsKey = "hs.tut.tips", PausesKey = "hs.tut.pauses", InsightKey = "hs.insight";

        static ITutorialStore _store;
        public static ITutorialStore Store
        {
            get => _store ??= new PlayerPrefsStore();
            set => _store = value;
        }

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _store = null;
            _cache = null;
            _cacheFor = null;
            Changed = null;
        }

        // Parsed once per store (the triggers ask every frame); every write goes through MarkSeen/ResetSeen.
        static ITutorialStore _cacheFor;
        static HashSet<string> _cache;

        static HashSet<string> ReadSeen()
        {
            if (_cache != null && ReferenceEquals(_cacheFor, Store)) return _cache;
            var raw = Store.GetString(SeenKey, "");
            var set = new HashSet<string>();
            foreach (var id in raw.Split(','))
                if (id.Length > 0) set.Add(id);
            _cacheFor = Store;
            return _cache = set;
        }

        public static bool IsSeen(string id) => !string.IsNullOrEmpty(id) && ReadSeen().Contains(id);

        public static IReadOnlyCollection<string> Seen => new List<string>(ReadSeen());

        public static void MarkSeen(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var seen = ReadSeen();
            if (!seen.Add(id)) return;
            Store.SetString(SeenKey, string.Join(",", seen));
            Store.Save();
            Changed?.Invoke();
        }

        /// <summary>"Reset tutorial": every lesson can be shown again. Settings are kept.</summary>
        public static void ResetSeen()
        {
            Store.SetString(SeenKey, "");
            Store.Save();
            _cache = null;
            Changed?.Invoke();
        }

        public static bool TipsEnabled
        {
            get => Store.GetInt(TipsKey, 1) != 0;
            set => SetFlag(TipsKey, value);
        }

        /// <summary>Freeze-frame lessons pause the game; off, they show as ordinary tips.</summary>
        public static bool LessonPauses
        {
            get => Store.GetInt(PausesKey, 1) != 0;
            set => SetFlag(PausesKey, value);
        }

        /// <summary>Hero Insight (his rule in words) at the start of a run. Off by default: the tutorial teaches the key.</summary>
        public static bool InsightDefault
        {
            get => Store.GetInt(InsightKey, 0) != 0;
            set => SetFlag(InsightKey, value);
        }

        static void SetFlag(string key, bool on)
        {
            Store.SetInt(key, on ? 1 : 0);
            Store.Save();
            Changed?.Invoke();
        }
    }
}
