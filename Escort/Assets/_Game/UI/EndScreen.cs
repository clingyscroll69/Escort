using System;
using System.Collections.Generic;
using HS.Rapport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// End-of-run screen: red-glyph title (loss) or System title (win) → the Curator's diagnosis typed line by line → the
    /// qualitative Post-Mortem → buttons (Restore Points / play again). Any click or Confirm skips the typing.
    /// </summary>
    public sealed class EndScreen : MonoBehaviour
    {
        public sealed class Model
        {
            public string Title;
            public bool Error;
            public List<string> Diagnosis = new List<string>();
            public string Quote;               // the hero's line (win)
            public List<PostMortem.Line> Lines = new List<PostMortem.Line>();
            public List<(string label, Action action)> Buttons = new List<(string, Action)>();
            /// <summary>A first-time note above the buttons (the tutorial's Restore Points lesson).</summary>
            public string Hint;
        }

        public static EndScreen Current { get; private set; }
        Model _m;
        TextMeshProUGUI _title, _diag, _pmHeader, _pm;
        RectTransform _buttons;
        CanvasGroup _group, _lower;
        float _t;
        int _line;
        bool _skip;
        const float LineTime = 2.1f;
        const string Glyphs = "#@%&$*+=<>?/\\|×";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        public static EndScreen Show(UIRoot root, Model m)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = UIKit.Stretch(root.Overlay, "EndScreen").gameObject;
            Current = go.AddComponent<EndScreen>();
            Current._m = m;
            Current.Build();
            return Current;
        }

        void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            var dim = UIKit.Image(transform, "Dim", null, new Color(0.02f, 0.02f, 0.04f, 0.86f), false);
            dim.raycastTarget = true;
            var col = _m.Error ? UIKit.Danger : UIKit.SystemCyan;
            _title = UIKit.Text(transform, "Title", "", UIKit.Mono, 54, col, TextAlignmentOptions.Top);
            _title.rectTransform.offsetMin = new Vector2(80f, 0f);
            _title.rectTransform.offsetMax = new Vector2(-80f, -70f);
            _diag = UIKit.Text(transform, "Diagnosis", "", UIKit.Mono, 30, new Color(0.86f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            _diag.rectTransform.offsetMin = new Vector2(260f, 0f);
            _diag.rectTransform.offsetMax = new Vector2(-260f, -170f);
            var lower = UIKit.Stretch(transform, "Lower");
            _lower = lower.gameObject.AddComponent<CanvasGroup>();
            _lower.alpha = 0f;
            _pmHeader = UIKit.Text(lower, "PMHeader", "POST-MORTEM", UIKit.Mono, 26, UIKit.Gold, TextAlignmentOptions.TopLeft);
            _pmHeader.rectTransform.offsetMin = new Vector2(260f, 0f);
            _pmHeader.rectTransform.offsetMax = new Vector2(-260f, -520f);
            _pm = UIKit.Text(lower, "PM", "", UIKit.Sans, 26, Color.white, TextAlignmentOptions.TopLeft);
            _pm.rectTransform.offsetMin = new Vector2(260f, 0f);
            _pm.rectTransform.offsetMax = new Vector2(-260f, -562f);
            var sb = new System.Text.StringBuilder();
            foreach (var l in _m.Lines)
                sb.Append(l.Good ? "<color=#E4A84E>+</color>  " : "<color=#9AA3AD>-</color>  ").Append(l.Text).Append('\n');
            if (_m.Lines.Count == 0) sb.Append("<color=#9AA3AD>Nothing was offered, nothing was taken.</color>");
            _pm.text = sb.ToString();
            if (!string.IsNullOrEmpty(_m.Hint))
            {
                var hint = UIKit.Text(lower, "Hint", "<color=#7BE6FF>»</color> " + _m.Hint, UIKit.Sans, 23, UIKit.Dim, TextAlignmentOptions.Bottom);
                hint.rectTransform.offsetMin = new Vector2(260f, 168f);
                hint.rectTransform.offsetMax = new Vector2(-260f, 0f);
            }
            _buttons = UIKit.Rect(lower, "Buttons", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1400f, 80f), new Vector2(0f, 70f));
            float w = 420f, gap = 30f, total = _m.Buttons.Count * w + (_m.Buttons.Count - 1) * gap;
            for (int i = 0; i < _m.Buttons.Count; i++)
            {
                var (label, act) = _m.Buttons[i];
                var b = UIKit.Button(_buttons, "Button" + i, label, new Vector2(w, 72f), new Vector2(-total * 0.5f + w * 0.5f + i * (w + gap), 0f), act);
                if (i == 0) b.Select();
            }
        }

        public bool Finished => _line > _m.Diagnosis.Count;

        public void Skip() => _skip = true;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt * 2.5f);
            var input = HS.Core.GameInput.Instance;
            if ((input.Confirm.enabled && input.Confirm.WasPressedThisFrame()) || (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame && !Finished))
                _skip = true;
            // Title: glyphs settle into the text.
            float settle = Mathf.Clamp01(_t / 1.2f);
            _title.text = Scramble(_m.Title, settle, _m.Error);
            if (_t < 1.2f && !_skip) return;
            // Diagnosis lines, typed one by one.
            if (_skip) _line = _m.Diagnosis.Count + 1;
            float since = _t - 1.2f;
            if (!_skip) _line = Mathf.Min(_m.Diagnosis.Count + 1, Mathf.FloorToInt(since / LineTime) + 1);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _m.Diagnosis.Count && i < _line; i++)
            {
                string s = _m.Diagnosis[i];
                bool typing = i == _line - 1 && !_skip && _line <= _m.Diagnosis.Count;
                if (typing)
                {
                    int n = Mathf.Clamp(Mathf.FloorToInt((since - i * LineTime) * 48f), 0, s.Length);
                    sb.Append(s, 0, n).Append("<color=#7BE6FF>_</color>");
                }
                else sb.Append(s);
                sb.Append("\n\n");
            }
            if (!string.IsNullOrEmpty(_m.Quote) && _line > _m.Diagnosis.Count) sb.Append("<color=#7DA1FF>“").Append(_m.Quote).Append("”</color>");
            _diag.text = sb.ToString();
            if (_line > _m.Diagnosis.Count) _lower.alpha = Mathf.MoveTowards(_lower.alpha, 1f, dt * 2f);
        }

        static string Scramble(string s, float settle, bool error)
        {
            if (settle >= 1f || !error) return s;
            int n = Mathf.FloorToInt(s.Length * settle);
            var sb = new System.Text.StringBuilder(s.Length);
            sb.Append(s, 0, n);
            int seed = (int)(Time.unscaledTime * 30f);
            for (int i = n; i < s.Length; i++) sb.Append(s[i] == ' ' ? ' ' : Glyphs[(seed + i * 5) % Glyphs.Length]);
            return sb.ToString();
        }
    }
}
