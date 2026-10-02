using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The one screen-space canvas (overlay: UI never goes through the scene's post-processing). Layers: World (bubbles
    /// tracking characters), Hud, Windows (System window), Overlay (end screens, menus). QA captures temporarily switch it
    /// to camera space so screenshots include the UI.
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }
        public Canvas Canvas { get; private set; }
        public RectTransform World { get; private set; }
        public RectTransform Hud { get; private set; }
        public RectTransform Windows { get; private set; }
        public RectTransform Overlay { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static UIRoot Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("UIRoot");
            Instance = go.AddComponent<UIRoot>();
            Instance.Build();
            return Instance;
        }

        void Build()
        {
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            World = UIKit.Stretch(transform, "World");
            Hud = UIKit.Stretch(transform, "Hud");
            Windows = UIKit.Stretch(transform, "Windows");
            Overlay = UIKit.Stretch(transform, "Overlay");
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>World position → anchored position in a full-screen layer (null when behind the camera).</summary>
        public bool WorldToLayer(Vector3 world, RectTransform layer, out Vector2 local)
        {
            local = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) return false;
            // Captures render at a different size than the screen: map through viewport space.
            var vp = cam.ScreenToViewportPoint(sp);
            var size = layer.rect.size;
            local = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
            return true;
        }
    }
}
