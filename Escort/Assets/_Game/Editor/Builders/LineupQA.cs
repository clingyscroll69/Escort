using HS.QA;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    public static class LineupQA
    {
        /// <summary>Play-mode: set the lineup program. Args Library/Agent/lineup_args.json {"mode":"jog","action":"salute"}</summary>
        [MenuItem("Tools/HS/QA/Lineup Set")]
        public static void Set()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("lineup_args.json");
            var d = Object.FindAnyObjectByType<LineupDriver>();
            if (d == null) return;
            d.Mode = args.TryGetValue("mode", out var m) ? m : "idle";
            d.Action = args.TryGetValue("action", out var a) ? a : "";
            d.CaptureDelay = args.TryGetValue("delay", out var dl) ? float.Parse(dl, System.Globalization.CultureInfo.InvariantCulture) : -1f;
            d.CaptureName = args.TryGetValue("capture", out var cn) ? cn : "";
            d.Apply();
        }
    }
}
