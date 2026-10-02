using System.Collections.Generic;
using HS.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The LitRPG System window (GDD §8): monospace, typed out, cyan; errors in red with cycling glyphs before the text
    /// settles. Queued; also shows <see cref="EventBus.SystemNotice"/>.
    /// </summary>
    public sealed class SystemWindow : MonoBehaviour
    {
        public static SystemWindow Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        struct Msg
        {
            public string Text;
            public float Hold;
            public bool Error;
        }

        readonly Queue<Msg> _queue = new Queue<Msg>();
        RectTransform _panel;
        CanvasGroup _group;
        TextMeshProUGUI _title, _body;
        Image _border;
        Msg _cur;
        float _t;
        bool _showing;
        RunContext _ctx;
        const float CharsPerSecond = 55f, Width = 600f;
        const string Glyphs = "#@%&$*+=<>?/\\|×";

        public static SystemWindow Create(UIRoot root)
        {
            var v = UIKit.Stretch(root.Windows, "SystemWindow").gameObject.AddComponent<SystemWindow>();
            v.Build();
            Instance = v;
            return v;
        }

        void Build()
        {
            // Top-right: the top-centre of the screen is where the look-ahead camera shows the road ahead and its threats.
            _panel = UIKit.Rect(transform, "Panel", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(Width, 150f), new Vector2(-28f, -24f));
            _group = _panel.gameObject.AddComponent<CanvasGroup>();
            UIKit.Image(_panel, "Bg", UIKit.Panel, UIKit.SystemBg);
            _border = UIKit.Image(_panel, "Border", UIKit.Border, UIKit.SystemCyan);
            _title = UIKit.Text(_panel, "Title", "» SYSTEM", UIKit.Mono, 20, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            _title.rectTransform.offsetMin = new Vector2(22f, 0f);
            _title.rectTransform.offsetMax = new Vector2(-22f, -12f);
            _body = UIKit.Text(_panel, "Body", "", UIKit.Mono, 25, new Color(0.88f, 0.97f, 1f), TextAlignmentOptions.TopLeft);
            _body.rectTransform.offsetMin = new Vector2(24f, 18f);
            _body.rectTransform.offsetMax = new Vector2(-24f, -44f);
            _group.alpha = 0f;
        }

        public void Show(string text, float hold = 3.5f, bool error = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            _queue.Enqueue(new Msg { Text = text, Hold = hold, Error = error });
        }

        public bool Busy => _showing || _queue.Count > 0;

        /// <summary>How far down from the top of the screen the window currently reaches (0 when hidden).</summary>
        public float OccupiedFromTop => _group != null && _group.alpha > 0.05f ? 24f + _panel.sizeDelta.y + 12f : 0f;
        /// <summary>The window's footprint in the full-screen layer (anchored top-right).</summary>
        public float OccupiedWidth => _group != null && _group.alpha > 0.05f ? Width + 40f : 0f;

        void Update()
        {
            var ctx = RunContext.Current;
            if (ctx != _ctx)
            {
                if (_ctx != null) _ctx.Events.SystemNotice -= OnNotice;
                _ctx = ctx;
                if (_ctx != null) _ctx.Events.SystemNotice += OnNotice;
            }
            float dt = Time.unscaledDeltaTime;
            if (!_showing)
            {
                if (_queue.Count == 0)
                {
                    _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, dt * 4f);
                    return;
                }
                _cur = _queue.Dequeue();
                _t = 0f;
                _showing = true;
                var col = _cur.Error ? UIKit.Danger : UIKit.SystemCyan;
                _border.color = col;
                _title.color = col;
                _title.text = _cur.Error ? "» SYSTEM  <size=80%>ERROR</size>" : "» SYSTEM";
                float h = _body.GetPreferredValues(_cur.Text, Width - 48f, 0f).y + 70f;
                _panel.sizeDelta = new Vector2(Width, Mathf.Max(110f, h));
            }
            _t += dt;
            _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt * 6f);
            int n = Mathf.Min(_cur.Text.Length, Mathf.FloorToInt(_t * CharsPerSecond));
            _body.text = Typed(_cur.Text, n, _cur.Error);
            float typeTime = _cur.Text.Length / CharsPerSecond;
            if (_t > typeTime + _cur.Hold) _showing = false;
        }

        void OnNotice(string text) => Show(text);

        void OnDestroy()
        {
            if (_ctx != null) _ctx.Events.SystemNotice -= OnNotice;
            if (Instance == this) Instance = null;
        }

        /// <summary>Typewriter; errors show 3 cycling glyphs ahead of the cursor (tags are copied whole).</summary>
        static string Typed(string s, int n, bool error)
        {
            if (n >= s.Length) return s;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            int i = 0;
            while (i < n && i < s.Length)
            {
                if (s[i] == '<')
                {
                    int close = s.IndexOf('>', i);
                    if (close > 0)
                    {
                        sb.Append(s, i, close - i + 1);
                        n += close - i;
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(s[i]);
                i++;
            }
            if (error)
            {
                int seed = (int)(Time.unscaledTime * 24f);
                for (int k = 0; k < 3 && i + k < s.Length; k++)
                    sb.Append("<color=#FF564A>").Append(Glyphs[(seed + k * 7) % Glyphs.Length]).Append("</color>");
            }
            else sb.Append("<color=#7BE6FF>_</color>");
            return sb.ToString();
        }
    }
}
