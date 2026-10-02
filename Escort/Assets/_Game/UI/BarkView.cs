using System.Collections.Generic;
using HS.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// Speech bubbles over the speaker's head (barks are where Callum's character lives, so they must be readable at the
    /// fixed camera), plus the sidekick's small "thought" lines. "system"/"curator" lines go to the System window.
    /// </summary>
    public sealed class BarkView : MonoBehaviour
    {
        static readonly Dictionary<string, Transform> Speakers = new Dictionary<string, Transform>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Speakers.Clear();

        public static void RegisterSpeaker(string id, Transform t) => Speakers[id] = t;

        sealed class Bubble
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public Image Accent;
            public string Speaker;
            public float Age, Duration;
            public bool Thought;
        }

        const float IconClearance = 36f;
        readonly List<Bubble> _live = new List<Bubble>();
        UIRoot _root;
        RunContext _ctx;

        public static BarkView Create(UIRoot root)
        {
            var v = root.gameObject.AddComponent<BarkView>();
            v._root = root;
            return v;
        }

        void Update()
        {
            var ctx = RunContext.Current;
            if (ctx != _ctx)
            {
                Unhook();
                _ctx = ctx;
                if (_ctx != null)
                {
                    _ctx.Events.Bark += OnBark;
                    _ctx.Events.ThoughtPopup += OnThought;
                }
            }
        }

        void OnDestroy() => Unhook();

        void Unhook()
        {
            if (_ctx == null) return;
            _ctx.Events.Bark -= OnBark;
            _ctx.Events.ThoughtPopup -= OnThought;
            _ctx = null;
        }

        Transform SpeakerTransform(string id)
        {
            if (Speakers.TryGetValue(id, out var t) && t != null) return t;
            if (_ctx == null) return null;
            if (id == "callum" || id == "hero") return _ctx.Hero != null ? _ctx.Hero.transform : null;
            if (id == "sidekick") return _ctx.Sidekick != null ? _ctx.Sidekick.transform : null;
            return null;
        }

        static Color Accent(string id) =>
            id == "callum" ? UIKit.HeroBlue : id == "sidekick" ? UIKit.Ochre : id == "ashgrave" ? UIKit.Violet : UIKit.SystemCyan;

        void OnBark(BarkInfo b)
        {
            if (b.SpeakerId == "system" || b.SpeakerId == "curator")
            {
                SystemWindow.Instance?.Show(b.Text, Mathf.Max(3f, b.Duration), b.SpeakerId == "curator");
                return;
            }
            // One bubble per speaker: a new line replaces the old one.
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].Speaker == b.SpeakerId && !_live[i].Thought) Kill(i);
            var bub = new Bubble { Speaker = b.SpeakerId, Duration = Mathf.Max(2.2f, b.Duration + b.Text.Length * 0.025f) };
            bub.Root = UIKit.Rect(_root.World, "Bark_" + b.SpeakerId, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(380f, 60f), Vector2.zero);
            bub.Group = bub.Root.gameObject.AddComponent<CanvasGroup>();
            var bg = UIKit.Image(bub.Root, "Bg", UIKit.Panel, UIKit.Paper);
            bub.Accent = UIKit.Image(bub.Root, "Accent", UIKit.Border, Accent(b.SpeakerId));
            var tail = UIKit.Rect(bub.Root, "Tail", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(26f, 22f), new Vector2(0f, 3f));
            var ti = tail.gameObject.AddComponent<Image>();
            ti.sprite = UIKit.Tail;
            ti.color = UIKit.Paper;
            ti.raycastTarget = false;
            bub.Text = UIKit.Text(bub.Root, "Text", b.Text, UIKit.Sans, 23, UIKit.Ink, TextAlignmentOptions.Center);
            bub.Text.rectTransform.offsetMin = new Vector2(16f, 10f);
            bub.Text.rectTransform.offsetMax = new Vector2(-16f, -10f);
            // size to the text (wrap at 380)
            var pref = bub.Text.GetPreferredValues(b.Text, 348f, 0f);
            float w = Mathf.Clamp(pref.x + 34f, 120f, 380f);
            float h = bub.Text.GetPreferredValues(b.Text, w - 32f, 0f).y + 22f;
            bub.Root.sizeDelta = new Vector2(w, h);
            bg.raycastTarget = false;
            _live.Add(bub);
            Position(bub);
        }

        void OnThought(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].Thought) Kill(i);
            var bub = new Bubble { Speaker = "sidekick", Duration = 2.2f, Thought = true };
            bub.Root = UIKit.Rect(_root.World, "Thought", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(420f, 34f), Vector2.zero);
            bub.Group = bub.Root.gameObject.AddComponent<CanvasGroup>();
            bub.Text = UIKit.Text(bub.Root, "Text", "<i>" + text + "</i>", UIKit.Sans, 21, new Color(1f, 0.93f, 0.8f), TextAlignmentOptions.Center);
            UIKit.Outline(bub.Text, 0.28f);
            _live.Add(bub);
            Position(bub);
        }

        void Kill(int i)
        {
            if (_live[i].Root != null) Destroy(_live[i].Root.gameObject);
            _live.RemoveAt(i);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.Age += dt;
                if (b.Age >= b.Duration || b.Root == null)
                {
                    Kill(i);
                    continue;
                }
                float fadeIn = Mathf.Clamp01(b.Age / 0.12f), fadeOut = Mathf.Clamp01((b.Duration - b.Age) / 0.35f);
                b.Group.alpha = Mathf.Min(fadeIn, fadeOut);
                Position(b);
            }
            Separate();
        }

        void Position(Bubble b)
        {
            var t = SpeakerTransform(b.Speaker);
            if (t == null)
            {
                b.Group.alpha = 0f;
                return;
            }
            // Bubbles sit above the rule-icon anchor (2.45 m) plus the icon's screen height, so they never hide it.
            float height = b.Thought ? 2.25f : 2.45f;
            float rise = b.Thought ? b.Age * 0.35f : 0f;
            if (!_root.WorldToLayer(t.position + Vector3.up * (height + rise), _root.World, out var p))
            {
                b.Group.alpha = 0f;
                return;
            }
            var half = _root.World.rect.size * 0.5f;
            var sz = b.Root.sizeDelta;
            if (!b.Thought)
            {
                // Beside the head, toward the screen centre: directly above it would cover whoever stands just
                // "north" of the speaker at this camera angle (his duel opponent, the next bandit).
                float side = p.x > 0f ? -1f : 1f;
                p.x += side * (sz.x * 0.5f + 34f);
                p.y += IconClearance * 0.25f;
            }
            p.x = Mathf.Clamp(p.x, -half.x + sz.x * 0.5f + 8f, half.x - sz.x * 0.5f - 8f);
            float top = TopBand;
            var sw = SystemWindow.Instance;
            if (sw != null && p.x + sz.x * 0.5f > half.x - sw.OccupiedWidth) top = Mathf.Max(top, sw.OccupiedFromTop);
            p.y = Mathf.Clamp(p.y, -half.y + 150f, half.y - sz.y - top);
            b.Root.anchoredPosition = p;
        }

        const float TopBand = 150f; // keep clear of the hero panel / boss bar

        /// <summary>Two bubbles must never cover each other: push the newer one up past the older.</summary>
        void Separate()
        {
            for (int i = 0; i < _live.Count; i++)
            for (int j = i + 1; j < _live.Count; j++)
            {
                var a = _live[i].Root;
                var b = _live[j].Root;
                if (a == null || b == null) continue;
                var pa = a.anchoredPosition;
                var pb = b.anchoredPosition;
                var sa = a.sizeDelta;
                var sb = b.sizeDelta;
                bool overlapX = Mathf.Abs(pa.x - pb.x) < (sa.x + sb.x) * 0.5f;
                bool overlapY = pb.y < pa.y + sa.y && pa.y < pb.y + sb.y; // pivots at the bottom
                if (overlapX && overlapY) b.anchoredPosition = new Vector2(pb.x, pa.y + sa.y + 8f);
            }
        }
    }
}
