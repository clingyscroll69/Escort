using System.Text;
using HS.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The opening's System window (GDD §8 beats 2–3). A slot reel spins through ~70 classes for 5 s and lands on HERO
    /// (gold). Then the error: the window goes red and shakes, red glyphs cycle on the class, and error pop-ups pile up in
    /// the empty margins either side for 5.5 s. They close one by one over 3 s while the window goes back to System blue,
    /// and the class settles on HERO's SIDEKICK, which stays red. Everything is a pure function of the opening clock, so
    /// tests and captures can scrub it.
    /// </summary>
    public sealed class ClassRoll
    {
        // ------------------------------------------------------------------------------------------------------ timing
        /// <summary>Offsets from the window popping in (the full version).</summary>
        public const float ReelAt = 0.8f, ReelLength = 5f, HeroHold = 2.8f, StormLength = 5.5f, RecoverLength = 3f,
            WhatDelay = 0.8f, EndHold = 3.2f;
        const float SpawnSpan = 5f, CloseSpan = 2.5f;
        const int PopupCount = 32, ShortPopups = 6;

        // -------------------------------------------------------------------------------------------- copy (drafts)
        // Draft copy (owner to rewrite, GDD §8). The last three before HERO are the GDD's (OpeningTests checks them).
        public static readonly string[] Classes =
        {
            "Bard (Kazoo)", "Cartographer", "Necromancer", "Intern", "Paladin", "Influencer", "Lich", "Accountant",
            "Dragon Tamer", "Mime", "Sous Chef", "Goblin Diplomat", "Rogue", "Notary Public", "Druid", "Middle Manager",
            "Alchemist", "Crossing Guard", "Berserker", "Librarian", "Pyromancer", "Dentist", "Ranger", "HR Liaison",
            "Beekeeper", "Warlock", "Supply Teacher", "Monk", "Sword Saint", "Main Character", "Cleric", "Mall Santa",
            "Oracle", "Plumber", "Knight", "Lifeguard", "Summoner", "IT Support", "Archmage", "Stunt Double", "Florist",
            "Assassin", "Dog Walker", "Sorcerer", "Parking Warden", "Valkyrie", "Locksmith", "Bounty Hunter",
            "Weatherman", "Spellblade", "Telemarketer", "Beast Master", "Sommelier", "Gladiator", "Life Coach",
            "Background NPC", "Shaman", "Truck Driver", "Demon King", "Tour Guide", "Witch", "Data Analyst", "Templar",
            "Juggler", "Slime", "Villager B", "Barista", "Tax Auditor", "Dark Lord",
        };

        static readonly string[] Titles =
        {
            "ERROR", "SYSTEM ERROR", "FATAL ERROR", "ERROR 0x5E1D", "CLASS ERROR", "EXCEPTION", "WARNING", "ERROR",
        };

        /// <summary>Pop-up bodies. A leading '~' marks a body that is corrupted into glyphs as it plays.</summary>
        static readonly string[] Bodies =
        {
            "Class conflict: HERO", "HERO slot already occupied.", "Subject not found in registry.",
            "Assignment failed.\nRetrying... (3/3)", "UNLISTED ENTITY DETECTED", "Cannot render subject.",
            "fate.dll stopped responding.", "NullHeroException", "Recalculating destiny...", "Permission denied:\nprotagonist",
            "Rolling back class...", "Stack overflow in /fate/core", "Duplicate HERO.\nAppending suffix...",
            "Unexpected party member.", "Checksum mismatch: soul", "~Assigning sidekick...", "~Subject is not the hero.",
            "~Who are you?",
        };

        const string Glyphs = "#@%&$*+=<>?/\\|×÷±§¶¤ø∆∑∞≠≈√∂µπ¥£€¢†‡¿¡";
        const string Blocks = "▓▒░█#@%&$×÷§¶∆∑≠≈√µπ¥€†‡ΣΨΞЖЯЮ";
        const string Tail = "'s SIDEKICK";
        /// <summary>Every glyph the window and pop-ups use, set once so the dynamic font atlases fill during the pre-roll.</summary>
        const string WarmGlyphs = Glyphs + Blocks + "_";
        const string Cyan = "#7BE6FF", Red = "#FF564A", GoldHex = "#F2C14E";

        // ------------------------------------------------------------------------------------------------------ state
        public float HeroAt { get; }
        public float ErrorAt { get; }
        public float RecoverAt { get; }
        public float SettleAt { get; }
        public float WhatAt { get; }
        public float EndAt { get; }
        public string ClassLine { get; private set; } = "";
        public string Thought => _thought.text;
        public int PopupsShown { get; private set; }
        /// <summary>How red the window's frame is (1 = error, 0 = System blue).</summary>
        public float Redness { get; private set; }
        /// <summary>Screen-space rects of the visible pop-ups and the window (canvas units), for tests.</summary>
        public Rect WindowRect => new Rect(-WindowSize.x / 2f, WindowPos.y - WindowSize.y / 2f, WindowSize.x, WindowSize.y);

        static readonly Vector2 WindowSize = new Vector2(980f, 380f), WindowPos = new Vector2(0f, 40f);
        const float RowH = 100f;

        readonly bool _short;
        readonly float _sys0;
        readonly float[] _starts, _durs;
        readonly Popup[] _popups;
        readonly StringBuilder _sb = new StringBuilder(64);

        RectTransform _window, _slot, _reel, _glowRt;
        CanvasGroup _windowGroup;
        Image _border, _slotBorder, _goldGlow, _edgeL, _edgeR;
        TextMeshProUGUI _head, _label, _thought;
        readonly TextMeshProUGUI[] _rows = new TextMeshProUGUI[5];
        readonly RectTransform[] _sparks = new RectTransform[22];
        readonly Image[] _sparkImgs = new Image[22];

        sealed class Popup
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Flash;
            public TextMeshProUGUI Title, Body;
            public RectTransform Fill;
            public float Spawn, Close;
            public Vector2 Pos;
            public string TitleText, BodyText;
            public bool Glyph, Corrupt, GlitchTitle;
            public int Seed;
        }

        // ------------------------------------------------------------------------------------------------------ build
        public ClassRoll(RectTransform parent, bool shortVersion, float sys0)
        {
            _short = shortVersion;
            _sys0 = sys0;
            if (_short)
            {
                HeroAt = sys0 + 0.05f;
                ErrorAt = HeroAt + 0.5f;
                RecoverAt = ErrorAt + 0.75f;
                SettleAt = RecoverAt + 0.4f;
                WhatAt = float.MaxValue;
                EndAt = float.MaxValue; // the short version's length is OpeningView's
            }
            else
            {
                HeroAt = sys0 + ReelAt + ReelLength;
                ErrorAt = HeroAt + HeroHold;
                RecoverAt = ErrorAt + StormLength;
                SettleAt = RecoverAt + RecoverLength;
                WhatAt = SettleAt + WhatDelay;
                EndAt = SettleAt + EndHold;
            }
            (_starts, _durs) = PlanReel(sys0 + ReelAt);
            BuildEdges(parent);
            BuildWindow(parent);
            _popups = BuildPopups(parent, _short ? ShortPopups : PopupCount);
            _thought = UIKit.Text(parent, "Thought", "", UIKit.Sans, 30, new Color(1f, 0.93f, 0.8f), TextAlignmentOptions.Center);
            _thought.rectTransform.offsetMax = new Vector2(0f, -760f);
            _thought.fontStyle = FontStyles.Italic;
        }

        /// <summary>
        /// Per-name dwell times: a quick spin-up, a ~22 names/s blur, then a slot machine's slowing ticks into HERO. The
        /// cruise speed is solved so the whole spin takes exactly <see cref="ReelLength"/>.
        /// </summary>
        static (float[] starts, float[] durs) PlanReel(float at)
        {
            int n = Classes.Length;
            var d = new float[n];
            float[] spinUp = { 0.16f, 0.10f, 0.07f, 0.05f };
            const int tail = 10;
            const float first = 0.05f, last = 0.55f;
            float r = Mathf.Pow(last / first, 1f / (tail - 1));
            float used = 0f;
            for (int i = 0; i < spinUp.Length; i++) used += d[i] = spinUp[i];
            for (int j = 0; j < tail; j++) used += d[n - tail + j] = first * Mathf.Pow(r, j);
            int cruise = n - tail - spinUp.Length;
            float dc = (ReelLength - used) / cruise;
            for (int i = spinUp.Length; i < n - tail; i++) d[i] = dc;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                s[i] = at;
                at += d[i];
            }
            return (s, d);
        }

        void BuildEdges(RectTransform parent)
        {
            // red light bleeding in from both sides while the error storms (where the pop-ups are)
            _edgeL = EdgeGlow(parent, "EdgeL", -1f);
            _edgeR = EdgeGlow(parent, "EdgeR", 1f);
        }

        static Image EdgeGlow(RectTransform parent, string name, float side)
        {
            var rt = UIKit.Rect(parent, name, new Vector2(side < 0 ? 0f : 1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 1500f), Vector2.zero);
            return UIKit.Image(rt, "Glow", UIKit.Glow, new Color(1f, 0.2f, 0.16f, 0f), false);
        }

        void BuildWindow(RectTransform parent)
        {
            _window = UIKit.Rect(parent, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), WindowSize, WindowPos);
            _windowGroup = _window.gameObject.AddComponent<CanvasGroup>();
            _windowGroup.alpha = 0f;
            _glowRt = UIKit.Rect(_window, "GoldGlow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 420f), new Vector2(150f, -24f));
            _goldGlow = UIKit.Image(_glowRt, "Glow", UIKit.Glow, new Color(1f, 0.78f, 0.3f, 0f), false);
            UIKit.Image(_window, "Bg", UIKit.Panel, UIKit.SystemBg);
            _border = UIKit.Image(_window, "Border", UIKit.Border, UIKit.SystemCyan);
            _head = UIKit.Text(_window, "Head", "» SYSTEM  <size=70%>assigning class…</size>", UIKit.Mono, 28, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            _head.rectTransform.offsetMin = new Vector2(32f, 0f);
            _head.rectTransform.offsetMax = new Vector2(-32f, -24f);
            _head.maxVisibleCharacters = 0;

            // "CLASS:" then the reel's slot, like a slot machine's window
            var label = UIKit.Rect(_window, "Label", new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(300f, 90f), new Vector2(-172f, -24f));
            _label = UIKit.Text(label, "T", "CLASS:", UIKit.Mono, 60, UIKit.SystemCyan, TextAlignmentOptions.Right);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            // sparkles burst from behind the slot when HERO lands (built first so the slot hides their start)
            for (int i = 0; i < _sparks.Length; i++)
            {
                float size = 7f + 11f * Hash(i, 21);
                _sparks[i] = UIKit.Rect(_window, "Spark" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size, size), new Vector2(150f, -24f));
                _sparkImgs[i] = UIKit.Image(_sparks[i], "Pip", UIKit.Pip, new Color(1f, 0.85f, 0.4f, 0f), false);
            }
            _slot = UIKit.Rect(_window, "Slot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(600f, 150f), new Vector2(150f, -24f));
            UIKit.Image(_slot, "Bg", UIKit.Panel, new Color(0.01f, 0.03f, 0.07f, 1f));
            _reel = UIKit.Stretch(_slot, "Reel", 6f);
            var mask = _reel.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(0, 48);
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = UIKit.Rect(_reel, "Row" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(590f, RowH), Vector2.zero);
                _rows[i] = UIKit.Text(row, "T", WarmGlyphs, UIKit.Mono, 58, Color.white, TextAlignmentOptions.Center);
                _rows[i].textWrappingMode = TextWrappingModes.NoWrap;
            }
            _slotBorder = UIKit.Image(_slot, "Border", UIKit.Border, UIKit.SystemCyan);
        }

        Popup[] BuildPopups(RectTransform parent, int count)
        {
            var root = UIKit.Stretch(parent, "ErrorPopups");
            var rng = new HS.Core.DetRandom(_short ? 4649 : 2026);
            var popups = new Popup[count];
            // Cascades like a real error storm: each side has three stacks, and each new pop-up in a stack sits a step
            // down-and-inward from the last one. Never over the window: x stays in the margins.
            var stackCount = new int[6];
            float spawnSpan = _short ? 0.6f : SpawnSpan, closeSpan = _short ? 0.35f : CloseSpan;
            for (int i = 0; i < count; i++)
            {
                var p = new Popup { Seed = rng.Range(0, 100000) };
                float x01 = count == 1 ? 0f : i / (count - 1f);
                p.Spawn = ErrorAt + 0.02f + spawnSpan * Mathf.Pow(x01, 0.55f);
                // newest closes first, slowly at first and then faster
                float c01 = count == 1 ? 0f : (count - 1 - i) / (count - 1f);
                p.Close = RecoverAt + 0.1f + closeSpan * Mathf.Pow(c01, 0.6f);
                int stack = i < 2 ? i * 3 : rng.Range(0, 6);   // the first two open one per side
                int side = stack < 3 ? -1 : 1;
                int k = stackCount[stack]++;
                float w = new[] { 300f, 330f, 360f, 400f }[rng.Range(0, 4)];
                float h = new[] { 128f, 142f, 156f }[rng.Range(0, 3)];
                float baseY = new[] { 300f, 20f, -270f }[stack % 3] + (float)(rng.NextDouble() * 60.0 - 30.0);
                float inner = WindowSize.x / 2f + 18f;                          // the window's edge plus a gap
                float outer = 960f - 14f;
                float x = side * (outer - w / 2f - (float)(rng.NextDouble() * 40.0)) - side * k * 24f;
                x = side < 0 ? Mathf.Clamp(x, -outer + w / 2f, -inner - w / 2f) : Mathf.Clamp(x, inner + w / 2f, outer - w / 2f);
                float y = Mathf.Clamp(baseY - k * 26f, -540f + h / 2f + 12f, 540f - h / 2f - 12f);
                p.Pos = new Vector2(x, y);
                p.TitleText = Titles[(i + rng.Range(0, Titles.Length)) % Titles.Length];
                string body = Bodies[(i * 7 + rng.Range(0, Bodies.Length)) % Bodies.Length];
                p.Glyph = i % 5 == 3;                       // pure glyph noise
                p.Corrupt = body[0] == '~' || i % 7 == 5;   // readable text that rots into glyphs
                p.BodyText = body.TrimStart('~');
                p.GlitchTitle = i % 4 == 2;
                BuildPopup(root, p, i, w, h, rng);
                popups[i] = p;
            }
            return popups;
        }

        void BuildPopup(RectTransform root, Popup p, int index, float w, float h, HS.Core.DetRandom rng)
        {
            p.Rt = UIKit.Rect(root, "Popup" + index, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(w, h), p.Pos);
            p.Group = p.Rt.gameObject.AddComponent<CanvasGroup>();
            p.Group.alpha = OpeningView.WarmAlpha; // drawn (shaders, glyph atlas) during the black pre-roll, invisibly
            var shadow = UIKit.Image(p.Rt, "Shadow", UIKit.Panel, new Color(0f, 0f, 0f, 0.55f));
            shadow.rectTransform.offsetMin = new Vector2(8f, -8f);
            shadow.rectTransform.offsetMax = new Vector2(8f, -8f);
            UIKit.Image(p.Rt, "Bg", UIKit.Panel, new Color(0.09f, 0.02f, 0.04f, 0.96f));
            UIKit.Image(p.Rt, "Border", UIKit.Border, UIKit.Danger);

            // title bar: a warning badge, the title, a close box
            var bar = UIKit.Rect(p.Rt, "TitleBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(w - 8f, 30f), new Vector2(0f, -4f));
            UIKit.Image(bar, "Bg", null, new Color(1f, 0.34f, 0.29f, 0.92f), false);
            var badge = UIKit.Rect(bar, "Badge", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 22f), new Vector2(5f, 0f));
            UIKit.Image(badge, "Bg", UIKit.Panel, UIKit.Ink);
            var bang = UIKit.Text(badge, "T", "!", UIKit.Mono, 20, UIKit.Danger, TextAlignmentOptions.Center);
            bang.fontStyle = FontStyles.Bold;
            var titleRt = UIKit.Rect(bar, "Title", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(w - 90f, 30f), new Vector2(34f, 0f));
            p.Title = UIKit.Text(titleRt, "T", p.TitleText + Glyphs, UIKit.Mono, 19, UIKit.Ink, TextAlignmentOptions.Left);
            p.Title.textWrappingMode = TextWrappingModes.NoWrap;
            p.Title.fontStyle = FontStyles.Bold;
            var close = UIKit.Rect(bar, "Close", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(26f, 22f), new Vector2(-4f, 0f));
            UIKit.Image(close, "Bg", UIKit.Panel, new Color(0.35f, 0.05f, 0.06f, 1f));
            UIKit.Text(close, "T", "×", UIKit.Mono, 20, new Color(1f, 0.85f, 0.82f), TextAlignmentOptions.Center);

            // body: a message (or glyph noise), and either a stuck progress bar or buttons
            var bodyRt = UIKit.Rect(p.Rt, "Body", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(w - 32f, h - 82f), new Vector2(0f, -42f));
            p.Body = UIKit.Text(bodyRt, "T", p.Glyph ? Blocks : p.BodyText + Glyphs, p.Glyph ? UIKit.Sans : UIKit.Mono, p.Glyph ? 21 : 19,
                new Color(1f, 0.82f, 0.79f), TextAlignmentOptions.TopLeft);
            if (index % 3 == 1)
            {
                var track = UIKit.Rect(p.Rt, "Progress", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(w - 32f, 10f), new Vector2(0f, 18f));
                UIKit.Image(track, "Track", null, new Color(1f, 1f, 1f, 0.12f), false);
                p.Fill = UIKit.Image(track, "Fill", null, UIKit.Danger, false).rectTransform;
                p.Fill.anchorMax = new Vector2(0.6f + 0.39f * (float)rng.NextDouble(), 1f);
            }
            else
            {
                string[] labels = index % 3 == 0 ? new[] { "OK" } : new[] { "RETRY", "IGNORE" };
                for (int b = 0; b < labels.Length; b++)
                {
                    var btn = UIKit.Rect(p.Rt, labels[b], new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(96f, 30f), new Vector2(-16f - b * 106f, 12f));
                    UIKit.Image(btn, "Bg", UIKit.Panel, new Color(0.2f, 0.04f, 0.06f, 1f));
                    UIKit.Image(btn, "Border", UIKit.Border, new Color(1f, 0.34f, 0.29f, 0.8f));
                    UIKit.Text(btn, "T", labels[b], UIKit.Mono, 17, new Color(1f, 0.8f, 0.76f), TextAlignmentOptions.Center);
                }
            }
            p.Flash = UIKit.Image(p.Rt, "Flash", UIKit.Panel, new Color(1f, 0.95f, 0.92f, 0f));
        }

        // ----------------------------------------------------------------------------------------------------- render
        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        /// <summary>Deterministic hash → [0, 1).</summary>
        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public void Render(float t)
        {
            if (t < _sys0)
            {
                // Linear lighting lifts even alpha 0.004 to ~13/255 on black, so the invisible warm-up draw only
                // happens behind the black pre-roll (t < 0); during the walk nothing of the window is drawn at all.
                float warm = t < 0f ? OpeningView.WarmAlpha : 0f;
                _windowGroup.alpha = warm;
                _thought.text = "";
                ClassLine = "";
                PopupsShown = 0;
                Redness = 0f;
                foreach (var p in _popups) p.Group.alpha = warm;
                _edgeL.color = _edgeR.color = new Color(1f, 0.2f, 0.16f, warm);
                return;
            }
            float pop = Smooth(_sys0, _sys0 + 0.25f, t);
            _windowGroup.alpha = pop;
            _window.localScale = Vector3.one * (0.96f + 0.04f * pop);
            _head.maxVisibleCharacters = t >= ErrorAt ? 99999 : Mathf.FloorToInt((t - _sys0) * 45f);

            RenderPopups(t, out int alive, out float burst);
            PopupsShown = alive;
            float storm = t >= ErrorAt ? 1f - Smooth(RecoverAt, RecoverAt + RecoverLengthFor(), t) : 0f;
            Redness = t < ErrorAt ? 0f : 1f - Smooth(RecoverAt + 0.1f * RecoverLengthFor(), SettleAt - 0.05f, t);

            // the frame: shake (a little all storm long, a kick per pop-up), and the occasional slice-glitch
            float shake = t >= ErrorAt ? (1f - Smooth(ErrorAt, ErrorAt + 0.4f, t)) * 8f + storm * (2.2f + burst * 9f) : 0f;
            var jitter = new Vector2(Mathf.Sin(t * 90f), Mathf.Cos(t * 77f)) * shake;
            int slice = Mathf.FloorToInt(t * 14f);
            if (storm > 0.5f && Hash(slice, 11) < 0.12f) jitter.x += (Hash(slice, 12) - 0.5f) * 40f;
            _window.anchoredPosition = WindowPos + jitter;

            var frame = Color.Lerp(UIKit.SystemCyan, UIKit.Danger, Redness);
            _border.color = frame;
            _slotBorder.color = new Color(frame.r, frame.g, frame.b, 0.75f);
            _label.color = frame;
            _head.color = frame;
            string hex = "#" + ColorUtility.ToHtmlStringRGB(frame);
            if (t < ErrorAt) _head.text = "» SYSTEM  <size=70%>assigning class…</size>";
            else if (t < RecoverAt) _head.text = $"» SYSTEM  <size=70%><color={Red}>ERROR · class conflict</color></size>";
            else if (t < SettleAt) _head.text = $"» SYSTEM  <size=70%><color={hex}>resolving conflict…</color></size>";
            else _head.text = $"» SYSTEM  <size=70%><color={Cyan}>class assigned</color></size>";

            float edge = t >= ErrorAt ? storm * Mathf.Clamp01(alive / 12f) * (0.3f + 0.25f * burst) : 0f;
            _edgeL.color = _edgeR.color = new Color(1f, 0.2f, 0.16f, edge);

            if (t < HeroAt) RenderReel(t);
            else if (t < ErrorAt) RenderHero(t);
            else RenderError(t);
            RenderSparks(t);
        }

        float RecoverLengthFor() => SettleAt - RecoverAt;

        void RenderReel(float t)
        {
            _goldGlow.color = new Color(1f, 0.78f, 0.3f, 0f);
            _thought.text = "";
            if (t < _starts[0])
            {
                ClassLine = "CLASS: " + (Mathf.Repeat(t, 0.5f) < 0.25f ? "_" : " ");
                SetRows(-1f, "", 0f);
                _rows[2].text = Mathf.Repeat(t, 0.5f) < 0.25f ? "_" : "";
                return;
            }
            int i = 0;
            while (i + 1 < _starts.Length && t >= _starts[i + 1]) i++;
            ClassLine = "CLASS: <color=#FFFFFF>" + Classes[i] + "</color>";
            float pos = ReelPos(i, t, _durs[i], 0.08f);
            float speed = 1f / _durs[i];
            SetRows(pos, null, Mathf.Clamp01((speed - 4f) / 16f));
        }

        /// <summary>
        /// The reel's position (in names) during name i's dwell: fast names slide continuously, slow ones tick over with a
        /// small overshoot, like a slot machine settling.
        /// </summary>
        float ReelPos(int i, float t, float dwell, float overshoot)
        {
            float slide = dwell <= 0.06f ? dwell : Mathf.Min(0.13f, dwell);
            float u = Mathf.Clamp01((t - _starts[Mathf.Min(i, _starts.Length - 1)]) / slide);
            if (i >= _starts.Length) u = Mathf.Clamp01((t - HeroAt) / slide);
            float back = Mathf.InverseLerp(0.06f, 0.25f, dwell) * overshoot;
            float eased = BackOut(u, back);
            float lin = u;
            float k = Mathf.InverseLerp(0.05f, 0.12f, dwell);
            return i - 1 + Mathf.Lerp(lin, eased, k);
        }

        /// <summary>Ease-out with an overshoot of about `amount` names, settling at 1.</summary>
        static float BackOut(float u, float amount)
        {
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            return e + amount * Mathf.Sin(Mathf.PI * u) * (1f - u) * 2.2f;
        }

        void RenderHero(float t)
        {
            float pos = ReelPos(Classes.Length, t, 0.6f, 0.22f);
            SetRows(pos, null, 0f);
            ClassLine = $"CLASS: <color={GoldHex}>HERO</color>";
            float popScale = 1f + 0.25f * (1f - Smooth(HeroAt, HeroAt + 0.3f, t));
            _rows[RowFor(Classes.Length, pos)].transform.localScale = Vector3.one * popScale;
            float glow = Smooth(HeroAt, HeroAt + 0.15f, t) * (0.28f + 0.3f * (1f - Smooth(HeroAt + 0.15f, HeroAt + 0.9f, t)));
            _goldGlow.color = new Color(1f, 0.78f, 0.3f, glow);
            _thought.text = !_short && t >= HeroAt + 0.6f ? "YES. I finally get to be the hero." : "";
        }

        void RenderError(float t)
        {
            _goldGlow.color = new Color(1f, 0.78f, 0.3f, 0.3f * (1f - Smooth(ErrorAt, ErrorAt + 0.25f, t)));
            for (int r = 0; r < _rows.Length; r++)
            {
                _rows[r].text = "";
                _rows[r].transform.localScale = Vector3.one;
            }
            var row = _rows[2];
            row.rectTransform.parent.localPosition = Vector3.zero;
            row.color = Color.white;
            // The tail grows as unreadable glyphs while the storm rages, then resolves letter by letter, left to right.
            int seed = Mathf.FloorToInt(t * 26f);
            int grown = Mathf.FloorToInt(Tail.Length * Smooth(ErrorAt + 0.2f, RecoverAt - 0.6f, t) + 0.0001f);
            int resolved = t >= SettleAt ? Tail.Length
                : Mathf.FloorToInt(Tail.Length * Mathf.InverseLerp(RecoverAt + 0.3f, SettleAt - 0.25f, t));
            _sb.Clear();
            bool heroGlitch = t < RecoverAt && Hash(seed, 3) < 0.18f;
            _sb.Append(heroGlitch ? "H" + Glyphs[(seed * 5) % Glyphs.Length] + "RO" : "HERO");
            for (int k = 0; k < Tail.Length; k++)
            {
                if (k < resolved) _sb.Append(Tail[k]);
                else if (k < Mathf.Max(grown, resolved + (t >= RecoverAt ? Tail.Length : 0)))
                    _sb.Append(Tail[k] == ' ' ? ' ' : Glyphs[(seed + k * 3) % Glyphs.Length]);
            }
            string name = _sb.ToString();
            row.text = $"<color={Red}>{name}</color>";
            ClassLine = $"CLASS: <color={Red}>{name}</color>";
            float settlePulse = t >= SettleAt ? 1f + 0.12f * (1f - Smooth(SettleAt, SettleAt + 0.35f, t)) : 1f;
            row.transform.localScale = Vector3.one * settlePulse;
            _thought.text = t >= SettleAt && t >= WhatAt ? "...Wait. What?" : "";
        }

        int RowFor(int index, float pos) => Mathf.Clamp(index - Mathf.FloorToInt(pos) + 1, 0, _rows.Length - 1);

        /// <summary>Lay the reel's rows out around `pos`; `blur` stretches and fades them at speed.</summary>
        void SetRows(float pos, string only, float blur)
        {
            int baseIndex = Mathf.FloorToInt(pos) - 1;
            for (int r = 0; r < _rows.Length; r++)
            {
                int idx = baseIndex + r;
                var rt = (RectTransform)_rows[r].transform.parent;
                if (only != null || idx < 0 || idx > Classes.Length)
                {
                    _rows[r].text = "";
                    continue;
                }
                bool hero = idx == Classes.Length;
                _rows[r].text = hero ? "HERO" : Classes[idx];
                float y = (idx - pos) * RowH;
                rt.localPosition = new Vector3(0f, y, 0f);
                float centred = 1f - Mathf.Clamp01(Mathf.Abs(idx - pos));
                var c = hero ? UIKit.Gold : Color.white;
                c.a = Mathf.Lerp(0.55f, 1f, centred) * (1f - 0.35f * blur * (1f - centred));
                _rows[r].color = c;
                _rows[r].transform.localScale = new Vector3(1f, 1f + 0.35f * blur, 1f);
            }
        }

        void RenderSparks(float t)
        {
            float u = Mathf.InverseLerp(HeroAt, HeroAt + 0.7f, t);
            bool on = t >= HeroAt && u < 1f;
            for (int i = 0; i < _sparks.Length; i++)
            {
                float a = (i + 0.5f) / _sparks.Length * Mathf.PI * 2f + Hash(i, 7) * 0.5f;
                float reach = 230f + 260f * Hash(i, 8);
                float dist = reach * (1f - (1f - u) * (1f - u) * (1f - u));
                _sparks[i].anchoredPosition = new Vector2(150f, -24f) + new Vector2(Mathf.Cos(a) * dist * 1.25f, Mathf.Sin(a) * dist * 0.55f);
                _sparks[i].localScale = Vector3.one * (1.3f - 0.8f * u);
                _sparkImgs[i].color = new Color(1f, 0.82f + 0.12f * Hash(i, 9), 0.35f, on ? 1f - u * u : 0f);
            }
        }

        void RenderPopups(float t, out int alive, out float burst)
        {
            alive = 0;
            burst = 0f;
            for (int i = 0; i < _popups.Length; i++)
            {
                var p = _popups[i];
                if (t < p.Spawn || t >= p.Close + 0.16f)
                {
                    p.Group.alpha = 0f;
                    continue;
                }
                float since = t - p.Spawn;
                burst = Mathf.Max(burst, 1f - Mathf.Clamp01(since / 0.25f));
                float open = Mathf.Clamp01(since / 0.12f);
                float closing = Mathf.Clamp01((t - p.Close) / 0.15f);
                if (closing <= 0f) alive++;
                float scale = (0.6f + 0.45f * open - 0.05f * Mathf.Sin(Mathf.PI * open)) * (1f - 0.12f * closing);
                if (open >= 1f) scale = 1f - 0.12f * closing;
                p.Rt.localScale = Vector3.one * scale;
                // glitches: a jolt every so often, some flicker
                int step = Mathf.FloorToInt(t * 9f + p.Seed % 7);
                var jolt = Hash(step, p.Seed) < 0.16f
                    ? new Vector2((Hash(step, p.Seed + 1) - 0.5f) * 18f, (Hash(step, p.Seed + 2) - 0.5f) * 8f)
                    : Vector2.zero;
                p.Rt.anchoredPosition = p.Pos + jolt * (1f - closing);
                float flicker = i % 3 == 2 && Hash(Mathf.FloorToInt(t * 20f), p.Seed + 3) < 0.1f ? 0.55f : 1f;
                p.Group.alpha = open * flicker * (1f - closing);
                p.Flash.color = new Color(1f, 0.95f, 0.92f, 0.65f * (1f - Mathf.Clamp01(since / 0.16f)));
                RenderPopupText(p, t);
                if (p.Fill != null)
                {
                    float f = Mathf.Repeat(0.6f + Hash(p.Seed, 9) * 0.3f + Mathf.Sin(t * 3f + p.Seed) * 0.04f, 1f);
                    p.Fill.anchorMax = new Vector2(Mathf.Clamp(f, 0.05f, 0.99f), 1f);
                }
            }
        }

        void RenderPopupText(Popup p, float t)
        {
            int step = Mathf.FloorToInt(t * 16f);
            if (p.GlitchTitle)
            {
                _sb.Clear();
                for (int k = 0; k < p.TitleText.Length; k++)
                    _sb.Append(p.TitleText[k] != ' ' && Hash(step + k, p.Seed + 4) < 0.25f ? Glyphs[(step + k) % Glyphs.Length] : p.TitleText[k]);
                p.Title.text = _sb.ToString();
            }
            else p.Title.text = p.TitleText; // (built with the warm-up glyphs appended)
            if (!p.Glyph && !p.Corrupt) p.Body.text = p.BodyText;
            if (p.Glyph)
            {
                _sb.Clear();
                int len = 28 + p.Seed % 14;
                for (int k = 0; k < len; k++)
                {
                    float h = Hash(step * 31 + k, p.Seed + 5);
                    _sb.Append(k % 13 == 12 ? '\n' : h < 0.14f ? ' ' : Blocks[(int)(h * 997f) % Blocks.Length]);
                }
                p.Body.text = _sb.ToString();
            }
            else if (p.Corrupt)
            {
                float rot = Mathf.Clamp01((t - p.Spawn - 0.4f) / 2.5f) * 0.55f;
                _sb.Clear();
                for (int k = 0; k < p.BodyText.Length; k++)
                {
                    char c = p.BodyText[k];
                    _sb.Append(c != ' ' && c != '\n' && Hash(step + k * 13, p.Seed + 6) < rot ? Glyphs[(step + k) % Glyphs.Length] : c);
                }
                p.Body.text = _sb.ToString();
            }
        }

        // ------------------------------------------------------------------------------------------------------ sound
        /// <summary>The window's sounds, crossed between the last frame's clock and this one's.</summary>
        public void Cues(float prevT, float t)
        {
            var a = AudioDirector.Instance;
            if (a == null) return;
            bool Crossed(float at) => prevT < at && t >= at;
            if (Crossed(_sys0)) a.Play("ui_open", null, 0.6f, 0.1f, 0f);
            if (!_short)
                for (int i = 0; i < _starts.Length; i++)
                    if (Crossed(_starts[i])) a.Play("ui_tick", null, _durs[i] > 0.1f ? 0.5f : 0.28f, 0.02f, 0.05f);
            if (Crossed(HeroAt)) a.Play("ui_confirm", null, 0.8f, 0.1f, 0f);
            if (Crossed(ErrorAt)) a.Play("ui_error", null, 0.9f, 0.1f, 0f);
            for (int i = 0; i < _popups.Length; i++)
            {
                if (i > 0 && Crossed(_popups[i].Spawn))
                {
                    if (i % 2 == 0) a.Play("ui_error", null, 0.32f, 0.07f, 0.1f);
                    else a.Play("ui_glitch", null, 0.45f, 0.07f, 0.1f);
                }
                if (Crossed(_popups[i].Close)) a.Play("ui_tick", null, 0.3f, 0.05f, 0.08f);
            }
            if (Crossed(SettleAt)) a.Play("ui_bong", null, 0.8f, 0.1f, 0f);
        }

        /// <summary>Canvas-space rect of pop-up i as laid out (ignores jolts), for tests.</summary>
        public Rect PopupRect(int i)
        {
            var p = _popups[i];
            var s = p.Rt.sizeDelta;
            return new Rect(p.Pos.x - s.x / 2f, p.Pos.y - s.y / 2f, s.x, s.y);
        }

        public int PopupTotal => _popups.Length;
    }
}
