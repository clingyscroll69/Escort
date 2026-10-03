using System;
using System.Collections.Generic;
using HS.Core;
using HS.Flow;
using HS.Sidekick;
using HS.Skills;
using HS.UI;
using UnityEngine;

namespace HS.Tutorial
{
    /// <summary>
    /// Decides when a lesson shows (spec §4): lessons are offered by <see cref="LessonTriggers"/> the first time their
    /// situation happens; this queues them by priority, keeps one toast on screen at a time with a short gap between
    /// toasts, drops a tip whose moment has passed (it can come back the next time), pauses the game for the three
    /// freeze-frame lessons at calm moments, and marks each lesson seen as it shows (Restore Points never repeat one).
    /// Reads the run; changes nothing in it except pausing the sim while a freeze-frame card is up.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        sealed class Pending
        {
            public Lesson L;
            public float At;
            public Func<bool> Done;
            public Func<Vector3?> Marker;
            public Func<Rect?> Spot;
            /// <summary>A freeze-frame's follow-up: shows right after it.</summary>
            public bool Chained;
            /// <summary>A tip a freeze-frame interrupted: comes back after the follow-ups.</summary>
            public bool Resumed;
            public int Seq;
        }

        public const float ToastGap = 3f, QuietAfterFocus = 2f, CoachSeconds = 7f;
        static readonly Dictionary<string, string> Chains = new Dictionary<string, string> { { "hero_rules", "insight" }, { "cone", "salute" } };

        public TipView View { get; private set; }
        public GameFlow Flow { get; private set; }
        public LessonTriggers Triggers { get; private set; }
        public event Action<Lesson> Shown;
        /// <summary>The lesson on screen left it (continued, timed out, done, or stepped aside for a freeze-frame).</summary>
        public event Action<Lesson> Closed;
        public event Action<string> Completed;

        /// <summary>The lesson on screen (toast or freeze-frame), or null.</summary>
        public string Showing => _current != null ? _current.L.Id : null;
        public bool FocusOpen => _current != null && _gate != null;

        readonly List<Pending> _queue = new List<Pending>();
        Pending _current;
        object _gate;
        float _lastToastEnd = -99f, _quietUntil = -99f;
        int _seq;

        public static TutorialDirector Create(GameFlow flow)
        {
            var go = new GameObject("TutorialDirector");
            if (flow != null) go.transform.SetParent(flow.transform, false); // the flow's: it goes when the flow goes
            var d = go.AddComponent<TutorialDirector>();
            d.Flow = flow;
            var hud = flow != null && flow.Chapter != null ? flow.Chapter.Hud : null;
            d.View = TipView.Create(UIRoot.Ensure(), hud);
            d.Triggers = new LessonTriggers(d, flow);
            return d;
        }

        static float Now => Time.unscaledTime;

        /// <summary>
        /// Queue a lesson. False when tips are off, it's unknown, already seen, already queued or showing, or an inline
        /// lesson (screens show those themselves). Notices go straight to the System window.
        /// </summary>
        public bool Offer(string id, Func<bool> doneWhen = null, Func<Vector3?> marker = null, Func<Rect?> spotlight = null) =>
            Offer(id, doneWhen, marker, spotlight, false);

        bool Offer(string id, Func<bool> doneWhen, Func<Vector3?> marker, Func<Rect?> spotlight, bool chained)
        {
            if (!TutorialProgress.TipsEnabled) return false;
            var l = Lessons.Get(id);
            if (l == null || l.Kind == LessonKind.Inline || TutorialProgress.IsSeen(id)) return false;
            if (_current != null && _current.L.Id == id) return false;
            foreach (var p in _queue)
                if (p.L.Id == id) return false;
            if (l.Kind == LessonKind.Notice)
            {
                TutorialProgress.MarkSeen(id);
                RunContext.Current?.Events.RaiseNotice(l.Body);
                Shown?.Invoke(l);
                return true;
            }
            _queue.Add(new Pending { L = l, At = Now, Done = doneWhen, Marker = marker, Spot = spotlight, Chained = chained, Seq = _seq++ });
            return true;
        }

        public bool IsQueued(string id) => _queue.Exists(p => p.L.Id == id);

        /// <summary>
        /// The player did what a lesson teaches. On screen: a tick and it goes. Still queued: it never needs to show
        /// (they already know), so it's dropped and counted as learned.
        /// </summary>
        public void Complete(string id)
        {
            if (_current != null && _current.L.Id == id && _gate == null)
            {
                View.MarkToastDone();
                _current.Done = null;
                Completed?.Invoke(id);
                return;
            }
            int i = _queue.FindIndex(p => p.L.Id == id);
            if (i < 0) return;
            _queue.RemoveAt(i);
            TutorialProgress.MarkSeen(id);
            Completed?.Invoke(id);
        }

        /// <summary>Close the freeze-frame card (its Continue button and the confirm key call this).</summary>
        public void ContinueFocus()
        {
            if (_current == null || _gate == null) return;
            var done = _current.L;
            View.HideFocus();
            ModalGate.Pop(_gate);
            _gate = null;
            _current = null;
            Closed?.Invoke(done);
            _quietUntil = Now + QuietAfterFocus;
            if (done.Coach != CoachTarget.None) View.Coach(done.Coach, CoachSeconds);
            if (Chains.TryGetValue(done.Id, out var next)) Offer(next, Triggers.DoneFor(next), null, null, true);
        }

        bool CanFocus()
        {
            if (Flow == null) return true;
            var s = Flow.Current;
            if (s != GameFlow.State.Chapter && s != GameFlow.State.Duel) return false;
            if (ModalGate.Any || EndScreen.Current != null || ScreenFade.Instance != null && ScreenFade.Instance.Busy) return false;
            return SimLoop.Instance == null || !SimLoop.Instance.Paused;
        }

        void Update()
        {
            Triggers?.Tick(Time.unscaledDeltaTime);
            float now = Now;
            View.Frozen = ModalGate.Any && _gate == null; // the pause menu holds a toast where it is
            if (_current != null)
            {
                if (_gate != null) return; // waiting on Continue
                if (_current.Done != null && _current.Done())
                {
                    View.MarkToastDone();
                    _current.Done = null;
                    Completed?.Invoke(_current.L.Id);
                }
                // A freeze-frame lesson's moment is now (he's at the doorway, the duel begins): it doesn't wait behind a
                // tip. The tip steps aside and comes back after, unless it was already done.
                // (With lesson pauses off they still go first, shown as tips.)
                var focusWaiting = _queue.Find(p => p.L.Kind == LessonKind.Focus);
                bool canPreempt = focusWaiting != null && _current.L.Kind != LessonKind.Focus && (!TutorialProgress.LessonPauses || CanFocus());
                if (View.ToastVisible && canPreempt)
                {
                    View.HideToast();
                    Closed?.Invoke(_current.L);
                    if (_current.Done != null)
                    {
                        _current.At = now;
                        _current.Resumed = true;
                        _queue.Add(_current);
                    }
                    _current = null;
                }
                else
                {
                    if (View.ToastVisible) return;
                    Closed?.Invoke(_current.L);
                    _current = null;
                    _lastToastEnd = now;
                }
            }
            if (_queue.Count == 0) return;
            // A tip whose moment has passed is dropped (unseen: it can come back next time its situation does).
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var q = _queue[i];
                float expiry = q.L.Kind == LessonKind.Focus ? Mathf.Max(q.L.Expiry, 10f) : q.L.Expiry;
                if (now - q.At > expiry && !q.Chained && !q.Resumed) _queue.RemoveAt(i);
            }
            if (_queue.Count == 0) return;
            // Order: a freeze-frame lesson that can show now; its follow-ups; tips it interrupted; then priority, age.
            bool canFocus = !TutorialProgress.LessonPauses || CanFocus();
            _queue.Sort((a, b) =>
            {
                bool fa = canFocus && a.L.Kind == LessonKind.Focus, fb = canFocus && b.L.Kind == LessonKind.Focus;
                if (fa != fb) return fa ? -1 : 1;
                if (a.Chained != b.Chained) return a.Chained ? -1 : 1;
                if (a.Resumed != b.Resumed) return a.Resumed ? -1 : 1;
                return b.L.Priority != a.L.Priority ? b.L.Priority.CompareTo(a.L.Priority) : a.Seq.CompareTo(b.Seq);
            });
            var next = _queue[0];
            bool focus = next.L.Kind == LessonKind.Focus && TutorialProgress.LessonPauses;
            if (!next.Chained && now < _quietUntil) return;
            if (focus)
            {
                if (!CanFocus()) return;
            }
            else
            {
                if (!next.Chained && !next.Resumed && now - _lastToastEnd < ToastGap) return;
                if (ModalGate.Any) return;
            }
            _queue.RemoveAt(0);
            Show(next, focus);
        }

        void Show(Pending p, bool focus)
        {
            _current = p;
            TutorialProgress.MarkSeen(p.L.Id);
            Debug.Log($"[Tutorial] {p.L.Id} ({(focus ? "freeze-frame" : "tip")}) t={Time.realtimeSinceStartup:0.0}s");
            string body = Body(p.L);
            if (focus)
            {
                _gate = ModalGate.Push("lesson");
                View.ShowFocus(p.L, body, p.Spot, ContinueFocus);
            }
            else
            {
                View.ShowToast(p.L, body, p.L.Duration);
                if (p.L.Coach != CoachTarget.None) View.Coach(p.L.Coach, Mathf.Min(p.L.Duration, CoachSeconds));
                if (p.Marker != null) View.Marker(p.Marker, p.L.Duration);
                if (p.L.Kind == LessonKind.Focus && Chains.TryGetValue(p.L.Id, out var next)) Offer(next, Triggers.DoneFor(next), null, null, true);
            }
            Shown?.Invoke(p.L);
        }

        /// <summary>The lesson's text with its variables filled in and its keys as glyphs.</summary>
        public string Body(Lesson l)
        {
            var text = l.Body;
            var skills = SkillsOf();
            if (text.Contains("{coverHint}"))
            {
                int slot = SlotOf(skills, "cover_story");
                text = text.Replace("{coverHint}", slot >= 0 ? $"Cover Story ({{{KeyGlyphs.SlotToken(slot)}}}) can talk him round." : "Some tricks can talk him round.");
            }
            if (text.Contains("{slots}")) text = text.Replace("{slots}", (skills != null ? skills.SlotCount : 4).ToString());
            if (text.Contains("{loosen}"))
            {
                int slot = SlotOf(skills, "loosen_bolt");
                text = text.Replace("{loosen}", slot >= 0 ? "{" + KeyGlyphs.SlotToken(slot) + "}" : "its key");
            }
            return KeyGlyphs.Format(text);
        }

        static SkillSystem SkillsOf()
        {
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as SidekickAgent : null;
            var s = sk != null ? sk.GetComponent<SidekickSkills>() : null;
            return s != null ? s.System : null;
        }

        static int SlotOf(SkillSystem s, string id)
        {
            if (s == null) return -1;
            for (int i = 0; i < s.Loadout.Count; i++)
                if (s.Loadout[i] == id) return i;
            return -1;
        }

        void OnDestroy()
        {
            Triggers?.Unbind();
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
            if (View != null) Destroy(View.gameObject);
        }
    }
}
