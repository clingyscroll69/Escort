using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>Prefab registry (Resources/GameAssets) so runtime code never hard-codes asset paths.</summary>
    [CreateAssetMenu(menuName = "HS/Game Assets", fileName = "GameAssets")]
    public sealed class GameAssets : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public GameObject prefab;
        }

        public GameObject hero;
        public GameObject sidekick;
        public GameObject[] roomModules;
        public GameObject startCap, endCap, campfire, boss;
        public List<Entry> enemies = new List<Entry>();
        [Tooltip("Props for the skill-demo stage (Tutorial/Demo): barrel stack, crate perch, bush, stone.")]
        public List<Entry> demoProps = new List<Entry>();

        static GameAssets _instance;
        public static GameAssets Load() => _instance != null ? _instance : _instance = Resources.Load<GameAssets>("GameAssets");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        public GameObject Enemy(string archetype)
        {
            foreach (var e in enemies) if (e.id == archetype) return e.prefab;
            return null;
        }

        public GameObject DemoProp(string id)
        {
            foreach (var e in demoProps) if (e.id == id) return e.prefab;
            return null;
        }
    }
}
