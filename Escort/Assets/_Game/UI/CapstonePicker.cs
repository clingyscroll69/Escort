using System;
using System.Collections.Generic;
using HS.Skills;
using HS.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HS.UI
{
    /// <summary>
    /// The capstone reveal at the chapter 4 campfire (GDD §5: "choose 1, revealed at the end of chapter 4"): a System window
    /// with one card per implemented capstone — what it does and how his code reads it — and one choice for the rest of
    /// the road. It never takes a loadout slot; it has its own key. Bots call <see cref="Pick"/>.
    /// </summary>
    public sealed class CapstonePicker : MonoBehaviour
    {
        /// <summary>The capstones this build implements (campaign spec §6).</summary>
        public static readonly string[] Offered = { "domino_effect", "crossfire", "hold_please", "silent_partner" };

        public event Action<string> Chosen;
        public string Picked { get; private set; }

        SkillSystem _sys;
        object _gate;
        readonly List<Button> _buttons = new List<Button>();
        const float CardW = 380f, CardH = 560f, Gap = 24f;

        public static CapstonePicker Show(UIRoot root, SkillSystem sys)
        {
            var go = UIKit.Stretch(root.Overlay, "CapstonePicker").gameObject;
            var p = go.AddComponent<CapstonePicker>();
            p._sys = sys;
            p._gate = ModalGate.Push("capstone", pauseSim: false, blockGameplay: true);
            p.Build();
            return p;
        }

        void Build()
        {
            var cat = SkillCatalog.Load();
            var dim = UIKit.Image(transform, "Dim", null, new Color(0.01f, 0.02f, 0.04f, 0.86f), false);
            dim.raycastTarget = true;
            float w = Offered.Length * CardW + (Offered.Length - 1) * Gap + 80f;
            var window = UIKit.Rect(transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(w, CardH + 190f), Vector2.zero);
            var bg = UIKit.Image(window, "Bg", UIKit.UISprite("card"), new Color(0.035f, 0.06f, 0.1f, 1f));
            bg.pixelsPerUnitMultiplier = 1.4f;
            UIKit.Image(window, "Border", UIKit.Border, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.7f));
            UIKit.Brackets(window, UIKit.Gold, 24f, -5f);
            var title = UIKit.Text(window, "Title", "» SYSTEM: A LAST TRICK UNLOCKED  ·  CHOOSE ONE", UIKit.Mono, 30, UIKit.Gold, TextAlignmentOptions.TopLeft);
            UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -26f), new Vector2(w - 80f, 40f));
            var hint = UIKit.Text(window, "Hint", KeyGlyphs.Format("It stays with you to the end of the road, on a key of its own ({capstone}). It never takes a slot."),
                UIKit.Sans, 20, UIKit.Dim, TextAlignmentOptions.TopLeft);
            UIKit.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -72f), new Vector2(w - 80f, 30f));
            for (int i = 0; i < Offered.Length; i++)
            {
                var def = cat != null ? cat.Get(Offered[i]) : null;
                if (def == null) continue;
                BuildCard(window, def, new Vector2(40f + i * (CardW + Gap), -118f));
            }
            for (int i = 0; i < _buttons.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = _buttons[(i + _buttons.Count - 1) % _buttons.Count];
                nav.selectOnRight = _buttons[(i + 1) % _buttons.Count];
                _buttons[i].navigation = nav;
            }
            if (_buttons.Count > 0 && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
        }

        void BuildCard(RectTransform window, SkillDefinition def, Vector2 pos)
        {
            var card = UIKit.Rect(window, "Card_" + def.id, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CardW, CardH), pos);
            UIKit.Card(card, UIKit.Gold);
            var tile = UIKit.Rect(card, "Tile", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(96f, 96f), new Vector2(0f, -22f));
            UIKit.Image(tile, "Bg", UIKit.Panel, new Color(0.02f, 0.03f, 0.06f, 0.9f));
            UIKit.Image(tile, "Frame", UIKit.UISprite("slot"), new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.9f));
            var icon = UIKit.SpriteImage(tile, "Icon", UIKit.Icon(SkillGuides.IconId(def.id)), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f));
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var name = UIKit.Text(card, "Name", def.displayName.ToUpperInvariant(), UIKit.Mono, 26, UIKit.Gold, TextAlignmentOptions.Top);
            UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -128f), new Vector2(CardW - 32f, 34f));
            var g = SkillGuides.Get(def.id);
            var tag = UIKit.Text(card, "Tagline", g != null ? "<i>" + g.Tagline + "</i>" : "", UIKit.Sans, 19, UIKit.Dim, TextAlignmentOptions.Top);
            UIKit.Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -166f), new Vector2(CardW - 40f, 50f));
            var uses = UIKit.Text(card, "Uses", def.uses, UIKit.Sans, 20, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(uses.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -222f), new Vector2(CardW - 44f, 170f));
            var view = UIKit.Text(card, "CallumView", g != null ? "HIS CODE: " + g.CallumView : "", UIKit.Sans, 17, UIKit.HeroBlue, TextAlignmentOptions.TopLeft);
            UIKit.Place(view.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -398f), new Vector2(CardW - 44f, 80f));
            string id = def.id;
            var b = UIKit.Button(card, "Take_" + def.id, "TAKE IT", new Vector2(CardW - 60f, 56f), Vector2.zero, () => Pick(id));
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 20f);
            _buttons.Add(b);
        }

        /// <summary>Learn it (once, for the run) and close.</summary>
        public void Pick(string id)
        {
            if (Picked != null) return;
            var def = SkillCatalog.Load()?.Get(id);
            if (def == null || !_sys.LearnCapstone(def)) return;
            Picked = id;
            HS.Audio.AudioDirector.Instance?.Play("ui_confirm", null, 0.6f, 0.05f, 0f);
            Close();
            Chosen?.Invoke(id);
        }

        void Close()
        {
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_gate != null) ModalGate.Pop(_gate);
            _gate = null;
        }
    }
}
