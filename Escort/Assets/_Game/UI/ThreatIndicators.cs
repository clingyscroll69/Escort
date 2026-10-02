using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// Red chevrons on the screen edge pointing at off-screen shooters who are drawing on someone (aiming) or just
    /// loosed — arrows must never come "from nowhere" (GDD §11.3 attributable losses).
    /// </summary>
    public sealed class ThreatIndicators : MonoBehaviour
    {
        UIRoot _root;
        readonly List<RectTransform> _pool = new List<RectTransform>();
        readonly Dictionary<EnemyAgent, float> _recent = new Dictionary<EnemyAgent, float>();
        const float Inset = 70f, Linger = 1.2f;

        public static ThreatIndicators Create(UIRoot root)
        {
            var v = UIKit.Stretch(root.Hud, "Threats").gameObject.AddComponent<ThreatIndicators>();
            v._root = root;
            return v;
        }

        RectTransform Get(int i)
        {
            while (_pool.Count <= i)
            {
                var rt = UIKit.Rect(transform, "Chevron" + _pool.Count, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46f, 40f), Vector2.zero);
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = UIKit.Tail;
                img.color = UIKit.Danger;
                img.raycastTarget = false;
                var glow = UIKit.Rect(rt, "Glow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110f, 110f), Vector2.zero);
                var gi = glow.gameObject.AddComponent<Image>();
                gi.sprite = UIKit.Glow;
                gi.color = new Color(1f, 0.25f, 0.2f, 0.35f);
                gi.raycastTarget = false;
                glow.SetAsFirstSibling();
                _pool.Add(rt);
            }
            return _pool[i];
        }

        RectTransform _ally;
        TMPro.TextMeshProUGUI _allyLabel;

        /// <summary>The hero off screen (camera leash): a gold marker on the edge with his name and HP.</summary>
        void UpdateAlly(Camera cam, Vector2 size, Vector2 half)
        {
            var hero = RunContext.Current != null ? RunContext.Current.Hero : null;
            bool show = false;
            if (cam != null && hero != null && hero.IsAlive)
            {
                var vp = cam.WorldToViewportPoint(hero.Position + Vector3.up * 1.6f);
                var p = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
                if (vp.z < 0f) p = -p;
                show = vp.z < 0f || Mathf.Abs(p.x) > half.x || Mathf.Abs(p.y) > half.y;
                if (show)
                {
                    if (_ally == null)
                    {
                        _ally = UIKit.Rect(transform, "HeroMarker", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(54f, 46f), Vector2.zero);
                        var img = _ally.gameObject.AddComponent<Image>();
                        img.sprite = UIKit.Tail;
                        img.color = UIKit.Gold;
                        img.raycastTarget = false;
                        _allyLabel = UIKit.Text(transform, "HeroMarkerLabel", "", UIKit.Sans, 22, Color.white, TMPro.TextAlignmentOptions.Center);
                        _allyLabel.rectTransform.anchorMin = _allyLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        _allyLabel.rectTransform.sizeDelta = new Vector2(220f, 30f);
                        UIKit.Outline(_allyLabel);
                    }
                    float k = Mathf.Min(half.x / Mathf.Max(1e-3f, Mathf.Abs(p.x)), half.y / Mathf.Max(1e-3f, Mathf.Abs(p.y)));
                    var edge = p * k;
                    _ally.anchoredPosition = edge;
                    _ally.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg + 90f);
                    _allyLabel.rectTransform.anchoredPosition = edge - edge.normalized * 56f;
                    _allyLabel.text = $"CALLUM  <color=#FF564A>{Mathf.CeilToInt(hero.Health.Current)}</color>";
                }
            }
            if (_ally != null)
            {
                _ally.gameObject.SetActive(show);
                _allyLabel.gameObject.SetActive(show);
            }
        }

        void LateUpdate()
        {
            int used = 0;
            var cam = Camera.main;
            if (cam != null && RunContext.Current != null)
            {
                float now = Time.unscaledTime;
                var size = ((RectTransform)transform).rect.size;
                var half = size * 0.5f - new Vector2(Inset, Inset);
                var all = AgentRegistry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is EnemyAgent e) || !e.IsAlive || !e.IsRanged || e.IsHidden) continue;
                    if (e.IsAiming) _recent[e] = now + Linger;
                    if (!_recent.TryGetValue(e, out var until) || now > until) continue;
                    var vp = cam.WorldToViewportPoint(e.Position + Vector3.up * 1.2f);
                    bool behind = vp.z < 0f;
                    var p = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
                    if (behind) p = -p;
                    bool onScreen = !behind && Mathf.Abs(p.x) < half.x && Mathf.Abs(p.y) < half.y;
                    if (onScreen) continue;
                    // clamp to the inset rectangle along the ray from the centre
                    float k = Mathf.Min(half.x / Mathf.Max(1e-3f, Mathf.Abs(p.x)), half.y / Mathf.Max(1e-3f, Mathf.Abs(p.y)));
                    var edge = p * k;
                    var rt = Get(used++);
                    rt.gameObject.SetActive(true);
                    rt.anchoredPosition = edge;
                    float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                    rt.localRotation = Quaternion.Euler(0f, 0f, ang + 90f); // tail sprite points down (−Y)
                    float pulse = e.IsAiming ? 1f + 0.18f * Mathf.Sin(now * 14f) : 0.8f;
                    rt.localScale = Vector3.one * pulse;
                }
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);
            var sz = ((RectTransform)transform).rect.size;
            UpdateAlly(Camera.main, sz, sz * 0.5f - new Vector2(Inset, Inset));
        }
    }
}
