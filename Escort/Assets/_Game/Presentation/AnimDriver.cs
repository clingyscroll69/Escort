using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Presentation
{
    /// <summary>
    /// Presentation-only animation layer (never drives gameplay timing). Maps action ids from the simulation to
    /// Animator states built by AnimatorBuilder, scales clips to the gameplay duration, normalises locomotion speed
    /// by stride so feet stay planted, and adds IK overlays for poses the CC0 library lacks (salute, surrender) —
    /// GDD §10: "use simple procedural upper-body overlays".
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class AnimDriver : MonoBehaviour, IAgentPresenter
    {
        public const int BaseLayer = 0;
        public const int UpperLayer = 1;

        /// <summary>Hips height of the UAL mannequin; the clip speeds in AnimatorBuilder were measured on it.</summary>
        public const float MannequinHipsHeight = 0.917f; // UAL mannequin pelvis height in its T-pose

        public struct ActionSpec
        {
            public string State;
            public int Layer;
            public float ClipLength;
            public bool Loop;
            public ActionSpec(string state, int layer, float clipLength, bool loop = false)
            {
                State = state;
                Layer = layer;
                ClipLength = clipLength;
                Loop = loop;
            }
        }

        /// <summary>Action id → Animator state. Keep in sync with AnimatorBuilder.States.</summary>
        public static readonly Dictionary<string, ActionSpec> Actions = new Dictionary<string, ActionSpec>
        {
            { "attack", new ActionSpec("Attack1", BaseLayer, 0.43f) },
            { "attack2", new ActionSpec("Attack2", BaseLayer, 0.53f) },
            { "attack3", new ActionSpec("Attack3", BaseLayer, 1.53f) },
            { "heavy", new ActionSpec("Heavy", BaseLayer, 1.53f) },
            { "block", new ActionSpec("Block", UpperLayer, 1.23f) },
            { "stab", new ActionSpec("Stab", UpperLayer, 0.87f) },
            { "stab2", new ActionSpec("Stab2", UpperLayer, 1.0f) },
            { "hit", new ActionSpec("Hit", UpperLayer, 0.33f) },
            { "hit_head", new ActionSpec("HitHead", UpperLayer, 0.43f) },
            { "hit_heavy", new ActionSpec("Knockback", BaseLayer, 0.83f) },
            { "death", new ActionSpec("Death", BaseLayer, 2.4f) },
            { "dodge", new ActionSpec("Dodge", BaseLayer, 1.47f) },
            { "throw", new ActionSpec("Throw", UpperLayer, 1.33f) },
            { "shoot", new ActionSpec("Shoot", UpperLayer, 0.63f) },
            { "aim", new ActionSpec("Aim", UpperLayer, 0.17f, true) },
            { "reload", new ActionSpec("Reload", UpperLayer, 1.67f) },
            { "kneel", new ActionSpec("Kneel", BaseLayer, 5.2f, true) },
            { "bandage", new ActionSpec("Kneel", BaseLayer, 5.2f, true) },
            { "interact", new ActionSpec("Interact", UpperLayer, 2.0f) },
            { "pickup", new ActionSpec("Pickup", UpperLayer, 0.83f) },
            { "scold", new ActionSpec("Scold", UpperLayer, 2.5f, true) },
            { "nod", new ActionSpec("Nod", UpperLayer, 2.5f) },
            { "folded", new ActionSpec("FoldArms", UpperLayer, 2.5f, true) },
            { "talk", new ActionSpec("Talk", UpperLayer, 2.93f, true) },
            { "sit", new ActionSpec("Sit", BaseLayer, 1.67f, true) },
            { "sit_talk", new ActionSpec("SitTalk", BaseLayer, 2.93f, true) },
            { "getup", new ActionSpec("GetUp", BaseLayer, 1.53f) },
            { "stagger", new ActionSpec("Stagger", BaseLayer, 1.07f) },
            { "consume", new ActionSpec("Consume", UpperLayer, 1.33f) },
            { "cheer", new ActionSpec("Nod", UpperLayer, 2.5f) },
            { "surrender", new ActionSpec("Surrender", BaseLayer, 2.93f, true) },
            { "salute", new ActionSpec("Salute", UpperLayer, 1.67f, true) },
            { "guard", new ActionSpec("Guard", UpperLayer, 2.5f, true) },
        };

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int CombatId = Animator.StringToHash("Combat");
        static readonly int CrouchRateId = Animator.StringToHash("CrouchRate");
        static readonly int LocoRateId = Animator.StringToHash("LocoRate");
        const float JogThreshold = 5.45f, CrouchStanceSpeed = 0.42f; // measured stance speeds (FootSlideTests)
        static readonly int FullSpeedId = Animator.StringToHash("FullSpeed");
        static readonly int UpperSpeedId = Animator.StringToHash("UpperSpeed");

        Animator _anim;
        float _strideScale = 1f;
        float _upperWeight, _upperTarget;
        float _upperTimer;
        bool _upperLoop;
        string _baseAction;
        float _baseTimer;
        bool _dead;

        static readonly Quaternion SaluteGrip = Quaternion.AngleAxis(180f, new Vector3(0f, 1f, 1f).normalized);

        // IK overlay state
        float _saluteW, _saluteTarget;
        float _surrenderW, _surrenderTarget;
        Transform _head;

        public Animator Animator => _anim;
        public bool IsDead => _dead;
        public string LastAction { get; private set; }

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _anim.applyRootMotion = false;
            if (_anim.isHuman)
            {
                var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    float h = hips.position.y - transform.position.y;
                    if (h > 0.3f) _strideScale = h / MannequinHipsHeight;
                }
                _head = _anim.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        float _targetSpeed;
        bool _targetCrouch;

        /// <summary>Called from the fixed sim tick; damping happens per rendered frame in Update (frame-rate independent).</summary>
        public void SetLocomotion(float planarSpeed, bool crouched)
        {
            _targetSpeed = planarSpeed / Mathf.Max(0.5f, _strideScale);
            _targetCrouch = crouched;
        }

        void ApplyLocomotion(float dt)
        {
            if (_anim == null || _dead) return;
            _anim.SetFloat(SpeedId, _targetSpeed, 0.05f, dt);
            _anim.SetBool(CrouchId, _targetCrouch);
            float v = _anim.GetFloat(SpeedId);
            // Above the jog threshold the jog cycle is sped up so stride keeps pace with the body.
            _anim.SetFloat(LocoRateId, Mathf.Max(1f, v / JogThreshold));
            // The stock sneak cycle's stance only covers ~0.42 m/s; speed it up (clamped so it stays readable).
            _anim.SetFloat(CrouchRateId, Mathf.Clamp(v / CrouchStanceSpeed, 0.6f, 4.2f));
        }

        public void SetFlag(string flag, bool on)
        {
            if (_anim == null) return;
            switch (flag)
            {
                case "combat": _anim.SetBool(CombatId, on); break;
                case "salute": _saluteTarget = on ? 1f : 0f; break;
                case "surrender": _surrenderTarget = on ? 1f : 0f; break;
            }
        }

        public void PlayAction(string action, float duration = -1f)
        {
            if (_anim == null) return;
            LastAction = action;
            if (action == "none" || action == "stop")
            {
                StopActions();
                return;
            }
            if (_dead && action != "getup") return;
            if (!Actions.TryGetValue(action, out var spec))
            {
                Debug.LogWarning($"[AnimDriver] unknown action '{action}' on {name}");
                return;
            }
            float rate = duration > 0f && !spec.Loop ? Mathf.Clamp(spec.ClipLength / duration, 0.25f, 4f) : 1f;
            if (action == "death") _dead = true;
            if (action == "getup") _dead = false;
            // IK overlays belong to their own action only; anything else releases them.
            _saluteTarget = action == "salute" ? 1f : 0f;
            _surrenderTarget = action == "surrender" ? 1f : 0f;

            if (spec.Layer == UpperLayer)
            {
                _anim.SetFloat(UpperSpeedId, rate);
                _anim.CrossFadeInFixedTime(spec.State, 0.08f, UpperLayer, 0f);
                _upperTarget = 1f;
                _upperLoop = spec.Loop;
                _upperTimer = spec.Loop ? float.PositiveInfinity : spec.ClipLength / rate;
            }
            else
            {
                _anim.SetFloat(FullSpeedId, rate);
                _anim.CrossFadeInFixedTime(spec.State, action == "death" ? 0.12f : 0.1f, BaseLayer, 0f);
                _baseAction = action;
                _baseTimer = spec.Loop ? float.PositiveInfinity : spec.ClipLength / rate;
                // Full-body action cancels any upper-body overlay so arms don't fight the body pose.
                _upperTarget = 0f;
            }
        }

        public void StopActions()
        {
            _upperTarget = 0f;
            _upperTimer = 0f;
            _saluteTarget = 0f;
            _surrenderTarget = 0f;
            if (!_dead && _baseAction != null)
            {
                _anim.CrossFadeInFixedTime(_anim.GetBool(CrouchId) ? "CrouchLocomotion" : "Locomotion", 0.15f, BaseLayer);
                _baseAction = null;
            }
        }

        public void StopUpper()
        {
            _upperTarget = 0f;
            _upperTimer = 0f;
            _saluteTarget = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            ApplyLocomotion(dt);
            if (_upperTimer > 0f && !float.IsPositiveInfinity(_upperTimer))
            {
                _upperTimer -= dt;
                if (_upperTimer <= 0f) _upperTarget = 0f;
            }
            if (_baseTimer > 0f && !float.IsPositiveInfinity(_baseTimer))
            {
                _baseTimer -= dt;
                if (_baseTimer <= 0f && !_dead) _baseAction = null; // AnimatorBuilder exit transitions return to locomotion
            }
            _upperWeight = Mathf.MoveTowards(_upperWeight, _upperTarget, dt * 8f);
            if (_anim != null && _anim.layerCount > 1) _anim.SetLayerWeight(UpperLayer, _upperWeight);
            _saluteW = Mathf.MoveTowards(_saluteW, _saluteTarget, dt * 3.5f);
            _surrenderW = Mathf.MoveTowards(_surrenderW, _surrenderTarget, dt * 3f);
        }

        PropSocket _rightProp;
        bool _socketSearched;

        void LateUpdate()
        {
            if (_saluteW <= 0.001f) return;
            if (!_socketSearched)
            {
                _socketSearched = true;
                var hand = _anim != null && _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand != null) _rightProp = hand.GetComponentInChildren<PropSocket>();
            }
            if (_rightProp == null) return;
            // Blade straight up, flat facing forward: the rigid prop makes this exact regardless of wrist retargeting.
            var root = transform;
            var desired = Quaternion.LookRotation(root.forward, root.up) *
                          Quaternion.Inverse(Quaternion.LookRotation(_rightProp.FlatNormal, _rightProp.BladeAxis));
            var t = _rightProp.transform;
            t.rotation = Quaternion.Slerp(t.rotation, desired, _saluteW);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null || !_anim.isHuman || layerIndex != BaseLayer || _head == null) return;
            var root = transform;
            if (_saluteW > 0.001f)
            {
                // Sword hilt raised before the face, blade vertical (knight's salute). Humanoid IK goal rotations are
                // relative to the T-pose hand frame (fingers +X, thumb/blade +Z, palm -Y for a +Z-facing body); a 180°
                // turn about (0,1,1) maps it to fingers across the chest, thumb/blade up, palm toward the face.
                var target = _head.position + root.forward * 0.3f + root.right * 0.05f - root.up * 0.14f;
                var rot = root.rotation * SaluteGrip;
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _saluteW);
                _anim.SetIKRotationWeight(AvatarIKGoal.RightHand, _saluteW * 0.5f);
                _anim.SetIKPosition(AvatarIKGoal.RightHand, target);
                _anim.SetIKRotation(AvatarIKGoal.RightHand, rot);
                _anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _saluteW);
                _anim.SetIKHintPosition(AvatarIKHint.RightElbow, _head.position + root.right * 0.35f - root.up * 0.35f + root.forward * 0.1f);
            }
            else
            {
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                _anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            }
            if (_surrenderW > 0.001f)
            {
                // Hands raised beside the head: an "Unready" enemy the hero must wait for.
                var up = _head.position + root.up * 0.28f;
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _surrenderW);
                _anim.SetIKPosition(AvatarIKGoal.LeftHand, up - root.right * 0.32f + root.forward * 0.05f);
                if (_saluteW <= 0.001f)
                {
                    _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _surrenderW);
                    _anim.SetIKPosition(AvatarIKGoal.RightHand, up + root.right * 0.32f + root.forward * 0.05f);
                }
            }
            else
            {
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            }
        }
    }
}
