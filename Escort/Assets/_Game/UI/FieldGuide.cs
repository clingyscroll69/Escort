using System;
using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Skills;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The Sidekick's Field Guide (unofficial): everything the tutorial has taught, re-readable (TIPS, by category;
    /// lessons not met yet show as "???"), every trick with its demo and numbers (SKILLS), and both control schemes
    /// (CONTROLS). Opened from the pause menu.
    /// </summary>
    public sealed class FieldGuide : MonoBehaviour
    {
        public static FieldGuide Current { get; private set; }
        public string Tab { get; private set; }
        public IReadOnlyList<string> ListedLessons => _listed;
        public int LockedCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        Action _onClose;
        object _gate;
        RectTransform _window, _body;
        readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        readonly List<string> _listed = new List<string>();
        LessonCategory _category = LessonCategory.Controls;
        string _lesson;
        const float W = 1640f, H = 940f;

        public static FieldGuide Show(UIRoot root, string tab, Action onClose)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = UIKit.Stretch(root.Overlay, "FieldGuide").gameObject;
            Current = go.AddComponent<FieldGuide>();
            Current._onClose = onClose;
            Current._gate = ModalGate.Push("guide");
            Current.Build();
            Current.SelectTab(string.IsNullOrEmpty(tab) ? "tips" : tab);
            return Current;
        }

        void Build()
        {
            var dim = UIKit.Image(transform, "Dim", null, new Color(0.01f, 0.02f, 0.04f, 0.86f), false);
            dim.raycastTarget = true;
            _window = UIKit.Rect(transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(W, H), Vector2.zero);
            UIKit.Image(_window, "Bg", UIKit.UISprite("card"), new Color(0.035f, 0.06f, 0.1f, 1f)).pixelsPerUnitMultiplier = 1.4f;
            UIKit.Image(_window, "Border", UIKit.Border, new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.6f));
            UIKit.Brackets(_window, UIKit.SystemCyan, 24f, -5f);
            var title = UIKit.Text(_window, "Title", "» SIDEKICK'S FIELD GUIDE  <size=60%><color=#8FB8C8>(UNOFFICIAL)</color></size>", UIKit.Mono, 30, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -26f), new Vector2(1000f, 40f));
            float x = 36f;
            foreach (var (id, label) in new[] { ("tips", "TIPS"), ("skills", "SKILLS"), ("controls", "CONTROLS") })
            {
                var t = id;
                var b = UIKit.Button(_window, "Tab_" + id, label, new Vector2(190f, 46f), Vector2.zero, () => SelectTab(t));
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, -80f);
                x += 202f;
                _tabs[id] = b;
            }
            var back = UIKit.Button(_window, "GuideBack", KeyGlyphs.Format("{cancel}  BACK"), new Vector2(220f, 46f), Vector2.zero, Close);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-36f, -26f);
            _body = UIKit.Rect(_window, "Body", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(W - 72f, H - 170f), new Vector2(36f, -146f));
            _listed.Clear();
            LockedCount = 0;
            foreach (var l in Lessons.All.Where(l => l.Kind != LessonKind.Notice))
            {
                if (TutorialProgress.IsSeen(l.Id)) _listed.Add(l.Id);
                else LockedCount++;
            }
        }

        public void SelectTab(string tab)
        {
            Tab = tab;
            foreach (var kv in _tabs)
            {
                bool on = kv.Key == tab;
                kv.Value.transform.Find("Border").GetComponent<Image>().color = on ? UIKit.Gold : new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.45f);
                kv.Value.GetComponentInChildren<TextMeshProUGUI>().color = on ? UIKit.Gold : UIKit.SystemCyan;
            }
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);
            switch (tab)
            {
                case "skills": BuildSkills(); break;
                case "controls": ControlsTable(_body, new Vector2(_body.sizeDelta.x, _body.sizeDelta.y)); break;
                default: BuildTips(); break;
            }
        }

        public void Close()
        {
            var cb = _onClose;
            _onClose = null;
            Destroy(gameObject);
            cb?.Invoke();
        }

        void OnDestroy()
        {
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
            if (Current == this) Current = null;
        }

        void Update()
        {
            if (GameInput.Instance.Cancel.WasPressedThisFrame()) Close();
        }

        // ---------------------------------------------------------------------------------------------------- tips
        void BuildTips()
        {
            var cats = new[] { LessonCategory.Controls, LessonCategory.TheHero, LessonCategory.TheRoad, LessonCategory.Camp };
            float x = 0f;
            foreach (var c in cats)
            {
                var cat = c;
                var lessons = Lessons.All.Where(l => l.Category == c && l.Kind != LessonKind.Notice).ToList();
                int seen = lessons.Count(l => TutorialProgress.IsSeen(l.Id));
                var b = UIKit.Button(_body, "Cat_" + c, $"{Lessons.CategoryName(c)}  <size=75%>{seen}/{lessons.Count}</size>", new Vector2(300f, 40f), Vector2.zero, () =>
                {
                    _category = cat;
                    _lesson = null;
                    SelectTab("tips");
                });
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, 0f);
                var label = b.GetComponentInChildren<TextMeshProUGUI>();
                label.fontSize = 18;
                bool on = c == _category;
                label.color = on ? UIKit.Gold : UIKit.SystemCyan;
                b.transform.Find("Border").GetComponent<Image>().color = on ? UIKit.Gold : new Color(1f, 1f, 1f, 0.2f);
                x += 312f;
            }
            var list = Lessons.All.Where(l => l.Category == _category && l.Kind != LessonKind.Notice).ToList();
            if (_lesson == null) _lesson = list.FirstOrDefault(l => TutorialProgress.IsSeen(l.Id))?.Id;
            float y = -58f;
            foreach (var l in list)
            {
                var lesson = l;
                bool seen = TutorialProgress.IsSeen(l.Id);
                var b = UIKit.Button(_body, "Lesson_" + l.Id, "", new Vector2(520f, 46f), Vector2.zero, () =>
                {
                    if (!TutorialProgress.IsSeen(lesson.Id)) return;
                    _lesson = lesson.Id;
                    SelectTab("tips");
                });
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                bool sel = l.Id == _lesson;
                b.transform.Find("Border").GetComponent<Image>().color = sel ? UIKit.Gold : new Color(1f, 1f, 1f, seen ? 0.25f : 0.1f);
                if (!seen)
                {
                    var hatch = UIKit.Image(rt, "Hatch", UIKit.UISprite("hatch"), new Color(1f, 1f, 1f, 0.5f), false);
                    hatch.type = Image.Type.Tiled;
                    hatch.rectTransform.SetSiblingIndex(1);
                }
                var icon = UIKit.SpriteImage(rt, "Icon", seen ? UIKit.Icon(l.Icon) : UIKit.Icon("question"), seen ? Color.white : new Color(1f, 1f, 1f, 0.3f),
                    new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(28f, 28f));
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.font = UIKit.Sans;
                t.fontSize = 21;
                t.alignment = TextAlignmentOptions.Left;
                t.rectTransform.offsetMin = new Vector2(52f, 0f);
                t.text = seen ? l.Title : "???";
                t.color = seen ? (sel ? UIKit.Gold : Color.white) : new Color(1f, 1f, 1f, 0.35f);
                y -= 52f;
            }
            // The lesson itself.
            var page = UIKit.Rect(_body, "Page", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_body.sizeDelta.x - 560f, _body.sizeDelta.y - 70f), new Vector2(560f, -58f));
            UIKit.Image(page, "Bg", UIKit.Panel, new Color(0.06f, 0.1f, 0.16f, 0.8f)).pixelsPerUnitMultiplier = 2f;
            var cur = Lessons.Get(_lesson);
            if (cur == null)
            {
                UIKit.Text(page, "Empty", "Nothing here yet. Keep walking: the road teaches.", UIKit.Sans, 24, UIKit.Dim, TextAlignmentOptions.Center);
                return;
            }
            UIKit.SpriteImage(page, "Icon", UIKit.Icon(cur.Icon), UIKit.SystemCyan, new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(64f, 64f));
            var kicker = UIKit.Text(page, "Kicker", Lessons.CategoryName(cur.Category), UIKit.Mono, 17, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(116f, -30f), new Vector2(700f, 24f));
            var head = UIKit.Text(page, "Title", cur.Title, UIKit.Sans, 38, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(116f, -54f), new Vector2(page.sizeDelta.x - 150f, 50f));
            head.fontStyle = FontStyles.Bold;
            var text = UIKit.Text(page, "Text", LessonText(cur), UIKit.Sans, 25, new Color(0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(32f, -130f), new Vector2(page.sizeDelta.x - 64f, page.sizeDelta.y - 160f));
            text.lineSpacing = 8f;
        }

        /// <summary>A lesson's text for reading back: keys as glyphs, variables filled in from the run if there is one.</summary>
        static string LessonText(Lesson l)
        {
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as HS.Sidekick.SidekickAgent : null;
            var sys = sk != null ? sk.GetComponent<SidekickSkills>()?.System : null;
            string body = l.Body.Replace("{coverHint}", "Cover Story can talk him round.")
                                .Replace("{slots}", (sys != null ? sys.SlotCount : 4).ToString())
                                .Replace("{loosen}", "its key");
            return KeyGlyphs.Format(body);
        }

        // -------------------------------------------------------------------------------------------------- skills
        SkillDetailPanel _panel;

        void BuildSkills()
        {
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as HS.Sidekick.SidekickAgent : null;
            var sys = sk != null ? sk.GetComponent<SidekickSkills>()?.System : null;
            var pool = SkillCatalog.Load()?.Implemented.ToList() ?? new List<SkillDefinition>();
            float y = 0f;
            foreach (var d in pool)
            {
                var def = d;
                var b = UIKit.Button(_body, "GuideSkill_" + d.id, "", new Vector2(420f, 70f), Vector2.zero, () => _panel?.Show(def.id, sys));
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                var fam = UIKit.Family(d.family);
                b.transform.Find("Border").GetComponent<Image>().color = new Color(fam.r, fam.g, fam.b, 0.6f);
                UIKit.SpriteImage(rt, "Icon", UIKit.Icon(SkillGuides.IconId(d.id)), Color.white, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(44f, 44f))
                    .rectTransform.pivot = new Vector2(0f, 0.5f);
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.font = UIKit.Sans;
                t.fontSize = 23;
                t.alignment = TextAlignmentOptions.Left;
                t.rectTransform.offsetMin = new Vector2(72f, 0f);
                int rank = sys != null ? sys.RankOf(d.id) : 0;
                t.text = d.displayName + $"\n<size=65%><color=#{ColorUtility.ToHtmlStringRGB(fam)}>{d.family.ToString().ToUpperInvariant()}</color>" +
                         (rank > 0 ? $"  <color=#F2C14E>KNOWN {(rank >= 2 ? "II" : "I")}</color>" : "") + "</size>";
                t.lineSpacing = -10f;
                y -= 78f;
            }
            _panel = SkillDetailPanel.Create(_body, new Vector2(456f, 0f), new Vector2(_body.sizeDelta.x - 456f, _body.sizeDelta.y));
            if (pool.Count > 0) _panel.Show(pool[0].id, sys);
        }

        // ------------------------------------------------------------------------------------------------ controls
        /// <summary>Both control schemes, read from the real bindings (also used by the pause menu).</summary>
        public static void ControlsTable(RectTransform parent, Vector2 size)
        {
            var rows = new (string action, string token)[]
            {
                ("Move", "move"), ("Walk carefully", "walk"), ("Aim", "aim"), ("Knife", "attack"), ("Dodge roll", "dodge"), ("Ping", "ping"),
                ("Interact (search, disarm)", "interact"), ("Crouch", "crouch"), ("Tricks", "skills"), ("Hero Insight", "insight"), ("Pause", "pause"),
            };
            var head = UIKit.Text(parent, "Head", "<pos=0%>ACTION<pos=46%>KEYBOARD & MOUSE<pos=74%>GAMEPAD", UIKit.Mono, 18, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(size.x, 26f));
            float y = -40f, rowH = Mathf.Min(54f, (size.y - 50f) / rows.Length);
            foreach (var (action, token) in rows)
            {
                var line = UIKit.Rect(parent, "Row_" + token, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(size.x, rowH), new Vector2(0f, y));
                UIKit.Image(line, "Rule", UIKit.UISprite("line"), new Color(1f, 1f, 1f, 0.08f)).rectTransform.offsetMax = new Vector2(0f, -(rowH - 2f));
                UIKit.Text(line, "Text", $"<pos=0%>{action}<pos=46%>{KeyGlyphs.Chip(KeyGlyphs.Label(token, GlyphDevice.Keyboard) ?? "-")}<pos=74%>{KeyGlyphs.Chip(KeyGlyphs.Label(token, GlyphDevice.Gamepad) ?? "-")}",
                    UIKit.Sans, Mathf.Min(24f, rowH * 0.5f), Color.white, TextAlignmentOptions.MidlineLeft);
                y -= rowH;
            }
        }
    }
}
