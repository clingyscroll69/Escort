using System;
using System.Collections.Generic;
using HS.Presentation;
using HS.Skills;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>
    /// A skill demo's timeline: beats that fire once as time passes them, tweens evaluated while their window is open
    /// (and once more at its end so final states land), and captions. Run(t) is idempotent for a given t and only moves
    /// forward; skipping is Run(Length).
    /// </summary>
    public sealed class DemoScript
    {
        struct Beat
        {
            public float At;
            public int Seq;
            public Action<DemoContext> Do;
        }

        sealed class Tween
        {
            public float From, To;
            public Action<DemoContext, float> Do;
            public bool Done;
        }

        readonly List<Beat> _beats = new List<Beat>();
        readonly List<Tween> _tweens = new List<Tween>();
        readonly List<(float at, string text)> _captions = new List<(float, string)>();
        int _next;
        bool _sorted;
        float _time = -1f;

        public float Length { get; private set; } = 8f;

        public DemoScript At(float t, Action<DemoContext> beat)
        {
            _beats.Add(new Beat { At = t, Seq = _beats.Count, Do = beat });
            _sorted = false;
            return this;
        }

        public DemoScript Over(float from, float to, Action<DemoContext, float> tween)
        {
            _tweens.Add(new Tween { From = from, To = Mathf.Max(from + 1e-3f, to), Do = tween });
            return this;
        }

        public DemoScript Caption(float t, string text)
        {
            _captions.Add((t, text));
            _captions.Sort((a, b) => a.at.CompareTo(b.at));
            return this;
        }

        public DemoScript End(float t)
        {
            Length = t;
            return this;
        }

        public string CaptionAt(float time)
        {
            string s = null;
            foreach (var (at, text) in _captions)
                if (at <= time) s = text;
            return s;
        }

        public int CaptionCount => _captions.Count;

        public void Run(DemoContext c, float time)
        {
            if (!_sorted)
            {
                _beats.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Seq.CompareTo(b.Seq));
                _sorted = true;
            }
            if (time < _time) return; // only forward
            _time = time;
            while (_next < _beats.Count && _beats[_next].At <= time)
            {
                var b = _beats[_next++];
                b.Do?.Invoke(c);
            }
            foreach (var tw in _tweens)
            {
                if (tw.Done || time < tw.From) continue;
                float u = Mathf.Clamp01((time - tw.From) / (tw.To - tw.From));
                tw.Do?.Invoke(c, u);
                if (u >= 1f) tw.Done = true;
            }
        }

        // ------------------------------------------------------------------------------------------- helpers
        /// <summary>Walk (or sneak) a puppet from a to b over [from, to]; it faces where it's going and stops there.</summary>
        public DemoScript Walk(string who, float from, float to, Vector2 a, Vector2 b, bool crouch = false)
        {
            float speed = Vector2.Distance(a, b) / Mathf.Max(0.1f, to - from);
            return Over(from, to, (c, u) =>
            {
                var p = c[who];
                if (p == null) return;
                p.Pos = Vector2.Lerp(a, b, u);
                p.Face(b + (b - a) * 0.01f);
                p.Locomotion(u < 1f ? speed : 0f, crouch);
            });
        }
    }

    /// <summary>What a demo script works with: the stage, its cast, props and decals, the overlay, the skill's data.</summary>
    public sealed class DemoContext
    {
        public DemoStage Stage;
        public DemoOverlay Overlay;
        public SkillDefinition Def;
        public int Rank = 1;
        public float Time;
        public readonly Dictionary<string, Puppet> Cast = new Dictionary<string, Puppet>();
        public readonly Dictionary<string, GameObject> Props = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> _decals = new Dictionary<string, GameObject>();
        Material _fx;

        public Puppet this[string id] => id != null && Cast.TryGetValue(id, out var p) ? p : null;

        public Puppet Spawn(string id, string kind, Vector2 pos, float yaw)
        {
            var p = Stage.Spawn(kind, pos, yaw);
            if (p != null) Cast[id] = p;
            return p;
        }

        public GameObject Prop(string id, string kind, Vector2 pos, float yaw, float scale = 1f)
        {
            var go = Stage.Prop(kind, pos, yaw, scale);
            if (go != null) Props[id] = go;
            return go;
        }

        Material Fx(Texture tex, Color color)
        {
            if (_fx == null) _fx = Resources.Load<Material>("FX/FX_Soft");
            var m = new Material(_fx);
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", color);
            return m;
        }

        public void RemoveDecal(string id)
        {
            if (!_decals.TryGetValue(id, out var go)) return;
            if (go != null) UnityEngine.Object.Destroy(go);
            _decals.Remove(id);
        }

        public GameObject Decal(string id) => _decals.TryGetValue(id, out var go) ? go : null;

        /// <summary>A witness cone on a puppet (follows it, faces its way). Angle/range read from the returned holder.</summary>
        public ConeHandle Cone(string id, Puppet who, float angle, float range)
        {
            RemoveDecal(id);
            var go = new GameObject("Cone_" + id);
            go.transform.SetParent(who.Root, false);
            var h = new ConeHandle { Angle = angle, Range = range };
            var view = go.AddComponent<WitnessConeView>();
            view.Angle = () => h.Angle;
            view.Range = () => h.Range;
            view.Base = new Color(1f, 0.95f, 0.78f, 0.3f);
            view.Emphasised = new Color(1f, 0.93f, 0.62f, 0.5f);
            h.View = view;
            _decals[id] = go;
            return h;
        }

        /// <summary>A flat ring on the floor (danger area, hearing radius).</summary>
        public GameObject Ring(string id, Vector2 at, float radius, Color color)
        {
            RemoveDecal(id);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Ring_" + id;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Stage.Decals, false);
            go.transform.position = Stage.World(at) + Vector3.up * 0.05f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * radius * 2f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Fx(Resources.Load<Sprite>("UI/ring")?.texture, color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _decals[id] = go;
            return go;
        }

        /// <summary>A line through the air (an aim line).</summary>
        public LineRenderer Line(string id, Vector3 a, Vector3 b, Color color, float width = 0.05f)
        {
            RemoveDecal(id);
            var go = new GameObject("Line_" + id);
            go.transform.SetParent(Stage.Decals, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = lr.endWidth = width;
            lr.sharedMaterial = Fx(null, color);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _decals[id] = go;
            return lr;
        }

        /// <summary>A small thing flying from a to b (sand pouch, bolt), returned so a tween can move it.</summary>
        public GameObject Missile(string id, PrimitiveType shape, Vector3 scale, Color color)
        {
            RemoveDecal(id);
            var go = GameObject.CreatePrimitive(shape);
            go.name = "Missile_" + id;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Stage.Decals, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Fx(null, color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _decals[id] = go;
            return go;
        }

        public static void Sound(string key, float volume = 0.5f) => HS.Audio.AudioDirector.Instance?.Play(key, null, volume, 0.02f, 0.04f);
    }

    public sealed class ConeHandle
    {
        public float Angle, Range;
        public WitnessConeView View;
    }
}
