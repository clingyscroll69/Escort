using System.IO;
using UnityEngine;

namespace HS.QA
{
    /// <summary>
    /// Player-build QA: screenshots of exactly what is on screen (UI included) at given real seconds after start
    /// (-hs-shots "6;12;30" -hs-shots-dir &lt;dir&gt;).
    /// </summary>
    public sealed class QaShots : MonoBehaviour
    {
        public float[] At = new float[0];
        public string Dir = "";
        int _next;
        float _t;

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_next >= At.Length || _t < At[_next]) return;
            Directory.CreateDirectory(Dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"player_{_next:00}_{At[_next]:0}s.png"));
            _next++;
        }
    }
}
