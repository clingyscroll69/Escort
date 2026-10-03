using System;
using System.Collections.Generic;
using HS.Core;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// How lessons look. Toasts: a card on the right edge under the System window, with the lesson's icon, title and
    /// text, a line that runs down while it stays, and a tick when you've done the thing. Focus: the world dims around a
    /// spotlight on what's being explained (Callum, his cone, the duel) and a card waits for a confirm. Coach marks: the
    /// HUD element a lesson is about pulses. Markers: a ring and arrow on a world thing (a stone, a trap, a cache).
    /// Presentation only; the <see cref="TutorialDirector"/> decides what shows when.
    /// </summary>
    public sealed class TipView : MonoBehaviour
    {
        UIRoot _root;
        HudView _hud;

        // toast
        RectTransform _toast, _toastProgress;
        CanvasGroup _toastGroup;
        Image _toastIcon, _toastCheck;
        TextMeshProUGUI _toastKicker, _toastTitle, _toastBody;
        string _toastId;
        float _toastT, _toastDuration, _toastDoneT = -1f, _toastY;
        bool _toastOn;
        const float ToastWidth = 450f;

        // focus
        RectTransform _focus, _focusCard;
        CanvasGroup _focusGroup;
        readonly Image[] _dim = new Image[4];
        Image _spot, _spotRing;
        TextMeshProUGUI _focusKicker, _focusTitle, _focusBody, _focusFooter;
        Func<Rect?> _spotlight;
        Action _onContinue;
        float _focusT;
        bool _focusOn;
        const float FocusDim = 0.74f, FocusInputDelay = 0.45f;

        // coach marks and world markers
        readonly List<(Image img, float until)> _coach = new List<(Image, float)>();
        readonly List<(RectTransform rt, Func<Vector3?> world, float until)> _markers = new List<(RectTransform, Func<Vector3?>, float)>();
        RectTransform _markerLayer;

        /// <summary>Hold the toast where it is (the pause menu is open over it).</summary>
        public bool Frozen;
        public bool ToastVisible => _toastOn;
        public string ToastId => _toastOn ? _toastId : null;
        public bool FocusVisible => _focusOn;

        public static TipView Create(UIRoot root, HudView hud)
        {
            var go = UIKit.Stretch(root.Windows, "TipView").gameObject;
            var v = go.AddComponent<TipView>();
            v._root = root;
            v._hud = hud;
            v.Build();
            return v;
        }

        void Build()
        {
            _markerLayer = UIKit.Stretch(transform, "Markers");
            BuildToast();
            BuildFocus();
        }

        static Color CategoryColor(LessonCategory c) => c switch
        {
            LessonCategory.TheHero => UIKit.HeroBlue,
            LessonCategory.TheRoad => UIKit.Ochre,
            LessonCategory.Camp => UIKit.Gold,
            _ => UIKit.SystemCyan,
        };

        // ------------------------------------------------------------------------------------------------------ toast
        void BuildToast()
        {
            _toast = UIKit.Rect(transform, "Toast", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(ToastWidth, 140f), new Vector2(-28f, -200f));
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            UIKit.Card(_toast, UIKit.SystemCyan);
            _toastIcon = UIKit.SpriteImage(_toast, "Icon", null, UIKit.SystemCyan, new Vector2(0f, 1f), new Vector2(18f, -16f), new Vector2(40f, 40f));
            _toastKicker = UIKit.Text(_toast, "Kicker", "", UIKit.Mono, 14, UIKit.Dim);
            UIKit.Place(_toastKicker.rectTransform, new Vector2(0f, 1f), new Vector2(70f, -14f), new Vector2(ToastWidth - 120f, 18f));
            _toastTitle = UIKit.Text(_toast, "Title", "", UIKit.Sans, 22, Color.white);
            UIKit.Place(_toastTitle.rectTransform, new Vector2(0f, 1f), new Vector2(70f, -32f), new Vector2(ToastWidth - 120f, 28f));
            _toastTitle.fontStyle = FontStyles.Bold;
            _toastBody = UIKit.Text(_toast, "Body", "", UIKit.Sans, 19, new Color(0.88f, 0.95f, 1f));
            UIKit.Place(_toastBody.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -68f), new Vector2(ToastWidth - 40f, 60f));
            _toastCheck = UIKit.SpriteImage(_toast, "Done", UIKit.Icon("check"), UIKit.Gold, new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(34f, 34f));
            _toastCheck.enabled = false;
            var track = UIKit.Rect(_toast, "Progress", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ToastWidth - 40f, 3f), new Vector2(20f, 10f));
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = new Color(1f, 1f, 1f, 0.08f);
            trackImg.raycastTarget = false;
            _toastProgress = UIKit.Rect(track, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            _toastProgress.anchorMax = new Vector2(1f, 1f);
            var fill = _toastProgress.gameObject.AddComponent<Image>();
            fill.color = new Color(0.48f, 0.9f, 1f, 0.7f);
            fill.raycastTarget = false;
            _toast.gameObject.SetActive(false);
        }

        public void ShowToast(Lesson l, string body, float duration)
        {
            _toastId = l.Id;
            _toastT = 0f;
            _toastDoneT = -1f;
            _toastDuration = Mathf.Max(1f, duration);
            _toastOn = true;
            var accent = CategoryColor(l.Category);
            _toastIcon.sprite = UIKit.Icon(l.Icon);
            _toastIcon.enabled = _toastIcon.sprite != null;
            _toastIcon.color = accent;
            _toastKicker.text = "» TIP  ·  " + Lessons.CategoryName(l.Category);
            _toastTitle.text = l.Title;
            _toastBody.text = body;
            _toastCheck.enabled = false;
            foreach (var img in _toast.GetComponentsInChildren<Image>())
                if (img.name.StartsWith("Bracket")) img.color = new Color(accent.r, accent.g, accent.b, 0.85f);
            float bodyH = _toastBody.GetPreferredValues(body, ToastWidth - 40f, 0f).y;
            _toastBody.rectTransform.sizeDelta = new Vector2(ToastWidth - 40f, bodyH + 4f);
            _toast.sizeDelta = new Vector2(ToastWidth, 68f + bodyH + 26f);
            _toast.gameObject.SetActive(true);
            _toastY = TargetToastY() - 10f;
            HS.Audio.AudioDirector.Instance?.Play("ui_open", null, 0.35f, 0.2f, 0f);
        }

        /// <summary>The player did the thing: a tick, then the card goes.</summary>
        public void MarkToastDone()
        {
            if (!_toastOn || _toastDoneT >= 0f) return;
            _toastDoneT = 0f;
            _toastCheck.enabled = true;
            HS.Audio.AudioDirector.Instance?.Play("ui_confirm", null, 0.35f, 0.1f, 0f);
        }

        public void HideToast()
        {
            _toastOn = false;
        }

        float TargetToastY()
        {
            var sw = SystemWindow.Instance;
            float top = sw != null ? sw.OccupiedFromTop : 0f;
            return -Mathf.Max(24f, top + 14f);
        }

        void TickToast(float dt)
        {
            if (_toast == null) return;
            if (Frozen) return;
            if (_toastOn)
            {
                _toastT += dt;
                if (_toastDoneT >= 0f)
                {
                    _toastDoneT += dt;
                    if (_toastDoneT > 1.2f) _toastOn = false;
                }
                else if (_toastT >= _toastDuration) _toastOn = false;
            }
            float target = _toastOn ? 1f : 0f;
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, target, dt * (_toastOn ? 5f : 3f));
            if (!_toastOn && _toastGroup.alpha <= 0.001f && _toast.gameObject.activeSelf) _toast.gameObject.SetActive(false);
            if (!_toast.gameObject.activeSelf) return;
            _toastY = Mathf.Lerp(_toastY, TargetToastY(), 1f - Mathf.Exp(-dt * 10f));
            float slide = (1f - _toastGroup.alpha) * 36f;
            _toast.anchoredPosition = new Vector2(-28f + slide, _toastY);
            float left = _toastDoneT >= 0f ? 0f : 1f - Mathf.Clamp01(_toastT / _toastDuration);
            _toastProgress.anchorMax = new Vector2(left, 1f);
            if (_toastDoneT >= 0f) _toastCheck.rectTransform.localScale = Vector3.one * (1f + 0.4f * Mathf.Max(0f, 1f - _toastDoneT * 4f));
        }

        // ------------------------------------------------------------------------------------------------------ focus
        void BuildFocus()
        {
            _focus = UIKit.Stretch(_root.Overlay, "TutorialFocus");
            _focusGroup = _focus.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < 4; i++)
            {
                _dim[i] = UIKit.Image(_focus, "Dim" + i, null, new Color(0.01f, 0.02f, 0.04f, FocusDim), false);
                _dim[i].raycastTarget = true; // nothing behind the lesson takes a click
            }
            _spot = UIKit.Image(_focus, "Spot", UIKit.UISprite("spot"), new Color(0.01f, 0.02f, 0.04f, FocusDim), false);
            _spotRing = UIKit.Image(_focus, "SpotRing", UIKit.UISprite("ring"), UIKit.SystemCyan, false);
            _focusCard = UIKit.Rect(_focus, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(640f, 360f), Vector2.zero);
            UIKit.Card(_focusCard, UIKit.SystemCyan);
            _focusKicker = UIKit.Text(_focusCard, "Kicker", "", UIKit.Mono, 17, UIKit.SystemCyan);
            UIKit.Place(_focusKicker.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -24f), new Vector2(580f, 22f));
            _focusTitle = UIKit.Text(_focusCard, "Title", "", UIKit.Sans, 36, Color.white);
            UIKit.Place(_focusTitle.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -50f), new Vector2(580f, 46f));
            _focusTitle.fontStyle = FontStyles.Bold;
            _focusBody = UIKit.Text(_focusCard, "Body", "", UIKit.Sans, 23, new Color(0.88f, 0.95f, 1f));
            UIKit.Place(_focusBody.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -108f), new Vector2(576f, 200f));
            _focusBody.lineSpacing = 6f;
            var cont = UIKit.Button(_focusCard, "FocusContinue", "", new Vector2(576f, 54f), Vector2.zero, Continue);
            var crt = (RectTransform)cont.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 26f);
            _focusFooter = cont.GetComponentInChildren<TextMeshProUGUI>();
            _focusFooter.font = UIKit.Mono;
            _focusFooter.fontSize = 22;
            _focus.gameObject.SetActive(false);
        }

        public void ShowFocus(Lesson l, string body, Func<Rect?> spotlight, Action onContinue)
        {
            _spotlight = spotlight;
            _onContinue = onContinue;
            _focusT = 0f;
            _focusOn = true;
            _focusKicker.text = "» LESSON  ·  " + Lessons.CategoryName(l.Category);
            _focusTitle.text = l.Title;
            _focusBody.text = body;
            float bodyH = _focusBody.GetPreferredValues(body, 576f, 0f).y;
            _focusBody.rectTransform.sizeDelta = new Vector2(576f, bodyH + 6f);
            _focusCard.sizeDelta = new Vector2(640f, 108f + bodyH + 110f);
            _focusFooter.text = KeyGlyphs.Format("{confirm}  CONTINUE");
            _focus.gameObject.SetActive(true);
            _focus.SetAsLastSibling();
            _focusGroup.alpha = 0f;
            HS.Audio.AudioDirector.Instance?.Play("ui_open", null, 0.55f, 0.1f, 0f);
        }

        public void HideFocus()
        {
            _focusOn = false;
            _spotlight = null;
            _onContinue = null;
        }

        /// <summary>The card's button (a deliberate click) or the confirm key once the card has been up a moment (a key
        /// still held from play mustn't skip the lesson).</summary>
        void Continue()
        {
            if (!_focusOn) return;
            var cb = _onContinue;
            HideFocus();
            cb?.Invoke();
        }

        void TickFocus(float dt)
        {
            if (_focus == null || !_focus.gameObject.activeSelf) return;
            _focusT += dt;
            _focusGroup.alpha = Mathf.MoveTowards(_focusGroup.alpha, _focusOn ? 1f : 0f, dt * 5f);
            _focusGroup.blocksRaycasts = _focusOn;
            if (!_focusOn)
            {
                if (_focusGroup.alpha <= 0.001f) _focus.gameObject.SetActive(false);
                return;
            }
            var input = GameInput.Instance;
            if (_focusT >= FocusInputDelay && input.Confirm.WasPressedThisFrame()) Continue();
            if (!_focusOn) return;
            var size = _focus.rect.size;
            var half = size * 0.5f;
            Rect? r = _spotlight?.Invoke();
            if (r.HasValue)
            {
                // A spotlight never swallows the screen (the dimming is the point).
                var rr = r.Value;
                float maxW = size.x * 0.55f, maxH = size.y * 0.62f;
                if (rr.width > maxW) rr = new Rect(rr.center.x - maxW * 0.5f, rr.y, maxW, rr.height);
                if (rr.height > maxH) rr = new Rect(rr.x, rr.center.y - maxH * 0.5f, rr.width, maxH);
                r = rr;
            }
            Rect s = r ?? new Rect(-half.x - 10f, -half.y - 10f, 0f, 0f);
            float pulse = 1f + 0.03f * Mathf.Sin(_focusT * 4f);
            var c = s.center;
            var e = s.size * 0.5f * pulse;
            // Dim everything but the spotlight: four panels anchored to the screen edges (so they always reach them,
            // whatever the canvas size), and a soft-edged hole over the spotlight itself.
            Edge(_dim[0], new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, c.y + e.y), Vector2.zero);                       // above
            Edge(_dim[1], new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, c.y - e.y));                       // below
            Edge(_dim[2], new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, c.y - e.y), new Vector2(c.x - e.x, c.y + e.y)); // left
            Edge(_dim[3], new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(c.x + e.x, c.y - e.y), new Vector2(0f, c.y + e.y)); // right
            _spot.enabled = _spotRing.enabled = r.HasValue;
            Place(_spot, c.x - e.x, c.x + e.x, c.y - e.y, c.y + e.y);
            var ring = e * 1.04f;
            Place(_spotRing, c.x - ring.x, c.x + ring.x, c.y - ring.y, c.y + ring.y);
            _spotRing.color = new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.35f + 0.25f * Mathf.Sin(_focusT * 4f));
            // The card sits on the side away from the spotlight.
            float side = r.HasValue && c.x > 0f ? -1f : 1f;
            float cardX = r.HasValue ? side * Mathf.Min(half.x - _focusCard.sizeDelta.x * 0.5f - 60f, Mathf.Max(360f, half.x * 0.5f)) : 0f;
            _focusCard.anchoredPosition = new Vector2(cardX, (1f - _focusGroup.alpha) * -24f);
        }

        /// <summary>Stretch between two anchors with offsets (in the layer's centre-origin units for the inner edge).</summary>
        static void Edge(Image img, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Offsets were given relative to the centre for inner edges; an edge anchored at 0/1 keeps offset 0 there.
            rt.offsetMin = new Vector2(anchorMin.x == 0.5f ? offsetMin.x : 0f, anchorMin.y == 0.5f ? offsetMin.y : 0f);
            rt.offsetMax = new Vector2(anchorMax.x == 0.5f ? offsetMax.x : 0f, anchorMax.y == 0.5f ? offsetMax.y : 0f);
        }

        static void Place(Image img, float x0, float x1, float y0, float y1)
        {
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x0, y0);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }

        // ---------------------------------------------------------------------------------------------- coach marks
        /// <summary>Pulse the HUD element a lesson is about for a while.</summary>
        public void Coach(CoachTarget t, float seconds)
        {
            var anchor = _hud != null ? _hud.CoachAnchor(t) : null;
            if (anchor == null) return;
            var img = UIKit.Image(anchor, "CoachMark", UIKit.Border, UIKit.Gold);
            img.rectTransform.offsetMin = new Vector2(-8f, -8f);
            img.rectTransform.offsetMax = new Vector2(8f, 8f);
            _coach.Add((img, Time.unscaledTime + seconds));
        }

        void TickCoach()
        {
            float now = Time.unscaledTime;
            for (int i = _coach.Count - 1; i >= 0; i--)
            {
                var (img, until) = _coach[i];
                if (img == null || now >= until)
                {
                    if (img != null) Destroy(img.gameObject);
                    _coach.RemoveAt(i);
                    continue;
                }
                float a = 0.45f + 0.45f * Mathf.Sin(now * 7f);
                img.color = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, a * Mathf.Clamp01(until - now));
            }
        }

        // -------------------------------------------------------------------------------------------- world markers
        /// <summary>A ring and an arrow on a world point (clamped to the screen edge when it's off screen).</summary>
        public void Marker(Func<Vector3?> world, float seconds)
        {
            if (world == null) return;
            var rt = UIKit.Rect(_markerLayer, "Marker", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70f, 70f), Vector2.zero);
            var ring = rt.gameObject.AddComponent<Image>();
            ring.sprite = UIKit.UISprite("ring");
            ring.color = UIKit.Gold;
            ring.raycastTarget = false;
            var arrow = UIKit.SpriteImage(rt, "Arrow", UIKit.Tail, UIKit.Gold, new Vector2(0.5f, 1f), new Vector2(0f, 30f), new Vector2(30f, 26f));
            arrow.rectTransform.pivot = new Vector2(0.5f, 0f);
            _markers.Add((rt, world, Time.unscaledTime + seconds));
        }

        void TickMarkers()
        {
            float now = Time.unscaledTime;
            var size = _markerLayer.rect.size;
            var half = size * 0.5f - new Vector2(60f, 60f);
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var (rt, world, until) = _markers[i];
                var w = world();
                if (rt == null || now >= until || !w.HasValue)
                {
                    if (rt != null) Destroy(rt.gameObject);
                    _markers.RemoveAt(i);
                    continue;
                }
                bool on = _root.WorldToLayer(w.Value, _markerLayer, out var p);
                if (!on) p = new Vector2(0f, -half.y);
                p.x = Mathf.Clamp(p.x, -half.x, half.x);
                p.y = Mathf.Clamp(p.y, -half.y, half.y);
                rt.anchoredPosition = p + Vector2.up * (6f * Mathf.Sin(now * 5f));
                float a = Mathf.Clamp01((until - now) * 2f);
                foreach (var img in rt.GetComponentsInChildren<Image>()) img.color = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, a);
                rt.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(now * 5f));
            }
        }

        // ---------------------------------------------------------------------------------------------------- tick
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickToast(dt);
            TickFocus(dt);
            TickCoach();
            TickMarkers();
        }

        void OnDestroy()
        {
            if (_focus != null) Destroy(_focus.gameObject);
        }
    }
}
