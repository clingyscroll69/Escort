using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using HS.UI;
using UnityEngine;

namespace HS.QA
{
    /// <summary>
    /// QA: plays the opening on a manual clock and captures exact frames at the requested opening times (seconds from the
    /// song's first note), so every beat can be reviewed without real-time drift. Writes Library/Agent/scrub.json.
    /// </summary>
    public sealed class OpeningScrub : MonoBehaviour
    {
        public float[] Times = { 1f };
        public bool Short;
        public string Prefix = "scrub";
        public int Width = 1600, Height = 900;
        public bool QuitWhenDone = true;

        IEnumerator Start()
        {
#if UNITY_EDITOR
            // captures must show real shaders, not the editor's cyan stand-in while variants compile in the background
            bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            var view = OpeningView.Show(UIRoot.Ensure(), Short);
            view.ManualClock = true;
            var log = new StringBuilder("{\"files\":[");
            // the pre-roll, then each requested time in 60 Hz steps (smooth for anything simulated per step)
            while (view.Current == OpeningView.Phase.PreRoll) view.Advance(1f / 60f);
            yield return null;
            for (int i = 0; i < Times.Length; i++)
            {
                int guard = 0;
                while (view.Current != OpeningView.Phase.Done && view.Elapsed < Times[i] && guard++ < 20000)
                {
                    view.Advance(1f / 60f);
                    if (guard % 30 == 0) yield return null; // let anything frame-based (cloth, particles) keep up
                }
                if (view.Current == OpeningView.Phase.Done) break;
                yield return new WaitForEndOfFrame();
                string name = Prefix + "_" + i.ToString("00") + "_t" + view.Elapsed.ToString("00.0", CultureInfo.InvariantCulture).Replace('.', '_');
                var file = QaCapture.Capture(null, name, Width, Height);
                log.Append(i > 0 ? "," : "").Append('"').Append(Path.GetFileName(file)).Append('"');
                var st = view.Street;
                Debug.Log($"[Scrub] {name}: t={view.Elapsed:0.000} phase={view.Current} truck={(st != null ? st.TruckFront : -1f):0.00} " +
                          $"eye={(st != null ? st.EyeStreet : Vector3.zero)} yaw={(st != null ? st.HeadYaw : 0f):0.0} cam={(Camera.main != null ? Camera.main.name : "none")}");
            }
            log.Append("]}");
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent/scrub.json")), log.ToString());
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = async;
            if (QuitWhenDone) UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }
    }
}
