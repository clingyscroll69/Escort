using System.Collections;
using HS.Presentation;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Play-mode QA: drives every AnimDriver in the lineup; optional timed capture after an action.</summary>
    public sealed class LineupDriver : MonoBehaviour
    {
        public string Mode = "idle";     // idle | walk | jog | sprint | crouch | combat
        public string Action = "";
        public float CaptureDelay = -1f;
        public string CaptureName = "";
        public Vector3 CamPos = new Vector3(-6.6f, 1.5f, -6.5f);
        public Vector3 CamLook = new Vector3(-6.6f, 1.0f, 0f);
        public float CamFov = 40f;
        AnimDriver[] _drivers;

        void Start()
        {
            _drivers = FindObjectsByType<AnimDriver>(FindObjectsSortMode.InstanceID);
            Apply();
        }

        public void Apply()
        {
            if (_drivers == null) return;
            foreach (var d in _drivers)
            {
                d.SetFlag("combat", Mode == "combat");
                if (!string.IsNullOrEmpty(Action)) d.PlayAction(Action);
            }
            if (CaptureDelay >= 0f && !string.IsNullOrEmpty(CaptureName)) StartCoroutine(CaptureLater());
        }

        IEnumerator CaptureLater()
        {
            yield return new WaitForSeconds(CaptureDelay);
            yield return new WaitForEndOfFrame();
            var go = new GameObject("QaCam");
            var cam = go.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.transform.position = CamPos;
            cam.transform.LookAt(CamLook);
            cam.fieldOfView = CamFov;
            QaCapture.Capture(cam, CaptureName, 1200, 500);
            Destroy(go);
            CaptureName = "";
        }

        void Update()
        {
            float speed = Mode switch { "walk" => 1.2f, "jog" => 5f, "sprint" => 7.5f, "crouch" => 1.5f, _ => 0f };
            foreach (var d in _drivers) d.SetLocomotion(speed, Mode == "crouch");
        }
    }
}
