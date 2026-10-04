using System.Collections.Generic;
using HS.Hero;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>The dotted line of his route ahead (Map Sketch). Presentation only.</summary>
    public sealed class PathSketch : MonoBehaviour
    {
        HeroAgent _hero;
        float _until;
        LineRenderer _line;
        static PathSketch _current;

        public static PathSketch Current => _current;
        public bool Showing => _line != null && _line.enabled;

        public static void Show(HeroAgent hero, float seconds)
        {
            if (_current == null)
            {
                _current = new GameObject("PathSketch").AddComponent<PathSketch>();
                _current._line = _current.gameObject.AddComponent<LineRenderer>();
                var l = _current._line;
                l.widthMultiplier = 0.18f;
                l.material = new Material(Shader.Find("Sprites/Default"));
                l.startColor = l.endColor = new Color(0.48f, 0.9f, 1f, 0.8f);
                l.textureMode = LineTextureMode.Tile;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _current._hero = hero;
            _current._until = Time.time + seconds;
            _current._line.enabled = true;
        }

        void LateUpdate()
        {
            if (_hero == null || Time.time > _until)
            {
                _line.enabled = false;
                return;
            }
            var pts = new List<Vector3> { _hero.Position + Vector3.up * 0.15f };
            var nodes = _hero.Route.Nodes;
            for (int i = Mathf.Max(0, _hero.Route.Index); i < nodes.Count && pts.Count < 24; i++) pts.Add(nodes[i].Position + Vector3.up * 0.15f);
            _line.positionCount = pts.Count;
            _line.SetPositions(pts.ToArray());
            // dashes: fade alternate segments by colour gradient is overkill; a gently pulsing alpha reads as "sketch"
            var col = new Color(0.48f, 0.9f, 1f, 0.55f + 0.25f * Mathf.Sin(Time.time * 6f));
            _line.startColor = _line.endColor = col;
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }
}
