using System;
using System.Collections.Generic;
using HS.Core;
using HS.Flow;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// Esc / Start on the road or in the duel: the game holds still (ModalGate) and offers RESUME, the FIELD GUIDE,
    /// CONTROLS, SETTINGS (tips, lesson pauses, Hero Insight at start, reset tutorial), RESTART CHAPTER and QUIT (each
    /// of the last two asks twice). Cancel steps back a page or resumes.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        UIRoot _root;
        GameFlow _flow;
        object _gate;
        RectTransform _panel, _page;
        TextMeshProUGUI _title, _sub;
        string _pageId = "main", _armed;
        int _guideClosedFrame = -1;
        readonly List<Button> _buttons = new List<Button>();

        public bool IsOpen { get; private set; }
        public string Page => _pageId;

        public static PauseMenu Create(UIRoot root, GameFlow flow)
        {
            var go = UIKit.Stretch(root.Overlay, "PauseMenu").gameObject;
            var m = go.AddComponent<PauseMenu>();
            m._root = root;
            m._flow = flow;
            m.Build();
            go.SetActive(true);
            m.SetVisible(false);
            return m;
        }

        CanvasGroup _group;

        void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            // Linear colour space: a dim must be strong to read as one.
            var dim = UIKit.Image(transform, "Dim", null, new Color(0.01f, 0.02f, 0.04f, 0.84f), false);
            dim.raycastTarget = true;
            _panel = UIKit.Rect(transform, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(680f, 760f), Vector2.zero);
            UIKit.Image(_panel, "Bg", UIKit.UISprite("card"), new Color(0.035f, 0.06f, 0.1f, 1f)).pixelsPerUnitMultiplier = 1.4f;
            UIKit.Image(_panel, "Border", UIKit.Border, new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.6f));
            UIKit.Brackets(_panel, UIKit.SystemCyan, 22f, -5f);
            _title = UIKit.Text(_panel, "Title", "» PAUSED", UIKit.Mono, 34, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            UIKit.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(600f, 44f));
            _sub = UIKit.Text(_panel, "Sub", "", UIKit.Sans, 20, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(_sub.rectTransform, new Vector2(0f, 1f), new Vector2(42f, -78f), new Vector2(600f, 26f));
            _page = UIKit.Rect(_panel, "Page", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(600f, 620f), new Vector2(0f, -120f));
        }

        void SetVisible(bool on)
        {
            _group.alpha = on ? 1f : 0f;
            _group.blocksRaycasts = on;
            _group.interactable = on;
            _panel.gameObject.SetActive(on);
            transform.GetChild(0).gameObject.SetActive(on); // the dim
        }

        bool CanOpen()
        {
            if (_flow != null && _flow.Current != GameFlow.State.Chapter && _flow.Current != GameFlow.State.Duel) return false;
            if (EndScreen.Current != null || ModalGate.Any) return false;
            return ScreenFade.Instance == null || !ScreenFade.Instance.Busy;
        }

        /// <summary>No-op outside the road and the duel (and over another modal screen).</summary>
        public void Open()
        {
            if (IsOpen || !CanOpen()) return;
            IsOpen = true;
            _gate = ModalGate.Push("pause");
            transform.SetAsLastSibling();
            SetVisible(true);
            ShowPage("main");
            HS.Audio.AudioDirector.Instance?.Play("ui_open", null, 0.5f, 0.1f, 0f);
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (FieldGuide.Current != null) FieldGuide.Current.Close();
            IsOpen = false;
            _armed = null;
            SetVisible(false);
            ModalGate.Pop(_gate);
            _gate = null;
        }

        void OnDestroy()
        {
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
        }

        // ------------------------------------------------------------------------------------------------- pages
        void ShowPage(string id)
        {
            _pageId = id;
            _armed = null;
            for (int i = _page.childCount - 1; i >= 0; i--) Destroy(_page.GetChild(i).gameObject);
            _buttons.Clear();
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as HS.Sidekick.SidekickAgent : null;
            _sub.text = (_flow != null && _flow.Current == GameFlow.State.Duel ? "The Rigged Duel" : "The Old Road") + (sk != null ? $"  ·  Callum's sidekick  ·  level {sk.Level}" : "");
            switch (id)
            {
                case "settings":
                    _title.text = "» SETTINGS";
                    Toggle("TIPS", () => TutorialProgress.TipsEnabled, v => TutorialProgress.TipsEnabled = v);
                    Toggle("BIG LESSONS PAUSE THE GAME", () => TutorialProgress.LessonPauses, v => TutorialProgress.LessonPauses = v);
                    Toggle("HERO INSIGHT AT START", () => TutorialProgress.InsightDefault, v => TutorialProgress.InsightDefault = v);
                    Confirmed("RESET TUTORIAL", "PRESS AGAIN: EVERY TIP SHOWS AGAIN", "reset", () =>
                    {
                        TutorialProgress.ResetSeen();
                        ShowPage("settings");
                    });
                    Add("BACK", () => ShowPage("main"));
                    break;
                case "controls":
                    _title.text = "» CONTROLS";
                    var table = UIKit.Rect(_page, "Table", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(600f, 520f), Vector2.zero);
                    FieldGuide.ControlsTable(table, table.sizeDelta);
                    Add("BACK", () => ShowPage("main"), -548f);
                    break;
                default:
                    _title.text = "» PAUSED";
                    Add("RESUME", Close);
                    Add("FIELD GUIDE", () =>
                    {
                        SetVisible(false);
                        FieldGuide.Show(_root, "tips", () =>
                        {
                            _guideClosedFrame = Time.frameCount; // the guide's own back press mustn't close this too
                            if (!IsOpen) return;
                            SetVisible(true);
                            ShowPage("main");
                        });
                    });
                    Add("CONTROLS", () => ShowPage("controls"));
                    Add("SETTINGS", () => ShowPage("settings"));
                    if (RunState.ChapterStart != null && _flow != null)
                        Confirmed("RESTART CHAPTER", "PRESS AGAIN TO RESTART", "restart", () =>
                        {
                            Close();
                            _flow.Restore("chapter");
                        });
                    Confirmed("QUIT", "PRESS AGAIN TO QUIT", "quit", () =>
                    {
                        Close();
                        if (_flow != null) _flow.Quit();
                    });
                    break;
            }
            WireNavigation();
        }

        Button Add(string label, Action onClick, float? atY = null)
        {
            var b = UIKit.Button(_page, "Pause_" + label.Replace(' ', '_'), label, new Vector2(600f, 66f), Vector2.zero, onClick);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, atY ?? -_buttons.Count * 80f);
            _buttons.Add(b);
            return b;
        }

        void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            Button b = null;
            b = Add(label, () =>
            {
                set(!get());
                Label(b, label, get());
            });
            Label(b, label, get());
        }

        static void Label(Button b, string label, bool on) =>
            b.GetComponentInChildren<TextMeshProUGUI>().text = $"{label}:  {(on ? "<color=#F2C14E>ON</color>" : "<color=#8FB8C8>OFF</color>")}";

        /// <summary>A button that asks twice (the first press relabels it).</summary>
        void Confirmed(string label, string again, string key, Action act)
        {
            Button b = null;
            b = Add(label, () =>
            {
                if (_armed == key)
                {
                    _armed = null;
                    act();
                    return;
                }
                _armed = key;
                b.GetComponentInChildren<TextMeshProUGUI>().text = $"<color=#FF564A>{again}</color>";
            });
        }

        void WireNavigation()
        {
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = _buttons[(i - 1 + _buttons.Count) % _buttons.Count],
                    selectOnDown = _buttons[(i + 1) % _buttons.Count],
                };
            if (_buttons.Count > 0 && EventSystem.current != null && KeyGlyphs.Current == GlyphDevice.Gamepad)
                EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
        }

        // -------------------------------------------------------------------------------------------------- input
        void Update()
        {
            var input = GameInput.Instance;
            if (!IsOpen)
            {
                if (input.Pause.enabled && input.Pause.WasPressedThisFrame()) Open();
                return;
            }
            if (FieldGuide.Current != null || Time.frameCount == _guideClosedFrame) return; // the guide has its own back
            if (input.Cancel.WasPressedThisFrame())
            {
                if (_pageId != "main") ShowPage("main");
                else Close();
            }
        }
    }
}
