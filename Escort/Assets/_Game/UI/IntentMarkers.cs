using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Skills.Impl;
using TMPro;
using UnityEngine;

namespace HS.UI
{
    /// <summary>
    /// Read the Room's labels (GDD §5 Scholar): while the room is read, a chip over every foe in range says what he means to
    /// do — "AIMING", "FAKE SURRENDER", "AMBUSH" over a hidden man where he lies. Presentation only; reads the sim.
    /// </summary>
    public sealed class IntentMarkers : MonoBehaviour
    {
        UIRoot _root;
        RectTransform _layer;
        readonly Dictionary<EnemyAgent, TextMeshProUGUI> _chips = new Dictionary<EnemyAgent, TextMeshProUGUI>();
        readonly List<EnemyAgent> _gone = new List<EnemyAgent>();
        /// <summary>Foe → the label shown this frame (tests).</summary>
        public readonly Dictionary<EnemyAgent, string> Shown = new Dictionary<EnemyAgent, string>();

        public static IntentMarkers Create(UIRoot root)
        {
            var v = root.gameObject.AddComponent<IntentMarkers>();
            v._root = root;
            v._layer = UIKit.Stretch(root.World, "IntentMarkers");
            return v;
        }

        void LateUpdate()
        {
            Shown.Clear();
            var ctx = RunContext.Current;
            var read = ReadTheRoom.Get(ctx);
            bool on = read != null && read.Active(ctx);
            var all = AgentRegistry.All;
            if (on)
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is EnemyAgent e) || !read.Covers(ctx, e.Position)) continue;
                    var label = ReadTheRoom.IntentOf(e);
                    if (label != null) Shown[e] = label;
                }
            _gone.Clear();
            foreach (var kv in _chips)
                if (kv.Key == null || !Shown.ContainsKey(kv.Key)) _gone.Add(kv.Key);
            foreach (var e in _gone)
            {
                if (_chips[e] != null) Destroy(_chips[e].transform.parent.gameObject);
                _chips.Remove(e);
            }
            foreach (var kv in Shown)
            {
                if (!_chips.TryGetValue(kv.Key, out var chip) || chip == null)
                {
                    chip = UIKit.Chip(_layer, "Intent", kv.Value, kv.Key.IsHidden ? UIKit.Danger : UIKit.SystemCyan, 22f);
                    _chips[kv.Key] = chip;
                }
                if (chip.text != kv.Value)
                {
                    chip.text = kv.Value;
                    UIKit.FitChip(chip);
                }
                var rt = (RectTransform)chip.transform.parent;
                bool visible = _root.WorldToLayer(kv.Key.Position + Vector3.up * (kv.Key.IsHidden ? 1.2f : 2.5f), _layer, out var p);
                rt.gameObject.SetActive(visible);
                if (visible) rt.anchoredPosition = p + new Vector2(-rt.sizeDelta.x * 0.5f, 0f);
            }
        }
    }
}
