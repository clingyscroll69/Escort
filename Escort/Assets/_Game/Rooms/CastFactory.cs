using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Puts the cast on the road. An archetype with its own registered prefab spawns from it; one without wears the body of
    /// the archetype its stats name (<see cref="EnemyStats.body"/>), tinted (<see cref="EnemyStats.tint"/>) — so a new face
    /// needs data, not an editor rebuild (GameplayPrefabBuilder still builds real prefabs; once registered, they win).
    /// Neutral people (scouts, hostages) and the Mirror are assembled the same way from bodies already in the registry.
    /// Objects are put together under an inactive holder, so their Awake and OnEnable see them finished.
    /// </summary>
    public static class CastFactory
    {
        static Transform _holder;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _holder = null;
        }

        static Transform Holder
        {
            get
            {
                if (_holder == null)
                {
                    var go = new GameObject("CastFactory_Holder");
                    go.SetActive(false);
                    _holder = go.transform;
                }
                return _holder;
            }
        }

        /// <summary>Scouts by id: who they are and whose body they wear when no prefab of their own is registered.</summary>
        public static readonly Dictionary<string, (string name, string body, int gift, bool heroBody, Color tint)> Scouts =
            new Dictionary<string, (string, string, int, bool, Color)>
            {
                ["quill"] = ("Mr. Quill", "scout_quill", 1, false, default),
                ["prisoner"] = ("the Prisoner", "scout_prisoner", 0, false, default),
                // A rival hero: a knight's harness, gilded.
                ["wren"] = ("Darian Wren", "scout_quill", 1, true, new Color(1f, 0.86f, 0.55f, 1f)),
            };

        /// <summary>The prefab an archetype spawns from: its own, else its body's (one level), else null.</summary>
        public static GameObject PrefabFor(string archetype, out bool aliased)
        {
            aliased = false;
            var assets = GameAssets.Load();
            if (assets == null || string.IsNullOrEmpty(archetype)) return null;
            var own = assets.Enemy(archetype);
            if (own != null) return own;
            var t = RunContext.Current != null ? RunContext.Current.Tuning : Tuning.LoadDefault();
            string body = null;
            foreach (var e in t.enemies)
                if (e.id == archetype)
                {
                    body = e.body;
                    break;
                }
            if (string.IsNullOrEmpty(body)) return null;
            aliased = true;
            return assets.Enemy(body);
        }

        /// <summary>
        /// Spawn anyone a <see cref="SpawnMarker"/> names: an enemy (configured as its own archetype), a scout
        /// ("scout_&lt;id&gt;"), or a hostage. Null (with a warning) when there is no body to wear.
        /// </summary>
        public static GameObject Spawn(string archetype, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (archetype == "hostage") return SpawnHostage(position, rotation, parent);
            if (archetype.StartsWith("scout_")) return SpawnScout(archetype.Substring(6), position, rotation, parent);
            var prefab = PrefabFor(archetype, out bool aliased);
            if (prefab == null)
            {
                Debug.LogWarning("[Cast] no prefab or body for " + archetype);
                return null;
            }
            var go = Object.Instantiate(prefab, position, rotation, Holder);
            var e = go.GetComponent<EnemyAgent>();
            if (e != null) e.Archetype = archetype; // configured (and tinted) as itself on Start
            return Release(go, parent);
        }

        static GameObject Release(GameObject go, Transform parent)
        {
            go.transform.SetParent(parent, true);
            if (parent == null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return go;
        }

        static GameObject SpawnScout(string id, Vector3 position, Quaternion rotation, Transform parent)
        {
            var assets = GameAssets.Load();
            var own = assets != null ? assets.Enemy("scout_" + id) : null;
            if (own != null) return Object.Instantiate(own, position, rotation, parent);
            if (!Scouts.TryGetValue(id, out var who))
            {
                Debug.LogWarning("[Cast] unknown scout " + id);
                return null;
            }
            var body = assets != null ? assets.Enemy(who.body) : null;
            if (body == null)
            {
                Debug.LogWarning("[Cast] no body for scout " + id);
                return null;
            }
            var go = Object.Instantiate(body, position, rotation, Holder);
            go.name = "Scout_" + id;
            var s = go.GetComponent<HS.Curator.Scout>();
            if (s != null)
            {
                s.ScoutId = id;
                s.DisplayName = who.name;
                s.GiftRations = who.gift;
                s.AgentId = "scout_" + id;
            }
            if (who.heroBody && assets.hero != null) SwapVisual(go, assets.hero);
            if (who.tint.a > 0f) Tint(go, who.tint);
            return Release(go, parent);
        }

        static GameObject SpawnHostage(Vector3 position, Quaternion rotation, Transform parent)
        {
            var assets = GameAssets.Load();
            var own = assets != null ? assets.Enemy("hostage") : null;
            if (own != null) return Object.Instantiate(own, position, rotation, parent);
            var body = assets != null ? assets.Enemy("scout_prisoner") : null;
            var go = new GameObject("Hostage");
            go.transform.SetParent(Holder, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.87f, 0f);
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.04f;
            if (body != null) CopyVisual(body, go.transform);
            go.AddComponent<Hostage>();
            var cam = new GameObject("CamTarget").transform;
            cam.SetParent(go.transform, false);
            cam.localPosition = new Vector3(0f, 1.15f, 0f);
            Tint(go, new Color(0.95f, 0.88f, 0.8f, 1f));
            return Release(go, parent);
        }

        /// <summary>
        /// The Mirror (GDD §4.5a): an enemy body wearing a copy of the hero's own visual, grey and glitch-edged. Built inactive;
        /// the caller configures it, then <see cref="Activate"/>s it.
        /// </summary>
        public static EnemyAgent BuildMirror(GameObject heroPrefabOrInstance, Vector3 position, Quaternion rotation)
        {
            var assets = GameAssets.Load();
            var own = assets.Enemy("mirror");
            var body = own != null ? own : assets.Enemy("ashgrave");
            var go = Object.Instantiate(body, position, rotation, Holder);
            go.name = "The Mirror";
            if (own == null && heroPrefabOrInstance != null) SwapVisual(go, heroPrefabOrInstance);
            var e = go.GetComponent<EnemyAgent>();
            e.Archetype = "mirror";
            e.AgentId = "mirror";
            return e;
        }

        public static void Activate(GameObject go, Transform parent) => Release(go, parent);

        /// <summary>Replace a body's "Visual" child with a copy of another's (its animator and interpolator come along).</summary>
        public static void SwapVisual(GameObject root, GameObject source)
        {
            var old = root.transform.Find("Visual");
            if (old != null)
            {
                old.SetParent(null, false);
                Object.Destroy(old.gameObject);
            }
            CopyVisual(source, root.transform);
        }

        static void CopyVisual(GameObject source, Transform root)
        {
            var src = source.transform.Find("Visual");
            if (src == null) return;
            var v = Object.Instantiate(src.gameObject, root, false);
            v.name = "Visual";
            v.transform.localPosition = Vector3.zero;
            v.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Multiply a body's colours (Toon _BaseColor). On its own material instances, not property blocks: the hit flash
        /// owns those (and clears them), and a tint must outlive every flash.
        /// </summary>
        public static void Tint(GameObject root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is SpriteRenderer) continue;
                foreach (var m in OwnedMaterials.Of(r))
                    if (m != null && m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, m.GetColor(BaseColorId) * tint);
            }
        }

        /// <summary>The glitch look (the Mirror, the Curator's mask): grey, edges dissolving, a cold glow.</summary>
        public static void Glitch(GameObject root, float desaturate, float dissolve, Color emission)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is SpriteRenderer) continue;
                foreach (var m in OwnedMaterials.Of(r))
                {
                    if (m == null || !m.HasProperty(DesaturateId)) continue;
                    m.SetFloat(DesaturateId, desaturate);
                    m.SetFloat(DissolveId, dissolve);
                    m.SetColor(EmissionId, emission);
                }
            }
        }
    }
}
