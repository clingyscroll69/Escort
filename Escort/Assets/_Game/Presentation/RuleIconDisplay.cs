using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Presentation
{
    /// <summary>
    /// Icon over the hero's head showing the active rule (GDD §4.2 Behavior, §9 HUD). Billboarded, constant on-screen
    /// size, pops when the rule changes so behaviour changes are always visible (fairness contract §11.3.1).
    /// </summary>
    public sealed class RuleIconDisplay : MonoBehaviour
    {
        public Func<string> IconSource;
        public float PixelSize = 40f;
        public Color Tint = new Color(1f, 0.97f, 0.9f, 1f);

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        SpriteRenderer _sr;
        string _current;
        float _pop;
        Camera _cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => Cache.Clear();

        public static Sprite Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!Cache.TryGetValue(id, out var s))
            {
                s = Resources.Load<Sprite>("Icons/" + id);
                if (s == null) Debug.LogWarning($"[RuleIconDisplay] missing icon sprite 'Icons/{id}' (is it imported as Sprite?)");
                Cache[id] = s;
            }
            return s;
        }

        void Awake()
        {
            var go = new GameObject("RuleIcon");
            go.transform.SetParent(transform, false);
            _sr = go.AddComponent<SpriteRenderer>();
            _sr.color = Tint;
            _sr.sortingOrder = 100;
            _sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LateUpdate()
        {
            string id = IconSource?.Invoke();
            if (id != _current)
            {
                _current = id;
                _sr.sprite = Load(id);
                _pop = 1f;
            }
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || _sr.sprite == null) return;
            var t = _sr.transform;
            t.rotation = _cam.transform.rotation;
            float dist = Vector3.Distance(_cam.transform.position, t.position);
            float worldPerPixel = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime * 3f);
            float spriteUnits = _sr.sprite.bounds.size.y;
            float scale = PixelSize * worldPerPixel / Mathf.Max(0.01f, spriteUnits) * (1f + 0.35f * _pop);
            t.localScale = Vector3.one * scale;
        }
    }
}
