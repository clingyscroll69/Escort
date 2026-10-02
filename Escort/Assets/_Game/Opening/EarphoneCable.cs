using System.Collections.Generic;
using UnityEngine;

namespace HS.Opening
{
    /// <summary>
    /// Wired earphones: a Y-shaped cable (jack → splitter → each ear) simulated as verlet ropes in small fixed steps and
    /// drawn as smoothed tubes. The jack end follows the phone's plug, the ear ends follow the head, the chest keeps the
    /// cable in front of the body; gravity and the walk do the rest. Simulated in the street root's space (small numbers).
    /// </summary>
    public sealed class EarphoneCable
    {
        const int MainN = 26, LeadN = 16, Sides = 6, Smooth = 3, Iterations = 18;
        // a little thicker than life so the cable reads in first person; enough slack to hang in a loop in front of you
        const float MainLen = 0.72f, LeadLen = 0.5f, MainRadius = 0.0021f, LeadRadius = 0.0016f, SubStep = 1f / 240f;

        /// <summary>Head-local ear positions (the earbuds sit just behind and below the eyes).</summary>
        public Vector3 EarL = new Vector3(-0.072f, -0.045f, -0.075f), EarR = new Vector3(0.072f, -0.045f, -0.075f);
        public Transform Splitter, Remote;

        readonly Transform _root;
        readonly Vector3[] _m = new Vector3[MainN], _mp = new Vector3[MainN];
        readonly Vector3[] _l = new Vector3[LeadN], _lp = new Vector3[LeadN];
        readonly Vector3[] _r = new Vector3[LeadN], _rp = new Vector3[LeadN];
        readonly Mesh _mesh;
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _norms = new List<Vector3>();
        readonly List<int> _tris = new List<int>();
        readonly List<Vector3> _path = new List<Vector3>();
        float _accum;
        bool _init;

        // anchors for the current step (root space)
        Vector3 _jack, _jackDir, _earL, _earR;
        Matrix4x4 _bodyToRoot, _rootToBody;

        public GameObject Object { get; }

        public EarphoneCable(Transform root, Material mat, int layer)
        {
            _root = root;
            Object = new GameObject("EarphoneCable") { layer = layer };
            Object.transform.SetParent(root, false);
            _mesh = new Mesh { name = "EarphoneCable" };
            _mesh.MarkDynamic();
            Object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = Object.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Lead points nearest the splitter/remote, for the little props riding on the cable.</summary>
        public Vector3 SplitterPos => _m[MainN - 1];

        /// <param name="jack">World position where the cable leaves the plug.</param>
        /// <param name="jackDir">World direction out of the plug.</param>
        /// <param name="head">The head (camera); ears are head-local.</param>
        /// <param name="body">The torso (chest collision), axes: +z forward, +y up, origin at the eyes.</param>
        public void Step(Vector3 jack, Vector3 jackDir, Transform head, Transform body, float dt)
        {
            _jack = _root.InverseTransformPoint(jack);
            _jackDir = _root.InverseTransformDirection(jackDir).normalized;
            _earL = _root.InverseTransformPoint(head.TransformPoint(EarL));
            _earR = _root.InverseTransformPoint(head.TransformPoint(EarR));
            _bodyToRoot = _root.worldToLocalMatrix * body.localToWorldMatrix;
            _rootToBody = _bodyToRoot.inverse;
            if (!_init) Reset();
            _accum += Mathf.Clamp(dt, 0f, 0.1f);
            int guard = 0;
            while (_accum >= SubStep && guard++ < 48)
            {
                _accum -= SubStep;
                Simulate(SubStep);
            }
            BuildMesh();
            if (Splitter != null)
            {
                Splitter.localPosition = _m[MainN - 1];
                var up = _m[MainN - 1] - _m[MainN - 3];
                if (up.sqrMagnitude > 1e-8) Splitter.localRotation = Quaternion.LookRotation(up.normalized, Vector3.up);
            }
            if (Remote != null)
            {
                int k = LeadN - 5; // ~13 cm below the right ear
                Remote.localPosition = _r[k];
                var along = _r[k + 1] - _r[k - 1];
                if (along.sqrMagnitude > 1e-8) Remote.localRotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            }
        }

        /// <summary>Lay the cable out hanging: jack down to a splitter at the chest, then up to each ear.</summary>
        void Reset()
        {
            var split = _bodyToRoot.MultiplyPoint3x4(new Vector3(0.0f, -0.36f, 0.16f));
            for (int i = 0; i < MainN; i++)
            {
                float u = i / (MainN - 1f);
                var p = Vector3.Lerp(_jack, split, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * 0.12f;
                _m[i] = _mp[i] = p;
            }
            for (int i = 0; i < LeadN; i++)
            {
                float u = i / (LeadN - 1f);
                _l[i] = _lp[i] = Vector3.Lerp(split, _earL, u);
                _r[i] = _rp[i] = Vector3.Lerp(split, _earR, u);
            }
            _init = true;
        }

        void Simulate(float h)
        {
            var g = Vector3.down * 9.81f * h * h;
            Verlet(_m, _mp, g);
            Verlet(_l, _lp, g);
            Verlet(_r, _rp, g);
            float segM = MainLen / (MainN - 1), segL = LeadLen / (LeadN - 1);
            for (int it = 0; it < Iterations; it++)
            {
                // pins: the plug (and the first segment leaves it straight), the earbuds
                _m[0] = _jack;
                _m[1] = _jack + _jackDir * segM;
                _l[LeadN - 1] = _earL;
                _r[LeadN - 1] = _earR;
                Chain(_m, segM, 2);
                Chain(_l, segL, 0);
                Chain(_r, segL, 0);
                // the splitter is one point shared by the three ropes
                var s = (_m[MainN - 1] * 2f + _l[0] + _r[0]) * 0.25f;
                _m[MainN - 1] = _l[0] = _r[0] = s;
                Chest(_m);
                Chest(_l);
                Chest(_r);
            }
        }

        static void Verlet(Vector3[] x, Vector3[] prev, Vector3 g)
        {
            for (int i = 0; i < x.Length; i++)
            {
                var v = (x[i] - prev[i]) * 0.985f; // air drag
                prev[i] = x[i];
                x[i] += v + g;
            }
        }

        /// <summary>Distance constraints; points below `firstFree` are pinned.</summary>
        static void Chain(Vector3[] x, float seg, int firstFree)
        {
            for (int i = 0; i < x.Length - 1; i++)
            {
                var d = x[i + 1] - x[i];
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                var corr = d * ((len - seg) / len);
                bool aPinned = i < firstFree, bPinned = i + 1 < firstFree;
                if (aPinned && bPinned) continue;
                if (aPinned) x[i + 1] -= corr;
                else if (bPinned) x[i] += corr;
                else
                {
                    x[i] += corr * 0.5f;
                    x[i + 1] -= corr * 0.5f;
                }
            }
        }

        /// <summary>Keep the cable in front of the chest/stomach (a slanted plane in torso space).</summary>
        void Chest(Vector3[] x)
        {
            for (int i = 0; i < x.Length; i++)
            {
                var b = _rootToBody.MultiplyPoint3x4(x[i]);
                if (b.y > -0.16f) continue;               // the neck: free
                float front = 0.15f + (-0.16f - b.y) * 0.25f; // a hoodie's chest, then the belly a little further out
                if (b.z < front)
                {
                    b.z = front;
                    x[i] = _bodyToRoot.MultiplyPoint3x4(b);
                }
            }
        }

        void BuildMesh()
        {
            _verts.Clear();
            _norms.Clear();
            _tris.Clear();
            Tube(_m, MainRadius);
            Tube(_l, LeadRadius);
            Tube(_r, LeadRadius);
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);
            _mesh.SetTriangles(_tris, 0);
            _mesh.bounds = new Bounds(_m[0], Vector3.one * 3f);
        }

        void Tube(Vector3[] pts, float radius)
        {
            // Catmull-Rom smoothing
            _path.Clear();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var p0 = pts[Mathf.Max(i - 1, 0)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                for (int s = 0; s < Smooth; s++)
                {
                    float t = s / (float)Smooth, t2 = t * t, t3 = t2 * t;
                    _path.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            _path.Add(pts[pts.Length - 1]);
            int start = _verts.Count;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < _path.Count; i++)
            {
                var tangent = (_path[Mathf.Min(i + 1, _path.Count - 1)] - _path[Mathf.Max(i - 1, 0)]).normalized;
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.up;
                // parallel transport keeps the rings from twisting
                if (i == 0) normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                else normal = Vector3.Cross(Vector3.Cross(tangent, normal), tangent).normalized;
                var binormal = Vector3.Cross(tangent, normal);
                for (int s = 0; s < Sides; s++)
                {
                    float a = s * Mathf.PI * 2f / Sides;
                    var dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    _verts.Add(_path[i] + dir * radius);
                    _norms.Add(dir);
                }
            }
            for (int i = 0; i < _path.Count - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int a = start + i * Sides + s, b = start + i * Sides + (s + 1) % Sides;
                int c = a + Sides, d = b + Sides;
                _tris.Add(a); _tris.Add(b); _tris.Add(c); // clockwise seen from outside
                _tris.Add(b); _tris.Add(d); _tris.Add(c);
            }
        }
    }
}
