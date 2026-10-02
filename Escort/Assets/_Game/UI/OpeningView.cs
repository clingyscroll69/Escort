using System;
using System.Globalization;
using HS.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The opening (GDD §8), about 35 s, UI and audio only; later runs get the 3-second version.
    /// 1. The walk: a phone lock screen bobbing with each step, a song in the earbuds, the street muffled. A horn rises
    ///    out of the muffle, headlights flood in from the right, the phone drops away, and the song cuts on the drop that
    ///    never comes. White screen, ringing.
    /// 2. The status window: CLASS rolls (… Barista, Tax Auditor, Dark Lord) and settles on HERO. "YES. I finally get to
    ///    be the hero."
    /// 3. A red error box; red glyphs cycle and settle with 's SIDEKICK appended: HERO's SIDEKICK.
    /// The song, street and truck (tools/make_opening.py) are scheduled on the audio clock and cut on the same sample; the
    /// picture follows the audio clock. Skipping takes two presses, so a stray click can't eat the joke.
    /// </summary>
    public sealed class OpeningView : MonoBehaviour
    {
        public enum Phase { PreRoll, Walk, White, System, Done }

        // Timeline in seconds from the song's first note. CutAt, HornAt, StepAt, Step and StepsEnd must match
        // tools/make_opening.py (OpeningTests checks the clip lengths against them).
        public const float CutAt = 16f, HornAt = 12.5f, StepAt = 0.15f, Step = 0.53f, StepsEnd = 14f;
        public const float PreRoll = 1f, LookUpAt = 14.2f, WhiteUntil = 18.2f, BlackAt = 19.4f, SystemAt = 20f;
        public const float ShortFlash = 0.6f, ShortLength = 3f;
        public static readonly int MaxSteps = Mathf.FloorToInt((StepsEnd - StepAt) / Step + 1e-4f) + 1;
        const float SongVol = 0.8f, SongDucked = 0.25f, StreetVol = 0.55f, HornVol = 0.9f, RingVol = 0.12f, StingVol = 0.85f;
        const int StepsBefore = 6384, TrackPos = 72, TrackLength = 223;
        /// <summary>
        /// Hidden UI isn't drawn at alpha 0, so its first frame compiled the masked-UI shader variants mid-walk (a 316 ms
        /// stall in the player build). Drawn at an invisible alpha, that happens behind the black pre-roll instead.
        /// </summary>
        public const float WarmAlpha = 0.004f;

        // Draft copy (owner to rewrite, GDD §8).
        static readonly (float at, string app, string title, string body)[] Notes =
        {
            (4.2f, "CALENDAR", "Shift at 8:00", "Starts in 8 minutes."),
            (8.6f, "HERO SUMMONER", "Your daily summon is ready!", "Will today be the day you're chosen?"),
        };

        public event Action Done;
        public Phase Current { get; private set; }
        /// <summary>The opening's clock: 0 = the song's first note (after the black pre-roll).</summary>
        public float Elapsed => _t;
        public int Steps { get; private set; }
        public bool SkipArmed => _real < _skipArmedUntil;
        public string ClassLine => _roll.ClassLine;
        public string Thought => _roll.Thought;
        /// <summary>The System window (class roll, error storm, recovery).</summary>
        public ClassRoll Roll => _roll;
        public float WhiteAlpha => _white.color.a;
        public float GlowAlpha => Mathf.Max(_wash.color.a, _lampImgL.color.a);
        public bool PhoneVisible => _phone != null && _phone.gameObject.activeSelf && _phoneGroup.alpha > 0.5f;
        public int NotificationsShown { get; private set; }
        public bool AudioScheduled => _dsp0 > 0;
        /// <summary>Tests drive <see cref="Advance"/> themselves (no real time, no input).</summary>
        public bool ManualClock;
        public bool Short => _short;
        /// <summary>The audio clock the picture follows (tests substitute one that never moves).</summary>
        public Func<double> AudioClock = () => AudioSettings.dspTime;

        /// <summary>Follow an audio clock that started at dsp0 (Begin does this when it schedules the clips).</summary>
        public void FollowAudio(double dsp0)
        {
            _dsp0 = dsp0;
            _lastDsp = -1;
            _dspStill = 0f;
        }
        public float EndsAt => _endAt;

        bool _short;
        float _pre, _real, _t, _prevT, _skipArmedUntil = -1f;
        double _dsp0, _lastDsp = -1;
        float _dspStill;
        AudioSource _song;
        float _sys0, _endAt;
        ClassRoll _roll;

        RectTransform _phone, _lampL, _lampR, _streak;
        CanvasGroup _phoneGroup;
        Image _white, _glare, _wash, _lampImgL, _lampImgR, _coreL, _coreR, _streakImg, _progress;
        TextMeshProUGUI _foot, _steps, _elapsed, _remain;
        RectTransform[] _cards;
        CanvasGroup[] _cardGroups;

        public static OpeningView Show(UIRoot root, bool shortVersion)
        {
            var go = UIKit.Stretch(root.Overlay, "Opening").gameObject;
            var v = go.AddComponent<OpeningView>();
            v._short = shortVersion;
            v.Plan();
            v.Build();
            return v;
        }

        void Plan()
        {
            _sys0 = _short ? ShortFlash + 0.25f : SystemAt;
            _endAt = ShortLength; // the full version's end comes from the class roll (Build)
        }

        // ------------------------------------------------------------------------------------------------------ build
        void Build()
        {
            var bg = UIKit.Image(transform, "Black", null, new Color(0.01f, 0.01f, 0.02f, 1f), false);
            bg.raycastTarget = true; // nothing behind the opening takes clicks
            if (!_short) BuildPhone();
            BuildHeadlights();
            _roll = new ClassRoll((RectTransform)transform, _short, _sys0);
            if (!_short) _endAt = _roll.EndAt;
            _foot = UIKit.Text(transform, "Foot", "", UIKit.Mono, 22, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Bottom);
            _foot.rectTransform.offsetMin = new Vector2(0f, 40f);
            _white = UIKit.Image(transform, "White", null, new Color(0.98f, 0.98f, 1f, 0f), false);
        }

        void BuildPhone()
        {
            _phone = UIKit.Rect(transform, "Phone", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(440f, 880f), new Vector2(0f, -20f));
            _phoneGroup = _phone.gameObject.AddComponent<CanvasGroup>();
            _phoneGroup.alpha = 0f;
            Round(UIKit.Image(_phone, "Body", UIKit.Panel, new Color(0.05f, 0.05f, 0.07f)), 0.22f);
            Round(UIKit.Image(_phone, "Rim", UIKit.Border, new Color(1f, 1f, 1f, 0.16f)), 0.22f);
            var screen = UIKit.Stretch(_phone, "Screen", 13f);
            var scr = screen.gameObject.AddComponent<Image>();
            scr.sprite = UIKit.Panel;
            scr.type = Image.Type.Sliced;
            scr.color = new Color(0.09f, 0.08f, 0.19f);
            Round(scr, 0.27f);
            screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Blob(screen, new Vector2(-150f, -260f), 760f, new Color(0.86f, 0.34f, 0.58f, 0.55f));
            Blob(screen, new Vector2(170f, 300f), 640f, new Color(0.30f, 0.45f, 1f, 0.45f));

            // status bar, camera pill, clock, date, step count
            Label(screen, "7:52", UIKit.Sans, 20, Color.white, new Vector2(0f, 1f), new Vector2(32f, -16f), new Vector2(120f, 30f), TextAlignmentOptions.Left);
            Label(screen, "5G  76%", UIKit.Mono, 18, Color.white, new Vector2(1f, 1f), new Vector2(-32f, -17f), new Vector2(140f, 30f), TextAlignmentOptions.Right);
            var pill = UIKit.Rect(screen, "Island", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(118f, 32f), new Vector2(0f, -14f));
            Round(UIKit.Image(pill, "Bg", UIKit.Panel, Color.black), 0.5f);
            var clock = Label(screen, "7:52", UIKit.Sans, 124, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(400f, 140f), TextAlignmentOptions.Center);
            clock.fontStyle = FontStyles.Bold;
            Label(screen, "Tuesday, 3 March", UIKit.Sans, 25, new Color(1f, 1f, 1f, 0.85f), new Vector2(0.5f, 1f), new Vector2(0f, -214f), new Vector2(400f, 34f), TextAlignmentOptions.Center);
            var stepPill = UIKit.Rect(screen, "Steps", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(210f, 38f), new Vector2(0f, -262f));
            Round(UIKit.Image(stepPill, "Bg", UIKit.Panel, new Color(1f, 1f, 1f, 0.14f)), 0.6f);
            _steps = UIKit.Text(stepPill, "Count", "", UIKit.Mono, 19, Color.white, TextAlignmentOptions.Center);

            // notifications (slide in at their times)
            _cards = new RectTransform[Notes.Length];
            _cardGroups = new CanvasGroup[Notes.Length];
            for (int i = 0; i < Notes.Length; i++)
            {
                var n = Notes[i];
                var card = UIKit.Rect(screen, "Note" + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(392f, 92f), new Vector2(0f, -318f - i * 102f));
                _cardGroups[i] = card.gameObject.AddComponent<CanvasGroup>();
                _cardGroups[i].alpha = 0f;
                Round(UIKit.Image(card, "Bg", UIKit.Panel, new Color(1f, 1f, 1f, 0.16f)), 0.55f);
                Label(card, n.app + "  <color=#FFFFFF66>now</color>", UIKit.Mono, 14, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(360f, 20f), TextAlignmentOptions.Left);
                var title = Label(card, n.title, UIKit.Sans, 20, Color.white, new Vector2(0f, 1f), new Vector2(16f, -32f), new Vector2(360f, 26f), TextAlignmentOptions.Left);
                title.fontStyle = FontStyles.Bold;
                Label(card, n.body, UIKit.Sans, 18, new Color(1f, 1f, 1f, 0.82f), new Vector2(0f, 1f), new Vector2(16f, -58f), new Vector2(360f, 24f), TextAlignmentOptions.Left);
                _cards[i] = card;
            }

            // now playing (in the earbuds)
            var music = UIKit.Rect(screen, "NowPlaying", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(392f, 222f), new Vector2(0f, 40f));
            Round(UIKit.Image(music, "Bg", UIKit.Panel, new Color(1f, 1f, 1f, 0.16f)), 0.55f);
            var art = UIKit.Rect(music, "Art", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(104f, 104f), new Vector2(16f, -16f));
            Round(UIKit.Image(art, "Bg", UIKit.Panel, new Color(0.98f, 0.55f, 0.26f)), 0.6f);
            Blob(art, new Vector2(24f, -22f), 120f, new Color(0.9f, 0.2f, 0.55f, 0.85f));
            var mc = UIKit.Text(art, "MC", "MC", UIKit.Sans, 44, Color.white, TextAlignmentOptions.Center);
            mc.fontStyle = FontStyles.Bold;
            Label(music, "NOW PLAYING", UIKit.Mono, 14, new Color(1f, 1f, 1f, 0.55f), new Vector2(0f, 1f), new Vector2(136f, -18f), new Vector2(150f, 20f), TextAlignmentOptions.Left);
            var buds = UIKit.Rect(music, "Earbuds", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(104f, 26f), new Vector2(-14f, -14f));
            Round(UIKit.Image(buds, "Bg", UIKit.Panel, new Color(0.48f, 0.9f, 1f, 0.18f)), 0.7f);
            UIKit.Text(buds, "Label", "EARBUDS", UIKit.Mono, 14, UIKit.SystemCyan, TextAlignmentOptions.Center);
            var song = Label(music, "Main Character", UIKit.Sans, 27, Color.white, new Vector2(0f, 1f), new Vector2(136f, -44f), new Vector2(240f, 34f), TextAlignmentOptions.Left);
            song.fontStyle = FontStyles.Bold;
            Label(music, "Nobody in Particular", UIKit.Sans, 20, new Color(1f, 1f, 1f, 0.7f), new Vector2(0f, 1f), new Vector2(136f, -80f), new Vector2(240f, 28f), TextAlignmentOptions.Left);
            // thin bars are plain rects: a 9-slice sprite smaller than its own borders renders as dashes
            var track = UIKit.Rect(music, "Track", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(360f, 5f), new Vector2(0f, -142f));
            UIKit.Image(track, "Bg", null, new Color(1f, 1f, 1f, 0.22f), false);
            _progress = UIKit.Image(track, "Fill", null, new Color(1f, 1f, 1f, 0.92f), false);
            _progress.rectTransform.anchorMax = new Vector2(0f, 1f);
            _elapsed = Label(music, "", UIKit.Mono, 15, new Color(1f, 1f, 1f, 0.65f), new Vector2(0f, 1f), new Vector2(16f, -152f), new Vector2(80f, 20f), TextAlignmentOptions.Left);
            _remain = Label(music, "", UIKit.Mono, 15, new Color(1f, 1f, 1f, 0.65f), new Vector2(1f, 1f), new Vector2(-16f, -152f), new Vector2(80f, 20f), TextAlignmentOptions.Right);
            for (int i = 0; i < 2; i++)
            {
                var bar = UIKit.Rect(music, "Pause" + i, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(8f, 28f), new Vector2(i == 0 ? -9f : 9f, 18f));
                UIKit.Image(bar, "Bg", null, Color.white, false);
            }
            var home = UIKit.Rect(screen, "Home", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(130f, 5f), new Vector2(0f, 12f));
            UIKit.Image(home, "Bg", null, new Color(1f, 1f, 1f, 0.8f), false);
            _glare = UIKit.Image(screen, "Glare", null, new Color(1f, 0.98f, 0.9f, 0f), false);
        }

        void BuildHeadlights()
        {
            _lampL = UIKit.Rect(transform, "LampL", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 900f), Vector2.zero);
            _lampImgL = UIKit.Image(_lampL, "Glow", UIKit.Glow, new Color(1f, 0.97f, 0.86f, 0f), false);
            _lampR = UIKit.Rect(transform, "LampR", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 900f), Vector2.zero);
            _lampImgR = UIKit.Image(_lampR, "Glow", UIKit.Glow, new Color(1f, 0.97f, 0.86f, 0f), false);
            _coreL = Core(_lampL);
            _coreR = Core(_lampR);
            _streak = UIKit.Rect(transform, "Streak", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), Vector2.zero);
            _streakImg = UIKit.Image(_streak, "Glow", UIKit.Glow, new Color(1f, 0.98f, 0.9f, 0f), false);
            var wash = UIKit.Rect(transform, "Wash", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4200f, 4200f), new Vector2(-200f, 0f));
            _wash = UIKit.Image(wash, "Glow", UIKit.Glow, new Color(1f, 0.98f, 0.92f, 0f), false);
        }

        /// <summary>A lamp's hot white centre.</summary>
        static Image Core(RectTransform lamp)
        {
            var img = UIKit.Image(lamp, "Core", UIKit.Glow, new Color(1f, 1f, 1f, 0f), false);
            img.rectTransform.anchorMin = new Vector2(0.32f, 0.32f);
            img.rectTransform.anchorMax = new Vector2(0.68f, 0.68f);
            return img;
        }

        static void Round(Image img, float multiplier)
        {
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = multiplier;
        }

        static void Blob(RectTransform parent, Vector2 pos, float size, Color c)
        {
            var r = UIKit.Rect(parent, "Blob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size, size), pos);
            UIKit.Image(r, "Glow", UIKit.Glow, c, false);
        }

        static TextMeshProUGUI Label(RectTransform parent, string text, TMP_FontAsset font, float size, Color c, Vector2 anchor,
            Vector2 pos, Vector2 box, TextAlignmentOptions align)
        {
            var r = UIKit.Rect(parent, "Text", anchor, anchor, box, pos);
            var t = UIKit.Text(r, "T", text, font, size, c, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        // ------------------------------------------------------------------------------------------------------- clock
        void Update()
        {
            if (ManualClock || Current == Phase.Done) return;
            var input = HS.Core.GameInput.Instance;
            bool press = (input.Confirm.enabled && input.Confirm.WasPressedThisFrame()) ||
                         (input.Cancel.enabled && input.Cancel.WasPressedThisFrame()) ||
                         (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame);
            if (press) PressSkip();
            Advance(Time.unscaledDeltaTime);
        }

        /// <summary>First press arms the skip ("click again to skip"); a second press within 2.5 s skips.</summary>
        public void PressSkip()
        {
            if (Current == Phase.Done) return;
            if (SkipArmed)
            {
                Finish(true);
                return;
            }
            _skipArmedUntil = _real + 2.5f;
        }

        public void Advance(float dt)
        {
            if (Current == Phase.Done) return;
            _real += dt;
            if (Current == Phase.PreRoll)
            {
                // The world builds and its shaders warm up behind black during the first frames (a one-off hitch of
                // ~1–2 s in the player build): count only smooth frames.
                _pre += Mathf.Min(dt, 0.05f);
                if (_pre >= PreRoll) Begin();
                Render();
                return;
            }
            _prevT = _t;
            _t += Mathf.Min(dt, 0.1f);
            if (_dsp0 > 0)
            {
                // The picture follows the audio clock, while there is one: with no audio device it never moves, and
                // following it would freeze the opening for good.
                double now = AudioClock();
                if (now > _lastDsp)
                {
                    _lastDsp = now;
                    _dspStill = 0f;
                    float a = (float)(now - _dsp0);
                    if (Mathf.Abs(a - _t) > 0.06f) _t = a;
                }
                else if ((_dspStill += dt) > 0.3f) _dsp0 = 0;
            }
            Render();
            Cues();
            if (_t >= _endAt) Finish(false);
        }

        void Begin()
        {
            Current = _short ? Phase.White : Phase.Walk;
            _t = _prevT = 0f;
            var a = AudioDirector.Instance;
            if (a == null || ManualClock) return;
            const double lead = 0.1; // schedule slightly ahead so every voice starts on its exact sample
            double d0 = AudioClock() + lead;
            if (_short)
            {
                if (a.Schedule("syn_open_sting", d0, 0, StingVol) == null) return;
            }
            else
            {
                _song = a.Schedule("syn_open_song", d0, d0 + CutAt, SongVol);
                if (_song == null) return;
                a.Schedule("syn_open_street", d0, d0 + CutAt, StreetVol);
                a.Schedule("syn_open_horn", d0 + HornAt, d0 + CutAt, HornVol);
                a.Schedule("syn_open_ring", d0 + CutAt, 0, RingVol);
            }
            FollowAudio(d0);
            _t = _prevT = -(float)lead;
        }

        void Finish(bool skipped)
        {
            if (Current == Phase.Done) return;
            Current = Phase.Done;
            var a = AudioDirector.Instance;
            if (skipped && a != null) a.StopCutscene();
            Done?.Invoke();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Current == Phase.Done) return;
            var a = AudioDirector.Instance;
            if (a != null) a.StopCutscene();
        }

        bool Crossed(float at) => _prevT < at && _t >= at;

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        // ----------------------------------------------------------------------------------------------------- render
        void Render()
        {
            float t = _t;
            bool started = Current != Phase.PreRoll;
            if (started)
            {
                if (_short) Current = t < _sys0 ? Phase.White : Phase.System;
                else Current = t < CutAt ? Phase.Walk : t < _sys0 ? Phase.White : Phase.System;
            }
            if (_phone != null) RenderPhone(started ? t : -1f);
            RenderLights(started ? t : -1f);
            float cut = _short ? ShortFlash : CutAt, hold = _short ? ShortFlash + 0.05f : WhiteUntil, black = _short ? ShortFlash + 0.35f : BlackAt;
            float white = !started || t < cut ? 0f : t < hold ? 1f : 1f - Smooth(hold, black, t);
            _white.color = new Color(0.98f, 0.98f, 1f, white);
            _roll.Render(started ? t : -1f);
            bool climax = started && !_short && t >= HornAt && t < _sys0;
            if (_real > 1.5f && Current != Phase.White && !climax)
                _foot.text = SkipArmed ? "<color=#FFFFFFDD>click again to skip</color>" : "click to skip";
            else _foot.text = "";
        }

        void RenderPhone(float t)
        {
            if (t >= CutAt)
            {
                _phone.gameObject.SetActive(false);
                return;
            }
            float k = (t - StepAt) / Step;
            float walking = t < 0f ? 0f : 1f - Smooth(StepsEnd, StepsEnd + 0.5f, t);
            float since = (k - Mathf.Floor(k)) * Step;                     // seconds since the last footfall
            float dip = t >= StepAt ? Mathf.Exp(-since / 0.09f) * 9f : 0f;
            float drop = Smooth(LookUpAt, LookUpAt + 0.9f, t);
            drop *= drop;                                                  // lowered: looking up into the light
            float rise = 1f - Smooth(0f, 0.8f, t);
            _phone.anchoredPosition = new Vector2(Mathf.Sin(Mathf.PI * k) * 7f * walking,
                (-dip + Mathf.Sin(2f * Mathf.PI * k) * 2f) * walking - 20f - rise * 60f - drop * 1150f);
            _phone.localRotation = Quaternion.Euler(0f, 0f, -3f + Mathf.Sin(Mathf.PI * k + 0.6f) * 0.8f * walking - drop * 14f);
            _phoneGroup.alpha = Mathf.Max(Smooth(0f, 0.8f, t), WarmAlpha);
            Steps = t < StepAt ? 0 : Mathf.Min(Mathf.FloorToInt((Mathf.Min(t, StepsEnd) - StepAt) / Step + 1e-4f) + 1, MaxSteps);
            _steps.text = string.Format(CultureInfo.InvariantCulture, "{0:N0} steps", StepsBefore + Steps);
            int pos = TrackPos + Mathf.Max(0, Mathf.FloorToInt(t));
            _elapsed.text = Clock(pos);
            _remain.text = "-" + Clock(TrackLength - pos);
            _progress.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((TrackPos + Mathf.Max(0f, t)) / TrackLength), 1f);
            NotificationsShown = 0;
            for (int i = 0; i < Notes.Length; i++)
            {
                float a = Smooth(Notes[i].at, Notes[i].at + 0.35f, t);
                if (t >= Notes[i].at) NotificationsShown++;
                _cardGroups[i].alpha = Mathf.Max(a, WarmAlpha);
                _cards[i].localScale = Vector3.one * (0.92f + 0.08f * a); // grows in place: sliding crossed the card above
            }
            _glare.color = new Color(1f, 0.98f, 0.9f, 0.55f * Smooth(HornAt + 1.4f, LookUpAt + 0.6f, t));
            if (_song != null) _song.volume = Mathf.Lerp(SongVol, SongDucked, Mathf.InverseLerp(LookUpAt, CutAt, t)); // the horn drowns it out
        }

        void RenderLights(float t)
        {
            float p = t < 0f ? 0f : _short ? Smooth(0f, ShortFlash, t) : Mathf.Pow(Smooth(HornAt + 1.2f, CutAt, t), 1.6f);
            if (t >= (_short ? ShortFlash : CutAt)) p = 0f;                // gone with the cut
            // Far off: two small bright lamps and a streak in the dark; close: they grow apart and flood the frame.
            float spread = Mathf.Lerp(34f, 560f, p), size = Mathf.Lerp(130f, 1700f, p * p);
            float x = Mathf.Lerp(-170f, -640f, p);
            _lampL.anchoredPosition = new Vector2(x - spread, -60f);
            _lampR.anchoredPosition = new Vector2(x + spread, -60f);
            _lampL.sizeDelta = _lampR.sizeDelta = new Vector2(size, size);
            var lamp = new Color(1f, 0.97f, 0.86f, p <= 0f ? 0f : Mathf.Clamp01(0.35f + p * 1.8f));
            _lampImgL.color = _lampImgR.color = lamp;
            _coreL.color = _coreR.color = new Color(1f, 1f, 1f, lamp.a);
            _streak.anchoredPosition = new Vector2(x, -60f);
            _streak.sizeDelta = new Vector2(Mathf.Lerp(500f, 3400f, p), Mathf.Lerp(40f, 150f, p));
            _streakImg.color = new Color(1f, 0.98f, 0.9f, 0.6f * Mathf.Clamp01(p * 2f));
            _wash.color = new Color(1f, 0.98f, 0.92f, 0.95f * Mathf.Pow(p, 2.5f));
        }

        /// <summary>The status window's sounds (the walk's audio is all in the scheduled clips).</summary>
        void Cues()
        {
            if (AudioDirector.Instance == null || Current != Phase.System) return;
            _roll.Cues(_prevT, _t);
        }

        static string Clock(int s) => (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
