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
    /// One skill explained (spec §5.1): name, family, type and known rank; tagline; the live demo beside its numbers
    /// at both ranks and how Callum's code reads it; what it does and how to use it; and how it pairs with a kit
    /// (owned partners first, then "also pairs with"). Shared by the level-up picker and the Field Guide.
    /// </summary>
    public sealed class SkillDetailPanel : MonoBehaviour
    {
        public const float ViewW = 620f, ViewH = 349f;

        RectTransform _root, _synergies;
        TextMeshProUGUI _name, _meta, _tagline, _stats, _conduct, _conductLabel, _uses;
        Image _conductBg;
        readonly List<string> _partners = new List<string>();

        public DemoViewport Demo { get; private set; }
        public string Shown { get; private set; }
        public IReadOnlyList<string> SynergyPartners => _partners;
        public RectTransform Root => _root;

        public static SkillDetailPanel Create(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var rt = UIKit.Rect(parent, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f), size, pos);
            var p = rt.gameObject.AddComponent<SkillDetailPanel>();
            p._root = rt;
            p.Build();
            return p;
        }

        void Build()
        {
            float dw = _root.sizeDelta.x;
            _name = UIKit.Text(_root, "Name", "", UIKit.Sans, 40, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(dw, 50f));
            _name.fontStyle = FontStyles.Bold;
            _meta = UIKit.Text(_root, "Meta", "", UIKit.Mono, 17, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(_meta.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -50f), new Vector2(dw, 24f));
            _tagline = UIKit.Text(_root, "Tagline", "", UIKit.Sans, 20, new Color(0.8f, 0.88f, 0.95f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_tagline.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -76f), new Vector2(dw, 28f));
            _tagline.fontStyle = FontStyles.Italic;
            // The demo, with the numbers and Callum's view beside it.
            var host = UIKit.Rect(_root, "DemoHost", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ViewW, ViewH), new Vector2(0f, -112f));
            Demo = DemoViewport.Create(host, new Vector2(ViewW, ViewH));
            var side = UIKit.Rect(_root, "Side", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(dw - ViewW - 24f, ViewH), new Vector2(ViewW + 24f, -112f));
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
            _conductLabel = UIKit.Text(chip, "ConductLabel", "", UIKit.Mono, 15, Color.white, TextAlignmentOptions.Center);
            _conduct = UIKit.Text(side, "CallumView", "", UIKit.Sans, 17, new Color(0.86f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_conduct.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -250f), new Vector2(side.sizeDelta.x - 32f, 96f));
            // What it does and how to use it.
            _uses = UIKit.Text(_root, "Uses", "", UIKit.Sans, 20, new Color(0.9f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(_uses.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -112f - ViewH - 18f), new Vector2(dw, 92f));
            // How it pairs with a kit.
            _synergies = UIKit.Rect(_root, "Synergies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(dw, 150f), new Vector2(0f, -112f - ViewH - 120f));
        }

        public static string Name(string id) => SkillCatalog.Load()?.Get(id)?.displayName ?? id;

        /// <summary>
        /// Show a skill. With a kit (the sidekick's skills): its known rank, the demo at the rank a next pick would give,
        /// and synergies with what's owned. Without one (no run): rank I and the pairs it has.
        /// </summary>
        public void Show(string id, SkillSystem kit)
        {
            var def = SkillCatalog.Load()?.Get(id);
            if (def == null) return;
            bool changed = Shown != id;
            Shown = id;
            int rank = kit != null ? kit.RankOf(id) : 0;
            int demoRank = Mathf.Min(rank + 1, SkillSystem.MaxRank);
            var fam = UIKit.Family(def.family);
            _name.text = def.displayName;
            _meta.text = $"<color=#{ColorUtility.ToHtmlStringRGB(fam)}>{def.family.ToString().ToUpperInvariant()}</color>  ·  " +
                         (def.type == SkillType.Passive ? "PASSIVE (no slot)" : "ACTIVE") +
                         (rank > 0 ? $"  ·  <color=#F2C14E>KNOWN: RANK {(rank >= 2 ? "II" : "I")}</color>" : "");
            var guide = SkillGuides.Get(id);
            _tagline.text = guide?.Tagline ?? "";
            // Both ranks; the one a next pick would give in gold.
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#8FB8C8><size=85%>RANK<pos=58%>I<pos=80%>II</size></color>\n");
            foreach (var (label, r1, r2) in SkillGuides.StatLines(def))
            {
                string c1 = demoRank == 1 ? "#F2C14E" : "#FFFFFF", c2 = demoRank == 2 ? "#F2C14E" : "#9AA3AD";
                sb.Append(label).Append("<pos=58%><color=").Append(c1).Append('>').Append(r1).Append("</color><pos=80%><color=")
                  .Append(c2).Append('>').Append(r2).Append("</color>\n");
            }
            _stats.text = sb.ToString();
            Color cc = def.dishonor == SabotageSeverity.Major ? UIKit.Danger : def.dishonor == SabotageSeverity.Minor ? UIKit.Ochre : (Color)new Color32(112, 204, 124, 255);
            _conductBg.color = new Color(cc.r * 0.25f, cc.g * 0.25f, cc.b * 0.25f, 0.95f);
            _conductLabel.text = SkillGuides.Conduct(def);
            _conductLabel.color = cc;
            _conduct.text = guide?.CallumView ?? "";
            _uses.text = def.uses + (guide != null ? "\n<color=#8FB8C8>" + KeyGlyphs.Format(guide.HowTo) + "</color>" : "");
            BuildSynergies(def, kit);
            if (changed || !Demo.Playing) Demo.Play(id, demoRank);
        }

        void BuildSynergies(SkillDefinition def, SkillSystem sys)
        {
            for (int i = _synergies.childCount - 1; i >= 0; i--) Destroy(_synergies.GetChild(i).gameObject);
            _partners.Clear();
            var kit = sys != null ? sys.Known.Keys.Where(k => k != def.id).ToList() : new List<string>();
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
                bool owned = kit.Count > 0;
                y = SynergyLine(other, $"<b>{Name(other)}</b>: {note}", owned ? new Color(1f, 1f, 1f, 0.35f) : Color.white,
                    owned ? new Color(1f, 1f, 1f, 0.5f) : new Color(0.9f, 0.95f, 1f, 0.9f), y);
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
    }
}
