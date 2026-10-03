using System;
using UnityEngine;

namespace HS.Presentation
{
    /// <summary>
    /// Ground wedge showing what the hero can see (his witness cone). The stat is hidden but its effects are always
    /// visible (GDD pillar 5): the player can learn where "unseen" is. Flashes red when he catches you.
    /// </summary>
    public sealed class WitnessConeView : MonoBehaviour
    {
        public Func<float> Angle;
        public Func<float> Range;
        public Func<bool> Visible;
        /// <summary>Stronger while the sidekick is up to something (sneaking, aiming a skill).</summary>
        public Func<bool> Emphasis;
        public Color Base = new Color(1f, 0.95f, 0.78f, 0.22f);
        public Color Emphasised = new Color(1f, 0.93f, 0.62f, 0.42f);
        public Color Alarm = new Color(1f, 0.25f, 0.2f, 0.55f);
        /// <summary>The tutorial is explaining this cone: it stands out (pulsing brighter) until switched off.</summary>
        public bool Spotlight;
        float _emph;
        const int Segments = 28;

        Mesh _mesh;
        MeshRenderer _mr;
        MaterialPropertyBlock _mpb;
        float _flash;
        Vector3[] _verts;
        float _lastAngle = -1f, _lastRange = -1f;

        void Awake()
        {
            var go = new GameObject("WitnessCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            _mesh = new Mesh { name = "WitnessCone" };
            _mesh.MarkDynamic();
            _verts = new Vector3[Segments + 2];
            var tris = new int[Segments * 3];
            var cols = new Color[Segments + 2];
            for (int i = 0; i < Segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            cols[0] = new Color(1, 1, 1, 0.95f);
            for (int i = 1; i < cols.Length; i++) cols[i] = new Color(1, 1, 1, 0.4f); // fades toward the rim
            var uvs = new Vector2[Segments + 2];
            for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(0.5f, 0.5f); // FX_Soft's opaque centre; vertex alpha does the fade
            _mesh.vertices = _verts;
            _mesh.triangles = tris;
            _mesh.colors = cols;
            _mesh.uv = uvs;
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = Resources.Load<Material>("FX/FX_Soft");
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mpb = new MaterialPropertyBlock();
        }

        public void Flash() => _flash = 1f;

        void LateUpdate()
        {
            bool vis = Visible == null || Visible();
            _mr.enabled = vis;
            if (!vis) return;
            float angle = Angle != null ? Angle() : 120f;
            float range = Range != null ? Range() : 12f;
            if (!Mathf.Approximately(angle, _lastAngle) || !Mathf.Approximately(range, _lastRange))
            {
                _lastAngle = angle;
                _lastRange = range;
                _verts[0] = Vector3.zero;
                for (int i = 0; i <= Segments; i++)
                {
                    float a = Mathf.Deg2Rad * (-angle * 0.5f + angle * i / Segments);
                    _verts[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * range;
                }
                _mesh.vertices = _verts;
                _mesh.RecalculateBounds();
            }
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 1.5f);
            _emph = Mathf.MoveTowards(_emph, Spotlight || (Emphasis != null && Emphasis()) ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            var col = Color.Lerp(Color.Lerp(Base, Emphasised, _emph), Alarm, _flash);
            if (Spotlight) col = Color.Lerp(col, new Color(1f, 0.97f, 0.85f, 0.6f), 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f));
            _mpb.SetColor("_Color", col);
            _mr.SetPropertyBlock(_mpb);
        }
    }
}
