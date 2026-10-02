using HS.Core;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace HS.Presentation
{
    /// <summary>
    /// Fixed-angle (~50°) third-person camera that keeps the hero and the sidekick in frame (GDD §2).
    /// Cinemachine: TargetGroup (hero + sidekick) → CinemachineFollow in world space at a fixed pitch/yaw,
    /// with CinemachineGroupFraming dollying (never rotating) so both stay on screen.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public CinemachineTargetGroup Group { get; private set; }
        public CinemachineCamera Vcam { get; private set; }
        public Camera Camera { get; private set; }
        CinemachineGroupFraming _framing;
        Transform _lookAhead, _hero;
        Vector3 _aheadDir = Vector3.forward;
        /// <summary>Travel direction for the look-ahead point (defaults to the hero's facing).</summary>
        public System.Func<Vector3> TravelDirection;
        public float LookAheadDistance = 6f;
        CinemachineBasicMultiChannelPerlin _noise;
        float _shakeTime;

        public static CameraRig Build(Transform hero, Transform sidekick, CameraTuning t = null)
        {
            t ??= new CameraTuning();
            var root = new GameObject("CameraRig");
            var rig = root.AddComponent<CameraRig>();

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.fieldOfView = t.fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 250f;
            if (cam.GetComponent<CinemachineBrain>() == null)
            {
                var brain = cam.gameObject.AddComponent<CinemachineBrain>();
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.6f);
            }
            rig.Camera = cam;

            var groupGo = new GameObject("CameraTargetGroup");
            groupGo.transform.SetParent(root.transform, false);
            var group = groupGo.AddComponent<CinemachineTargetGroup>();
            group.PositionMode = CinemachineTargetGroup.PositionModes.GroupCenter;
            group.RotationMode = CinemachineTargetGroup.RotationModes.Manual;
            rig.Group = group;
            rig._hero = hero;
            rig._lookAhead = new GameObject("LookAhead").transform;
            rig._lookAhead.SetParent(root.transform, false);
            if (hero != null) rig._lookAhead.position = hero.position + Vector3.forward * rig.LookAheadDistance;
            rig.SetTargets(hero, sidekick);

            var vcamGo = new GameObject("GameplayVcam");
            vcamGo.transform.SetParent(root.transform, false);
            var rot = Quaternion.Euler(t.pitch, t.yaw, 0f);
            vcamGo.transform.rotation = rot;
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = group.transform;
            vcam.Lens = new LensSettings { FieldOfView = t.fov, NearClipPlane = 0.3f, FarClipPlane = 250f };
            vcam.Priority = 10;

            float d = (t.minDistance + t.maxDistance) * 0.5f;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.6f, 0.6f, 0.6f);
            follow.FollowOffset = rot * new Vector3(0f, 0f, -d);

            var framing = vcamGo.AddComponent<CinemachineGroupFraming>();
            framing.FramingMode = CinemachineGroupFraming.FramingModes.HorizontalAndVertical;
            framing.FramingSize = 1f / t.framingPadding;
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.DollyOnly;
            framing.LateralAdjustment = CinemachineGroupFraming.LateralAdjustmentModes.ChangePosition;
            framing.DollyRange = new Vector2(t.minDistance - d, t.maxDistance - d);
            framing.Damping = 1.2f;
            rig._framing = framing;

            var noise = vcamGo.AddComponent<CinemachineBasicMultiChannelPerlin>();
            noise.AmplitudeGain = 0f;
            noise.FrequencyGain = 1f;
            rig._noise = noise;

            rig.Vcam = vcam;
            // Place the real camera immediately so the first frame is already framed.
            var center = group.transform.position;
            cam.transform.SetPositionAndRotation(center + follow.FollowOffset, rot);
            return rig;
        }

        Transform _sidekick;
        /// <summary>
        /// What the fixed camera can fit at full dolly, per screen axis (world metres): wide across, shallow in depth.
        /// Beyond it the player's own sidekick wins the frame and the hero gets an edge marker.
        /// </summary>
        public float FitAcross = 32f, FitDepth = 18f;
        public bool HeroOffFrame { get; private set; }

        public void SetTargets(Transform hero, Transform sidekick)
        {
            _sidekick = sidekick;
            var targets = new System.Collections.Generic.List<CinemachineTargetGroup.Target>();
            if (hero != null) targets.Add(new CinemachineTargetGroup.Target { Object = hero, Weight = 1f, Radius = 1.6f });
            if (sidekick != null) targets.Add(new CinemachineTargetGroup.Target { Object = sidekick, Weight = 1.15f, Radius = 1.6f });
            // Look-ahead: the road ahead of the hero is where threats come from; keep it on screen.
            if (_lookAhead != null) targets.Add(new CinemachineTargetGroup.Target { Object = _lookAhead, Weight = 0.7f, Radius = 2.2f });
            Group.Targets = targets;
        }

        /// <summary>Additional temporary member (boss, focus point) with a weight.</summary>
        public void AddFocus(Transform t, float weight, float radius)
        {
            if (t == null) return;
            foreach (var m in Group.Targets) if (m.Object == t) return;
            Group.Targets.Add(new CinemachineTargetGroup.Target { Object = t, Weight = weight, Radius = radius });
        }

        public void RemoveFocus(Transform t)
        {
            Group.Targets.RemoveAll(m => m.Object == t);
        }

        public void Shake(float amplitude, float duration)
        {
            if (_noise == null) return;
            if (_noise.NoiseProfile == null) return; // noise profile assigned by the scene builder
            _noise.AmplitudeGain = Mathf.Max(_noise.AmplitudeGain, amplitude);
            _shakeTime = Mathf.Max(_shakeTime, duration);
        }

        void LateUpdate()
        {
            if (_hero == null || _sidekick == null || Group == null) return;
            // Leash: past the fit distance the player's own character wins the frame (weights fade, never snap).
            var cam = Camera != null ? Camera.transform : null;
            var right = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
            var depth = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            var sep = _hero.position - _sidekick.position;
            float need = Mathf.Max(Mathf.Abs(Vector3.Dot(sep, right)) / FitAcross, Mathf.Abs(Vector3.Dot(sep, depth)) / FitDepth);
            float k = Mathf.InverseLerp(0.85f, 1.15f, need); // 0 = both fit, 1 = sidekick only
            HeroOffFrame = k > 0.5f;
            var targets = Group.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t.Object == _hero) t.Weight = Mathf.Lerp(1f, 0f, k);
                else if (t.Object == _lookAhead) t.Weight = Mathf.Lerp(0.7f, 0f, k);
                targets[i] = t;
            }
        }

        void Update()
        {
            if (_hero != null && _lookAhead != null)
            {
                var want = TravelDirection != null ? TravelDirection() : _hero.forward;
                want.y = 0f;
                if (want.sqrMagnitude > 0.01f) _aheadDir = Vector3.Slerp(_aheadDir, want.normalized, 1f - Mathf.Exp(-2f * Time.deltaTime));
                _lookAhead.position = _hero.position + _aheadDir * LookAheadDistance;
            }
            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.unscaledDeltaTime;
                if (_shakeTime <= 0f && _noise != null) _noise.AmplitudeGain = 0f;
            }
        }

        public void SetNoiseProfile(NoiseSettings profile)
        {
            if (_noise != null) _noise.NoiseProfile = profile;
        }
    }
}
