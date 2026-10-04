using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Sidekick;
using HS.Skills;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>Gameplay prefabs: simulation root (CharacterController + Agent) with the interpolated visual as a child.</summary>
    public static class GameplayPrefabBuilder
    {
        public const string Dir = "Assets/_Game/Prefabs/Gameplay";

        [MenuItem("Tools/HS/Build/Gameplay Prefabs")]
        public static void BuildAll()
        {
            System.IO.Directory.CreateDirectory(Dir);
            BuildSidekick("Sidekick", "Sidekick_Visual", 0.96f);
            BuildHero("Callum", "Callum_Visual", 1.06f, EnsureCallumRuleSet());
            foreach (var (arch, visual, scale) in Enemies) BuildEnemy(arch, visual, scale);
            foreach (var (id, name, visual, scale) in Scouts) BuildScout(id, name, visual, scale);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"gameplay prefabs\"}");
        }

        public static GameObject NewRoot(string name, float scale, float radius = 0.34f)
        {
            var root = new GameObject(name);
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.78f * scale;
            cc.radius = radius * scale;
            cc.center = new Vector3(0f, cc.height * 0.5f + 0.02f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.04f;
            return root;
        }

        /// <summary>Camera framing target at chest height so heads and rule icons stay on screen.</summary>
        public static void AddCamTarget(GameObject root, float height)
        {
            var t = new GameObject("CamTarget").transform;
            t.SetParent(root.transform, false);
            t.localPosition = new Vector3(0f, height, 0f);
        }

        public static void AddVisual(GameObject root, string visualPrefab)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterPrefabBuilder.PrefabDir}/{visualPrefab}.prefab");
            var v = (GameObject)PrefabUtility.InstantiatePrefab(p, root.transform);
            v.name = "Visual";
            v.transform.localPosition = Vector3.zero;
            v.transform.localRotation = Quaternion.identity;
            v.AddComponent<VisualInterpolator>();
        }

        public static readonly (string arch, string visual, float scale)[] Enemies =
        {
            ("thug", "Thug_Visual", 1f), ("brute", "Brute_Visual", 1.14f), ("crossbowman", "Crossbowman_Visual", 1f),
            ("turncoat", "Turncoat_Visual", 0.98f), ("ambusher", "Ambusher_Visual", 0.98f), ("archer", "Archer_Visual", 1f),
            ("ashgrave", "Ashgrave_Visual", 1.09f),
            ("poacher", "Poacher_Visual", 1f), ("woodsman", "Woodsman_Visual", 1.12f), ("fern_ambusher", "Ambusher_Visual", 0.98f),
        };

        /// <summary>Curator scouts (GDD §4.3): id, display name, visual, scale.</summary>
        public static readonly (string id, string name, string visual, float scale)[] Scouts =
        {
            ("quill", "Mr. Quill", "Quill_Visual", 0.97f),
        };

        public static string ScoutPrefabPath(string id) => $"{Dir}/Scout_{id}.prefab";

        static void BuildScout(string id, string displayName, string visual, float scale)
        {
            var root = NewRoot("Scout_" + id, scale);
            var s = root.AddComponent<HS.Curator.Scout>();
            s.ScoutId = id;
            s.DisplayName = displayName;
            s.AgentId = "scout_" + id;
            AddVisual(root, visual);
            AddCamTarget(root, 1.2f * scale);
            PrefabUtility.SaveAsPrefabAsset(root, ScoutPrefabPath(id));
            Object.DestroyImmediate(root);
        }

        public static string EnemyPrefabPath(string arch) => $"{Dir}/Enemy_{arch}.prefab";

        static void BuildEnemy(string arch, string visual, float scale)
        {
            var root = NewRoot("Enemy_" + arch, scale, arch == "brute" ? 0.42f : 0.34f);
            var e = root.AddComponent<EnemyAgent>();
            e.Archetype = arch;
            AddVisual(root, visual);
            AddCamTarget(root, 1.2f * scale);
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath(arch));
            Object.DestroyImmediate(root);
        }

        public const string RuleSetPath = "Assets/_Game/Data/Callum_RuleSet.asset";

        /// <summary>Callum's S0/S1 rule lists (GDD §6.1). Priority is list order; numbers are args.</summary>
        static HeroRuleSetDef EnsureCallumRuleSet()
        {
            var def = AssetDatabase.LoadAssetAtPath<HeroRuleSetDef>(RuleSetPath);
            if (def == null)
            {
                AssetDatabase.DeleteAsset(RuleSetPath); // a stale asset with a missing script reference
                System.IO.Directory.CreateDirectory("Assets/_Game/Data");
                def = ScriptableObject.CreateInstance<HeroRuleSetDef>();
                AssetDatabase.CreateAsset(def, RuleSetPath);
            }
            def.heroId = "callum";
            def.s0.Clear();
            def.s1.Clear();
            def.s2.Clear();
            def.s3.Clear();
            // Recall (GDD §4.2 table): [channel, max hostiles near her (−1 any), wound, rise, leaves a duel, sprints].
            var recall = new[]
            {
                new[] { 8f, 0f, 1f, 0.3f, 0f, 0f },  // S0: only with nobody near; +1 wound
                new[] { 5f, 2f, 1f, 0.3f, 0f, 0f },  // S1: 2 or fewer near
                new[] { 3f, -1f, 0f, 0.3f, 1f, 0f }, // S2: breaks off combat; no wound
                new[] { 1.5f, -1f, 0f, 0.5f, 1f, 1f }, // S3: sprints mid-fight; she rises at 50%
            };
            // S2 adds Look Away (and waits only 1 s on a flagged cheater: CallumModule.WaitCap); S3's own rules come in plan 5.
            foreach (var (list, wait, stage) in new[] { (def.s0, 3f, 0), (def.s1, 2f, 1), (def.s2, 2f, 2), (def.s3, 2f, 3) })
            {
                list.Add(new RuleEntry("callum_recall", recall[stage]));
                if (stage >= 2) list.Add(new RuleEntry("callum_look_away")); // S2: Look Away when asked
                list.Add(new RuleEntry("callum_finisher"));             // Judgment (from chapter 3; idle before)
                list.Add(new RuleEntry("callum_fallback", 3f));        // 4. fall back at 3+ engagers
                list.Add(new RuleEntry("callum_wait_unready", wait));   // 3. wait on the Unready (S0 3 s, S1 2 s)
                list.Add(new RuleEntry("callum_salute"));               // 2. the 1.2 s salute
                list.Add(new RuleEntry("callum_fight"));                // 1. fight the challenged target
                list.Add(new RuleEntry("callum_challenge", 15f));       // 2. challenge nearest within 15 m
                list.Add(new RuleEntry("threshold_pause"));
                list.Add(new RuleEntry("follow_route"));
            }
            EditorUtility.SetDirty(def);
            return def;
        }

        static void BuildHero(string name, string visual, float scale, HeroRuleSetDef rules)
        {
            var root = NewRoot(name, scale, 0.36f);
            if (name == "Callum") root.AddComponent<HS.Hero.Callum.CallumModule>();
            var hero = root.AddComponent<HeroAgent>();
            hero.AgentId = name.ToLowerInvariant();
            hero.HeroId = name.ToLowerInvariant();
            hero.RuleSet = rules;
            AddVisual(root, visual);
            AddCamTarget(root, 1.3f * scale);
            var iconAnchor = new GameObject("RuleIconAnchor");
            iconAnchor.transform.SetParent(root.transform, false);
            iconAnchor.transform.localPosition = new Vector3(0f, 2.45f * scale, 0f);
            iconAnchor.AddComponent<RuleIconDisplay>();
            root.AddComponent<WitnessConeView>();
            PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            Object.DestroyImmediate(root);
        }

        static void BuildSidekick(string name, string visual, float scale)
        {
            var root = NewRoot(name, scale, 0.32f);
            root.AddComponent<SidekickAgent>().AgentId = "sidekick";
            root.AddComponent<SidekickSkills>();
            root.AddComponent<PlayerCommands>();
            AddVisual(root, visual);
            AddCamTarget(root, 1.2f * scale);
            PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            Object.DestroyImmediate(root);
        }
    }
}
