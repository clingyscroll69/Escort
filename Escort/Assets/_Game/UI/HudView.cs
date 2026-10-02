using System.Text;
using HS.Core;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using HS.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// Gameplay HUD: hero (HP, Honor, wounds; Hero Insight shows his current rule in words), sidekick (HP, dodge charges,
    /// four skill slots with cooldowns, interact prompt, channel progress), and a boss bar. Rapport is never shown.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        UIBar _heroHp, _honor, _skHp, _bossHp, _channel;
        TextMeshProUGUI _heroName, _wounds, _insight, _honorLabel, _prompt, _channelLabel, _bossName;
        Image[] _pips;
        RectTransform _bossRoot, _channelRoot;
        Slot[] _slots;
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
            public RectTransform Root;
            public Image Bg, Cool;
            public TextMeshProUGUI Name, Key, Secs;
        }

        public static HudView Create(UIRoot root)
        {
            var go = UIKit.Stretch(root.Hud, "HudView").gameObject;
            var v = go.AddComponent<HudView>();
            v.Build((RectTransform)go.transform);
            return v;
        }

        void Build(RectTransform root)
        {
            // Hero panel (top-left)
            var heroBack = UIKit.Rect(root, "HeroBack", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, 190f), new Vector2(0f, 0f));
            UIKit.Image(heroBack, "Shade", UIKit.Glow, new Color(0f, 0f, 0f, 0.42f), false);
            var hero = UIKit.Rect(root, "Hero", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(420f, 150f), new Vector2(28f, -24f));
            _heroName = UIKit.Text(hero, "Name", "SIR CALLUM", UIKit.Sans, 28, Color.white);
            _heroName.rectTransform.offsetMax = new Vector2(0f, 0f);
            _heroName.rectTransform.offsetMin = new Vector2(0f, 112f);
            _heroName.fontStyle = FontStyles.Bold;
            UIKit.Outline(_heroName);
            _heroHp = new UIBar(hero, "HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(400f, 22f), new Vector2(0f, -40f), UIKit.HpRed, true);
            _honor = new UIBar(hero, "Honor", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, 9f), new Vector2(0f, -70f), UIKit.Gold, false);
            _honorLabel = UIKit.Text(hero, "HonorLabel", "HONOR", UIKit.Mono, 16, UIKit.Gold);
            Place(_honorLabel.rectTransform, new Vector2(308f, -64f), new Vector2(110f, 22f));
            UIKit.Outline(_honorLabel, 0.2f);
            _wounds = UIKit.Text(hero, "Wounds", "", UIKit.Sans, 19, UIKit.Danger);
            Place(_wounds.rectTransform, new Vector2(0f, -86f), new Vector2(420f, 26f));
            UIKit.Outline(_wounds, 0.25f);
            _insight = UIKit.Text(hero, "Insight", "", UIKit.Mono, 19, UIKit.SystemCyan);
            Place(_insight.rectTransform, new Vector2(0f, -114f), new Vector2(520f, 26f));
            UIKit.Outline(_insight, 0.25f);

            // Sidekick panel (bottom-left)
            var skBack = UIKit.Rect(root, "SidekickBack", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(470f, 150f), new Vector2(0f, 0f));
            UIKit.Image(skBack, "Shade", UIKit.Glow, new Color(0f, 0f, 0f, 0.42f), false);
            var sk = UIKit.Rect(root, "Sidekick", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(360f, 80f), new Vector2(28f, 28f));
            var skName = _skName = UIKit.Text(sk, "Name", string.Format(SkLabel, "HERO"), UIKit.Sans, 24, Color.white);
            Place(skName.rectTransform, new Vector2(0f, -4f), new Vector2(420f, 30f));
            skName.fontStyle = FontStyles.Bold;
            UIKit.Outline(skName);
            _skHp = new UIBar(sk, "HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, 16f), new Vector2(0f, -36f), UIKit.Ochre, true);
            _pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pr = UIKit.Rect(sk, "Dodge" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(i * 22f, -60f));
                _pips[i] = pr.gameObject.AddComponent<Image>();
                _pips[i].sprite = UIKit.Pip;
                _pips[i].raycastTarget = false;
            }

            // Skill bar (bottom-centre)
            var bar = UIKit.Rect(root, "Skills", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(4 * 96f, 92f), new Vector2(0f, 24f));
            _slots = new Slot[4];
            for (int i = 0; i < 4; i++)
            {
                var s = new Slot { Root = UIKit.Rect(bar, "Slot" + i, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(88f, 88f), new Vector2(i * 96f, 0f)) };
                s.Bg = UIKit.Image(s.Root, "Bg", UIKit.Panel, UIKit.PanelDark);
                UIKit.Image(s.Root, "Border", UIKit.Border, new Color(1f, 1f, 1f, 0.35f));
                s.Name = UIKit.Text(s.Root, "Name", "", UIKit.Sans, 16, Color.white, TextAlignmentOptions.Bottom);
                s.Name.rectTransform.offsetMin = new Vector2(5f, 6f);
                s.Name.rectTransform.offsetMax = new Vector2(-5f, -22f);
                s.Cool = UIKit.Image(s.Root, "Cool", UIKit.Panel, new Color(0f, 0f, 0f, 0.62f), false);
                s.Cool.type = Image.Type.Filled;
                s.Cool.fillMethod = Image.FillMethod.Radial360;
                s.Cool.fillOrigin = (int)Image.Origin360.Top;
                s.Cool.fillClockwise = false;
                s.Secs = UIKit.Text(s.Root, "Secs", "", UIKit.Mono, 30, Color.white, TextAlignmentOptions.Top);
                s.Secs.rectTransform.offsetMax = new Vector2(0f, -14f);
                UIKit.Outline(s.Secs);
                s.Key = UIKit.Text(s.Root, "Key", (i + 1).ToString(), UIKit.Mono, 18, UIKit.Gold, TextAlignmentOptions.TopLeft);
                s.Key.rectTransform.offsetMin = new Vector2(7f, 0f);
                s.Key.rectTransform.offsetMax = new Vector2(0f, -4f);
                _slots[i] = s;
            }
            _prompt = UIKit.Text(root, "Prompt", "", UIKit.Sans, 24, Color.white, TextAlignmentOptions.Center);
            Place(_prompt.rectTransform, new Vector2(0f, 128f), new Vector2(700f, 32f), new Vector2(0.5f, 0f));
            UIKit.Outline(_prompt);
            _channelRoot = UIKit.Rect(root, "Channel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(320f, 40f), new Vector2(0f, 170f));
            _channelLabel = UIKit.Text(_channelRoot, "Label", "", UIKit.Sans, 20, Color.white, TextAlignmentOptions.Center);
            _channelLabel.rectTransform.offsetMin = new Vector2(0f, 16f);
            UIKit.Outline(_channelLabel);
            _channel = new UIBar(_channelRoot, "Bar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(320f, 10f), Vector2.zero, UIKit.SystemCyan, false);

            // Boss bar (top-centre)
            _bossRoot = UIKit.Rect(root, "Boss", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(760f, 70f), new Vector2(0f, -22f));
            _bossName = UIKit.Text(_bossRoot, "Name", "", UIKit.Mono, 26, UIKit.Violet, TextAlignmentOptions.Center);
            _bossName.rectTransform.offsetMin = new Vector2(0f, 30f);
            UIKit.Outline(_bossName);
            _bossHp = new UIBar(_bossRoot, "HP", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(760f, 20f), new Vector2(0f, 4f), UIKit.Violet, false);
            _bossRoot.gameObject.SetActive(false);
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0f, 1f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = a;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        public void ShowBoss(Agent boss, string displayName)
        {
            _boss = boss;
            _bossName.text = displayName;
            _bossRoot.gameObject.SetActive(boss != null);
        }

        readonly StringBuilder _sb = new StringBuilder();

        public string SidekickLabel => _skName.text;

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

        void Update()
        {
            var input = GameInput.Instance;
            if (input.Insight.enabled && input.Insight.WasPressedThisFrame()) InsightOn = !InsightOn;
            TickSwap(Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            var ctx = RunContext.Current;
            if (ctx == null) return;
            float dt = Time.unscaledDeltaTime;
            var hero = ctx.Hero as HeroAgent;
            if (hero != null)
            {
                _heroHp.Set(hero.Health.Fraction, $"{Mathf.CeilToInt(hero.Health.Current)} / {Mathf.RoundToInt(hero.Health.Max)}");
                if (hero.Module is CallumModule cm)
                {
                    _honor.Set(cm.Honor / cm.T.honorMax);
                    bool low = cm.HonorLow;
                    _honor.FillColor = low ? Color.Lerp(UIKit.Danger, UIKit.Gold, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : UIKit.Gold;
                    _honorLabel.text = low ? "HONOR  <color=#FF564A>LOW</color>" : "HONOR";
                }
                _sb.Clear();
                foreach (var w in hero.Wounds.All)
                {
                    if (_sb.Length > 0) _sb.Append("  ·  ");
                    _sb.Append(WoundName(w));
                }
                if (hero.Crippled) _sb.Insert(0, "<b>CRIPPLED</b>  ");
                _wounds.text = _sb.ToString();
                _insight.text = InsightOn && hero.IsAlive ? "» " + hero.ActiveLabel : "";
                _heroHp.Tick(dt);
                _honor.Tick(dt);
            }
            var sk = ctx.Sidekick as SidekickAgent;
            if (sk != null)
            {
                _skHp.Set(sk.Health.Fraction, $"{Mathf.CeilToInt(sk.Health.Current)} / {Mathf.RoundToInt(sk.Health.Max)}");
                _skHp.Tick(dt);
                for (int i = 0; i < _pips.Length; i++)
                    _pips[i].color = sk.Dodge != null && i < sk.Dodge.Charges ? UIKit.SystemCyan : new Color(1f, 1f, 1f, 0.18f);
                var skills = sk.GetComponent<SidekickSkills>();
                for (int i = 0; i < _slots.Length; i++) UpdateSlot(_slots[i], skills != null ? skills.System.InSlot(i) : null);
                if (sk.IsChanneling)
                {
                    _channelRoot.gameObject.SetActive(true);
                    _channelLabel.text = sk.ChannelLabel;
                    _channel.Set(sk.ChannelProgress);
                    _channel.Tick(10f);
                    _prompt.text = "";
                }
                else
                {
                    _channelRoot.gameObject.SetActive(false);
                    var near = Interactables.Nearest(sk, ctx.Tuning.sidekick.interactRange);
                    _prompt.text = near != null ? $"<color=#F2C14E>[E]</color>  {near.Prompt}" : "";
                }
            }
            if (_boss != null)
            {
                _bossHp.Set(_boss.Health.Fraction);
                _bossHp.Tick(dt);
                if (!_boss.IsAlive) _bossRoot.gameObject.SetActive(false);
            }
        }

        static void UpdateSlot(Slot s, SkillState st)
        {
            if (st == null)
            {
                s.Name.text = "";
                s.Cool.fillAmount = 0f;
                s.Secs.text = "";
                s.Bg.color = new Color(0.08f, 0.08f, 0.1f, 0.45f);
                return;
            }
            s.Bg.color = UIKit.PanelDark;
            s.Name.text = st.Def.displayName + (st.Rank >= 2 ? " <color=#F2C14E>II</color>" : "");
            s.Name.color = st.CooldownRemaining > 0.05f ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            float f = st.CooldownFraction;
            s.Cool.fillAmount = f;
            s.Secs.text = st.CooldownRemaining > 0.05f ? Mathf.CeilToInt(st.CooldownRemaining).ToString() : "";
        }

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
