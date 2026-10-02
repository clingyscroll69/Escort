using System.IO;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Runtime camera capture for QA (precise timing, unlike editor-driven captures).</summary>
    public static class QaCapture
    {
        public static string ShotsDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/qa/shots"));

        public static string Capture(Camera cam, string name, int w = 1600, int h = 900)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return null;
            // A fresh, cleared target per shot: a pooled one would hand back the previous shot if a render ever failed.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            // Overlay canvases aren't drawn by cameras: borrow camera space for this one render so the UI is captured
            // (and let the camera see the UI's layer: the opening's camera sees only its street, which hid the HUD from
            // captures while it was on the player's screen).
            var overlays = new System.Collections.Generic.List<Canvas>();
            int mask = cam.cullingMask;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add(c);
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f;
                cam.cullingMask |= 1 << c.gameObject.layer;
            }
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.cullingMask = mask;
            foreach (var c in overlays) c.renderMode = RenderMode.ScreenSpaceOverlay;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.Destroy(rt);
            Directory.CreateDirectory(ShotsDir);
            var file = Path.Combine(ShotsDir, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            return file;
        }
    }
}
