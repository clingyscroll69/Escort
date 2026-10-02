using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Frame-time probe: average / p95 / worst frame and fps, written as JSON (player builds and QA runs).</summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public string OutPath;
        public float Warmup = 3f;
        readonly List<float> _frames = new List<float>(20000);
        float _t;

        readonly List<string> _hitches = new List<string>();

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (_t > Warmup) _frames.Add(ms);
            if (ms > 50f && _hitches.Count < 40)
            {
                var flow = FindAnyObjectByType<HS.Flow.GameFlow>();
                _hitches.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0}s {1:0}ms {2}", _t, ms, flow != null ? flow.Current.ToString() : "-"));
            }
        }

        public string Json()
        {
            if (_frames.Count == 0) return "{}";
            var sorted = _frames.OrderBy(f => f).ToList();
            float avg = _frames.Average();
            float p95 = sorted[Mathf.Clamp((int)(sorted.Count * 0.95f), 0, sorted.Count - 1)];
            float p99 = sorted[Mathf.Clamp((int)(sorted.Count * 0.99f), 0, sorted.Count - 1)];
            float worst = sorted[sorted.Count - 1];
            var ci = CultureInfo.InvariantCulture;
            return "{" + string.Format(ci, "\"frames\":{0},\"avg_ms\":{1:0.00},\"p95_ms\":{2:0.00},\"p99_ms\":{3:0.00},\"worst_ms\":{4:0.00},\"avg_fps\":{5:0.0},\"res\":\"{6}x{7}\"",
                _frames.Count, avg, p95, p99, worst, 1000f / avg, Screen.width, Screen.height) + ",\"hitches\":[" + string.Join(",", _hitches.Select(h => "\"" + h + "\"")) + "]}";
        }

        public void Write()
        {
            if (string.IsNullOrEmpty(OutPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, Json());
        }

        void OnApplicationQuit() => Write();
    }
}
