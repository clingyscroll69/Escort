using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// Explicit Humanoid mapping for the shared Quaternius (UE-style) rig. Unity's auto-mapper was inconsistent across
    /// our Blender re-exports (e.g. Hips→"root" on some characters), which broke retargeting. The same map is applied to
    /// the UAL animation sources and every character so all avatars agree (GDD §2: one shared Humanoid rig).
    /// </summary>
    public static class HumanoidConfigurator
    {
        static readonly (string human, string bone)[] Map =
        {
            ("Hips", "pelvis"), ("Spine", "spine_01"), ("Chest", "spine_02"), ("UpperChest", "spine_03"), ("Neck", "neck_01"), ("Head", "Head"),
            ("LeftShoulder", "clavicle_l"), ("LeftUpperArm", "upperarm_l"), ("LeftLowerArm", "lowerarm_l"), ("LeftHand", "hand_l"),
            ("RightShoulder", "clavicle_r"), ("RightUpperArm", "upperarm_r"), ("RightLowerArm", "lowerarm_r"), ("RightHand", "hand_r"),
            ("LeftUpperLeg", "thigh_l"), ("LeftLowerLeg", "calf_l"), ("LeftFoot", "foot_l"), ("LeftToes", "ball_l"),
            ("RightUpperLeg", "thigh_r"), ("RightLowerLeg", "calf_r"), ("RightFoot", "foot_r"), ("RightToes", "ball_r"),
        };

        static readonly (string finger, string bone)[] Fingers =
        {
            ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky"),
        };

        static readonly string[] Phalanges = { "Proximal", "Intermediate", "Distal" };

        public static HumanBone[] BuildHumanBones(ICollection<string> available)
        {
            var list = new List<HumanBone>();
            void Add(string human, string bone)
            {
                if (!available.Contains(bone)) return;
                var hb = new HumanBone { humanName = human, boneName = bone };
                hb.limit.useDefaultValues = true;
                list.Add(hb);
            }
            foreach (var (h, b) in Map) Add(h, b);
            foreach (var side in new[] { ("Left", "l"), ("Right", "r") })
            foreach (var (finger, bone) in Fingers)
                for (int i = 0; i < 3; i++)
                    Add($"{side.Item1} {finger} {Phalanges[i]}", $"{bone}_0{i + 1}_{side.Item2}");
            return list.ToArray();
        }

        public static IEnumerable<string> Targets()
        {
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art/Characters", "Assets/_Game/Art/Animations" }))
                yield return AssetDatabase.GUIDToAssetPath(g);
        }

        [MenuItem("Tools/HS/Build/Configure Humanoids")]
        public static void ConfigureAll()
        {
            var report = new List<string>();
            foreach (var path in Targets().ToList())
                report.Add(Configure(path));
            HS.Agent.AgentBridge.Write("humanoid.json", "[" + string.Join(",", report.Select(r => "\"" + r + "\"")) + "]");
        }

        static string Configure(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (importer == null || go == null) return path + ": missing";
            var transforms = go.GetComponentsInChildren<Transform>(true);
            // Humanoid reference T-pose must face +Z. The UAL mannequin's armature faces -Z (left hand at +X);
            // taken literally that inverts every arm muscle (abduction → arms overhead). Turn its reference around.
            var handL = transforms.FirstOrDefault(t => t.name == "hand_l");
            bool facesBack = handL != null && handL.position.x > 0f;
            var skeleton = transforms.Select(t => new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = facesBack && t.parent == go.transform && t.name == "Armature"
                    ? Quaternion.Euler(0f, 180f, 0f) * t.localRotation
                    : t.localRotation,
                scale = t.localScale,
            }).ToArray();
            var hd = new HumanDescription
            {
                human = BuildHumanBones(transforms.Select(t => t.name).ToHashSet()),
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = hd;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            return $"{System.IO.Path.GetFileName(path)} bones={hd.human.Length} valid={(avatar != null && avatar.isValid)} human={(avatar != null && avatar.isHuman)} turned={facesBack}";
        }
    }
}
