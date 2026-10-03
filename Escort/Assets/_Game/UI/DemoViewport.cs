using System.Text;
using HS.Core;
using HS.Skills;
using HS.Tutorial;
using HS.Tutorial.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The skill demo window ("» SIMULATION"): the demo stage's picture, its overlay, a SKIP and a REPLAY button
    /// (keys too: <see cref="GameInput.DemoSkip"/>, <see cref="GameInput.DemoReplay"/>), and an end card with the
    /// skill's numbers. Plays on real time, so it runs while the game is paused.
    /// </summary>
    public sealed class DemoViewport : MonoBehaviour
    {
        RectTransform _root;
        RawImage _image;
        DemoOverlay _overlay;
        TextMeshProUGUI _header;
        Button _skip, _replay;
        DemoStage _stage;
        DemoScript _script;
        DemoContext _ctx;
        string _id;
        int _rank;
        float _t;
        bool _playing, _end;

        /// <summary>Tests drive the clock with <see cref="Advance"/>.</summary>
        public bool ManualClock;
        public bool Playing => _playing;
        public float Time => _t;
        public string CaptionText => _overlay != null ? _overlay.CaptionText : "";
        public bool AtEndCard => _end;
        public string SkillId => _id;
        public RectTransform Root => _root;

        public static DemoViewport Create(RectTransform parent, Vector2 size)
        {
            var rt = UIKit.Rect(parent, "DemoViewport", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, Vector2.zero);
            var v = rt.gameObject.AddComponent<DemoViewport>();
            v._root = rt;
            v.Build(size);
            return v;
        }

        void Build(Vector2 size)
        {
            _stage = DemoStage.Ensure();
            _root.gameObject.AddComponent<RectMask2D>(); // marks and bubbles never spill outside the picture
            UIKit.Image(_root, "Back", null, new Color32(9, 17, 30, 255), false);
            _image = UIKit.Stretch(_root, "Picture").gameObject.AddComponent<RawImage>();
            _image.texture = _stage.Texture;
            _image.raycastTarget = false;
            _overlay = DemoOverlay.Create(_root, _stage);
            var frame = UIKit.Image(_root, "Frame", UIKit.Border, new Color(UIKit.SystemCyan.r, UIKit.SystemCyan.g, UIKit.SystemCyan.b, 0.55f));
            frame.raycastTarget = false;
            UIKit.Brackets(_root, UIKit.SystemCyan, 20f, -4f);
            _header = UIKit.Text(_root, "Header", "» SIMULATION", UIKit.Mono, 15, UIKit.SystemCyan, TextAlignmentOptions.TopLeft);
            _header.rectTransform.offsetMin = new Vector2(14f, 0f);
            _header.rectTransform.offsetMax = new Vector2(-14f, -10f);
            UIKit.Outline(_header, 0.2f);
            _skip = SmallButton("DemoSkip", "SKIP »", "demoskip", new Vector2(-12f, -10f), Skip);
            _replay = SmallButton("DemoReplay", "REPLAY", "demoreplay", new Vector2(-12f, -10f), Replay);
            _replay.gameObject.SetActive(false);
        }

        Button SmallButton(string name, string label, string token, Vector2 pos, System.Action onClick)
        {
            var b = UIKit.Button(_root, name, label, new Vector2(150f, 32f), Vector2.zero, onClick);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = pos;
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            t.fontSize = 15;
            t.text = KeyGlyphs.Format("{" + token + "} " + label);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        /// <summary>Play a skill's demo at a rank (restarts it). Skills without a demo show a still card.</summary>
        public void Play(string skillId, int rank)
        {
            Stop();
            _id = skillId;
            _rank = Mathf.Clamp(rank, 1, SkillSystem.MaxRank);
            var def = SkillCatalog.Load()?.Get(skillId);
            _ctx = new DemoContext { Stage = _stage, Overlay = _overlay, Def = def, Rank = _rank };
            _script = SkillDemos.Build(skillId, _ctx);
            _header.text = $"» SIMULATION  ·  <color=#FFFFFF>{(def != null ? def.displayName.ToUpperInvariant() : skillId)}</color>  <size=80%>RANK {(_rank >= 2 ? "II" : "I")}</size>";
            _t = 0f;
            _end = false;
            _playing = true;
            _stage.Rendering = true;
            _skip.gameObject.SetActive(true);
            _replay.gameObject.SetActive(false);
            if (_script == null) Finish();
            else Advance(0f);
        }

        public void Skip()
        {
            if (!_playing || _end) return;
            _t = _script != null ? _script.Length : 0f;
            _script?.Run(_ctx, _t);
            Finish();
        }

        public void Replay()
        {
            if (_id != null) Play(_id, _rank);
        }

        public void Stop()
        {
            _playing = false;
            _end = false;
            _script = null;
            if (_stage != null)
            {
                _stage.Clear();
                _stage.Rendering = false;
            }
            if (_overlay != null) _overlay.Clear(); // (Unity null: it may already be gone when the picker closes)
        }

        void Finish()
        {
            _end = true;
            _skip.gameObject.SetActive(false);
            _replay.gameObject.SetActive(true);
            var def = SkillCatalog.Load()?.Get(_id);
            _overlay.EndCard(def != null ? def.displayName.ToUpperInvariant() + (_rank >= 2 ? "  II" : "") : _id, EndCardText(def, _rank));
        }

        /// <summary>The numbers at this rank, and how Callum's code reads it.</summary>
        public static string EndCardText(SkillDefinition def, int rank)
        {
            if (def == null) return "";
            var sb = new StringBuilder();
            foreach (var (label, r1, r2) in SkillGuides.StatLines(def))
            {
                if (sb.Length > 0) sb.Append("   ·   ");
                sb.Append(label).Append(' ').Append("<b>").Append(rank >= 2 ? r2 : r1).Append("</b>");
            }
            sb.Append("\n<size=85%><color=#F2C14E>").Append(SkillGuides.Conduct(def)).Append("</color></size>");
            return sb.ToString();
        }

        public void Advance(float dt)
        {
            if (!_playing) return;
            if (!_end)
            {
                _t += dt;
                _script?.Run(_ctx, _t);
                _overlay.Caption(_script?.CaptionAt(_t));
                if (_script != null && _t >= _script.Length) Finish();
            }
            _overlay.Tick(_t, dt);
        }

        void Update()
        {
            var input = GameInput.Instance;
            if (_playing && !_end && input.DemoSkip.WasPressedThisFrame()) Skip();
            else if (_id != null && input.DemoReplay.WasPressedThisFrame()) Replay();
            if (!ManualClock) Advance(UnityEngine.Time.unscaledDeltaTime);
        }

        void OnDisable() => Stop();
    }
}
