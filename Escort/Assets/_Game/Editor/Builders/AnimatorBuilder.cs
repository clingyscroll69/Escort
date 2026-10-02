using System.Collections.Generic;
using System.Linq;
using HS.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// Builds the shared Humanoid animator (one rig for every character, GDD §2) from the UAL1/UAL2 CC0 clips.
    /// Locomotion blend-tree thresholds are the clips' measured root speeds (tools/blender/clip_speeds.json) so the
    /// stride-normalised Speed parameter from AnimDriver keeps feet planted (slice gate 5: no obvious foot-sliding).
    /// </summary>
    public static class AnimatorBuilder
    {
        public const string ControllerPath = "Assets/_Game/Art/Animations/HS_Humanoid.controller";
        public const string RangedOverridePath = "Assets/_Game/Art/Animations/HS_Ranged.overrideController";
        public const string MaskPath = "Assets/_Game/Art/Animations/UpperBody.mask";

        // In-engine calibrated contact-foot ground speeds per clip, normalised by stride scale (StrideCalibration test).
        // Sprint is excluded: its effective stride on the retargeted cast is far below its root-motion speed, so above
        // the jog threshold the whole tree is sped up via LocoRate instead.
        public const float WalkSpeed = 0.9f, JogSpeed = 5.45f, CrouchSpeed = 0.66f;

        static Dictionary<string, AnimationClip> _clips;

        static AnimationClip Clip(string name)
        {
            if (_clips == null)
            {
                _clips = new Dictionary<string, AnimationClip>();
                foreach (var path in new[] { "Assets/_Game/Art/Animations/UAL1.fbx", "Assets/_Game/Art/Animations/UAL2.fbx" })
                    foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                        if (!c.name.StartsWith("__preview")) _clips[c.name] = c;
            }
            if (!_clips.TryGetValue(name, out var clip)) Debug.LogError("[AnimatorBuilder] missing clip " + name);
            return clip;
        }

        [MenuItem("Tools/HS/Build/Animator Controller")]
        public static void Build()
        {
            _clips = null;
            var mask = BuildMask();
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Combat", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("CrouchRate", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("FullSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("UpperSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("LocoRate", AnimatorControllerParameterType.Float);
            foreach (var p in ctrl.parameters)
            {
                if (p.name == "CrouchRate" || p.name == "FullSpeed" || p.name == "UpperSpeed" || p.name == "LocoRate") p.defaultFloat = 1f;
            }
            ctrl.parameters = ctrl.parameters; // re-assign so defaults persist

            // ---------- Base layer ----------
            // AnimatorController.layers returns a copy: modify and re-assign or the IK pass is silently lost.
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            var sm = ctrl.layers[0].stateMachine;

            var loco = sm.AddState("Locomotion", new Vector3(300, 0));
            loco.motion = LocoTree(ctrl, "LocoTree", "Idle_Loop");
            loco.iKOnFeet = false;
            loco.speedParameter = "LocoRate";
            loco.speedParameterActive = true;
            sm.defaultState = loco;

            var combat = sm.AddState("CombatLocomotion", new Vector3(300, 120));
            combat.motion = LocoTree(ctrl, "CombatTree", "Sword_Idle");
            combat.iKOnFeet = false;
            combat.speedParameter = "LocoRate";
            combat.speedParameterActive = true;

            var crouch = sm.AddState("CrouchLocomotion", new Vector3(300, -120));
            var crouchTree = new BlendTree { name = "CrouchTree", blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(crouchTree, ctrl);
            crouchTree.AddChild(Clip("Crouch_Idle_Loop"), 0f);
            crouchTree.AddChild(Clip("Crouch_Fwd_Loop"), CrouchSpeed);
            crouch.motion = crouchTree;
            crouch.speedParameter = "CrouchRate";
            crouch.speedParameterActive = true;
            crouch.iKOnFeet = false;

            Link(loco, crouch, "Crouch", true);
            Link(crouch, loco, "Crouch", false);
            Link(loco, combat, "Combat", true);
            Link(combat, loco, "Combat", false);
            Link(combat, crouch, "Crouch", true);

            var fullActions = new (string state, string clip, bool loop)[]
            {
                ("Attack1", "Sword_Regular_A", false), ("Attack2", "Sword_Regular_B", false), ("Attack3", "Sword_Regular_C", false),
                ("Heavy", "Sword_Attack", false), ("Knockback", "Hit_Knockback", false), ("Death", "Death01", false),
                ("Dodge", "Roll", false), ("Kneel", "Fixing_Kneeling", true), ("Sit", "Sitting_Idle_Loop", true),
                ("SitTalk", "Sitting_Talking_Loop", true), ("GetUp", "LayToIdle", false), ("Stagger", "Idle_Shield_Break", false),
                ("Surrender", "Crouch_Idle_Loop", true),
            };
            int i = 0;
            foreach (var (state, clip, loop) in fullActions)
            {
                var s = sm.AddState(state, new Vector3(650, -300 + 60 * i++));
                s.motion = Clip(clip);
                s.speedParameter = "FullSpeed";
                s.speedParameterActive = true;
                if (!loop && state != "Death")
                {
                    var t = s.AddTransition(loco);
                    t.hasExitTime = true;
                    t.exitTime = 0.9f;
                    t.duration = 0.12f;
                    t.hasFixedDuration = true;
                }
            }

            // ---------- Upper-body layer (masked, weight driven by AnimDriver) ----------
            var upper = new AnimatorControllerLayer
            {
                name = "UpperBody",
                defaultWeight = 0f,
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy },
            };
            AssetDatabase.AddObjectToAsset(upper.stateMachine, ctrl);
            ctrl.AddLayer(upper);
            var usm = upper.stateMachine;
            var empty = usm.AddState("Empty", new Vector3(300, 0));
            empty.writeDefaultValues = false;
            usm.defaultState = empty;
            var upperActions = new (string state, string clip, bool loop)[]
            {
                ("Block", "Sword_Block", false), ("Stab", "Punch_Jab", false), ("Stab2", "Punch_Cross", false),
                ("Hit", "Hit_Chest", false), ("HitHead", "Hit_Head", false), ("Throw", "OverhandThrow", false),
                ("Shoot", "Pistol_Shoot", false), ("Aim", "Pistol_Aim_Neutral", true), ("Reload", "Pistol_Reload", false),
                ("Interact", "Interact", false), ("Pickup", "PickUp_Table", false), ("Scold", "Idle_No_Loop", true),
                ("Nod", "Yes", false), ("FoldArms", "Idle_FoldArms_Loop", true), ("Talk", "Idle_Talking_Loop", true),
                ("Consume", "Consume", false), ("Salute", "Sword_Idle", true), ("Guard", "Idle_Shield_Loop", true),
            };
            i = 0;
            foreach (var (state, clip, loop) in upperActions)
            {
                var s = usm.AddState(state, new Vector3(650, -400 + 50 * i++));
                s.motion = Clip(clip);
                s.writeDefaultValues = false;
                s.speedParameter = "UpperSpeed";
                s.speedParameterActive = true;
                if (!loop)
                {
                    var t = s.AddTransition(empty);
                    t.hasExitTime = true;
                    t.exitTime = 0.95f;
                    t.duration = 0.1f;
                    t.hasFixedDuration = true;
                }
            }

            // Ranged variant: combat idle holds the crossbow like a pistol.
            AssetDatabase.DeleteAsset(RangedOverridePath);
            var ov = new AnimatorOverrideController(ctrl) { name = "HS_Ranged" };
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(pairs);
            for (int k = 0; k < pairs.Count; k++)
                if (pairs[k].Key != null && pairs[k].Key.name == "Sword_Idle")
                    pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[k].Key, Clip("Pistol_Idle_Loop"));
            ov.ApplyOverrides(pairs);
            AssetDatabase.CreateAsset(ov, RangedOverridePath);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"animator built\"}");
        }

        static BlendTree LocoTree(AnimatorController ctrl, string name, string idle)
        {
            var tree = new BlendTree { name = name, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            tree.AddChild(Clip(idle), 0f);
            tree.AddChild(Clip("Walk_Loop"), WalkSpeed);
            tree.AddChild(Clip("Jog_Fwd_Loop"), JogSpeed);
            return tree;
        }

        static void Link(AnimatorState from, AnimatorState to, string param, bool value)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.15f;
            t.hasFixedDuration = true;
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        }

        static AvatarMask BuildMask()
        {
            AssetDatabase.DeleteAsset(MaskPath);
            var mask = new AvatarMask();
            for (int p = 0; p < (int)AvatarMaskBodyPart.LastBodyPart; p++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)p, false);
            foreach (var p in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                mask.SetHumanoidBodyPartActive(p, true);
            AssetDatabase.CreateAsset(mask, MaskPath);
            return mask;
        }
    }
}
