using System.Collections.Generic;
using UnityEngine;

namespace HS.Opening
{
    /// <summary>Accumulates quads/fans into one mesh with a submesh per material (the street's procedural ground).</summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int>[] _tris;

        public MeshKit(int submeshes)
        {
            _tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) _tris[i] = new List<int>();
        }

        /// <summary>A quad a-b-c-d (clockwise seen from the front, Unity's winding), flat normal.</summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _n.Add(n); _n.Add(n); _n.Add(n); _n.Add(n);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            var t = _tris[sub];
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>A horizontal rectangle at height y, UVs in world metres / uvScale (planar, so pieces tile seamlessly).</summary>
        public void Flat(int sub, float x0, float x1, float z0, float z1, float y, float uvScale)
        {
            Vector2 U(float x, float z) => new Vector2(x / uvScale, z / uvScale);
            Quad(sub, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0),
                U(x0, z0), U(x0, z1), U(x1, z1), U(x1, z0));
        }

        /// <summary>A vertical wall from p to q (outward normal to the right of p→q seen from above), y0..y1.</summary>
        public void Wall(int sub, Vector2 p, Vector2 q, float y0, float y1, float uvScale)
        {
            float len = Vector2.Distance(p, q);
            var a = new Vector3(p.x, y0, p.y);
            var b = new Vector3(p.x, y1, p.y);
            var c = new Vector3(q.x, y1, q.y);
            var d = new Vector3(q.x, y0, q.y);
            float u0 = (p.x + p.y) / uvScale, u1 = u0 + len / uvScale;
            Quad(sub, a, b, c, d, new Vector2(u0, y0 / uvScale), new Vector2(u0, y1 / uvScale), new Vector2(u1, y1 / uvScale), new Vector2(u1, y0 / uvScale));
        }

        /// <summary>A flat fan (the top of a rounded curb return): centre + arc from angle a0 to a1 (degrees, ccw from +x).</summary>
        public void Fan(int sub, Vector2 centre, float radius, float a0, float a1, float y, float uvScale, int segments)
        {
            int i0 = _v.Count;
            _v.Add(new Vector3(centre.x, y, centre.y));
            _n.Add(Vector3.up);
            _uv.Add(centre / uvScale);
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, s / (float)segments);
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                _v.Add(new Vector3(p.x, y, p.y));
                _n.Add(Vector3.up);
                _uv.Add(p / uvScale);
            }
            var t = _tris[sub];
            for (int s = 0; s < segments; s++)
            {
                // ccw angles seen from above → clockwise winding needs (centre, next, this)
                t.Add(i0); t.Add(i0 + 2 + s); t.Add(i0 + 1 + s);
            }
        }

        /// <summary>The curb face along an arc (outward), y0..y1.</summary>
        public void ArcWall(int sub, Vector2 centre, float radius, float a0, float a1, float y0, float y1, float uvScale, int segments)
        {
            for (int s = 0; s < segments; s++)
            {
                float aa = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, s / (float)segments);
                float ab = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (s + 1) / (float)segments);
                var p = centre + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * radius;
                var q = centre + new Vector2(Mathf.Cos(ab), Mathf.Sin(ab)) * radius;
                // ccw along the arc, so the outward side is to the right of p→q
                Wall(sub, p, q, y0, y1, uvScale);
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.subMeshCount = _tris.Length;
            for (int i = 0; i < _tris.Length; i++) m.SetTriangles(_tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}
