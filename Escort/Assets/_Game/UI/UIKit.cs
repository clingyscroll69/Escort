using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>Code-built uGUI helpers, palette and shared sprites/fonts (Resources/UI, TMP essentials).</summary>
    public static class UIKit
    {
        public static readonly Color Ink = new Color32(20, 18, 26, 255);
        public static readonly Color Paper = new Color32(250, 245, 232, 240);
        public static readonly Color SystemCyan = new Color32(123, 230, 255, 255);
        public static readonly Color SystemBg = new Color32(8, 16, 30, 232);
        public static readonly Color Danger = new Color32(255, 86, 74, 255);
        public static readonly Color Gold = new Color32(242, 193, 78, 255);
        public static readonly Color HeroBlue = new Color32(96, 140, 255, 255);
        public static readonly Color Ochre = new Color32(228, 168, 78, 255);
        public static readonly Color Violet = new Color32(170, 120, 236, 255);
        public static readonly Color HpRed = new Color32(214, 66, 58, 255);
        public static readonly Color Track = new Color32(0, 0, 0, 160);
        public static readonly Color PanelDark = new Color32(14, 14, 20, 200);

        static Sprite _panel, _border, _bar, _pip, _glow, _tail;
        public static Sprite Panel => _panel != null ? _panel : _panel = Resources.Load<Sprite>("UI/panel");
        public static Sprite Border => _border != null ? _border : _border = Resources.Load<Sprite>("UI/panel_border");
        public static Sprite BarSprite => _bar != null ? _bar : _bar = Resources.Load<Sprite>("UI/bar");
        public static Sprite Pip => _pip != null ? _pip : _pip = Resources.Load<Sprite>("UI/pip");
        public static Sprite Glow => _glow != null ? _glow : _glow = Resources.Load<Sprite>("UI/glow");
        public static Sprite Tail => _tail != null ? _tail : _tail = Resources.Load<Sprite>("UI/tail");

        static TMP_FontAsset _sans, _mono;
        public static TMP_FontAsset Sans => _sans != null ? _sans : _sans = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        public static TMP_FontAsset Mono
        {
            get
            {
                if (_mono == null) _mono = Resources.Load<TMP_FontAsset>("UI/Fonts/ShareTechMono SDF");
                return _mono != null ? _mono : Sans;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _panel = _border = _bar = _pip = _glow = _tail = null;
            _sans = _mono = null;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name, float pad = 0f)
        {
            var rt = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool sliced = true)
        {
            var rt = Stretch(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced && sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Stretch(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>Dark outline for text drawn straight over the 3D scene.</summary>
        public static void Outline(TMP_Text t, float width = 0.22f)
        {
            t.fontMaterial = new Material(t.fontSharedMaterial);
            t.outlineWidth = width;
            t.outlineColor = new Color32(10, 10, 14, 255);
        }

        public static Button Button(Transform parent, string name, string label, Vector2 size, Vector2 pos, System.Action onClick)
        {
            var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, pos);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Panel;
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = new Color32(18, 34, 52, 240);
            var b = rt.gameObject.AddComponent<Button>();
            var cs = b.colors;
            cs.normalColor = Color.white;
            cs.highlightedColor = new Color(0.75f, 0.95f, 1f);
            cs.selectedColor = new Color(0.75f, 0.95f, 1f);
            cs.pressedColor = new Color(0.55f, 0.8f, 0.9f);
            b.colors = cs;
            var border = Image(rt, "Border", Border, SystemCyan);
            border.raycastTarget = false;
            var t = Text(rt, "Label", label, Mono, 26, SystemCyan, TextAlignmentOptions.Center);
            b.onClick.AddListener(() =>
            {
                HS.Audio.AudioDirector.Instance?.Play("ui_select", null, 0.6f, 0.05f, 0f);
                onClick?.Invoke();
            });
            return b;
        }
    }

    /// <summary>Track + fill + a lagging "ghost" fill (shows the size of the last hit) + optional label.</summary>
    public sealed class UIBar
    {
        public readonly RectTransform Root;
        readonly RectTransform _fill, _ghost;
        readonly Image _fillImg;
        readonly TextMeshProUGUI _label;
        float _value = 1f, _ghostValue = 1f, _ghostHold;

        public UIBar(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos, Color fill, bool label)
        {
            Root = UIKit.Rect(parent, name, anchor, pivot, size, pos);
            UIKit.Image(Root, "Track", UIKit.BarSprite, UIKit.Track);
            _ghost = UIKit.Image(Root, "Ghost", UIKit.BarSprite, new Color(1f, 0.95f, 0.85f, 0.85f)).rectTransform;
            _fillImg = UIKit.Image(Root, "Fill", UIKit.BarSprite, fill);
            _fill = _fillImg.rectTransform;
            if (label)
            {
                _label = UIKit.Text(Root, "Label", "", UIKit.Sans, Mathf.Clamp(size.y * 0.8f, 12f, 22f), Color.white, TextAlignmentOptions.Center);
                UIKit.Outline(_label, 0.25f);
            }
        }

        public Color FillColor
        {
            get => _fillImg.color;
            set => _fillImg.color = value;
        }

        public void Set(float fraction, string text = null)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction < _value) _ghostHold = 0.45f;
            else if (fraction > _ghostValue) _ghostValue = fraction;
            _value = fraction;
            if (_label != null && text != null) _label.text = text;
        }

        public void Tick(float dt)
        {
            if (_ghostHold > 0f) _ghostHold -= dt;
            else _ghostValue = Mathf.MoveTowards(_ghostValue, _value, dt * 0.8f);
            SetWidth(_fill, _value);
            SetWidth(_ghost, Mathf.Max(_value, _ghostValue));
        }

        static void SetWidth(RectTransform rt, float f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.gameObject.SetActive(f > 0.001f);
        }
    }
}
