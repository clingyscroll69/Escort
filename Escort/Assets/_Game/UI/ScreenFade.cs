using System;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>Full-screen fade for scene transitions (chapter → camp → duel). Real time, so it works while paused.</summary>
    public sealed class ScreenFade : MonoBehaviour
    {
        public static ScreenFade Instance { get; private set; }
        Image _img;
        float _alpha, _target;
        Action _atBlack;
        const float Speed = 2.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static ScreenFade Ensure(UIRoot root)
        {
            if (Instance != null) return Instance;
            var img = UIKit.Image(root.Overlay, "Fade", null, Color.black, false);
            img.raycastTarget = false;
            Instance = img.gameObject.AddComponent<ScreenFade>();
            Instance._img = img;
            img.color = new Color(0f, 0f, 0f, 0f);
            img.transform.SetAsLastSibling();
            return Instance;
        }

        public bool Busy => _atBlack != null || _alpha > 0.001f;

        /// <summary>Fade to black, run the action, fade back in.</summary>
        public void Through(Action atBlack)
        {
            _atBlack = atBlack ?? (() => { });
            _target = 1f;
            transform.SetAsLastSibling();
        }

        void Update()
        {
            _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime * Speed);
            _img.color = new Color(0f, 0f, 0f, _alpha);
            _img.raycastTarget = _alpha > 0.5f;
            if (_target >= 1f && _alpha >= 1f && _atBlack != null)
            {
                var a = _atBlack;
                _atBlack = null;
                a();
                _target = 0f;
            }
        }
    }
}
