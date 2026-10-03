using System;
using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Skills;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// "Skill soup" pick window (GDD §4.1: pick any skill at any level-up, a second pick is rank 2) and, at camp, the
    /// loadout editor (4 active slots; passives are free). Each skill explains itself (spec §5): what it does and how
    /// to use it, its numbers at both ranks, how Callum's code reads it, a live demo, and how it pairs with the skills
    /// you already own; learning one that completes a pair says so. Mouse: click a card to read it (and watch), click
    /// it again to learn it. Pad/keys: moving onto a card shows it; confirm twice learns it. Bots use Pick/ToggleEquip.
    /// </summary>
    public sealed class SkillPicker : MonoBehaviour
    {
        public event Action Done;
        /// <summary>(learned, partner) for each pair a new skill completes with your kit.</summary>
        public event Action<string, string> SynergyFormed;
        public int PicksLeft { get; private set; }
        public bool LoadoutEditable { get; private set; }
        public string Selected => _selected;
        public string Shown => _shown;
        public DemoViewport Demo { get; private set; }
        public IReadOnlyList<string> SynergyPartners => _partners;

        SkillSystem _sys;
        string _selected, _shown, _titleText, _continueLabel;
        readonly List<string> _partners = new List<string>();
        readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();
        RectTransform _window, _list, _detail, _synergies, _loadout, _flourish;
        TextMeshProUGUI _title, _picks, _hint, _name, _meta, _tagline, _stats, _conduct, _uses, _loadoutHeader, _flourishText;
        Image _conductBg;
        Button _continue;
        CanvasGroup _flourishGroup;
        float _flourishT = -1f;
        object _gate;

        const float W = 1640f, H = 940f, ListW = 470f, CardH = 88f, ViewW = 620f, ViewH = 349f;

        sealed class Row
        {
            public SkillDefinition Def;
            public Button Button;
            public Image Bg, Frame, Icon;
            public TextMeshProUGUI Name, Meta, Badge;
            public Image[] Ranks;
        }

        /// <summary>Moving onto a card with a pad or the keys shows it (without learning it).</summary>
        sealed class RowSelect : MonoBehaviour, ISelectHandler
        {
            public Action OnSelected;
            public void OnSelect(BaseEventData e) => OnSelected?.Invoke();
        }

        public static SkillPicker Show(UIRoot root, SkillSystem sys, int picks, string title, bool loadout, string continueLabel)
        {
            var go = UIKit.Stretch(root.Overlay, "SkillPicker").gameObject;
            var p = go.AddComponent<SkillPicker>();
            p._sys = sys;
            p.PicksLeft = picks;
            p.LoadoutEditable = loadout;
            p._titleText = title;
            p._continueLabel = continueLabel;
            sys.AtCamp = loadout;
            // Clicks here must not reach the sidekick (a click is the knife button): gameplay input waits.
            p._gate = ModalGate.Push("picker", pauseSim: false, blockGameplay: true);
            p.Build();
            p.Refresh();
            p.ShowFirst();
            return p;
        }

        IEnumerable<SkillDefinition> Pool => SkillCatalog.Load()?.Implemented ?? Enumerable.Empty<SkillDefinition>();

        // ------------------------------------------------------------------------------------------------------ build
        void Build()
        {
            var dim = UIKit.Image(transform, "Dim", null, new Color(0.01f, 0.02f, 0.04f, 0.72f), false);
            dim.raycastTarget = true;
            _window = UIKit.Rect(transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(W, H), Vector2.zero);
            var bg = UIKit.Image(_window, "Bg", UIKit.UISprite("card"), new Color(0.035f, 0.06f, 0.1f, 1f));
            bg.pixelsPerUnitMultiplier = 1.4f;
            UIKit.Image(_window, "Border", UIKit.Border, new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.6f));
            UIKit.Brackets(_window, UIKit.SystemCyan, 24f, -5f);
            // Header: title, picks left, and a first-time hint.
            _title = UIKit.Text(_window, "Title", _titleText, UIKit.Mono, 30, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            UIKit.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -26f), new Vector2(1000f, 40f));
            _picks = UIKit.Text(_window, "Picks", "", UIKit.Mono, 22, UIKit.Gold, TextAlignmentOptions.TopRight);
            UIKit.Place(_picks.rectTransform, new Vector2(1f, 1f), new Vector2(-36f, -30f), new Vector2(520f, 32f));
            _hint = UIKit.Text(_window, "Hint", "", UIKit.Sans, 19, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(_hint.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -70f), new Vector2(W - 72f, 26f));
            // Left: one card per skill.
            _list = UIKit.Rect(_window, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ListW, 640f), new Vector2(36f, -108f));
            int i = 0;
            foreach (var def in Pool) _rows[def.id] = BuildRow(def, i++);
            // Right: the selected skill in detail.
            _detail = UIKit.Rect(_window, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(W - ListW - 36f - 70f, 760f), new Vector2(36f + ListW + 34f, -104f));
            BuildDetail();
            // Footer: loadout (camp) and Continue.
            _loadoutHeader = UIKit.Text(_window, "LoadoutHeader", "", UIKit.Mono, 20, UIKit.Gold, TextAlignmentOptions.BottomLeft);
            UIKit.Place(_loadoutHeader.rectTransform, new Vector2(0f, 0f), new Vector2(36f, 92f), new Vector2(800f, 26f));
            _loadout = UIKit.Rect(_window, "Loadout", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1100f, 56f), new Vector2(36f, 28f));
            _continue = UIKit.Button(_window, "Continue", _continueLabel, new Vector2(440f, 64f), Vector2.zero, Continue);
            var crt = (RectTransform)_continue.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-36f, 28f);
            // "New synergy" flourish: over the detail pane's tagline for a moment.
            _flourish = UIKit.Rect(_detail, "Flourish", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_detail.sizeDelta.x, 46f), new Vector2(0f, -58f));
            _flourishGroup = _flourish.gameObject.AddComponent<CanvasGroup>();
            _flourishGroup.alpha = 0f;
            _flourishGroup.blocksRaycasts = false;
            UIKit.Card(_flourish, UIKit.Gold, 1.2f); // opaque: it sits over the tagline
            _flourishText = UIKit.Text(_flourish, "Text", "", UIKit.Mono, 22, UIKit.Gold, TextAlignmentOptions.Center);
            WireNavigation();
        }

        Row BuildRow(SkillDefinition def, int index)
        {
            var r = new Row { Def = def };
            r.Button = UIKit.Button(_list, "Skill_" + def.id, "", new Vector2(ListW, CardH - 8f), Vector2.zero, () => OnRowClicked(def.id));
            var rt = (RectTransform)r.Button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, -index * CardH);
            r.Bg = r.Button.GetComponent<Image>();
            r.Frame = rt.Find("Border").GetComponent<Image>();
            rt.Find("Label").gameObject.SetActive(false);
            r.Button.gameObject.AddComponent<RowSelect>().OnSelected = () => ShowSkill(def.id);
            var fam = UIKit.Family(def.family);
            var tile = UIKit.Rect(rt, "Tile", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(62f, 62f), new Vector2(10f, 0f));
            UIKit.Image(tile, "Bg", UIKit.Panel, new Color(0.02f, 0.03f, 0.06f, 0.9f));
            UIKit.Image(tile, "Frame", UIKit.UISprite("slot"), new Color(fam.r, fam.g, fam.b, 0.9f));
            r.Icon = UIKit.SpriteImage(tile, "Icon", UIKit.Icon(SkillGuides.IconId(def.id)), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
            r.Icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            r.Name = UIKit.Text(rt, "Name", def.displayName, UIKit.Sans, 25, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(r.Name.rectTransform, new Vector2(0f, 1f), new Vector2(86f, -12f), new Vector2(300f, 32f));
            r.Name.fontStyle = FontStyles.Bold;
            r.Meta = UIKit.Text(rt, "Meta", "", UIKit.Mono, 15, fam, TextAlignmentOptions.TopLeft);
            UIKit.Place(r.Meta.rectTransform, new Vector2(0f, 1f), new Vector2(86f, -46f), new Vector2(300f, 22f));
            r.Badge = UIKit.Text(rt, "Badge", "", UIKit.Mono, 14, UIKit.Gold, TextAlignmentOptions.TopRight);
            UIKit.Place(r.Badge.rectTransform, new Vector2(1f, 1f), new Vector2(-14f, -48f), new Vector2(200f, 20f));
            r.Ranks = new Image[2];
            for (int k = 0; k < 2; k++)
            {
                r.Ranks[k] = UIKit.SpriteImage(rt, "Rank" + k, UIKit.UISprite("diamond"), UIKit.Gold, new Vector2(1f, 1f), new Vector2(-14f - k * 16f, -16f), new Vector2(14f, 14f));
                r.Ranks[k].rectTransform.pivot = new Vector2(1f, 1f);
            }
            return r;
        }

        void BuildDetail()
        {
            float dw = _detail.sizeDelta.x;
            _name = UIKit.Text(_detail, "Name", "", UIKit.Sans, 40, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(dw, 50f));
            _name.fontStyle = FontStyles.Bold;
            _meta = UIKit.Text(_detail, "Meta", "", UIKit.Mono, 17, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(_meta.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -50f), new Vector2(dw, 24f));
            _tagline = UIKit.Text(_detail, "Tagline", "", UIKit.Sans, 20, new Color(0.8f, 0.88f, 0.95f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_tagline.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -76f), new Vector2(dw, 28f));
            _tagline.fontStyle = FontStyles.Italic;
            // The demo, with the numbers and Callum's view beside it.
            var host = UIKit.Rect(_detail, "DemoHost", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ViewW, ViewH), new Vector2(0f, -112f));
            Demo = DemoViewport.Create(host, new Vector2(ViewW, ViewH));
            var side = UIKit.Rect(_detail, "Side", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(dw - ViewW - 24f, ViewH), new Vector2(ViewW + 24f, -112f));
            UIKit.Image(side, "Bg", UIKit.Panel, new Color(0.06f, 0.1f, 0.16f, 0.8f)).pixelsPerUnitMultiplier = 2f;
            var statsHead = UIKit.Text(side, "StatsHead", "AT EACH RANK", UIKit.Mono, 15, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(statsHead.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(300f, 20f));
            _stats = UIKit.Text(side, "Stats", "", UIKit.Sans, 18, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(_stats.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -36f), new Vector2(side.sizeDelta.x - 32f, 150f));
            var callumHead = UIKit.Text(side, "CallumHead", "HOW CALLUM SEES IT", UIKit.Mono, 15, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(callumHead.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -192f), new Vector2(300f, 20f));
            var chip = UIKit.Rect(side, "Conduct", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, 26f), new Vector2(16f, -216f));
            _conductBg = UIKit.Image(chip, "Bg", UIKit.Panel, Color.clear);
            _conductBg.pixelsPerUnitMultiplier = 2.4f;
            var conductLabel = UIKit.Text(chip, "Label", "", UIKit.Mono, 15, Color.white, TextAlignmentOptions.Center);
            conductLabel.name = "ConductLabel";
            _conduct = UIKit.Text(side, "CallumView", "", UIKit.Sans, 17, new Color(0.86f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_conduct.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -250f), new Vector2(side.sizeDelta.x - 32f, 96f));
            // What it does and how to use it.
            _uses = UIKit.Text(_detail, "Uses", "", UIKit.Sans, 20, new Color(0.9f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_uses.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -112f - ViewH - 18f), new Vector2(dw, 92f));
            // Synergies with your kit.
            _synergies = UIKit.Rect(_detail, "Synergies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(dw, 150f), new Vector2(0f, -112f - ViewH - 120f));
        }

        void WireNavigation()
        {
            var ordered = Pool.Select(d => _rows[d.id].Button).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? ordered[i - 1] : null,
                    selectOnDown = i < ordered.Count - 1 ? ordered[i + 1] : _continue,
                    selectOnRight = _continue,
                };
                ordered[i].navigation = nav;
            }
            if (ordered.Count > 0)
                _continue.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = ordered[ordered.Count - 1], selectOnLeft = ordered[ordered.Count - 1] };
        }

        // ------------------------------------------------------------------------------------------------- actions
        void OnRowClicked(string id)
        {
            // First click reads the skill (and plays its demo); a second click on the selected card learns it.
            if (_selected != id)
            {
                _selected = id;
                ShowSkill(id);
                Refresh();
            }
            else Pick(id);
        }

        /// <summary>Read a skill: the detail pane and its demo (what the first click does).</summary>
        public void Select(string id)
        {
            if (!_rows.ContainsKey(id)) return;
            _selected = id;
            ShowSkill(id);
            Refresh();
        }

        public bool Pick(string id)
        {
            var def = SkillCatalog.Load()?.Get(id);
            if (PicksLeft <= 0 || def == null || !_sys.CanLearn(def)) return false;
            bool isNew = !_sys.Has(id);
            var kitBefore = _sys.Known.Keys.ToList();
            _sys.Learn(def);
            HS.Audio.AudioDirector.Instance?.Play("ui_confirm", null, 0.8f, 0.05f, 0f);
            PicksLeft--;
            _selected = id;
            if (isNew)
            {
                var formed = SkillSynergies.With(id, kitBefore);
                foreach (var (other, _) in formed) SynergyFormed?.Invoke(id, other);
                if (formed.Count > 0) Flourish(def, formed.Select(f => f.other).ToList());
            }
            ShowSkill(id);
            Refresh();
            return true;
        }

        public bool ToggleEquip(string id)
        {
            if (!LoadoutEditable) return false;
            var st = _sys.Get(id);
            if (st == null || !st.Def.UsesSlot) return false;
            bool ok = _sys.Loadout.Contains(id) ? _sys.Unequip(id) : _sys.Equip(id, _sys.Loadout.Count);
            HS.Audio.AudioDirector.Instance?.Play("ui_select", null, 0.5f, 0.05f, 0f);
            Refresh();
            return ok;
        }

        public void Continue()
        {
            if (PicksLeft > 0) return;
            _sys.AtCamp = false;
            Done?.Invoke();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
        }

        void Flourish(SkillDefinition def, List<string> partners)
        {
            var names = partners.Select(p => SkillCatalog.Load()?.Get(p)?.displayName ?? p);
            _flourishText.text = $"NEW SYNERGY  ·  {def.displayName} + {string.Join(", ", names)}";
            _flourishT = 0f;
            HS.Audio.AudioDirector.Instance?.Play("chime", null, 0.5f, 0.05f, 0f);
        }

        void ShowFirst()
        {
            var first = Pool.FirstOrDefault(d => _sys.CanLearn(d)) ?? Pool.FirstOrDefault();
            if (first != null) ShowSkill(first.id);
            // A pad player needs a focus to move from; a mouse player's first click must still only read.
            if (KeyGlyphs.Current == GlyphDevice.Gamepad && first != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_rows[first.id].Button.gameObject);
        }

        // ------------------------------------------------------------------------------------------------ detail
        void ShowSkill(string id)
        {
            var def = SkillCatalog.Load()?.Get(id);
            if (def == null) return;
            bool changed = _shown != id;
            _shown = id;
            int rank = _sys.RankOf(id);
            int demoRank = Mathf.Min(rank + 1, SkillSystem.MaxRank);
            var fam = UIKit.Family(def.family);
            _name.text = def.displayName;
            _meta.text = $"<color=#{ColorUtility.ToHtmlStringRGB(fam)}>{def.family.ToString().ToUpperInvariant()}</color>  ·  " +
                         (def.type == SkillType.Passive ? "PASSIVE (no slot)" : "ACTIVE") +
                         (rank > 0 ? $"  ·  <color=#F2C14E>KNOWN: RANK {(rank >= 2 ? "II" : "I")}</color>" : "");
            var guide = SkillGuides.Get(id);
            _tagline.text = guide?.Tagline ?? "";
            // Stats: both ranks, the one your next pick would give in gold.
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#8FB8C8><size=85%>RANK<pos=58%>I<pos=80%>II</size></color>\n");
            foreach (var (label, r1, r2) in SkillGuides.StatLines(def))
            {
                string c1 = demoRank == 1 ? "#F2C14E" : "#FFFFFF", c2 = demoRank == 2 ? "#F2C14E" : "#9AA3AD";
                sb.Append(label).Append("<pos=58%><color=").Append(c1).Append('>').Append(r1).Append("</color><pos=80%><color=")
                  .Append(c2).Append('>').Append(r2).Append("</color>\n");
            }
            _stats.text = sb.ToString();
            var conduct = SkillGuides.Conduct(def);
            var cc = def.dishonor == SabotageSeverity.Major ? UIKit.Danger : def.dishonor == SabotageSeverity.Minor ? UIKit.Ochre : new Color32(112, 204, 124, 255);
            _conductBg.color = new Color(cc.r * 0.25f, cc.g * 0.25f, cc.b * 0.25f, 0.95f);
            var cl = _conductBg.transform.parent.Find("ConductLabel").GetComponent<TextMeshProUGUI>();
            cl.text = conduct;
            cl.color = cc;
            _conduct.text = guide?.CallumView ?? "";
            _uses.text = def.uses + (guide != null ? "\n<color=#8FB8C8>" + KeyGlyphs.Format(guide.HowTo) + "</color>" : "");
            BuildSynergies(def);
            if (changed || !Demo.Playing) Demo.Play(id, demoRank);
        }

        void BuildSynergies(SkillDefinition def)
        {
            for (int i = _synergies.childCount - 1; i >= 0; i--) Destroy(_synergies.GetChild(i).gameObject);
            _partners.Clear();
            var kit = _sys.Known.Keys.Where(k => k != def.id).ToList();
            var with = SkillSynergies.With(def.id, kit);
            var without = SkillSynergies.Without(def.id, kit).Where(w => w.other != def.id).ToList();
            float y = 0f;
            var head = UIKit.Text(_synergies, "Head", with.Count > 0 ? "SYNERGIES WITH YOUR KIT" : kit.Count > 0 ? "SYNERGIES" : "PAIRS WELL WITH", UIKit.Mono, 15, UIKit.Dim);
            UIKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(_synergies.sizeDelta.x, 20f));
            y -= 26f;
            int lines = 0;
            foreach (var (other, note) in with)
            {
                _partners.Add(other);
                y = SynergyLine(other, $"With your <b>{Name(other)}</b>: {note}", UIKit.Gold, Color.white, y);
                lines++;
            }
            if (with.Count == 0 && kit.Count > 0)
            {
                var none = UIKit.Text(_synergies, "None", "No direct synergy with your current kit.", UIKit.Sans, 17, UIKit.Dim);
                UIKit.Place(none.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(_synergies.sizeDelta.x, 22f));
                y -= 28f;
            }
            if (without.Count > 0 && lines < 3 && (with.Count > 0 || kit.Count > 0))
            {
                var also = UIKit.Text(_synergies, "Also", "ALSO PAIRS WITH", UIKit.Mono, 13, new Color(0.56f, 0.72f, 0.78f, 0.7f));
                UIKit.Place(also.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y - 2f), new Vector2(_synergies.sizeDelta.x, 18f));
                y -= 22f;
            }
            foreach (var (other, note) in without)
            {
                if (lines >= 3) break;
                y = SynergyLine(other, $"<b>{Name(other)}</b>: {note}", new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.5f), y);
                lines++;
            }
        }

        float SynergyLine(string other, string text, Color iconTint, Color textColor, float y)
        {
            var rt = UIKit.Rect(_synergies, "Syn_" + other, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_synergies.sizeDelta.x, 26f), new Vector2(0f, y));
            UIKit.SpriteImage(rt, "Icon", UIKit.Icon(SkillGuides.IconId(other)), iconTint, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(24f, 24f));
            var t = UIKit.Text(rt, "Text", text, UIKit.Sans, 17, textColor, TextAlignmentOptions.TopLeft);
            t.rectTransform.offsetMin = new Vector2(34f, 0f);
            float h = Mathf.Max(24f, t.GetPreferredValues(text, _synergies.sizeDelta.x - 34f, 0f).y);
            rt.sizeDelta = new Vector2(_synergies.sizeDelta.x, h);
            return y - h - 6f;
        }

        static string Name(string id) => SkillCatalog.Load()?.Get(id)?.displayName ?? id;

        static string Roman(int r) => r <= 0 ? "" : r == 1 ? "I" : "II";

        // ----------------------------------------------------------------------------------------------- refresh
        void Refresh()
        {
            var sbPicks = new System.Text.StringBuilder();
            if (PicksLeft > 0) sbPicks.Append(PicksLeft == 1 ? "1 PICK LEFT" : PicksLeft + " PICKS LEFT");
            else sbPicks.Append("<color=#8FB8C8>PICKS SPENT</color>");
            _picks.text = sbPicks.ToString();
            var kit = _sys.Known.Keys.ToList();
            foreach (var r in _rows.Values)
            {
                var d = r.Def;
                int rank = _sys.RankOf(d.id);
                bool can = PicksLeft > 0 && _sys.CanLearn(d);
                bool sel = _selected == d.id;
                var fam = UIKit.Family(d.family);
                r.Meta.text = d.family.ToString().ToUpperInvariant() + (d.type == SkillType.Passive ? "  ·  PASSIVE" : "");
                var partners = rank == 0 ? SkillSynergies.With(d.id, kit) : new List<(string other, string note)>();
                r.Badge.text = sel && can ? (rank > 0 ? "CLICK AGAIN: RANK II" : "CLICK AGAIN TO LEARN")
                    : partners.Count == 1 ? "PAIRS WITH " + Name(partners[0].other).ToUpperInvariant()
                    : partners.Count > 1 ? $"PAIRS WITH {partners.Count} OF YOURS"
                    : rank >= SkillSystem.MaxRank ? "MAX RANK" : "";
                r.Badge.color = sel && can ? Color.white : UIKit.Gold;
                for (int k = 0; k < r.Ranks.Length; k++) r.Ranks[k].color = k < rank ? UIKit.Gold : new Color(1f, 1f, 1f, 0.16f);
                r.Name.color = can || rank > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                r.Icon.color = can || rank > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                r.Bg.color = sel ? new Color(0.1f, 0.2f, 0.3f, 0.98f) : new Color32(14, 26, 42, 235);
                r.Frame.color = sel ? UIKit.Gold : new Color(fam.r, fam.g, fam.b, 0.45f);
            }
            bool first = TutorialProgress.TipsEnabled && !TutorialProgress.IsSeen(LoadoutEditable ? "loadout" : "levelup");
            var lesson = Lessons.Get(LoadoutEditable ? "loadout" : "levelup");
            _hint.text = first && lesson != null
                ? "<color=#7BE6FF>»</color> " + KeyGlyphs.Format(lesson.Body.Replace("{slots}", _sys.SlotCount.ToString()))
                : LoadoutEditable ? "Click a trick to read it and watch it; click again to learn it." : "";
            RefreshLoadout();
            _continue.interactable = PicksLeft <= 0;
            var label = _continue.GetComponentInChildren<TextMeshProUGUI>();
            label.text = PicksLeft > 0 ? $"<color=#8FB8C8>{_continueLabel}</color>" : _continueLabel;
        }

        void RefreshLoadout()
        {
            for (int i = _loadout.childCount - 1; i >= 0; i--) Destroy(_loadout.GetChild(i).gameObject);
            _loadoutHeader.gameObject.SetActive(LoadoutEditable);
            if (!LoadoutEditable) return;
            _loadoutHeader.text = $"LOADOUT  <size=80%>{_sys.Loadout.Count}/{_sys.SlotCount} slots  ·  passives are free  ·  click to bench or bring back</size>";
            int k = 0;
            foreach (var st in _sys.Known.Values.Where(s => s.Def.UsesSlot).OrderBy(s => s.Def.displayName))
            {
                var id = st.Id;
                bool on = _sys.Loadout.Contains(id);
                var b = UIKit.Button(_loadout, "Equip_" + id, "", new Vector2(250f, 52f), Vector2.zero, () => ToggleEquip(id));
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(k * 262f, 0f);
                rt.Find("Border").GetComponent<Image>().color = on ? UIKit.Gold : new Color(1f, 1f, 1f, 0.25f);
                b.GetComponent<Image>().color = on ? new Color(0.12f, 0.16f, 0.12f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, 0.9f);
                UIKit.SpriteImage(rt, "Icon", UIKit.Icon(SkillGuides.IconId(id)), on ? Color.white : new Color(1f, 1f, 1f, 0.45f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(34f, 34f))
                    .rectTransform.pivot = new Vector2(0f, 0.5f);
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.font = UIKit.Sans;
                t.fontSize = 19;
                t.alignment = TextAlignmentOptions.Left;
                t.rectTransform.offsetMin = new Vector2(52f, 0f);
                t.text = (on ? "<color=#F2C14E>EQUIPPED</color>\n" : "<color=#8FB8C8>BENCHED</color>\n") + st.Def.displayName + (st.Rank >= 2 ? " II" : "");
                t.color = on ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                t.lineSpacing = -8f;
                k++;
            }
        }

        void Update()
        {
            if (_flourishT >= 0f)
            {
                _flourishT += Time.unscaledDeltaTime;
                _flourishGroup.alpha = _flourishT < 0.25f ? _flourishT / 0.25f : Mathf.Clamp01((3.2f - _flourishT) / 0.6f);
                _flourish.localScale = Vector3.one * (1f + 0.08f * Mathf.Max(0f, 1f - _flourishT * 4f));
                if (_flourishT > 3.2f) _flourishT = -1f;
            }
        }
    }
}
