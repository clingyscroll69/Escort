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
        /// <summary>HUD card body (blue-black, see-through enough to keep the road readable behind it).</summary>
        public static readonly Color CardBg = new Color32(11, 17, 29, 222);
        public static readonly Color KeyFace = new Color32(236, 230, 214, 255);
        public static readonly Color Dim = new Color32(143, 184, 200, 255);

        /// <summary>One colour per skill family, used on slots, picker cards and chips.</summary>
        public static Color Family(HS.Skills.SkillFamily f) => f switch
        {
            HS.Skills.SkillFamily.Fixer => new Color32(232, 150, 64, 255),
            HS.Skills.SkillFamily.Handler => new Color32(178, 132, 242, 255),
            HS.Skills.SkillFamily.Provisioner => new Color32(112, 204, 124, 255),
            HS.Skills.SkillFamily.Scholar => new Color32(98, 172, 255, 255),
            HS.Skills.SkillFamily.Combat => new Color32(234, 94, 82, 255),
            _ => Gold,
        };

        static readonly System.Collections.Generic.Dictionary<string, Sprite> _icons = new System.Collections.Generic.Dictionary<string, Sprite>();
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _ui = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>Resources/Icons/&lt;id&gt; (cached; null for an empty id).</summary>
        public static Sprite Icon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_icons.TryGetValue(id, out var s)) _icons[id] = s = Resources.Load<Sprite>("Icons/" + id);
            return s;
        }

        /// <summary>Resources/UI/&lt;id&gt; (cached).</summary>
        public static Sprite UISprite(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_ui.TryGetValue(id, out var s)) _ui[id] = s = Resources.Load<Sprite>("UI/" + id);
            return s;
        }

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
            _icons.Clear();
            _ui.Clear();
        }

        /// <summary>Anchor + pivot at the same corner/edge, then size and position (the HUD's layout idiom).</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>An Image of a sprite at a fixed size (icons, badges).</summary>
        public static Image SpriteImage(Transform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, anchor, size, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        /// <summary>
        /// A keycap: light key with a darker lip and a dark mono label; grows to fit its label. <see cref="SetKey"/>
        /// relabels it (the HUD follows whichever device was used last).
        /// </summary>
        public static RectTransform Keycap(Transform parent, string name, string label, float height)
        {
            var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(height, height), Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UISprite("keycap");
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = KeyFace;
            img.raycastTarget = false;
            var t = Text(rt, "Label", label, Mono, height * 0.56f, Ink, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.offsetMin = new Vector2(0f, height * 0.12f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            SetKey(rt, label);
            return rt;
        }

        public static void SetKey(RectTransform keycap, string label)
        {
            if (keycap == null) return;
            var t = keycap.GetComponentInChildren<TMP_Text>();
            if (t.text == label && keycap.sizeDelta.x > 0f) return;
            t.text = label ?? "";
            float h = keycap.sizeDelta.y;
            float w = t.GetPreferredValues(t.text, 999f, h).x + h * 0.6f;
            keycap.sizeDelta = new Vector2(Mathf.Max(h, w), h);
        }

        /// <summary>A pill-shaped label (status chips, family tags). Grows to fit; returns its text.</summary>
        public static TextMeshProUGUI Chip(Transform parent, string name, string text, Color color, float height = 24f)
        {
            var rt = Rect(parent, name, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, height), Vector2.zero);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Panel;
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.4f;
            bg.color = new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 0.88f);
            bg.raycastTarget = false;
            var t = Text(rt, "Text", text, Mono, height * 0.62f, color, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            FitChip(t);
            return t;
        }

        public static void FitChip(TMP_Text t)
        {
            var rt = (RectTransform)t.transform.parent;
            float h = rt.sizeDelta.y;
            rt.sizeDelta = new Vector2(t.GetPreferredValues(t.text, 999f, h).x + h * 0.9f, h);
        }

        /// <summary>Four corner brackets (the System-window look) on a panel.</summary>
        public static void Brackets(RectTransform panel, Color color, float size = 18f, float inset = -3f)
        {
            var s = UISprite("corner");
            for (int i = 0; i < 4; i++)
            {
                var a = new Vector2(i % 2, i / 2);       // (0,0) (1,0) (0,1) (1,1)
                float half = size * 0.5f + inset;         // inset < 0 sits the bracket just outside the panel
                var rt = Rect(panel, "Bracket" + i, a, new Vector2(0.5f, 0.5f), new Vector2(size, size),
                    new Vector2(a.x > 0 ? -half : half, a.y > 0 ? -half : half));
                // The sprite is a top-left bracket: rotate it into each corner.
                rt.localRotation = Quaternion.Euler(0f, 0f, a.x < 0.5f ? (a.y > 0.5f ? 0f : 90f) : (a.y > 0.5f ? -90f : 180f));
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = s;
                img.color = color;
                img.raycastTarget = false;
            }
        }

        /// <summary>A HUD card: bevelled body plus corner brackets in an accent colour.</summary>
        public static Image Card(RectTransform panel, Color accent, float alpha = 1f)
        {
            var bg = Image(panel, "Card", UISprite("card"), new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * alpha));
            bg.pixelsPerUnitMultiplier = 1.4f;
            Brackets(panel, new Color(accent.r, accent.g, accent.b, 0.85f * alpha));
            return bg;
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

        /// <summary>Thin dark notches dividing the bar into <paramref name="parts"/> (reads health at a glance).</summary>
        public void AddTicks(int parts, float alpha = 0.55f)
        {
            for (int i = 1; i < parts; i++) AddMarker((float)i / parts, new Color(0f, 0f, 0f, alpha), 2f);
        }

        /// <summary>A vertical line at a fraction of the bar (e.g. the Honor "low" line), drawn over the fill.</summary>
        public RectTransform AddMarker(float fraction, Color color, float width = 3f, float overhang = 0f)
        {
            var rt = UIKit.Rect(Root, "Marker", new Vector2(fraction, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(width, Root.sizeDelta.y + overhang * 2f), Vector2.zero);
            rt.anchorMin = new Vector2(fraction, 0f);
            rt.anchorMax = new Vector2(fraction, 1f);
            rt.sizeDelta = new Vector2(width, overhang * 2f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        public float Value => _value;

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
