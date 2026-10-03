using System.Collections.Generic;
using HS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.Tutorial.Demo
{
    /// <summary>
    /// UI drawn over the demo picture, pinned to stage positions: captions along the bottom, icons over heads (blinded,
    /// "!", "?", his rule), speech bubbles, small bars (health, Honor, a channel), key and skill callouts, and the end
    /// card. Times are the script's, so a skip clears what a beat put up.
    /// </summary>
    public sealed class DemoOverlay : MonoBehaviour
    {
        sealed class Pinned
        {
            public RectTransform Rt;
            public Puppet Who;
            public float Until, Born, Lift, Side;
            public bool Pop;
        }

        sealed class BarView
        {
            public RectTransform Rt;
            public Image Fill;
            public Puppet Who;
            public float Lift;
        }

        RectTransform _root, _pins, _caption, _endCard, _endDim;
        TextMeshProUGUI _captionText, _endTitle, _endBody;
        CanvasGroup _captionGroup, _endGroup;
        DemoStage _stage;
        readonly List<Pinned> _pinned = new List<Pinned>();
        readonly Dictionary<string, BarView> _bars = new Dictionary<string, BarView>();
        float _time;
        string _captionShown;

        public string CaptionText => _captionShown ?? "";
        public bool EndCardShowing => _endCard != null && _endCard.gameObject.activeSelf;

        public static DemoOverlay Create(RectTransform over, DemoStage stage)
        {
            var root = UIKit.Stretch(over, "Overlay");
            var o = root.gameObject.AddComponent<DemoOverlay>();
            o._root = root;
            o._stage = stage;
            o.Build();
            return o;
        }

        void Build()
        {
            _pins = UIKit.Stretch(_root, "Pins");
            // Caption strip along the bottom of the picture.
            _caption = UIKit.Rect(_root, "Caption", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 58f), Vector2.zero);
            _caption.anchorMin = new Vector2(0f, 0f);
            _caption.anchorMax = new Vector2(1f, 0f);
            _caption.sizeDelta = new Vector2(0f, 58f);
            _captionGroup = _caption.gameObject.AddComponent<CanvasGroup>();
            UIKit.Image(_caption, "Bg", null, new Color(0.02f, 0.04f, 0.08f, 0.78f), false);
            _captionText = UIKit.Text(_caption, "Text", "", UIKit.Sans, 20, Color.white, TextAlignmentOptions.Center);
            _captionText.rectTransform.offsetMin = new Vector2(18f, 4f);
            _captionText.rectTransform.offsetMax = new Vector2(-18f, -4f);
            _captionGroup.alpha = 0f;
            // End card, over a dimmed picture.
            _endDim = UIKit.Image(_root, "EndDim", null, new Color(0.01f, 0.02f, 0.05f, 0.62f), false).rectTransform;
            _endDim.gameObject.SetActive(false);
            _endCard = UIKit.Rect(_root, "EndCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(460f, 200f), new Vector2(0f, 14f));
            _endGroup = _endCard.gameObject.AddComponent<CanvasGroup>();
            UIKit.Card(_endCard, UIKit.SystemCyan);
            _endTitle = UIKit.Text(_endCard, "Title", "", UIKit.Mono, 24, UIKit.SystemCyan, TextAlignmentOptions.Top);
            _endTitle.rectTransform.offsetMax = new Vector2(-16f, -18f);
            _endTitle.rectTransform.offsetMin = new Vector2(16f, 0f);
            _endBody = UIKit.Text(_endCard, "Body", "", UIKit.Sans, 19, new Color(0.88f, 0.95f, 1f), TextAlignmentOptions.Top);
            _endBody.rectTransform.offsetMax = new Vector2(-22f, -60f);
            _endBody.rectTransform.offsetMin = new Vector2(22f, 14f);
            _endCard.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------------------------------------ API
        public void Caption(string text)
        {
            if (text == _captionShown) return;
            _captionShown = text;
            _captionText.text = text ?? "";
            _captionGroup.alpha = 0f;
        }

        /// <summary>An icon over a puppet's head for a while ("blind", "alert", "question", "check", rule icons...).</summary>
        public void Mark(Puppet who, string icon, float seconds, Color? tint = null, float lift = 0f)
        {
            if (who == null) return;
            var rt = UIKit.Rect(_pins, "Mark_" + icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(40f, 40f), Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIKit.Icon(icon);
            img.color = tint ?? Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
            _pinned.Add(new Pinned { Rt = rt, Who = who, Until = _time + seconds, Born = _time, Lift = lift, Pop = true });
        }

        /// <summary>Remove the marks on a puppet (e.g. "blinded" when it wears off early).</summary>
        public void ClearMarks(Puppet who)
        {
            for (int i = _pinned.Count - 1; i >= 0; i--)
                if (_pinned[i].Who == who && _pinned[i].Rt != null && _pinned[i].Rt.name.StartsWith("Mark_"))
                {
                    Destroy(_pinned[i].Rt.gameObject);
                    _pinned.RemoveAt(i);
                }
        }

        public void Bubble(Puppet who, string text, float seconds)
        {
            if (who == null) return;
            var rt = UIKit.Rect(_pins, "Bubble", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(260f, 50f), Vector2.zero);
            var bg = UIKit.Image(rt, "Bg", UIKit.Panel, UIKit.Paper);
            bg.pixelsPerUnitMultiplier = 1.6f;
            var tail = UIKit.Rect(rt, "Tail", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(18f, 14f), new Vector2(0f, 3f));
            var ti = tail.gameObject.AddComponent<Image>();
            ti.sprite = UIKit.Tail;
            ti.color = UIKit.Paper;
            ti.raycastTarget = false;
            var t = UIKit.Text(rt, "Text", text, UIKit.Sans, 16, UIKit.Ink, TextAlignmentOptions.Center);
            t.rectTransform.offsetMin = new Vector2(10f, 6f);
            t.rectTransform.offsetMax = new Vector2(-10f, -6f);
            var pref = t.GetPreferredValues(text, 240f, 0f);
            float w = Mathf.Clamp(pref.x + 24f, 90f, 260f);
            rt.sizeDelta = new Vector2(w, t.GetPreferredValues(text, w - 20f, 0f).y + 14f);
            _pinned.Add(new Pinned { Rt = rt, Who = who, Until = _time + seconds, Born = _time, Lift = 18f });
        }

        /// <summary>A small labelled bar over a head; set again to move it, RemoveBar to drop it.</summary>
        public void Bar(string id, Puppet who, float fill, Color color, float lift = 0f)
        {
            if (who == null) return;
            if (!_bars.TryGetValue(id, out var b) || b.Rt == null)
            {
                var rt = UIKit.Rect(_pins, "Bar_" + id, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(70f, 8f), Vector2.zero);
                UIKit.Image(rt, "Track", UIKit.BarSprite, new Color(0f, 0f, 0f, 0.7f));
                var f = UIKit.Image(rt, "Fill", UIKit.BarSprite, color);
                b = new BarView { Rt = rt, Fill = f, Who = who, Lift = lift };
                _bars[id] = b;
            }
            b.Fill.color = color;
            var fr = b.Fill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
        }

        public void RemoveBar(string id)
        {
            if (_bars.TryGetValue(id, out var b) && b.Rt != null) Destroy(b.Rt.gameObject);
            _bars.Remove(id);
        }

        /// <summary>A key to press, shown beside a puppet (keyboard or gamepad).</summary>
        public void Key(Puppet who, string token, float seconds)
        {
            if (who == null) return;
            var label = KeyGlyphs.Label(token, KeyGlyphs.Current) ?? token;
            var holder = UIKit.Rect(_pins, "Key_" + token, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(60f, 30f), Vector2.zero);
            var key = UIKit.Keycap(holder, "Cap", label, 28f);
            key.anchoredPosition = Vector2.zero;
            _pinned.Add(new Pinned { Rt = holder, Who = who, Until = _time + seconds, Born = _time, Lift = -34f, Side = 58f, Pop = true });
        }

        /// <summary>The skill being used, as a badge beside the user (the slot it sits in varies, so no key).</summary>
        public void SkillCallout(Puppet who, string skillId, float seconds)
        {
            if (who == null) return;
            var holder = UIKit.Rect(_pins, "Callout_" + skillId, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(46f, 46f), Vector2.zero);
            UIKit.Image(holder, "Bg", UIKit.Panel, new Color(0.04f, 0.06f, 0.1f, 0.9f));
            UIKit.Image(holder, "Frame", UIKit.UISprite("slot"), UIKit.Gold);
            var icon = UIKit.SpriteImage(holder, "Icon", UIKit.Icon(HS.Skills.SkillGuides.IconId(skillId)), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _pinned.Add(new Pinned { Rt = holder, Who = who, Until = _time + seconds, Born = _time, Lift = -40f, Side = -60f, Pop = true });
        }

        public void EndCard(string title, string body)
        {
            ClearPins();
            Caption(null);
            _endTitle.text = title;
            _endBody.text = body;
            float h = _endBody.GetPreferredValues(body, 416f, 0f).y;
            _endCard.sizeDelta = new Vector2(460f, 84f + h);
            _endCard.gameObject.SetActive(true);
            _endDim.gameObject.SetActive(true);
            _endGroup.alpha = 0f;
        }

        public void HideEndCard()
        {
            _endCard.gameObject.SetActive(false);
            _endDim.gameObject.SetActive(false);
        }

        void ClearPins()
        {
            foreach (var p in _pinned)
                if (p.Rt != null) Destroy(p.Rt.gameObject);
            _pinned.Clear();
            foreach (var b in _bars.Values)
                if (b.Rt != null) Destroy(b.Rt.gameObject);
            _bars.Clear();
        }

        public void Clear()
        {
            ClearPins();
            Caption(null);
            HideEndCard();
            _time = 0f;
        }

        // ----------------------------------------------------------------------------------------------- tick
        /// <summary>Position everything for the script's time t (called by the viewport each frame).</summary>
        public void Tick(float t, float dt)
        {
            _time = t;
            var size = _root.rect.size;
            for (int i = _pinned.Count - 1; i >= 0; i--)
            {
                var p = _pinned[i];
                if (p.Rt == null || t >= p.Until || p.Who == null || p.Who.Root == null)
                {
                    if (p.Rt != null) Destroy(p.Rt.gameObject);
                    _pinned.RemoveAt(i);
                    continue;
                }
                Pin(p.Rt, p.Who, size, p.Lift, p.Side);
                float age = t - p.Born;
                p.Rt.localScale = Vector3.one * (p.Pop ? 1f + 0.4f * Mathf.Max(0f, 1f - age * 5f) : 1f);
            }
            foreach (var b in _bars.Values)
                if (b.Rt != null && b.Who != null && b.Who.Root != null) Pin(b.Rt, b.Who, size, -6f + b.Lift, 0f);
            _captionGroup.alpha = string.IsNullOrEmpty(_captionShown) ? 0f : Mathf.MoveTowards(_captionGroup.alpha, 1f, dt * 4f);
            if (_endCard.gameObject.activeSelf) _endGroup.alpha = Mathf.MoveTowards(_endGroup.alpha, 1f, dt * 3f);
        }

        void Pin(RectTransform rt, Puppet who, Vector2 size, float lift, float side)
        {
            if (!_stage.ToViewport(who.Head + Vector3.up * 0.25f, out var v))
            {
                rt.gameObject.SetActive(false);
                return;
            }
            rt.gameObject.SetActive(true);
            var p = new Vector2((v.x - 0.5f) * size.x + side, (v.y - 0.5f) * size.y + lift);
            // Keep it inside the picture (pivot at its bottom centre), clear of the header and the caption strip.
            var half = size * 0.5f;
            var sz = rt.sizeDelta;
            p.x = Mathf.Clamp(p.x, -half.x + sz.x * 0.5f + 6f, half.x - sz.x * 0.5f - 6f);
            p.y = Mathf.Clamp(p.y, -half.y + 64f, half.y - sz.y - 40f);
            rt.anchoredPosition = p;
        }
    }
}
