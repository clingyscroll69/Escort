using HS.Core;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// The Oath Glyph circle on the arena floor (GDD §6.1 boss). Dim until the terms are sworn, then it glows; it flares
    /// red while the sidekick stands inside it during the duel — the visual for "no aid".
    /// </summary>
    public sealed class OathGlyph : MonoBehaviour
    {
        public float Radius = 5f;
        public bool Sworn;
        MeshRenderer _mr;
        MaterialPropertyBlock _mpb;
        float _t, _flare;

        public static OathGlyph Create(Transform at, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "OathGlyph";
            go.transform.SetParent(at, false);
            go.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * radius * 2.1f;
            var g = go.AddComponent<OathGlyph>();
            g.Radius = radius;
            g._mr = go.GetComponent<MeshRenderer>();
            g._mr.sharedMaterial = Resources.Load<Material>("FX/FX_OathGlyph");
            g._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g._mr.receiveShadows = false;
            g._mpb = new MaterialPropertyBlock();
            return g;
        }

        public bool Contains(Vector3 p) => Geo.FlatDistance(p, transform.position) <= Radius;

        void LateUpdate()
        {
            _t += Time.deltaTime;
            var ctx = RunContext.Current;
            bool intruder = Sworn && ctx != null && ctx.Sidekick != null && ctx.Sidekick.IsAlive && Contains(ctx.Sidekick.Position);
            _flare = Mathf.MoveTowards(_flare, intruder ? 1f : 0f, Time.deltaTime * 4f);
            float pulse = Sworn ? 0.75f + 0.25f * Mathf.Sin(_t * 2.4f) : 0.28f;
            var gold = new Color(1f, 0.82f, 0.45f, 0.85f) * pulse;
            var red = new Color(1f, 0.25f, 0.2f, 0.95f);
            _mpb.SetColor("_Color", Color.Lerp(gold, red, _flare * (0.6f + 0.4f * Mathf.Sin(_t * 12f))));
            _mr.SetPropertyBlock(_mpb);
        }
    }
}
