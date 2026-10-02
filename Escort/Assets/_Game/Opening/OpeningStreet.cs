using System.Collections.Generic;
using HS.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS.Opening
{
    /// <summary>
    /// The opening's first beat (GDD §8) as a place: a first-person walk up a city sidewalk and across a zebra crosswalk
    /// at 7:52 on a hazy March morning, phone in hand and wired earphones in, until a cab-over truck runs the red light.
    /// Built at runtime from the opening kits (Resources/OpeningAssets; a placeholder wherever a kit is missing), far from
    /// the chapter world and on its own layer, with its own camera, sun, sky and post-processing; Dispose restores the
    /// chapter's. Each frame is a pure function of the opening clock (the cable and particles step with it), so the
    /// picture stays on the audio's cues (tools/make_opening.py) and tests and captures can scrub it.
    /// Layout and timeline: docs/superpowers/specs/2026-10-01-opening-street-design.md.
    /// </summary>
    public sealed partial class OpeningStreet : MonoBehaviour
    {
        /// <summary>Street space sits here in the world: 900 m from the chapter, beyond any camera's reach.</summary>
        public static readonly Vector3 Origin = new Vector3(0f, 0f, -900f);
        public const float Eye = 1.56f, Stride = 0.7f, Curb = 0.15f, WalkX = 0.35f;
        public const float WalkSpeed = Stride / OpeningView.Step;
        /// <summary>The pedestrian light: the red hand turns to WALK (the audio's ticks speed up), then flashes.</summary>
        public const float WalkAt = 7f, FlashAt = 11f;
        /// <summary>The truck: on screen from TruckAt, braking from BrakeAt, its bumper this far from the eye at the cut.</summary>
        public const float TruckAt = 11f, BrakeAt = 15.3f, TruckSpeed = 13f, BrakeDecel = 7f, TruckEndGap = 1.6f;
        /// <summary>The last moments flood with the headlights before the cut to white.</summary>
        public const float FloodFrom = 15.55f;

        public Camera Camera { get; private set; }
        /// <summary>The phone's screen: a world-space canvas (414 x 854 units = the 69.2 x 142.7 mm display).</summary>
        public RectTransform PhoneScreen { get; private set; }
        public bool PhoneInView { get; private set; }
        /// <summary>The truck's light reaching the walker, 0 (no truck) … 1 (the flood).</summary>
        public float Glow { get; private set; }
        /// <summary>How much of the headlights' glare lands on the phone's screen (0..1).</summary>
        public float ScreenGlare { get; private set; }
        public bool WalkSignal { get; private set; }
        public float TruckFront { get; private set; }
        public bool TruckVisible { get; private set; }
        public Vector3 EyeStreet { get; private set; }
        public float HeadYaw { get; private set; }
        public float HeadPitch { get; private set; }

        int _layer;
        bool _short, _disposed;
        float _lastT = float.NaN;
        Transform _root, _body, _head, _phoneRig;
        OpeningAssets _assets;
        EarphoneCable _cable;
        Transform _cableStart;

        // the chapter's state, restored on Dispose
        readonly List<Camera> _disabledCams = new List<Camera>();
        readonly List<Light> _disabledLights = new List<Light>();
        AmbientMode _ambMode;
        Color _ambSky, _ambEq, _ambGround;
        bool _fog;
        FogMode _fogMode;
        Color _fogColor;
        float _fogStart, _fogEnd;
        Light _prevSun;
        Material _prevSky;
        SphericalHarmonicsL2 _prevProbe;

        Light _sun;
        Volume _volume;
        VolumeProfile _profile;
        DepthOfField _dof;
        Bloom _bloom;
        Vignette _vignette;
        ChromaticAberration _chroma;
        MotionBlur _motion;
        ColorAdjustments _grade;
        LensDistortion _lens;

        // ---------------------------------------------------------------------------------------------------- build
        public static OpeningStreet Build(bool shortVersion)
        {
            var go = new GameObject("OpeningStreet");
            go.transform.position = Origin;
            var s = go.AddComponent<OpeningStreet>();
            s._short = shortVersion;
            s._root = go.transform;
            s._assets = OpeningAssets.Load();
            int layer = LayerMask.NameToLayer("Opening");
            s._layer = layer >= 0 ? layer : 0;
            s.TakeOver();
            s.BuildCamera();
            s.BuildWorld();
            if (!shortVersion) s.BuildPhone();
            SetLayer(go.transform, s._layer);
            s.Render(shortVersion ? 0f : -0.5f);
            return s;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>The chapter's cameras and suns stand down; its ambient, fog and sky are kept for Dispose.</summary>
        void TakeOver()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.enabled)
                {
                    c.enabled = false;
                    _disabledCams.Add(c);
                }
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.type == LightType.Directional)
                {
                    l.enabled = false;
                    _disabledLights.Add(l);
                }
            _ambMode = RenderSettings.ambientMode;
            _ambSky = RenderSettings.ambientSkyColor;
            _ambEq = RenderSettings.ambientEquatorColor;
            _ambGround = RenderSettings.ambientGroundColor;
            _fog = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;
            _prevSun = RenderSettings.sun;
            _prevSky = RenderSettings.skybox;
            _prevProbe = RenderSettings.ambientProbe;

            // A hazy spring morning: a low warm sun from behind the walker's left, cool sky fill, haze down the streets.
            _sun = new GameObject("OpeningSun").AddComponent<Light>();
            _sun.transform.SetParent(_root, false);
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.89f, 0.76f);
            _sun.intensity = 1.45f;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.82f;
            _sun.transform.rotation = Quaternion.Euler(31f, 32f, 0f);
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.64f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.27f);
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(0.47f, 0.5f, 0.56f));
            probe.AddDirectionalLight(Vector3.up, new Color(0.24f, 0.27f, 0.34f), 1f);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.71f, 0.75f, 0.82f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 270f;
            if (_assets != null && _assets.sky != null) RenderSettings.skybox = _assets.sky;
        }

        void BuildCamera()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(_root, false);
            _head = new GameObject("OpeningCamera") { tag = "MainCamera" }.transform;
            _head.SetParent(_body, false);
            Camera = _head.gameObject.AddComponent<Camera>();
            Camera.fieldOfView = 58f;
            Camera.nearClipPlane = 0.03f;
            Camera.farClipPlane = 420f;
            Camera.clearFlags = _assets != null && _assets.sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            Camera.backgroundColor = RenderSettings.fogColor;
            Camera.depth = 50f;
            var data = _head.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            data.volumeLayerMask = 1 << (LayerMask.NameToLayer("Opening") >= 0 ? LayerMask.NameToLayer("Opening") : 0);

            // the cutscene's own grade on top of the game's (priority wins; the game's global volume still applies)
            var vgo = new GameObject("OpeningVolume");
            vgo.transform.SetParent(_root, false);
            _volume = vgo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.sharedProfile = _profile;
            var tm = _profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            _grade = _profile.Add<ColorAdjustments>(true);
            _grade.postExposure.Override(0.1f);
            _grade.contrast.Override(10f);
            _grade.saturation.Override(4f);
            _grade.colorFilter.Override(new Color(0.98f, 0.99f, 1.02f));
            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.Override(1.0f);
            _bloom.intensity.Override(0.55f);
            _bloom.scatter.Override(0.68f);
            _bloom.tint.Override(new Color(1f, 0.96f, 0.9f));
            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.26f);
            _vignette.smoothness.Override(0.5f);
            _dof = _profile.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focusDistance.Override(12f);
            _dof.focalLength.Override(30f);
            _dof.aperture.Override(9f);
            _dof.bladeCount.Override(6);
            var grain = _profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);
            _chroma = _profile.Add<ChromaticAberration>(true);
            _chroma.intensity.Override(0.04f);
            _motion = _profile.Add<MotionBlur>(true);
            _motion.intensity.Override(0f);
            _motion.quality.Override(MotionBlurQuality.High);
            _lens = _profile.Add<LensDistortion>(true);
            _lens.intensity.Override(0f);
            SetLayer(vgo.transform, _layer >= 0 ? _layer : 0);
            Camera.cullingMask = 1 << _layer;
        }

        /// <summary>The chapter gets its world back (cameras, sun, ambient, fog, sky) and the street is destroyed.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var c in _disabledCams) if (c != null) c.enabled = true;
            foreach (var l in _disabledLights) if (l != null) l.enabled = true;
            RenderSettings.ambientMode = _ambMode;
            RenderSettings.ambientSkyColor = _ambSky;
            RenderSettings.ambientEquatorColor = _ambEq;
            RenderSettings.ambientGroundColor = _ambGround;
            RenderSettings.ambientProbe = _prevProbe;
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
            RenderSettings.sun = _prevSun;
            RenderSettings.skybox = _prevSky;
            if (_profile != null) Destroy(_profile);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (!_disposed)
            {
                _disposed = true;
                foreach (var c in _disabledCams) if (c != null) c.enabled = true;
                foreach (var l in _disabledLights) if (l != null) l.enabled = true;
                RenderSettings.sun = _prevSun;
                RenderSettings.skybox = _prevSky;
                RenderSettings.ambientProbe = _prevProbe;
                RenderSettings.fogStartDistance = _fogStart;
                RenderSettings.fogEndDistance = _fogEnd;
                RenderSettings.fogColor = _fogColor;
            }
        }

        // ------------------------------------------------------------------------------------------------- timeline
        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        static float EaseOutBack(float a, float b, float t, float overshoot)
        {
            float u = Mathf.Clamp01(Mathf.InverseLerp(a, b, t)) - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        /// <summary>How far up the street the walker is (z); they stop dead on the last step.</summary>
        public static float WalkerZ(float t)
        {
            const float stop = OpeningView.StepsEnd, ease = 0.3f;
            if (t <= stop) return WalkSpeed * t;
            float u = Mathf.Clamp01((t - stop) / ease);
            return WalkSpeed * stop + WalkSpeed * ease * (u - 0.5f * u * u);
        }

        /// <summary>The ground under the walker: sidewalk, the curb ramp down, the road.</summary>
        public static float GroundY(float z)
        {
            if (z < RampTop) return Curb;
            if (z < SouthCurb) return Mathf.Lerp(Curb, 0.012f, Mathf.SmoothStep(0f, 1f, (z - RampTop) / (SouthCurb - RampTop)));
            return 0f;
        }

        /// <summary>The truck's front bumper (x along the cross street) at time t.</summary>
        public static float TruckX(float t)
        {
            if (t < BrakeAt) return TruckEndGap + WalkX + BrakeRun + TruckSpeed * (BrakeAt - t);
            float u = Mathf.Min(t - BrakeAt, OpeningView.CutAt - BrakeAt);
            return TruckEndGap + WalkX + BrakeRun - (TruckSpeed * u - 0.5f * BrakeDecel * u * u);
        }

        const float BrakeRun = TruckSpeed * (OpeningView.CutAt - BrakeAt) - 0.5f * BrakeDecel * (OpeningView.CutAt - BrakeAt) * (OpeningView.CutAt - BrakeAt);

        public void Render(float t)
        {
            if (_disposed) return;
            float dt = float.IsNaN(_lastT) ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f);
            _lastT = t;
            if (_short) RenderShort(t);
            else RenderWalk(t, dt);
            RenderWorld(t, dt);
        }

        void RenderWalk(float t, float dt)
        {
            // ---- the body: walking up the sidewalk, down the ramp, onto the crosswalk; a dip and sway on every footfall
            float z = WalkerZ(t);
            float k = (t - OpeningView.StepAt) / OpeningView.Step;
            float walking = t < 0f ? 1f : 1f - Smooth(OpeningView.StepsEnd, OpeningView.StepsEnd + 0.35f, t);
            float since = (k - Mathf.Floor(k)) * OpeningView.Step;
            float impact = t >= OpeningView.StepAt ? Mathf.Exp(-since / 0.08f) : 0f;
            float bobY = (-0.016f * (0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * k)) - 0.006f * impact) * walking;
            float bobX = 0.012f * Mathf.Sin(Mathf.PI * k) * walking;
            float roll = 0.6f * Mathf.Sin(Mathf.PI * k + 0.4f) * walking;
            EyeStreet = new Vector3(WalkX + bobX, GroundY(z) + Eye + bobY, z);
            _body.localPosition = EyeStreet;
            _body.localRotation = Quaternion.identity;

            // ---- the head: up the street, down to the phone, up at the signal, back; then the truck
            float pitch = -7f, yaw = 0f;
            pitch = Mathf.Lerp(pitch, -17f, Smooth(0.9f, 1.9f, t));
            pitch = Mathf.Lerp(pitch, -1.5f, Smooth(7.05f, 7.5f, t));
            yaw = Mathf.Lerp(yaw, -5f, Smooth(7.05f, 7.5f, t));
            pitch = Mathf.Lerp(pitch, -17.5f, Smooth(8.15f, 8.65f, t));
            yaw = Mathf.Lerp(yaw, 0f, Smooth(8.15f, 8.65f, t));
            float whip = EaseOutBack(OpeningView.LookUpAt, OpeningView.LookUpAt + 0.42f, t, 1.2f);
            pitch = Mathf.Lerp(pitch, 2.5f, whip);
            yaw = Mathf.Lerp(yaw, 86f, whip);
            pitch = Mathf.Lerp(pitch, 7f, Smooth(14.7f, 16f, t));   // it towers over you
            // frozen, then the horn and the brakes shake everything
            float dread = Smooth(OpeningView.LookUpAt, OpeningView.CutAt, t);
            float jolt = t >= BrakeAt ? Mathf.Exp(-(t - BrakeAt) / 0.25f) : 0f;
            float shake = dread * 0.35f + jolt * 0.8f;
            pitch += (Mathf.PerlinNoise(t * 9f, 0.3f) - 0.5f) * shake * 2f;
            yaw += (Mathf.PerlinNoise(0.7f, t * 9f) - 0.5f) * shake * 2f;
            HeadPitch = pitch;
            HeadYaw = yaw;
            _head.localPosition = new Vector3(0f, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-pitch, yaw, roll * (1f - whip));
            Camera.fieldOfView = Mathf.Lerp(58f, 54f, Smooth(14.6f, 16f, t));

            // ---- the phone, held in the right hand: low, raised to read, lowered for the glance, dropped at the end
            if (_phoneRig != null)
            {
                Vector3 low = new Vector3(0.11f, -0.46f, 0.24f), walk = new Vector3(0.07f, -0.215f, 0.3f),
                    read = new Vector3(0.055f, -0.185f, 0.27f), glance = new Vector3(0.09f, -0.27f, 0.29f),
                    drop = new Vector3(0.17f, -0.66f, 0.16f);
                var p = low;
                p = Vector3.Lerp(p, walk, Smooth(0.9f, 1.9f, t));
                p = Vector3.Lerp(p, read, Smooth(4.25f, 4.7f, t));
                p = Vector3.Lerp(p, walk, Smooth(6.0f, 6.6f, t));
                p = Vector3.Lerp(p, glance, Smooth(7.05f, 7.5f, t));
                p = Vector3.Lerp(p, walk, Smooth(8.15f, 8.65f, t));
                p = Vector3.Lerp(p, read, Smooth(8.7f, 9.15f, t));
                float fall = Mathf.Pow(Smooth(OpeningView.LookUpAt + 0.02f, OpeningView.LookUpAt + 0.55f, t), 1.6f);
                p = Vector3.Lerp(p, drop, fall);
                // the hand rides the walk a beat behind the head
                float lag = Mathf.Sin(Mathf.PI * (k - 0.25f)) * walking;
                p += new Vector3(0.004f * lag, -0.004f * impact * walking + 0.002f * Mathf.Sin(2f * Mathf.PI * (k - 0.2f)) * walking, 0f);
                _phoneRig.localPosition = p;
                // screen toward the eyes, tilted a touch, swaying with the steps; it tips away as it falls
                var toEye = (-p).normalized;
                var rot = Quaternion.LookRotation(toEye, Vector3.up) * Quaternion.Euler(0f, 0f, -4f + 1.2f * lag);
                rot = Quaternion.Slerp(rot, rot * Quaternion.Euler(-70f, 15f, 25f), fall);
                _phoneRig.localRotation = rot;
                PhoneInView = _phoneRig.gameObject.activeSelf && InView(_phoneRig.position, 0.05f);
                ScreenGlare = Smooth(OpeningView.HornAt + 1.2f, OpeningView.LookUpAt + 0.3f, t) * (1f - fall);
            }

            // ---- focus: the street, the phone, the signal, the phone, the truck
            float focus = 14f;
            focus = Mathf.Lerp(focus, 0.31f, Smooth(1.0f, 1.8f, t));
            focus = Mathf.Lerp(focus, 15f, Smooth(7.1f, 7.5f, t));
            focus = Mathf.Lerp(focus, 0.3f, Smooth(8.15f, 8.6f, t));
            focus = Mathf.Lerp(focus, Mathf.Max(1.2f, TruckX(t) - WalkX), Smooth(OpeningView.LookUpAt, OpeningView.LookUpAt + 0.3f, t));
            _dof.focusDistance.value = focus;
            _dof.aperture.value = Mathf.Lerp(9f, 5.6f, Smooth(OpeningView.LookUpAt, 16f, t));
            _motion.intensity.value = 0.55f * Mathf.Clamp01(1f - Mathf.Abs(t - (OpeningView.LookUpAt + 0.15f)) / 0.25f);
            _chroma.intensity.value = Mathf.Lerp(0.04f, 0.55f, Smooth(BrakeAt - 0.4f, OpeningView.CutAt, t));
            _vignette.intensity.value = Mathf.Lerp(0.26f, 0.42f, dread);
            _lens.intensity.value = Mathf.Lerp(0f, -0.22f, Smooth(BrakeAt, OpeningView.CutAt, t));
            _grade.postExposure.value = 0.1f + 1.6f * Mathf.Pow(Smooth(FloodFrom, OpeningView.CutAt, t), 2f);
        }

        /// <summary>The replay version's callback: 0.6 s of the truck already on top of you.</summary>
        void RenderShort(float t)
        {
            float u = Mathf.Clamp01(t / OpeningView.ShortFlash);
            EyeStreet = new Vector3(WalkX, Eye, WalkerZ(OpeningView.StepsEnd + 0.3f));
            _body.localPosition = EyeStreet;
            HeadYaw = 86f;
            HeadPitch = Mathf.Lerp(3f, 7f, u);
            float shake = 0.6f;
            _head.localRotation = Quaternion.Euler(-HeadPitch + (Mathf.PerlinNoise(t * 11f, 0.2f) - 0.5f) * shake, HeadYaw, 0f);
            Camera.fieldOfView = 56f;
            _dof.focusDistance.value = 4f;
            _motion.intensity.value = 0f;
            _chroma.intensity.value = 0.4f;
            _grade.postExposure.value = 0.1f + 1.4f * u * u;
            PhoneInView = false;
        }

        bool InView(Vector3 world, float margin)
        {
            var vp = Camera.WorldToViewportPoint(world);
            return vp.z > 0f && vp.x > -margin && vp.x < 1f + margin && vp.y > -margin && vp.y < 1f + margin;
        }

        void LateUpdate()
        {
            // keep the earbud cable's anchors honest even if a frame renders without Render (e.g. editor pause)
        }
    }
}
