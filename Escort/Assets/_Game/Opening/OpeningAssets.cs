using System.Collections.Generic;
using UnityEngine;

namespace HS.Opening
{
    /// <summary>
    /// The opening street's prefabs and materials (Resources/OpeningAssets), built by OpeningAssetsBuilder from the
    /// Blender kits in build_art/opening and the street textures. Anything missing falls back to a placeholder at runtime,
    /// so the opening always plays.
    /// </summary>
    public sealed class OpeningAssets : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string id;
            public GameObject prefab;
        }

        public List<Entry> prefabs = new List<Entry>();
        public Material road, sidewalk, curb, tactile, paintWhite, paintYellow, gutter, cable, placeholder, sky, smoke, glow;
        public Texture2D placeholderTexture;

        static OpeningAssets _instance;
        static Dictionary<string, GameObject> _index;

        public static OpeningAssets Load()
        {
            if (_instance != null) return _instance;
            _instance = Resources.Load<OpeningAssets>("OpeningAssets");
            _index = null;
            return _instance;
        }

        public GameObject Get(string id)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, GameObject>();
                foreach (var e in prefabs)
                    if (e.prefab != null && !string.IsNullOrEmpty(e.id))
                        _index[e.id] = e.prefab;
            }
            return _index.TryGetValue(id, out var p) ? p : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            _index = null;
        }
    }
}
