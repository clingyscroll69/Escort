using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HS.Core;
using HS.Flow;
using HS.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace HS.EditorTools
{
    /// <summary>
    /// QA: watch and drive the game while it plays in the editor, the way a player would. Status → Library/Agent/live.json;
    /// a capture with the UI; a click on a named button through the EventSystem (whatever is on top there gets it); keys
    /// held through the Input System. Args: Library/Agent/live_args.json.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveProbe
    {
        static readonly List<string> Errors = new List<string>();
        static int _errorCount;

        static LiveProbe()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            Errors.Clear();
            _errorCount = 0;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (!EditorApplication.isPlaying || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (msg.Contains("McpUnity")) return;
            _errorCount++;
            if (Errors.Count < 20) Errors.Add(type + ": " + msg.Split('\n')[0] + " @ " + (stack ?? "").Split('\n').FirstOrDefault());
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "[{0:0.00},{1:0.00},{2:0.00}]", v.x, v.y, v.z);
        static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        [MenuItem("Tools/HS/QA/Live Status")]
        public static void Status()
        {
            var o = new List<string>();
            void S(string k, string v) => o.Add($"\"{k}\":\"{Esc(v)}\"");
            void N(string k, string raw) => o.Add($"\"{k}\":{raw}");
            N("playing", EditorApplication.isPlaying ? "true" : "false");
            N("realtime", F(Time.realtimeSinceStartup));
            N("timeScale", F(Time.timeScale));
            N("frame", Time.frameCount.ToString());
            N("fps", F(Time.unscaledDeltaTime > 0 ? 1f / Time.unscaledDeltaTime : 0f));
            S("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            var flow = Object.FindAnyObjectByType<GameFlow>();
            S("flow", flow != null ? flow.Current.ToString() : "-");
            var op = Object.FindAnyObjectByType<OpeningView>();
            S("opening", op != null ? $"{op.Current} t={op.Elapsed:0.00} short={op.Short} audio={op.AudioScheduled}" : "-");
            S("simPaused", SimLoop.Instance != null ? SimLoop.Instance.Paused.ToString() : "-");
            var main = Camera.main;
            S("mainCamera", main != null ? $"{main.name} enabled={main.enabled} pos={V(main.transform.position)}" : "none");
            S("cameras", string.Join(" | ", Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                .Select(c => $"{c.name} go={c.gameObject.activeInHierarchy} en={c.enabled} depth={c.depth} tag={c.tag}")));
            S("lights", string.Join(" | ", Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                .Where(l => l.type == LightType.Directional).Select(l => $"{l.name} en={l.enabled} go={l.gameObject.activeInHierarchy} i={F(l.intensity)}")));
            S("render", $"sun={(RenderSettings.sun ? RenderSettings.sun.name : "none")} fog={RenderSettings.fog} {F(RenderSettings.fogStartDistance)}-{F(RenderSettings.fogEndDistance)} ambient={RenderSettings.ambientMode} sky={(RenderSettings.skybox ? RenderSettings.skybox.name : "none")}");
            N("listeners", Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled).ToString());
            if (flow != null && flow.Chapter != null && flow.Chapter.Hero != null)
            {
                S("hero", $"{V(flow.Chapter.Hero.transform.position)} alive={flow.Chapter.Hero.IsAlive}");
                S("sidekick", $"{V(flow.Chapter.Sidekick.transform.position)} alive={flow.Chapter.Sidekick.IsAlive}");
            }
            var gi = EditorApplication.isPlaying ? GameInput.Instance : null;
            if (gi != null) S("input", $"gameplay={gi.Gameplay.enabled} ui={gi.UI.enabled} move={gi.Move.ReadValue<Vector2>()} kb={(Keyboard.current != null && Keyboard.current.enabled)} bg={InputSystem.settings.backgroundBehavior} editor={InputSystem.settings.editorInputBehaviorInPlayMode}");
            var root = UIRoot.Instance;
            if (root != null)
            {
                string Kids(RectTransform layer) => string.Join(",", layer.Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(t => t.name));
                S("ui", $"windows=[{Kids(root.Windows)}] overlay=[{Kids(root.Overlay)}] hud=[{Kids(root.Hud)}]");
            }
            if (EventSystem.current != null)
            {
                var hits = Raycast(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                S("centerHit", hits.Count > 0 ? Path(hits[0].gameObject.transform) : "-");
                S("eventSystem", $"{EventSystem.current.name} module={(EventSystem.current.currentInputModule ? EventSystem.current.currentInputModule.GetType().Name : "none")}");
            }
            N("screen", $"[{Screen.width},{Screen.height}]");
            N("errorCount", _errorCount.ToString());
            o.Add("\"errors\":[" + string.Join(",", Errors.Select(e => "\"" + Esc(e) + "\"")) + "]");
            HS.Agent.AgentBridge.Write("live.json", "{" + string.Join(",", o) + "}");
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        static List<RaycastResult> Raycast(Vector2 screen)
        {
            var hits = new List<RaycastResult>();
            if (EventSystem.current == null) return hits;
            var ped = new PointerEventData(EventSystem.current) { position = screen };
            EventSystem.current.RaycastAll(ped, hits);
            return hits;
        }

        /// <summary>Enter play mode without waiting on it (the MCP play call blocks until it times out).</summary>
        [MenuItem("Tools/HS/QA/Live Play")]
        public static void Play() => EditorApplication.EnterPlaymode();

        /// <summary>Args {"name":"shot"}: the game camera plus the UI, as the player sees it.</summary>
        [MenuItem("Tools/HS/QA/Live Capture")]
        public static void Capture()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("live_args.json");
            var file = HS.QA.QaCapture.Capture(null, args.TryGetValue("name", out var n) ? n : "live", 1600, 900);
            HS.Agent.AgentBridge.Write("live_capture.json", $"{{\"file\":\"{Esc(file)}\"}}");
        }

        /// <summary>
        /// Args {"target":"GameObject name"}: click the middle of that UI element through the EventSystem, so whatever is
        /// drawn on top there takes the click (as it would for a player). Reports what was hit → live_click.json.
        /// </summary>
        [MenuItem("Tools/HS/QA/Live Click")]
        public static void Click()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("live_args.json");
            string name = args.TryGetValue("target", out var n) ? n : "";
            var target = Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == name && r.gameObject.activeInHierarchy);
            if (target == null)
            {
                HS.Agent.AgentBridge.Write("live_click.json", $"{{\"ok\":false,\"error\":\"no active '{Esc(name)}'\"}}");
                return;
            }
            var screen = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
            var hits = Raycast(screen);
            var top = hits.Count > 0 ? hits[0].gameObject : null;
            bool onTarget = top != null && top.transform.IsChildOf(target);
            var ped = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left, clickCount = 1 };
            GameObject handled = null;
            if (top != null)
            {
                ped.pointerPressRaycast = hits[0];
                ped.pointerCurrentRaycast = hits[0];
                ExecuteEvents.ExecuteHierarchy(top, ped, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(top, ped, ExecuteEvents.pointerUpHandler);
                handled = ExecuteEvents.ExecuteHierarchy(top, ped, ExecuteEvents.pointerClickHandler);
            }
            HS.Agent.AgentBridge.Write("live_click.json",
                $"{{\"ok\":{(onTarget && handled != null ? "true" : "false")},\"screen\":\"{screen}\",\"top\":\"{Esc(top ? Path(top.transform) : "-")}\",\"handled\":\"{Esc(handled ? handled.name : "-")}\",\"hits\":{hits.Count}}}");
        }

        /// <summary>Args {"action":"killhero" | "playagain" | "restore:chapter" | "restore:campfire"}: jump to a flow branch.</summary>
        [MenuItem("Tools/HS/QA/Live Invoke")]
        public static void Invoke()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("live_args.json");
            string action = args.TryGetValue("action", out var a) ? a : "";
            var flow = Object.FindAnyObjectByType<GameFlow>();
            string result = "ok";
            if (flow == null) result = "no GameFlow";
            else if (action == "killhero")
            {
                var hero = flow.Chapter.Hero;
                hero.Health.ApplyDamage(DamageInfo.Make(null, hero, 1e6f, DamageKind.Environment, "qa"));
            }
            else if (action == "playagain") flow.PlayAgain();
            else if (action.StartsWith("restore:")) flow.Restore(action.Substring(8));
            else result = "unknown action " + action;
            HS.Agent.AgentBridge.Write("live_invoke.json", $"{{\"result\":\"{Esc(result)}\"}}");
        }

        static float _releaseAt = -1f;

        /// <summary>Args {"keys":"w,d","seconds":"2"}: hold keyboard keys through the Input System (as a player would).</summary>
        [MenuItem("Tools/HS/QA/Live Keys")]
        public static void Keys()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("live_args.json");
            var kb = Keyboard.current;
            if (kb == null)
            {
                HS.Agent.AgentBridge.Write("live_keys.json", "{\"ok\":false,\"error\":\"no keyboard\"}");
                return;
            }
            var keys = (args.TryGetValue("keys", out var k) ? k : "w").Split(',')
                .Select(s => (Key)System.Enum.Parse(typeof(Key), s.Trim(), true)).ToArray();
            float seconds = args.TryGetValue("seconds", out var sec) ? float.Parse(sec, CultureInfo.InvariantCulture) : 1f;
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            _releaseAt = Time.realtimeSinceStartup + seconds;
            EditorApplication.update -= Release;
            EditorApplication.update += Release;
            HS.Agent.AgentBridge.Write("live_keys.json", $"{{\"ok\":true,\"keys\":\"{string.Join(",", keys)}\",\"seconds\":{F(seconds)}}}");
        }

        static void Release()
        {
            if (Time.realtimeSinceStartup < _releaseAt && EditorApplication.isPlaying) return;
            EditorApplication.update -= Release;
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }
    }
}
