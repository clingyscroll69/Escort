using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HS.Presentation
{
    /// <summary>
    /// A chapter's light (campaign spec §7): sun colour, intensity and angle, trilight ambient, linear fog. "old_road" is
    /// the scene as authored (captured on the first Apply in a scene), so chapter 1 is exactly what it was.
    /// </summary>
    public sealed class ChapterTheme
    {
        public string Id;
        public Color Sun = Color.white;
        public float SunIntensity = 1.35f;
        public Vector3 SunEuler = new Vector3(50f, -30f, 0f);
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public bool Fog = true;
        public Color FogColor;
        public float FogStart = 45f, FogEnd = 140f;

        static ChapterTheme _scene;
        static int _sceneHandle = -1;

        static readonly Dictionary<string, ChapterTheme> Themes = new Dictionary<string, ChapterTheme>
        {
            ["whisperwood"] = new ChapterTheme
            {
                Id = "whisperwood", Sun = new Color(0.82f, 0.9f, 0.8f), SunIntensity = 0.95f, SunEuler = new Vector3(46f, 20f, 0f),
                AmbientSky = new Color(0.46f, 0.56f, 0.5f), AmbientEquator = new Color(0.36f, 0.42f, 0.34f), AmbientGround = new Color(0.2f, 0.22f, 0.17f),
                FogColor = new Color(0.52f, 0.6f, 0.55f), FogStart = 22f, FogEnd = 85f,
            },
            ["catacombs"] = new ChapterTheme
            {
                Id = "catacombs", Sun = new Color(0.66f, 0.6f, 0.76f), SunIntensity = 0.55f, SunEuler = new Vector3(62f, -10f, 0f),
                AmbientSky = new Color(0.3f, 0.27f, 0.34f), AmbientEquator = new Color(0.22f, 0.2f, 0.22f), AmbientGround = new Color(0.11f, 0.1f, 0.1f),
                FogColor = new Color(0.07f, 0.06f, 0.09f), FogStart = 26f, FogEnd = 70f,
            },
            ["sunken_bastion"] = new ChapterTheme
            {
                Id = "sunken_bastion", Sun = new Color(0.74f, 0.83f, 0.96f), SunIntensity = 0.85f, SunEuler = new Vector3(40f, -50f, 0f),
                AmbientSky = new Color(0.42f, 0.5f, 0.62f), AmbientEquator = new Color(0.34f, 0.38f, 0.42f), AmbientGround = new Color(0.16f, 0.18f, 0.2f),
                FogColor = new Color(0.48f, 0.55f, 0.63f), FogStart = 30f, FogEnd = 100f,
            },
            ["gallery"] = new ChapterTheme
            {
                Id = "gallery", Sun = new Color(1f, 0.94f, 0.82f), SunIntensity = 1.15f, SunEuler = new Vector3(55f, 10f, 0f),
                AmbientSky = new Color(0.7f, 0.66f, 0.6f), AmbientEquator = new Color(0.56f, 0.52f, 0.46f), AmbientGround = new Color(0.3f, 0.27f, 0.24f),
                Fog = false, FogColor = new Color(0.8f, 0.76f, 0.7f),
            },
        };

        /// <summary>Tests: the next Apply captures the scene afresh.</summary>
        public static void ForgetSceneDefaults()
        {
            _scene = null;
            _sceneHandle = -1;
        }

        public static void Apply(string id)
        {
            int handle = SceneManager.GetActiveScene().handle;
            if (_scene == null || _sceneHandle != handle)
            {
                _scene = Capture();
                _sceneHandle = handle;
            }
            var t = id != null && Themes.TryGetValue(id, out var theme) ? theme : _scene;
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = t.Sun;
                sun.intensity = t.SunIntensity;
                sun.transform.rotation = Quaternion.Euler(t.SunEuler);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.AmbientSky;
            RenderSettings.ambientEquatorColor = t.AmbientEquator;
            RenderSettings.ambientGroundColor = t.AmbientGround;
            RenderSettings.fog = t.Fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = t.FogColor;
            RenderSettings.fogStartDistance = t.FogStart;
            RenderSettings.fogEndDistance = t.FogEnd;
        }

        static ChapterTheme Capture()
        {
            var sun = RenderSettings.sun;
            return new ChapterTheme
            {
                Id = "old_road",
                Sun = sun != null ? sun.color : Color.white,
                SunIntensity = sun != null ? sun.intensity : 1.35f,
                SunEuler = sun != null ? sun.transform.rotation.eulerAngles : new Vector3(50f, -30f, 0f),
                AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor, AmbientGround = RenderSettings.ambientGroundColor,
                Fog = RenderSettings.fog, FogColor = RenderSettings.fogColor, FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
            };
        }
    }
}
