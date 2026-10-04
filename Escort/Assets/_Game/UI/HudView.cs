using System.Collections.Generic;
using System.Text;
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using HS.Skills;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// Gameplay HUD (reference 1920×1080). Hero card, top-left: his current rule (the icon over his head, mirrored here
    /// so it reads even when he's off screen; Hero Insight adds it in words), health, Honor with its "low" line, wound
    /// chips. Your card, bottom-left: level and XP (a level earned on the road is granted at the camp), health, dodge
    /// charges refilling, sneaking / out-of-reach status. Skill bar, bottom-centre: icons, keys (keyboard or gamepad,
    /// whichever was used last), cooldown sweep and a flash when ready, rank pips; your basic verbs to its left and
    /// passives to its right. Interact prompt and channel bar above it; a boss bar on top. Rapport is never shown.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        UIBar _heroHp, _honor, _skHp, _bossHp, _channel, _xp;
        TextMeshProUGUI _heroName, _insight, _honorLabel, _promptText, _channelLabel, _channelHint, _bossName, _level, _levelUp, _xpLabel;
        RectTransform _ruleChip, _ruleIconRt, _honorRow, _woundRow, _pipsRow, _statusRow, _xpRow, _skillsRt, _passivesRt, _bossRoot, _channelRoot, _promptRoot, _promptKey;
        Image _ruleIcon;
        Pip[] _pips;
        Slot[] _slots;
        Verb[] _verbs;
        TextMeshProUGUI _downedChip, _recallChip, _rationsChip, _sneakChip, _reachChip;
        readonly List<GameObject> _woundChips = new List<GameObject>();
        readonly List<(string id, Image frame)> _passives = new List<(string, Image)>();
        string _woundSig = "", _passiveSig = "", _rule;
        float _rulePop;
        GlyphDevice _device = (GlyphDevice)(-1);
        Agent _boss;
        public bool InsightOn;
        TextMeshProUGUI _skName;
        string _metName;
        float _swapT = -1f;
        const float SwapTime = 0.8f;
        const string SkLabel = "YOU  <size=70%><color=#E4A84E>{0}'s SIDEKICK</color></size>";
        const string SwapGlyphs = "#@%&$*+=<>?/\\|";

        sealed class Slot
        {
            public RectTransform Root, Key;
            public Image Bg, Frame, Icon, Cool, Flash, Under;
            public Image[] Ranks;
            public TextMeshProUGUI Name, Secs;
            public string Id;
            public bool Cooling;
            public float FlashT;
        }

        sealed class Pip
        {
            public Image Back, Fill;
        }

        sealed class Verb
        {
            public string Token;
            public RectTransform Key;
        }

        public static HudView Create(UIRoot root)
        {
            var go = UIKit.Stretch(root.Hud, "HudView").gameObject;
            var v = go.AddComponent<HudView>();
            v.Build((RectTransform)go.transform);
            return v;
        }

        // ------------------------------------------------------------------------------------------------------ build
        void Build(RectTransform root)
        {
            BuildHeroCard(root);
            BuildSidekickCard(root);
            BuildSkillBar(root);
            BuildPromptAndChannel(root);
            BuildBossBar(root);
        }

        static RectTransform At(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size) =>
            UIKit.Place(UIKit.Rect(parent, name, anchor, anchor, size, pos), anchor, pos, size);

        void BuildHeroCard(RectTransform root)
        {
            var card = At(root, "HeroCard", new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(560f, 170f));
            UIKit.Card(card, UIKit.HeroBlue);
            // Rule chip: a ring with his current rule's icon (the one over his head).
            _ruleChip = At(card, "RuleChip", new Vector2(0f, 1f), new Vector2(16f, -18f), new Vector2(74f, 74f));
            UIKit.Image(_ruleChip, "Back", UIKit.Pip, new Color(0.03f, 0.05f, 0.1f, 0.95f), false);
            UIKit.Image(_ruleChip, "Ring", UIKit.UISprite("ring"), UIKit.HeroBlue, false);
            _ruleIcon = UIKit.SpriteImage(_ruleChip, "Icon", null, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
            _ruleIcon.enabled = false;
            _ruleIconRt = _ruleIcon.rectTransform;
            _ruleIconRt.pivot = new Vector2(0.5f, 0.5f);
            _heroName = UIKit.Text(card, "Name", "SIR CALLUM", UIKit.Sans, 26, Color.white);
            UIKit.Place(_heroName.rectTransform, new Vector2(0f, 1f), new Vector2(104f, -12f), new Vector2(300f, 32f));
            _heroName.fontStyle = FontStyles.Bold;
            UIKit.Outline(_heroName);
            _insight = UIKit.Text(card, "Insight", "", UIKit.Mono, 18, UIKit.SystemCyan);
            UIKit.Place(_insight.rectTransform, new Vector2(0f, 1f), new Vector2(104f, -44f), new Vector2(440f, 24f));
            _insight.textWrappingMode = TextWrappingModes.NoWrap;
            _insight.overflowMode = TextOverflowModes.Ellipsis;
            _heroHp = new UIBar(card, "HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(436f, 20f), new Vector2(104f, -72f), UIKit.HpRed, true);
            _heroHp.AddTicks(4);
            // Honor: icon, bar with a line where "low" begins, label.
            _honorRow = At(card, "HonorRow", new Vector2(0f, 1f), new Vector2(104f, -100f), new Vector2(436f, 22f));
            UIKit.SpriteImage(_honorRow, "Icon", UIKit.Icon("honor"), UIKit.Gold, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(22f, 22f)).rectTransform.pivot = new Vector2(0f, 0.5f);
            _honor = new UIBar(_honorRow, "Honor", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(300f, 10f), new Vector2(30f, 0f), UIKit.Gold, false);
            var tuning = Tuning.LoadDefault();
            float low = tuning != null && tuning.callum != null && tuning.callum.honorMax > 0f ? tuning.callum.honorLow / tuning.callum.honorMax : 0.4f;
            _honor.AddMarker(low, new Color(1f, 0.34f, 0.29f, 0.95f), 3f, 4f);
            _honorLabel = UIKit.Text(_honorRow, "Label", "HONOR", UIKit.Mono, 15, UIKit.Gold);
            UIKit.Place(_honorLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(340f, 0f), new Vector2(110f, 20f));
            _honorLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            UIKit.Outline(_honorLabel, 0.2f);
            _woundRow = At(card, "Wounds", new Vector2(0f, 1f), new Vector2(104f, -128f), new Vector2(440f, 26f));
        }

        void BuildSidekickCard(RectTransform root)
        {
            var card = At(root, "SidekickCard", new Vector2(0f, 0f), new Vector2(24f, 20f), new Vector2(520f, 150f));
            UIKit.Card(card, UIKit.Ochre);
            _skName = UIKit.Text(card, "Name", string.Format(SkLabel, "HERO"), UIKit.Sans, 24, Color.white);
            UIKit.Place(_skName.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(380f, 30f));
            _skName.fontStyle = FontStyles.Bold;
            _skName.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Outline(_skName);
            var lv = At(card, "Level", new Vector2(1f, 1f), new Vector2(-18f, -14f), new Vector2(78f, 26f));
            var lvBg = UIKit.Image(lv, "Bg", UIKit.Panel, new Color(0.95f, 0.76f, 0.31f, 0.18f));
            lvBg.pixelsPerUnitMultiplier = 2.4f;
            _level = UIKit.Text(lv, "Text", "LV 1", UIKit.Mono, 18, UIKit.Gold, TextAlignmentOptions.Center);
            _skHp = new UIBar(card, "HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(480f, 16f), new Vector2(20f, -50f), UIKit.Ochre, true);
            _skHp.AddTicks(4, 0.4f);
            _xpRow = At(card, "XpRow", new Vector2(0f, 1f), new Vector2(20f, -76f), new Vector2(480f, 14f));
            _xpLabel = UIKit.Text(_xpRow, "Label", "XP", UIKit.Mono, 13, UIKit.Dim);
            UIKit.Place(_xpLabel.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(28f, 14f));
            _xpLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            _xp = new UIBar(_xpRow, "Xp", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(286f, 6f), new Vector2(30f, 0f), UIKit.SystemCyan, false);
            _levelUp = UIKit.Text(_xpRow, "LevelUp", "LEVEL UP AT CAMP", UIKit.Mono, 13, UIKit.Gold);
            UIKit.Place(_levelUp.rectTransform, new Vector2(0f, 0.5f), new Vector2(326f, 0f), new Vector2(160f, 14f));
            _levelUp.rectTransform.pivot = new Vector2(0f, 0.5f);
            _levelUp.gameObject.SetActive(false);
            // Dodge charges: each pip refills as it recharges.
            _pipsRow = At(card, "Dodge", new Vector2(0f, 1f), new Vector2(20f, -98f), new Vector2(76f, 26f));
            _pips = new Pip[3];
            for (int i = 0; i < 3; i++)
            {
                var back = UIKit.SpriteImage(_pipsRow, "Pip" + i, UIKit.UISprite("ring"), new Color(1f, 1f, 1f, 0.3f), new Vector2(0f, 0.5f), new Vector2(i * 26f, 0f), new Vector2(20f, 20f));
                back.rectTransform.pivot = new Vector2(0f, 0.5f);
                var fill = UIKit.Image(back.rectTransform, "Fill", UIKit.Pip, UIKit.SystemCyan, false);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.rectTransform.offsetMin = new Vector2(3f, 3f);
                fill.rectTransform.offsetMax = new Vector2(-3f, -3f);
                _pips[i] = new Pip { Back = back, Fill = fill };
            }
            _statusRow = At(card, "Status", new Vector2(0f, 1f), new Vector2(104f, -98f), new Vector2(400f, 26f));
            _sneakChip = UIKit.Chip(_statusRow, "Sneak", "SNEAKING", UIKit.SystemCyan, 22f);
            _reachChip = UIKit.Chip(_statusRow, "Reach", "OUT OF REACH", UIKit.Danger, 22f);
            _rationsChip = UIKit.Chip(_statusRow, "Rations", "RATIONS 0", UIKit.Gold, 22f);
            _rationsChip.transform.parent.gameObject.SetActive(false);
            _downedChip = UIKit.Chip(_statusRow, "Downed", "DOWNED", UIKit.Danger, 22f);
            _downedChip.transform.parent.gameObject.SetActive(false);
            _recallChip = UIKit.Chip(_statusRow, "Recall", "RECALL ×2", UIKit.SystemCyan, 22f);
            _recallChip.transform.parent.gameObject.SetActive(false);
            _sneakChip.transform.parent.gameObject.SetActive(false);
            _reachChip.transform.parent.gameObject.SetActive(false);
        }

        const float SlotSize = 84f, SlotGap = 10f, VerbSize = 46f, VerbGap = 8f, BarGap = 30f;

        void BuildSkillBar(RectTransform root)
        {
            float barW = 4 * SlotSize + 3 * SlotGap;
            _skillsRt = At(root, "Skills", new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(barW, SlotSize + 24f));
            _skillsRt.pivot = new Vector2(0.5f, 0f);
            _skillsRt.anchoredPosition = new Vector2(0f, 20f);
            _slots = new Slot[MaxSlots];
            for (int i = 0; i < MaxSlots; i++) _slots[i] = BuildSlot(_skillsRt, i);
            // Verbs (left of the bar): the knife, the roll, the ping, the crouch.
            var verbs = new[] { ("verb_knife", "attack", "Knife"), ("verb_dodge", "dodge", "Dodge"), ("verb_ping", "ping", "Ping"), ("verb_crouch", "crouch", "Crouch") };
            float verbsW = verbs.Length * VerbSize + (verbs.Length - 1) * VerbGap;
            var vr = At(root, "Verbs", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(verbsW, VerbSize + 24f));
            vr.pivot = new Vector2(1f, 0f);
            vr.anchoredPosition = new Vector2(-barW * 0.5f - BarGap, 20f);
            _verbsRt = vr;
            _verbs = new Verb[verbs.Length];
            for (int i = 0; i < verbs.Length; i++)
            {
                var (icon, token, label) = verbs[i];
                var tile = At(vr, "Verb_" + token, new Vector2(0f, 0f), new Vector2(i * (VerbSize + VerbGap), 24f), new Vector2(VerbSize, VerbSize));
                UIKit.Image(tile, "Bg", UIKit.Panel, new Color(0.04f, 0.06f, 0.1f, 0.62f));
                UIKit.Image(tile, "Frame", UIKit.UISprite("slot"), new Color(1f, 1f, 1f, 0.22f));
                UIKit.SpriteImage(tile, "Icon", UIKit.Icon(icon), new Color(1f, 1f, 1f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
                var key = UIKit.Keycap(tile, "Key", "?", 20f);
                key.anchorMin = key.anchorMax = new Vector2(0f, 1f);
                key.anchoredPosition = new Vector2(6f, -2f);
                var name = UIKit.Text(vr, "Name_" + token, label, UIKit.Sans, 13, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Center);
                UIKit.Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(i * (VerbSize + VerbGap) - 4f, 2f), new Vector2(VerbSize + 8f, 16f));
                UIKit.Outline(name, 0.2f);
                _verbs[i] = new Verb { Token = token, Key = key };
            }
            // Passives (right of the bar), built when known.
            _passivesRt = At(root, "Passives", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(160f, VerbSize + 24f));
            _passivesRt.pivot = new Vector2(0f, 0f);
            _passivesRt.anchoredPosition = new Vector2(barW * 0.5f + BarGap, 20f);
            ShowSlots(4);
        }

        // Chapter 1 has 4 slots, chapter 2 five, chapter 3 on six (GDD §4.1). The bar widens; verbs and passives move out.
        const int MaxSlots = 6;
        int _shownSlots = -1;
        RectTransform _verbsRt;
        public int VisibleSlots => _shownSlots;

        void ShowSlots(int n)
        {
            n = Mathf.Clamp(n, 1, MaxSlots);
            if (n == _shownSlots) return;
            _shownSlots = n;
            float barW = n * SlotSize + (n - 1) * SlotGap;
            _skillsRt.sizeDelta = new Vector2(barW, _skillsRt.sizeDelta.y);
            for (int i = 0; i < _slots.Length; i++) _slots[i].Root.gameObject.SetActive(i < n);
            if (_verbsRt != null) _verbsRt.anchoredPosition = new Vector2(-barW * 0.5f - BarGap, 20f);
            if (_passivesRt != null) _passivesRt.anchoredPosition = new Vector2(barW * 0.5f + BarGap, 20f);
        }

        Slot BuildSlot(RectTransform bar, int i)
        {
            var s = new Slot { Root = At(bar, "Slot" + i, new Vector2(0f, 0f), new Vector2(i * (SlotSize + SlotGap), 22f), new Vector2(SlotSize, SlotSize)) };
            s.Bg = UIKit.Image(s.Root, "Bg", UIKit.Panel, new Color(0.04f, 0.06f, 0.1f, 0.84f));
            s.Under = UIKit.Image(s.Root, "Family", UIKit.BarSprite, Color.clear);
            s.Under.rectTransform.anchorMin = new Vector2(0f, 0f);
            s.Under.rectTransform.anchorMax = new Vector2(1f, 0f);
            s.Under.rectTransform.offsetMin = new Vector2(10f, 6f);
            s.Under.rectTransform.offsetMax = new Vector2(-10f, 10f);
            s.Icon = UIKit.SpriteImage(s.Root, "Icon", null, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(56f, 56f));
            s.Icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            s.Icon.enabled = false; // an Image without a sprite draws a white square
            s.Cool = UIKit.Image(s.Root, "Cool", UIKit.Panel, new Color(0f, 0f, 0f, 0.66f), false);
            s.Cool.type = Image.Type.Filled;
            s.Cool.fillMethod = Image.FillMethod.Radial360;
            s.Cool.fillOrigin = (int)Image.Origin360.Top;
            s.Cool.fillClockwise = false;
            s.Cool.rectTransform.offsetMin = new Vector2(4f, 4f);
            s.Cool.rectTransform.offsetMax = new Vector2(-4f, -4f);
            s.Secs = UIKit.Text(s.Root, "Secs", "", UIKit.Mono, 30, Color.white, TextAlignmentOptions.Center);
            UIKit.Outline(s.Secs);
            s.Flash = UIKit.Image(s.Root, "Flash", UIKit.Panel, new Color(1f, 1f, 1f, 0f));
            s.Frame = UIKit.Image(s.Root, "Frame", UIKit.UISprite("slot"), new Color(1f, 1f, 1f, 0.3f));
            s.Ranks = new Image[2];
            for (int r = 0; r < 2; r++)
            {
                s.Ranks[r] = UIKit.SpriteImage(s.Root, "Rank" + r, UIKit.UISprite("diamond"), UIKit.Gold, new Vector2(1f, 1f), new Vector2(-8f - r * 12f, -8f), new Vector2(11f, 11f));
                s.Ranks[r].rectTransform.pivot = new Vector2(1f, 1f);
            }
            s.Key = UIKit.Keycap(s.Root, "Key", (i + 1).ToString(), 24f);
            s.Key.anchorMin = s.Key.anchorMax = new Vector2(0f, 1f);
            s.Key.anchoredPosition = new Vector2(10f, -4f);
            s.Name = UIKit.Text(bar, "Name" + i, "", UIKit.Sans, 15, Color.white, TextAlignmentOptions.Center);
            UIKit.Place(s.Name.rectTransform, new Vector2(0f, 0f), new Vector2(i * (SlotSize + SlotGap) - 6f, 0f), new Vector2(SlotSize + 12f, 20f));
            s.Name.textWrappingMode = TextWrappingModes.NoWrap;
            s.Name.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Outline(s.Name, 0.25f);
            return s;
        }

        void BuildPromptAndChannel(RectTransform root)
        {
            _promptRoot = At(root, "Prompt", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(600f, 34f));
            _promptRoot.pivot = new Vector2(0.5f, 0f);
            _promptRoot.anchoredPosition = new Vector2(0f, 150f);
            _promptKey = UIKit.Keycap(_promptRoot, "Key", "E", 30f);
            _promptKey.anchorMin = _promptKey.anchorMax = new Vector2(0f, 0.5f);
            _promptKey.pivot = new Vector2(0f, 0.5f);
            _promptText = UIKit.Text(_promptRoot, "Text", "", UIKit.Sans, 24, Color.white, TextAlignmentOptions.Left);
            _promptText.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Outline(_promptText);
            _promptRoot.gameObject.SetActive(false);
            _channelRoot = At(root, "Channel", new Vector2(0.5f, 0f), new Vector2(0f, 196f), new Vector2(340f, 52f));
            _channelRoot.pivot = new Vector2(0.5f, 0f);
            _channelRoot.anchoredPosition = new Vector2(0f, 196f);
            _channelLabel = UIKit.Text(_channelRoot, "Label", "", UIKit.Sans, 20, Color.white, TextAlignmentOptions.Center);
            _channelLabel.rectTransform.offsetMin = new Vector2(0f, 26f);
            UIKit.Outline(_channelLabel);
            _channel = new UIBar(_channelRoot, "Bar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(320f, 10f), new Vector2(0f, 14f), UIKit.SystemCyan, false);
            _channelHint = UIKit.Text(_channelRoot, "Hint", "move to cancel", UIKit.Mono, 13, UIKit.Dim, TextAlignmentOptions.Center);
            _channelHint.rectTransform.offsetMax = new Vector2(0f, -40f);
            UIKit.Outline(_channelHint, 0.2f);
            _channelRoot.gameObject.SetActive(false);
        }

        void BuildBossBar(RectTransform root)
        {
            _bossRoot = At(root, "Boss", new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(780f, 70f));
            _bossRoot.pivot = new Vector2(0.5f, 1f);
            _bossRoot.anchoredPosition = new Vector2(0f, -22f);
            var plate = At(_bossRoot, "Plate", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 34f));
            plate.pivot = new Vector2(0.5f, 1f);
            plate.anchoredPosition = Vector2.zero;
            UIKit.Card(plate, UIKit.Violet, 0.9f);
            _bossName = UIKit.Text(plate, "Name", "", UIKit.Mono, 24, UIKit.Violet, TextAlignmentOptions.Center);
            UIKit.Outline(_bossName, 0.2f);
            _bossHp = new UIBar(_bossRoot, "HP", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(760f, 18f), new Vector2(0f, 4f), UIKit.Violet, false);
            _bossHp.AddTicks(10, 0.45f);
            _bossRoot.gameObject.SetActive(false);
        }

        // --------------------------------------------------------------------------------------------- public API
        /// <summary>Whoever has the big bar at the top of the screen (null = none).</summary>
        public Agent Boss => _boss;

        public void ShowBoss(Agent boss, string displayName)
        {
            _boss = boss;
            _bossName.text = displayName;
            _bossRoot.gameObject.SetActive(boss != null);
        }

        readonly StringBuilder _sb = new StringBuilder();

        public string SidekickLabel => _skName.text;

        /// <summary>The HUD element a tutorial lesson points at.</summary>
        public RectTransform CoachAnchor(CoachTarget t) => t switch
        {
            CoachTarget.RuleChip => _ruleChip,
            CoachTarget.HonorBar => _honorRow,
            CoachTarget.SkillBar => _skillsRt,
            CoachTarget.DodgePips => _pipsRow,
            CoachTarget.WoundChips => _woundRow,
            CoachTarget.XpBar => _xpRow,
            _ => null,
        };

        /// <summary>The icon shown in a skill slot (its sprite name), or null when the slot is empty.</summary>
        public string SlotIcon(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot].Icon.sprite != null ? _slots[slot].Icon.sprite.name : null;

        public string LevelText => _level.text;
        public float XpFill => _xp.Value;
        public bool LevelBanked { get; private set; }

        /// <summary>
        /// GDD §8: the opening names you HERO's SIDEKICK; once you've met him, "HERO" quietly becomes his name (a brief
        /// glyph flicker, no sound, no announcement).
        /// </summary>
        public void MeetHero(string name, bool instant = false)
        {
            _metName = name;
            _swapT = instant ? -1f : 0f;
            if (instant) _skName.text = string.Format(SkLabel, name);
        }

        public void TickSwap(float dt)
        {
            if (_swapT < 0f) return;
            _swapT += dt;
            if (_swapT >= SwapTime)
            {
                _skName.text = string.Format(SkLabel, _metName);
                _swapT = -1f;
                return;
            }
            int shown = Mathf.FloorToInt(_metName.Length * _swapT / SwapTime);
            int seed = Mathf.FloorToInt(_swapT * 24f);
            _sb.Clear().Append("YOU  <size=70%><color=#FF564A>").Append(_metName, 0, shown);
            for (int k = shown; k < _metName.Length; k++) _sb.Append(SwapGlyphs[(seed + k * 5) % SwapGlyphs.Length]);
            _skName.text = _sb.Append("</color><color=#E4A84E>'s SIDEKICK</color></size>").ToString();
        }

        // ----------------------------------------------------------------------------------------------- per frame
        void Update()
        {
            var input = GameInput.Instance;
            if (input.Insight.enabled && input.Insight.WasPressedThisFrame()) InsightOn = !InsightOn;
            TickSwap(Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            RefreshKeys();
            var ctx = RunContext.Current;
            if (ctx == null) return;
            UpdateHero(ctx.Hero as HeroAgent, dt);
            UpdateSidekick(ctx, ctx.Sidekick as SidekickAgent, dt);
            if (_boss != null)
            {
                _bossHp.Set(_boss.Health.Fraction);
                _bossHp.Tick(dt);
                if (!_boss.IsAlive) _bossRoot.gameObject.SetActive(false);
            }
        }

        void RefreshKeys()
        {
            var d = KeyGlyphs.Current;
            if (d == _device) return;
            _device = d;
            for (int i = 0; i < _slots.Length; i++) UIKit.SetKey(_slots[i].Key, KeyGlyphs.Label(KeyGlyphs.SlotToken(i), d));
            foreach (var v in _verbs) UIKit.SetKey(v.Key, KeyGlyphs.Label(v.Token, d));
            UIKit.SetKey(_promptKey, KeyGlyphs.Label("interact", d));
            LayoutPrompt();
        }

        void UpdateHero(HeroAgent hero, float dt)
        {
            if (hero == null) return;
            _heroHp.Set(hero.Health.Fraction, $"{Mathf.CeilToInt(hero.Health.Current)} / {Mathf.RoundToInt(hero.Health.Max)}");
            if (hero.Module is CallumModule cm)
            {
                _honor.Set(cm.Honor / cm.T.honorMax);
                bool low = cm.HonorLow;
                _honor.FillColor = low ? Color.Lerp(UIKit.Danger, UIKit.Gold, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : UIKit.Gold;
                _honorLabel.text = low ? "HONOR  <color=#FF564A>LOW</color>" : "HONOR";
            }
            // The rule chip mirrors the icon over his head; it pops when the rule changes.
            string rule = hero.IsAlive ? hero.ActiveIcon : "";
            if (rule != _rule)
            {
                _rule = rule;
                _ruleIcon.sprite = UIKit.Icon(rule);
                _ruleIcon.enabled = _ruleIcon.sprite != null;
                _rulePop = 1f;
            }
            _rulePop = Mathf.MoveTowards(_rulePop, 0f, dt * 3f);
            _ruleIconRt.localScale = Vector3.one * (1f + 0.3f * _rulePop);
            _insight.text = InsightOn && hero.IsAlive ? "» " + hero.ActiveLabel : "";
            UpdateWounds(hero);
            _heroHp.Tick(dt);
            _honor.Tick(dt);
        }

        void UpdateWounds(HeroAgent hero)
        {
            _sb.Clear();
            if (hero.Crippled) _sb.Append("C|");
            if (hero.Module is CallumModule fm && fm.FinisherCharging) _sb.Append("J|");
            if (hero.Hunger.Starving) _sb.Append("S|");
            else if (hero.Hunger.Hungry) _sb.Append("H|");
            foreach (var w in hero.Wounds.All) _sb.Append((int)w).Append('|');
            var sig = _sb.ToString();
            if (sig == _woundSig) return;
            _woundSig = sig;
            foreach (var c in _woundChips) Destroy(c);
            _woundChips.Clear();
            float x = 0f;
            void Add(string icon, string text)
            {
                var chip = UIKit.Chip(_woundRow, "Wound", text, UIKit.Danger, 24f);
                var rt = (RectTransform)chip.transform.parent;
                if (icon != null)
                {
                    var img = UIKit.SpriteImage(rt, "Icon", UIKit.Icon(icon), UIKit.Danger, new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(18f, 18f));
                    img.rectTransform.pivot = new Vector2(0f, 0.5f);
                    chip.rectTransform.offsetMin = new Vector2(20f, 0f);
                    rt.sizeDelta += new Vector2(20f, 0f);
                }
                rt.anchoredPosition = new Vector2(x, 0f);
                x += rt.sizeDelta.x + 6f;
                _woundChips.Add(rt.gameObject);
            }
            if (hero.Module is CallumModule jm && jm.FinisherCharging) Add("judgment", "JUDGMENT");
            if (hero.Crippled) Add(null, "CRIPPLED");
            if (hero.Hunger.Starving) Add(null, "STARVING");
            else if (hero.Hunger.Hungry) Add(null, "HUNGRY");
            foreach (var w in hero.Wounds.All) Add(WoundIcon(w), WoundName(w));
        }

        void UpdateSidekick(RunContext ctx, SidekickAgent sk, float dt)
        {
            if (sk == null) return;
            _skHp.Set(sk.Health.Fraction, $"{Mathf.CeilToInt(sk.Health.Current)} / {Mathf.RoundToInt(sk.Health.Max)}");
            _skHp.Tick(dt);
            UpdateLevel(ctx.Get<XpTracker>(), sk, dt);
            for (int i = 0; i < _pips.Length; i++)
            {
                var d = sk.Dodge;
                float f = d == null ? 0f : i < d.Charges ? 1f : i == d.Charges ? d.RechargeProgress : 0f;
                _pips[i].Fill.fillAmount = f;
                _pips[i].Fill.color = f >= 1f ? UIKit.SystemCyan : new Color(0.48f, 0.9f, 1f, 0.45f);
            }
            bool sneaking = sk.IsSneaking, crouched = sk.Crouched;
            SetChip(_sneakChip, sneaking || crouched, sneaking ? "SNEAKING" : "CROUCHED", sneaking ? UIKit.SystemCyan : UIKit.Dim);
            SetChip(_reachChip, sk.IsAlive && !sk.InSupportRange, "OUT OF REACH", UIKit.Danger);
            var heroAgent = ctx.Hero as HeroAgent;
            bool showRations = sk.Rations.Count > 0 || (heroAgent != null && heroAgent.Hunger.Enabled);
            SetChip(_rationsChip, showRations, "RATIONS " + sk.Rations.Count, sk.Rations.Count > 0 ? UIKit.Gold : UIKit.Dim);
            SetChip(_downedChip, sk.IsDowned, $"DOWNED {Mathf.CeilToInt(sk.DownedRemaining)} s", UIKit.Danger);
            var recall = ctx.Get<RecallState>();
            SetChip(_recallChip, recall != null && recall.Learned, "RECALL ×" + (recall != null ? recall.UsesLeft : 0), recall != null && recall.UsesLeft > 0 ? UIKit.SystemCyan : UIKit.Dim);
            LayoutStatus();
            var skills = sk.GetComponent<SidekickSkills>();
            ShowSlots(skills != null ? skills.System.SlotCount : 4);
            for (int i = 0; i < _slots.Length; i++) UpdateSlot(_slots[i], skills != null ? skills.System.InSlot(i) : null, dt);
            UpdatePassives(skills, sk);
            if (sk.IsChanneling)
            {
                _channelRoot.gameObject.SetActive(true);
                _channelLabel.text = sk.ChannelLabel;
                _channel.Set(sk.ChannelProgress);
                _channel.Tick(10f);
                SetPrompt(null);
            }
            else
            {
                _channelRoot.gameObject.SetActive(false);
                var near = Interactables.Nearest(sk, ctx.Tuning.sidekick.interactRange);
                SetPrompt(near?.Prompt);
            }
        }

        void UpdateLevel(XpTracker xp, SidekickAgent sk, float dt)
        {
            string lv = "LV " + sk.Level;
            if (_level.text != lv) _level.text = lv;
            _xpRow.gameObject.SetActive(xp != null);
            if (xp == null) return;
            var t = XpTracker.Thresholds;
            int level = sk.Level;
            LevelBanked = XpTracker.LevelFor(xp.Xp) > level;
            float fill;
            if (LevelBanked || level - 1 >= t.Length) fill = 1f;
            else
            {
                float prev = level >= 2 ? t[level - 2] : 0f, next = t[level - 1];
                fill = Mathf.Clamp01((xp.Xp - prev) / Mathf.Max(1f, next - prev));
            }
            _xp.Set(fill);
            _xp.FillColor = LevelBanked ? Color.Lerp(UIKit.Gold, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f)) : UIKit.SystemCyan;
            _levelUp.gameObject.SetActive(LevelBanked);
            _xp.Tick(dt);
        }

        static void SetChip(TextMeshProUGUI chip, bool on, string text, Color color)
        {
            var go = chip.transform.parent.gameObject;
            if (go.activeSelf != on) go.SetActive(on);
            if (!on) return;
            if (chip.text != text)
            {
                chip.text = text;
                chip.color = color;
                var bg = go.GetComponent<Image>();
                bg.color = new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 0.88f);
                UIKit.FitChip(chip);
            }
        }

        void LayoutStatus()
        {
            float x = 0f;
            foreach (var chip in new[] { _downedChip, _sneakChip, _reachChip, _rationsChip, _recallChip })
            {
                var rt = (RectTransform)chip.transform.parent;
                if (!rt.gameObject.activeSelf) continue;
                rt.anchoredPosition = new Vector2(x, 0f);
                x += rt.sizeDelta.x + 6f;
            }
        }

        string _promptShown;

        void SetPrompt(string text)
        {
            if (text == _promptShown) return;
            _promptShown = text;
            _promptRoot.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (string.IsNullOrEmpty(text)) return;
            _promptText.text = text;
            LayoutPrompt();
        }

        void LayoutPrompt()
        {
            if (_promptText == null) return;
            float keyW = _promptKey.sizeDelta.x, textW = _promptText.GetPreferredValues(_promptText.text, 999f, 34f).x;
            float total = keyW + 12f + textW;
            _promptRoot.sizeDelta = new Vector2(total, 34f);
            _promptKey.anchoredPosition = Vector2.zero;
            var rt = _promptText.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(textW + 4f, 0f);
            rt.anchoredPosition = new Vector2(keyW + 12f, 0f);
        }

        void UpdateSlot(Slot s, SkillState st, float dt)
        {
            string id = st?.Id;
            if (id != s.Id)
            {
                s.Id = id;
                s.Icon.sprite = id != null ? UIKit.Icon(SkillGuides.IconId(id)) : null;
                s.Icon.enabled = s.Icon.sprite != null;
                var fam = st != null ? UIKit.Family(st.Def.family) : Color.white;
                s.Under.color = st != null ? new Color(fam.r, fam.g, fam.b, 0.9f) : Color.clear;
                s.Frame.color = st != null ? new Color(fam.r, fam.g, fam.b, 0.75f) : new Color(1f, 1f, 1f, 0.22f);
                s.Bg.color = st != null ? new Color(0.04f, 0.06f, 0.1f, 0.84f) : new Color(0.04f, 0.06f, 0.1f, 0.45f);
                s.Name.text = st != null ? st.Def.displayName : "";
                s.Cooling = false;
            }
            for (int r = 0; r < s.Ranks.Length; r++)
                s.Ranks[r].color = st == null ? Color.clear : r < st.Rank ? UIKit.Gold : new Color(1f, 1f, 1f, 0.2f);
            if (st == null)
            {
                s.Cool.fillAmount = 0f;
                s.Secs.text = "";
                return;
            }
            bool cooling = st.CooldownRemaining > 0.05f;
            if (s.Cooling && !cooling) s.FlashT = 1f; // ready again
            s.Cooling = cooling;
            s.Cool.fillAmount = st.CooldownFraction;
            s.Secs.text = cooling ? Mathf.CeilToInt(st.CooldownRemaining).ToString() : "";
            s.Icon.color = cooling ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            s.Name.color = cooling ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            s.FlashT = Mathf.MoveTowards(s.FlashT, 0f, dt * 2.8f);
            s.Flash.color = new Color(1f, 1f, 1f, 0.55f * s.FlashT);
            s.Root.localScale = Vector3.one * (1f + 0.08f * s.FlashT);
        }

        void UpdatePassives(SidekickSkills skills, SidekickAgent sk)
        {
            _sb.Clear();
            if (skills != null)
                foreach (var st in skills.System.Known.Values)
                    if (!st.Def.UsesSlot) _sb.Append(st.Id).Append(st.Rank).Append('|');
            var sig = _sb.ToString();
            if (sig != _passiveSig)
            {
                _passiveSig = sig;
                for (int i = _passivesRt.childCount - 1; i >= 0; i--) Destroy(_passivesRt.GetChild(i).gameObject);
                _passives.Clear();
                int k = 0;
                if (skills != null)
                    foreach (var st in skills.System.Known.Values)
                    {
                        if (st.Def.UsesSlot) continue;
                        var tile = At(_passivesRt, "Passive_" + st.Id, new Vector2(0f, 0f), new Vector2(k * (VerbSize + VerbGap), 24f), new Vector2(VerbSize, VerbSize));
                        UIKit.Image(tile, "Bg", UIKit.Panel, new Color(0.04f, 0.06f, 0.1f, 0.62f));
                        var frame = UIKit.Image(tile, "Frame", UIKit.UISprite("slot"), new Color(1f, 1f, 1f, 0.3f));
                        UIKit.SpriteImage(tile, "Icon", UIKit.Icon(SkillGuides.IconId(st.Id)), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
                        var name = UIKit.Text(_passivesRt, "Name_" + st.Id, st.Def.displayName, UIKit.Sans, 13, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Center);
                        UIKit.Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(k * (VerbSize + VerbGap) - 10f, 2f), new Vector2(VerbSize + 20f, 16f));
                        name.textWrappingMode = TextWrappingModes.NoWrap;
                        UIKit.Outline(name, 0.2f);
                        _passives.Add((st.Id, frame));
                        k++;
                    }
            }
            foreach (var (id, frame) in _passives)
            {
                bool lit = id == "quiet_feet" && sk.IsSneaking;
                frame.color = lit ? UIKit.SystemCyan : new Color(1f, 1f, 1f, 0.3f);
            }
        }

        public static string WoundIcon(WoundType w) => w switch
        {
            WoundType.SprainedAnkle => "wound_ankle",
            WoundType.CrackedRibs => "wound_ribs",
            WoundType.SwordArmStrain => "wound_arm",
            WoundType.Fever => "wound_fever",
            _ => "wound_concussion",
        };

        public static string WoundName(WoundType w)
        {
            switch (w)
            {
                case WoundType.SprainedAnkle: return "Sprained ankle";
                case WoundType.CrackedRibs: return "Cracked ribs";
                case WoundType.SwordArmStrain: return "Sword-arm strain";
                case WoundType.Fever: return "Fever";
                default: return "Concussion";
            }
        }
    }
}
