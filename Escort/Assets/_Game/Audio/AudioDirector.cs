using System.Collections.Generic;
using HS.Core;
using HS.Rooms;
using UnityEngine;

namespace HS.Audio
{
    /// <summary>
    /// Sound for the slice (GDD §9: royalty-free or self-made): CC0 Kenney packs + synthesized clips in Resources/Audio.
    /// Listens to the event bus — presentation only, never touches the simulation. One-shots are pooled, panned by screen
    /// position and rate-limited per sound so crowd fights don't turn to noise; ambience and music crossfade by game state.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public float Sfx = 0.85f, Ambience = 0.32f, Music = 0.28f;
        /// <summary>Every sound key the game uses (validated by tests: each must resolve to at least one clip).</summary>
        public static readonly string[] Keys =
        {
            "sword", "riposte", "plate", "thunk", "knife", "heavy", "trap", "chop", "thud", "punch", "syn_whoosh", "cloth", "click",
            "latch", "creak", "syn_twang", "unsheath", "j_room", "collapse", "ui_open", "ui_confirm", "ui_select", "bell", "chime",
            "syn_horn", "j_camp", "j_duel", "j_win", "j_loss", "syn_forest_loop", "syn_fire_loop", "syn_camp_pad", "syn_duel_pad",
            // the opening (GDD §8)
            "syn_open_song", "syn_open_street", "syn_open_horn", "syn_open_sting", "syn_open_ring", "ui_tick", "ui_error", "ui_glitch",
            "ui_bong",
        };
        /// <summary>The last sounds started (QA/tests).</summary>
        public readonly List<string> Recent = new List<string>();
        public int ClipCount(string key) => Clips(key).Length;
        readonly Dictionary<string, AudioClip[]> _clips = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> _last = new Dictionary<string, float>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly List<AudioSource> _cut = new List<AudioSource>();
        readonly List<double> _cutBusyUntil = new List<double>();
        AudioSource _ambA, _ambB, _musA, _musB;
        string _amb, _mus;
        RunContext _ctx;
        int _next;

        public static AudioDirector Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("AudioDirector").AddComponent<AudioDirector>();
            return Instance;
        }

        void Awake()
        {
            // Load every clip now (behind the opening's black screen): first-use loads + decodes caused 0.3–1.2 s
            // hitches mid-fight in the player build.
            foreach (var k in Keys)
            foreach (var c in Clips(k))
                if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
            for (int i = 0; i < 12; i++) _pool.Add(NewSource("Sfx" + i, false));
            _ambA = NewSource("AmbienceA", true);
            _ambB = NewSource("AmbienceB", true);
            _musA = NewSource("MusicA", true);
            _musB = NewSource("MusicB", true);
        }

        AudioSource NewSource(string n, bool loop)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        void OnDestroy()
        {
            Unhook();
            if (Instance == this) Instance = null;
        }

        AudioClip[] Clips(string key)
        {
            if (_clips.TryGetValue(key, out var c)) return c;
            var list = new List<AudioClip>();
            var single = Resources.Load<AudioClip>("Audio/" + key);
            if (single != null) list.Add(single);
            for (int i = 0; i < 8; i++)
            {
                var v = Resources.Load<AudioClip>($"Audio/{key}_{i}");
                if (v == null) break;
                list.Add(v);
            }
            c = list.ToArray();
            _clips[key] = c;
            return c;
        }

        /// <summary>Play a one-shot (variants key_0..n picked at random); pans toward the world position on screen.</summary>
        public void Play(string key, Vector3? world = null, float volume = 1f, float minGap = 0.05f, float pitchJitter = 0.06f)
        {
            if (AudioListener.volume <= 0f || !HasListener()) return;
            float now = Time.unscaledTime;
            if (_last.TryGetValue(key, out var t) && now - t < minGap) return;
            var clips = Clips(key);
            if (clips.Length == 0) return;
            _last[key] = now;
            Recent.Add(key);
            if (Recent.Count > 64) Recent.RemoveAt(0);
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.clip = clips[Random.Range(0, clips.Length)]; // presentation-only randomness
            src.volume = Sfx * volume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.panStereo = 0f;
            var cam = Camera.main;
            if (world.HasValue && cam != null)
            {
                var vp = cam.WorldToViewportPoint(world.Value);
                src.panStereo = Mathf.Clamp((vp.x - 0.5f) * 1.2f, -0.6f, 0.6f);
            }
            src.Play();
        }

        int _listenerFrame = -1;
        bool _hasListener;

        /// <summary>No listener (test scenes, odd setups): stay silent rather than make Unity complain every frame.</summary>
        bool HasListener()
        {
            if (_listenerFrame != Time.frameCount)
            {
                _listenerFrame = Time.frameCount;
                _hasListener = FindAnyObjectByType<AudioListener>() != null;
            }
            return _hasListener;
        }

        /// <summary>
        /// Cutscene audio on dedicated voices (one-shots never steal them), started and stopped on exact samples so the
        /// opening's song, street and truck all cut together. endDsp &lt;= startDsp plays the clip out. Returns null when
        /// there is nothing to hear (muted, no listener, no clip).
        /// </summary>
        public AudioSource Schedule(string key, double startDsp, double endDsp, float volume)
        {
            if (AudioListener.volume <= 0f || !HasListener()) return null;
            var clips = Clips(key);
            if (clips.Length == 0) return null;
            // A scheduled voice may not report isPlaying before it starts: track when each one is free again.
            double now = AudioSettings.dspTime;
            int i = _cutBusyUntil.FindIndex(b => b < now);
            if (i < 0)
            {
                _cut.Add(NewSource("Cutscene" + _cut.Count, false));
                _cutBusyUntil.Add(0);
                i = _cut.Count - 1;
            }
            var s = _cut[i];
            s.clip = clips[0];
            s.volume = volume;
            s.pitch = 1f;
            s.panStereo = 0f;
            s.PlayScheduled(startDsp);
            if (endDsp > startDsp) s.SetScheduledEndTime(endDsp);
            _cutBusyUntil[i] = endDsp > startDsp ? endDsp : startDsp + clips[0].length;
            Recent.Add(key);
            if (Recent.Count > 64) Recent.RemoveAt(0);
            return s;
        }

        /// <summary>Silence every cutscene voice (a skipped opening).</summary>
        public void StopCutscene()
        {
            for (int i = 0; i < _cut.Count; i++)
            {
                if (_cut[i] != null) _cut[i].Stop();
                _cutBusyUntil[i] = 0;
            }
        }

        /// <summary>Crossfade the ambience and music beds (null = silence).</summary>
        public void SetBeds(string ambience, string music)
        {
            if (ambience != _amb)
            {
                _amb = ambience;
                (_ambA, _ambB) = (_ambB, _ambA);
                StartBed(_ambA, ambience);
            }
            if (music != _mus)
            {
                _mus = music;
                (_musA, _musB) = (_musB, _musA);
                StartBed(_musA, music);
            }
        }

        void StartBed(AudioSource s, string key)
        {
            var c = key != null && HasListener() ? Clips(key) : null;
            if (c == null || c.Length == 0)
            {
                s.Stop();
                return;
            }
            s.clip = c[0];
            s.volume = 0f;
            s.time = 0f;
            s.Play();
        }

        void Update()
        {
            var ctx = RunContext.Current;
            if (ctx != _ctx)
            {
                Unhook();
                _ctx = ctx;
                Hook();
            }
            float k = Time.unscaledDeltaTime / 1.5f; // 1.5 s crossfades
            _ambA.volume = Mathf.MoveTowards(_ambA.volume, _amb != null ? Ambience : 0f, k * Ambience);
            _ambB.volume = Mathf.MoveTowards(_ambB.volume, 0f, k * Ambience);
            _musA.volume = Mathf.MoveTowards(_musA.volume, _mus != null ? Music : 0f, k * Music);
            _musB.volume = Mathf.MoveTowards(_musB.volume, 0f, k * Music);
            if (_ambB.volume <= 0f && _ambB.isPlaying) _ambB.Stop();
            if (_musB.volume <= 0f && _musB.isPlaying) _musB.Stop();
        }

        // ------------------------------------------------------------------ event → sound
        void Hook()
        {
            if (_ctx == null) return;
            var e = _ctx.Events;
            e.Damage += OnDamage;
            e.Death += OnDeath;
            e.SkillUsed += OnSkill;
            e.DuelStarted += OnDuel;
            e.RoomCleared += OnRoomCleared;
            e.HazardSprung += OnHazard;
            e.PropCollapsed += OnCollapse;
            e.SystemNotice += OnNotice;
            e.ProjectileFired += OnFired;
            e.CoverStory += OnCover;
        }

        void Unhook()
        {
            if (_ctx == null) return;
            var e = _ctx.Events;
            e.Damage -= OnDamage;
            e.Death -= OnDeath;
            e.SkillUsed -= OnSkill;
            e.DuelStarted -= OnDuel;
            e.RoomCleared -= OnRoomCleared;
            e.HazardSprung -= OnHazard;
            e.PropCollapsed -= OnCollapse;
            e.SystemNotice -= OnNotice;
            e.ProjectileFired -= OnFired;
            e.CoverStory -= OnCover;
            _ctx = null;
        }

        void OnDamage(DamageInfo d, float applied)
        {
            var at = d.Target != null ? d.Target.Position + Vector3.up : (Vector3?)null;
            switch (d.Tag)
            {
                case "sword": Play("sword", at, 0.9f); return;
                case "riposte": Play("riposte", at); Play("plate", at, 0.6f); return;
                case "bolt":
                case "arrow":
                case "crossbow": Play("thunk", at, 0.9f); return;
                case "knife": Play("knife", at, 0.8f); return;
                case "cheap_shot":
                case "ambush": Play("knife", at); Play("heavy", at); return;
                case "spike_plate": Play("trap", at); Play("chop", at, 0.7f); return;
                case "tripwire": Play("thud", at, 0.8f); return;
                case "fever":
                case "loosen_bolt": return;
            }
            if (d.Kind == DamageKind.Heavy) Play("heavy", at);
            else if (d.Kind == DamageKind.Melee || d.Kind == DamageKind.Blade) Play("punch", at, 0.75f);
        }

        void OnDeath(Agent a, DamageInfo d) => Play("thud", a.Position, 0.9f, 0.1f);

        void OnSkill(string id, Agent user)
        {
            var at = user != null ? user.Position : (Vector3?)null;
            switch (id)
            {
                case "pocket_sand": Play("syn_whoosh", at); Play("cloth", at, 0.6f); break;
                case "dodge": Play("syn_whoosh", at, 0.55f); break;
                case "bandage": Play("cloth", at, 0.9f); break;
                case "disarm": Play("click", at); Play("latch", at, 0.7f, 0f); break;
                case "loosen_bolt": Play("creak", at, 0.8f); break;
            }
        }

        void OnFired(Agent shooter, string tag) => Play("syn_twang", shooter != null ? shooter.Position : (Vector3?)null, tag == "crossbow" ? 0.9f : 0.6f, 0.03f, 0.1f);
        void OnDuel(Agent hero, Agent target) => Play("unsheath", hero != null ? hero.Position : (Vector3?)null, 0.8f);
        void OnRoomCleared(int room) => Play("j_room", null, 0.7f, 1f, 0f);
        void OnCollapse(Vector3 at, string kind) => Play("collapse", at);
        void OnNotice(string text) => Play("ui_open", null, 0.45f, 0.3f, 0f);
        void OnCover(float restore, float window) => Play("ui_confirm", null, 0.35f, 0.3f, 0f);

        void OnHazard(Vector3 at, string kind, Agent victim)
        {
            if (kind == HazardKind.Tripwire.ToString()) Play("bell", at, 1f, 0.5f, 0f); // the alarm bell
        }

        /// <summary>Game-state beds and stingers (called by the flow).</summary>
        public void OnFlow(string state, bool won = false)
        {
            switch (state)
            {
                case "Chapter": SetBeds("syn_forest_loop", null); break;
                case "Camp": SetBeds("syn_fire_loop", "syn_camp_pad"); Play("j_camp", null, 0.7f, 1f, 0f); break;
                case "Duel": SetBeds("syn_forest_loop", "syn_duel_pad"); Play("j_duel", null, 0.8f, 1f, 0f); break;
                // The Gallery's last phase: the room falls away; only the duel pad and a cold sting are left.
                case "Mirror": SetBeds(null, "syn_duel_pad"); Play("j_loss", null, 0.55f, 1f, 0f); Play("syn_whoosh", null, 0.8f, 0f, 0f); break;
                case "End": SetBeds(won ? "syn_fire_loop" : null, null); Play(won ? "j_win" : "j_loss", null, 0.9f, 1f, 0f); break;
            }
        }
    }
}
