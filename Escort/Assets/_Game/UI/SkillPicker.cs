using System;
using System.Collections.Generic;
using System.Linq;
using HS.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// "Skill soup" pick window (GDD §4.1: pick any skill at any level-up, a second pick is rank 2) and, at camp, the
    /// loadout editor (4 active slots; passives are free). Driven by mouse/pad, or by bots through Pick/ToggleEquip.
    /// </summary>
    public sealed class SkillPicker : MonoBehaviour
    {
        public event Action Done;
        public int PicksLeft { get; private set; }
        public bool LoadoutEditable { get; private set; }
        SkillSystem _sys;
        TextMeshProUGUI _title, _desc, _loadoutHeader;
        RectTransform _rows, _loadout;
        Button _continue;
        string _selected;
        string _titleText;

        public static SkillPicker Show(UIRoot root, SkillSystem sys, int picks, string title, bool loadout, string continueLabel)
        {
            var go = UIKit.Stretch(root.Overlay, "SkillPicker").gameObject;
            var p = go.AddComponent<SkillPicker>();
            p._sys = sys;
            p.PicksLeft = picks;
            p.LoadoutEditable = loadout;
            p._titleText = title;
            sys.AtCamp = loadout;
            p.Build(continueLabel);
            p.Refresh();
            return p;
        }

        void Build(string continueLabel)
        {
            var panel = UIKit.Rect(transform, "Panel", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(640f, 960f), new Vector2(-40f, 0f));
            UIKit.Image(panel, "Bg", UIKit.Panel, UIKit.SystemBg);
            UIKit.Image(panel, "Border", UIKit.Border, UIKit.SystemCyan);
            _title = UIKit.Text(panel, "Title", "", UIKit.Mono, 30, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            _title.rectTransform.offsetMin = new Vector2(28f, 0f);
            _title.rectTransform.offsetMax = new Vector2(-28f, -22f);
            _rows = UIKit.Rect(panel, "Rows", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(584f, 420f), new Vector2(0f, -80f));
            _desc = UIKit.Text(panel, "Desc", "", UIKit.Sans, 21, new Color(0.85f, 0.93f, 1f), TextAlignmentOptions.TopLeft);
            Place(_desc.rectTransform, new Vector2(28f, -510f), new Vector2(584f, 120f));
            _loadoutHeader = UIKit.Text(panel, "LoadoutHeader", "", UIKit.Mono, 24, UIKit.Gold, TextAlignmentOptions.TopLeft);
            Place(_loadoutHeader.rectTransform, new Vector2(28f, -630f), new Vector2(584f, 34f));
            _loadout = UIKit.Rect(panel, "Loadout", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(584f, 150f), new Vector2(0f, -668f));
            _continue = UIKit.Button(panel, "Continue", continueLabel, new Vector2(584f, 70f), Vector2.zero, Continue);
            var crt = (RectTransform)_continue.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 26f);
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        IEnumerable<SkillDefinition> Pool => SkillCatalog.Load()?.Implemented ?? Enumerable.Empty<SkillDefinition>();

        public bool Pick(string id)
        {
            var def = SkillCatalog.Load()?.Get(id);
            if (PicksLeft <= 0 || def == null || !_sys.CanLearn(def)) return false;
            _sys.Learn(def);
            HS.Audio.AudioDirector.Instance?.Play("ui_confirm", null, 0.8f, 0.05f, 0f);
            PicksLeft--;
            _selected = id;
            Refresh();
            return true;
        }

        public bool ToggleEquip(string id)
        {
            if (!LoadoutEditable) return false;
            var st = _sys.Get(id);
            if (st == null || !st.Def.UsesSlot) return false;
            bool ok = _sys.Loadout.Contains(id) ? _sys.Unequip(id) : _sys.Equip(id, _sys.Loadout.Count);
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

        static string Roman(int r) => r <= 0 ? "—" : r == 1 ? "I" : "II";

        void Refresh()
        {
            _title.text = $"{_titleText}\n<size=70%><color=#F2C14E>{(PicksLeft > 0 ? $"{PicksLeft} pick{(PicksLeft == 1 ? "" : "s")} left" : "picks spent")}</color></size>";
            for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);
            int row = 0;
            foreach (var def in Pool)
            {
                var d = def;
                int rank = _sys.RankOf(d.id);
                bool can = PicksLeft > 0 && _sys.CanLearn(d);
                bool sel = _selected == d.id;
                string hint = sel && can ? "  <size=70%><color=#F2C14E>click again to learn</color></size>" : "";
                string label = $"<align=left>{d.displayName}  <size=70%><color=#8FB8C8>{d.family}{(d.type == SkillType.Passive ? " · passive" : "")}</color></size>{hint}<line-height=0>\n<align=right>{Roman(rank)}</align>";
                // First click reads the skill; a second click on the selected row learns it (a pick is permanent).
                var b = UIKit.Button(_rows, "Skill_" + d.id, "", new Vector2(584f, 62f), Vector2.zero, () =>
                {
                    if (_selected != d.id)
                    {
                        _selected = d.id;
                        Refresh();
                    }
                    else Pick(d.id);
                });
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -row * 69f);
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.font = UIKit.Sans;
                t.fontSize = 24;
                t.text = label;
                t.color = can || rank > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                t.rectTransform.offsetMin = new Vector2(18f, 0f);
                t.rectTransform.offsetMax = new Vector2(-22f, 0f);
                b.interactable = true;
                row++;
            }
            var selDef = _selected != null ? SkillCatalog.Load()?.Get(_selected) : null;
            _desc.text = selDef != null ? $"<b>{selDef.displayName}</b> — {selDef.uses}" : "<color=#8FB8C8>Click a skill to read it; click it again to learn it. A second pick of the same skill is rank II.</color>";
            for (int i = _loadout.childCount - 1; i >= 0; i--) Destroy(_loadout.GetChild(i).gameObject);
            _loadoutHeader.gameObject.SetActive(LoadoutEditable);
            if (LoadoutEditable)
            {
                _loadoutHeader.text = $"LOADOUT  <size=75%>{_sys.Loadout.Count}/{_sys.SlotCount} slots · passives are free</size>";
                int k = 0;
                foreach (var st in _sys.Known.Values.Where(s => s.Def.UsesSlot).OrderBy(s => s.Def.displayName))
                {
                    var id = st.Id;
                    bool on = _sys.Loadout.Contains(id);
                    var b = UIKit.Button(_loadout, "Equip_" + id, (on ? "» " : "  ") + st.Def.displayName, new Vector2(286f, 46f), Vector2.zero, () => ToggleEquip(id));
                    var rt = (RectTransform)b.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2((k % 2) * 298f, -(k / 2) * 52f);
                    var t = b.GetComponentInChildren<TextMeshProUGUI>();
                    t.fontSize = 22;
                    t.color = on ? UIKit.Gold : new Color(1f, 1f, 1f, 0.6f);
                    k++;
                }
            }
            _continue.interactable = PicksLeft <= 0;
        }
    }
}
