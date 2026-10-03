using System.Collections.Generic;
using HS.Core;
using HS.Presentation;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>
    /// The "System simulation" stage for skill demos: a glowing grid disc far outside the world (2 km away, 400 m down),
    /// a camera at the game's own angle rendering into a texture the picker and the Field Guide show, and a factory for
    /// puppets (the real characters without their Agents) and props. Nothing here registers with the simulation, so a
    /// demo can never touch the run; it plays on real time, so it works while the game is paused.
    /// </summary>
    public sealed class DemoStage : MonoBehaviour
    {
        public static readonly Vector3 Origin = new Vector3(-2000f, -400f, -2000f);
        public const int TextureWidth = 960, TextureHeight = 540;
        static DemoStage _instance;

        public RenderTexture Texture { get; private set; }
        public Camera Camera { get; private set; }
        public Transform Decals { get; private set; }
        Transform _cast, _props;
        Material _blob;
        readonly List<Puppet> _puppets = new List<Puppet>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        public static DemoStage Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("SkillDemoStage");
            _instance = go.AddComponent<DemoStage>();
            _instance.Build();
            return _instance;
        }

        /// <summary>The camera renders only while some viewport is showing the stage.</summary>
        public bool Rendering
        {
            get => Camera != null && Camera.enabled;
            set
            {
                if (Camera != null) Camera.enabled = value;
            }
        }

        public IReadOnlyList<Puppet> Cast => _puppets;

        void Build()
        {
            transform.position = Origin;
            _cast = new GameObject("Cast").transform;
            _cast.SetParent(transform, false);
            _props = new GameObject("Props").transform;
            _props.SetParent(transform, false);
            Decals = new GameObject("Decals").transform;
            Decals.SetParent(transform, false);

            var fx = Resources.Load<Material>("FX/FX_Soft");
            // Floor: a dark disc with a glowing grid that fades at its rim (the texture carries both).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Floor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(20f, 20f, 1f);
            var fm = new Material(fx) { name = "DemoFloor" };
            var grid = Resources.Load<Sprite>("UI/grid_disc");
            if (grid != null) fm.SetTexture("_MainTex", grid.texture);
            fm.SetColor("_Color", Color.white);
            fm.renderQueue = 2990; // under the decals and blob shadows
            var fr = floor.GetComponent<MeshRenderer>();
            fr.sharedMaterial = fm;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _blob = new Material(fx) { name = "DemoBlob" };
            var glow = Resources.Load<Sprite>("UI/glow");
            if (glow != null) _blob.SetTexture("_MainTex", glow.texture);
            _blob.SetColor("_Color", new Color(0f, 0f, 0f, 0.55f));

            Texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32) { name = "SkillDemo", antiAliasing = 4 };
            var camGo = new GameObject("DemoCamera");
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.targetTexture = Texture;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color32(9, 17, 30, 255);
            Camera.fieldOfView = 30f;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 80f;
            Camera.enabled = false;
            Frame(new Vector2(0.3f, -0.4f), 13.5f);
        }

        /// <summary>Point the camera at a stage spot from the game's own angle (50° down), at a distance.</summary>
        public void Frame(Vector2 center, float distance)
        {
            var target = World(center) + Vector3.up * 0.8f;
            var rot = Quaternion.Euler(50f, 0f, 0f);
            Camera.transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
        }

        public Vector3 World(Vector2 stagePos) => Origin + new Vector3(stagePos.x, 0f, stagePos.y);

        /// <summary>World → 0..1 in the demo picture; false when behind the camera.</summary>
        public bool ToViewport(Vector3 world, out Vector2 viewport01)
        {
            var v = Camera.WorldToViewportPoint(world);
            viewport01 = new Vector2(v.x, v.y);
            return v.z > 0f;
        }

        /// <summary>A character by kind: "sidekick", "callum", or an enemy archetype ("thug", "crossbowman", ...).</summary>
        public Puppet Spawn(string kind, Vector2 pos, float yaw)
        {
            var assets = GameAssets.Load();
            var prefab = kind == "sidekick" ? assets.sidekick : kind == "callum" ? assets.hero : assets.Enemy(kind);
            if (prefab == null)
            {
                Debug.LogWarning("[DemoStage] no character for " + kind);
                return null;
            }
            // Instantiated under an inactive holder, nothing on the gameplay prefab ever wakes (no Agent registers);
            // only its Visual is kept.
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            var inst = Instantiate(prefab, holder.transform);
            var visual = inst.transform.Find("Visual");
            var root = new GameObject("Puppet_" + kind).transform;
            root.SetParent(_cast, false);
            if (visual != null)
            {
                var interp = visual.GetComponent<VisualInterpolator>();
                if (interp != null) DestroyImmediate(interp);
                visual.SetParent(root, false);
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
            }
            DestroyImmediate(holder);
            var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blob.name = "Shadow";
            Destroy(blob.GetComponent<Collider>());
            blob.transform.SetParent(root, false);
            blob.transform.localPosition = Vector3.up * 0.02f;
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            blob.transform.localScale = Vector3.one * 1.5f;
            var br = blob.GetComponent<MeshRenderer>();
            br.sharedMaterial = _blob;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var anim = root.GetComponentInChildren<AnimDriver>(true);
            if (anim != null) anim.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var p = new Puppet { Kind = kind, Root = root, Anim = anim, Stage = this };
            p.Pos = pos;
            p.Yaw = yaw;
            _puppets.Add(p);
            return p;
        }

        /// <summary>A prop from GameAssets.demoProps ("barrel_stack", "crate_perch", "bush", "stone", "rock").</summary>
        public GameObject Prop(string id, Vector2 pos, float yaw, float scale = 1f)
        {
            var prefab = GameAssets.Load().DemoProp(id);
            if (prefab == null)
            {
                Debug.LogWarning("[DemoStage] no demo prop " + id);
                return null;
            }
            var go = Instantiate(prefab, _props);
            go.name = id;
            go.transform.SetPositionAndRotation(World(pos), Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>Empty the stage (between demos).</summary>
        public void Clear()
        {
            _puppets.Clear();
            for (int i = _cast.childCount - 1; i >= 0; i--) Destroy(_cast.GetChild(i).gameObject);
            for (int i = _props.childCount - 1; i >= 0; i--) Destroy(_props.GetChild(i).gameObject);
            for (int i = Decals.childCount - 1; i >= 0; i--) Destroy(Decals.GetChild(i).gameObject);
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
            if (_instance == this) _instance = null;
        }
    }
}
