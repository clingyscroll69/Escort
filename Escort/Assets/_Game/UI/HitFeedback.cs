using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Presentation;
using TMPro;
using UnityEngine;

namespace HS.UI
{
    /// <summary>
    /// Makes every hit on an enemy unmistakable: the body flashes white, a burst of streaks marks the impact, the damage
    /// pops up over its head (white for Callum's blows, ochre for the sidekick's, grey for traps and strays), and a small
    /// HP bar appears over anyone wounded (its pale ghost shows the chunk the last hit took). Presentation only: it listens
    /// to the event bus and never feeds back into the simulation.
    /// </summary>
    public sealed class HitFeedback : MonoBehaviour
    {
        sealed class Number
        {
            public TextMeshProUGUI Text;
            public Vector3 Anchor;
            public float Age, Pop, Drift;
        }

        sealed class Bar
        {
            public UIBar View;
            public CanvasGroup Group;
            public float SinceHit;
        }

        sealed class Flash
        {
            public Renderer[] Renderers;
            public float T, Duration;
        }

        static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly Color FlashTint = new Color(1f, 0.97f, 0.92f, 1f);
        public static readonly Color HeroHit = Color.white;
        public static readonly Color SidekickHit = UIKit.Ochre;
        public static readonly Color OtherHit = new Color32(196, 202, 212, 255);

        // Screen offsets above the head: the camera looks steeply down, so a world-space lift barely clears the hair.
        const float BarLift = 20f, NumberLift = 48f, NumberRise = 80f;
        const float NumberLife = 0.95f, FlashPeak = 0.85f;
        const float BarFresh = 3f, BarRestAlpha = 0.6f, BarDeathHold = 0.7f, BarFadeOut = 0.35f;
        const int MaxNumbers = 24;
        static readonly float[] Drifts = { 0f, 24f, -24f, 12f, -12f };

        UIRoot _root;
        HudView _hud;
        RunContext _ctx;
        RectTransform _barLayer, _numberLayer;
        readonly List<Number> _live = new List<Number>();
        readonly Stack<Number> _pool = new Stack<Number>();
        readonly Dictionary<EnemyAgent, Bar> _bars = new Dictionary<EnemyAgent, Bar>();
        readonly Dictionary<EnemyAgent, Flash> _flashes = new Dictionary<EnemyAgent, Flash>();
        readonly List<EnemyAgent> _scratch = new List<EnemyAgent>();
        MaterialPropertyBlock _mpb;
        int _seq;

        /// <param name="hud">Its boss already has the big bar at the top of the screen, so he gets no overhead one.</param>
        public static HitFeedback Create(UIRoot root, HudView hud)
        {
            var v = root.gameObject.AddComponent<HitFeedback>();
            v._root = root;
            v._hud = hud;
            // Bars under the speech bubbles; numbers over everything in the layer (they only live a second).
            v._barLayer = UIKit.Stretch(root.World, "EnemyBars");
            v._numberLayer = UIKit.Stretch(root.World, "DamageNumbers");
            v._mpb = new MaterialPropertyBlock();
            v.Rehook();
            return v;
        }

        // ------------------------------------------------------------------ test/QA probes
        /// <summary>Damage numbers on screen now, oldest first.</summary>
        public IEnumerable<TMP_Text> Numbers
        {
            get { foreach (var n in _live) yield return n.Text; }
        }

        public bool BarShown(Agent a) => a is EnemyAgent e && _bars.TryGetValue(e, out var b) && b.View.Root.gameObject.activeSelf && b.Group.alpha > 0.01f;
        public float BarValue(Agent a) => a is EnemyAgent e && _bars.TryGetValue(e, out var b) ? b.View.Value : -1f;
        public bool Flashing(Agent a) => a is EnemyAgent e && _flashes.ContainsKey(e);

        // ------------------------------------------------------------------ events
        void Update() => Rehook();

        void OnDestroy() => Unhook();

        void Rehook()
        {
            var ctx = RunContext.Current;
            if (ctx == _ctx) return;
            Unhook();
            _ctx = ctx;
            if (_ctx != null) _ctx.Events.Damage += OnDamage;
        }

        void Unhook()
        {
            if (_ctx == null) return;
            _ctx.Events.Damage -= OnDamage;
            _ctx = null;
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (!(d.Target is EnemyAgent e) || e.IsHidden || applied <= 0f) return;
            bool kill = !e.IsAlive;
            bool big = kill || d.Kind == DamageKind.Heavy || d.Tag == "riposte";
            StartFlash(e, big ? 0.22f : 0.15f);
            Vfx.Burst(VfxKind.Hit, ImpactPoint(e, d), big ? 1.4f : 1f);
            ShowNumber(e, applied, ColorFor(d), kill ? 1.4f : big ? 1.2f : 1f);
            if (!_bars.TryGetValue(e, out var bar)) _bars[e] = bar = MakeBar();
            bar.SinceHit = 0f;
            bar.View.Set(e.Health.Fraction);
        }

        public static Color ColorFor(DamageInfo d)
        {
            if (d.Source == null) return OtherHit;
            if (d.Source.Faction == Faction.Hero) return HeroHit;
            return d.FromSidekick ? SidekickHit : OtherHit;
        }

        static float HeadHeight(Agent a) => a.Controller != null ? a.Controller.height : 1.8f;

        /// <summary>Chest height on the side facing whoever struck (the camera for traps and strays).</summary>
        static Vector3 ImpactPoint(Agent a, DamageInfo d)
        {
            var chest = a.Position + Vector3.up * HeadHeight(a) * 0.68f;
            var cam = Camera.main;
            var toward = d.Source != null ? d.Source.Position : cam != null ? cam.transform.position : chest;
            return chest + Geo.DirTo(chest, toward) * (a.Radius + 0.05f);
        }

        // ------------------------------------------------------------------ flash
        void StartFlash(EnemyAgent e, float duration)
        {
            if (!_flashes.TryGetValue(e, out var f))
            {
                var rs = new List<Renderer>();
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(FlashAmountId)) rs.Add(r);
                _flashes[e] = f = new Flash { Renderers = rs.ToArray() };
            }
            f.T = f.Duration = duration;
            ApplyFlash(f, FlashPeak);
        }

        /// <summary>
        /// The flash owns the property block of an enemy's toon renderers (nothing else sets one) and removes it when
        /// done, so the renderers go back to the SRP Batcher.
        /// </summary>
        void ApplyFlash(Flash f, float amount)
        {
            foreach (var r in f.Renderers)
            {
                if (r == null) continue;
                if (amount <= 0f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                _mpb.Clear();
                _mpb.SetColor(FlashColorId, FlashTint);
                _mpb.SetFloat(FlashAmountId, amount);
                r.SetPropertyBlock(_mpb);
            }
        }

        void TickFlashes(float dt)
        {
            _scratch.Clear();
            foreach (var kv in _flashes)
            {
                var f = kv.Value;
                f.T -= dt;
                // Full white for the first third, then fade.
                float amount = f.T > 0f ? FlashPeak * Mathf.Clamp01(f.T / (f.Duration * 0.67f)) : 0f;
                ApplyFlash(f, kv.Key != null ? amount : 0f);
                if (amount <= 0f || kv.Key == null) _scratch.Add(kv.Key);
            }
            foreach (var e in _scratch) _flashes.Remove(e);
        }

        // ------------------------------------------------------------------ numbers
        void ShowNumber(EnemyAgent e, float applied, Color color, float scale)
        {
            if (_live.Count >= MaxNumbers) Recycle(0);
            var n = _pool.Count > 0 ? _pool.Pop() : MakeNumber();
            n.Text.gameObject.SetActive(true);
            n.Text.text = Mathf.Max(1, Mathf.RoundToInt(applied)).ToString();
            n.Text.color = color;
            n.Anchor = e.Position + Vector3.up * HeadHeight(e);
            n.Age = 0f;
            n.Pop = scale;
            n.Drift = Drifts[_seq++ % Drifts.Length];
            n.Text.transform.SetAsLastSibling();
            _live.Add(n);
            PlaceNumber(n);
        }

        Number MakeNumber()
        {
            var t = UIKit.Text(_numberLayer, "Damage", "", UIKit.Sans, 38, Color.white, TextAlignmentOptions.Center);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(160f, 50f);
            t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Outline(t, 0.3f);
            return new Number { Text = t };
        }

        void Recycle(int i)
        {
            var n = _live[i];
            _live.RemoveAt(i);
            n.Text.gameObject.SetActive(false);
            _pool.Push(n);
        }

        void PlaceNumber(Number n)
        {
            float k = Mathf.Clamp01(n.Age / NumberLife);
            if (!_root.WorldToLayer(n.Anchor, _root.World, out var p))
            {
                n.Text.alpha = 0f;
                return;
            }
            float rise = 1f - (1f - k) * (1f - k) * (1f - k); // ease out
            n.Text.rectTransform.anchoredPosition = p + new Vector2(n.Drift * rise, NumberLift + NumberRise * rise);
            // Pop in large, settle, then fade over the last third.
            float pop = Mathf.Lerp(n.Pop * 1.45f, n.Pop, Mathf.Clamp01(n.Age / 0.12f));
            n.Text.rectTransform.localScale = Vector3.one * pop;
            n.Text.alpha = Mathf.Clamp01((1f - k) / 0.35f);
        }

        // ------------------------------------------------------------------ bars
        Bar MakeBar()
        {
            var v = new UIBar(_barLayer, "EnemyHp", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(84f, 10f), Vector2.zero, UIKit.HpRed, false);
            return new Bar { View = v, Group = v.Root.gameObject.AddComponent<CanvasGroup>() };
        }

        void TickBars(float dt)
        {
            var boss = _hud != null ? _hud.Boss : null;
            _scratch.Clear();
            foreach (var kv in _bars)
            {
                var e = kv.Key;
                var b = kv.Value;
                if (e == null)
                {
                    _scratch.Add(e);
                    continue;
                }
                b.SinceHit += dt;
                b.View.Set(e.Health.Fraction);
                b.View.Tick(dt);
                // Dead: hold a moment so the ghost shows the killing blow, then fade out for good.
                float alpha = e.IsAlive
                    ? (b.SinceHit < BarFresh ? 1f : BarRestAlpha)
                    : 1f - Mathf.Clamp01((b.SinceHit - BarDeathHold) / BarFadeOut);
                if (alpha <= 0f)
                {
                    _scratch.Add(e);
                    continue;
                }
                var p = Vector2.zero;
                bool show = !e.IsHidden && e.State != EnemyState.Spared && e != boss
                            && _root.WorldToLayer(e.Position + Vector3.up * HeadHeight(e), _root.World, out p);
                b.View.Root.gameObject.SetActive(show);
                if (!show) continue;
                b.View.Root.anchoredPosition = p + new Vector2(0f, BarLift);
                b.Group.alpha = alpha;
            }
            foreach (var e in _scratch)
            {
                if (_bars.TryGetValue(e, out var b) && b.View.Root != null) Destroy(b.View.Root.gameObject);
                _bars.Remove(e);
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (_numberLayer.GetSiblingIndex() != _root.World.childCount - 1) _numberLayer.SetAsLastSibling();
            TickFlashes(dt);
            TickBars(dt);
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var n = _live[i];
                n.Age += dt;
                if (n.Age >= NumberLife) Recycle(i);
                else PlaceNumber(n);
            }
        }
    }
}
