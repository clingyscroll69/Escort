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
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            // Overlay canvases aren't drawn by cameras: borrow camera space for this one render so the UI is captured.
            var overlays = new System.Collections.Generic.List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add(c);
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f;
            }
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var c in overlays) c.renderMode = RenderMode.ScreenSpaceOverlay;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            Directory.CreateDirectory(ShotsDir);
            var file = Path.Combine(ShotsDir, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            return file;
        }
    }
}
