using System;
using HS.Core;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rapport;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.Tutorial;
using HS.UI;
using UnityEngine;

namespace HS.Flow
{
    /// <summary>
    /// The campfire (GDD §4.6 / slice: "one campfire scene in two variants"). On arrival the hidden Stage check runs
    /// (the slice's check acts like the Ch2 one: max S1) and the scene shows it: cold (S0) — he sits apart, back to you,
    /// a small fire, he recites the Code at you; warm (S1) — a bigger fire, he faces you, and notices. Rest treats his
    /// worst wound and heals you both; stones relay their clips; level-up picks and the loadout; then a Restore Point.
    /// </summary>
    public sealed class CampfireDirector : MonoBehaviour
    {
        public bool Warm { get; private set; }
        public Stage StageAfter { get; private set; }
        public string RestNote { get; private set; }
        public SkillPicker Picker { get; private set; }
        /// <summary>The fireside lines have played (or were skipped): the level-up is open.</summary>
        public bool SceneDone { get; private set; }
        /// <summary>How long the fireside lines take before the level-up opens on its own.</summary>
        public float SceneLength => _lines == null ? 0f : 0.6f + _lines.Length * 4.2f + 0.8f;
        public event Action Finished;
        public float AutoSceneSeconds = 15f;
        bool _finished;
        HS.Sidekick.ISidekickCommands _savedCommands;
        HS.Presentation.CameraRig _rig;
        Transform _fire;
        float _savedLookAhead;

        HeroAgent _hero;
        SidekickAgent _sk;
        Light _light;
        Transform _flame;
        float _t;
        int _line;
        int _pendingPicks;
        SidekickSkills _skills;
        RectTransform _skipChip;
        bool _sawDishonour;
        string[] _lines;

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public void Begin(RoomModule camp, HeroAgent hero, SidekickAgent sk, int picks, bool autoPicks, string[] autoPickIds)
        {
            var ctx = RunContext.Current;
            _hero = hero;
            _sk = sk;
            var ledger = ctx.Get<RapportLedger>();
            // 1) The hidden Stage check.
            float rate = ledger != null ? ledger.CaptureRate : 0f;
            StageAfter = StageEvaluator.Evaluate(hero.Stage, rate, StageCheck.Chapter2);
            hero.ApplyStage(StageAfter);
            Warm = StageAfter >= Stage.S1;
            var cm = hero.Module as CallumModule;
            _sawDishonour = cm != null && cm.Honor < cm.T.honorMax;
            // 2) Places: warm — by the fire, facing you; cold — apart, facing the dark.
            var heroSeat = Find(camp.transform, "HeroSeat");
            var skSeat = Find(camp.transform, "SidekickSeat");
            var fire = Find(camp.transform, "Fire");
            var heroPos = Warm ? heroSeat.position : heroSeat.position + (heroSeat.position - fire.position).normalized * 2.6f;
            hero.Route.SetNodes(new System.Collections.Generic.List<HS.Hero.RouteNode>());
            hero.Motor.Teleport(heroPos + Vector3.up * 0.05f);
            var face = Warm ? Geo.DirTo(heroPos, skSeat.position) : Geo.DirTo(fire.position, heroPos);
            hero.Motor.FaceInstant(face);
            sk.Motor.Teleport(skSeat.position + Vector3.up * 0.05f);
            sk.Motor.FaceInstant(Geo.DirTo(skSeat.position, fire.position));
            sk.Presenter?.PlayAction("kneel", 30f);
            // The scene owns the pair: no player/bot steering, and the camera frames the fire, not the road ahead.
            _savedCommands = sk.Commands;
            sk.Commands = null;
            var rig = FindAnyObjectByType<HS.Presentation.CameraRig>();
            if (rig != null)
            {
                _rig = rig;
                _savedLookAhead = rig.LookAheadDistance;
                rig.LookAheadDistance = 0f;
                rig.AddFocus(fire, 1f, 2.5f);
                _fire = fire;
            }
            // 3) The fire.
            _flame = HS.Presentation.Vfx.FireAt(fire, Warm ? 1.25f : 0.7f);
            var lgo = new GameObject("FireLight");
            lgo.transform.SetParent(fire, false);
            lgo.transform.localPosition = Vector3.up * 0.6f;
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.62f, 0.3f);
            _light.range = Warm ? 9f : 5f;
            _light.intensity = Warm ? 3.2f : 1.4f;
            // 4) Rest.
            var tw = hero.Wounds.All.Count > 0 ? hero.Wounds.All[0] : (WoundType?)null;
            bool treated = hero.Wounds.TreatWorst();
            hero.Health.Heal(99999f);
            sk.Health.Heal(99999f);
            RestNote = treated ? "Rest: one of Callum's wounds is tended. You are both rested." : "Rest: you are both rested.";
            ctx.Events.RaiseNotice(RestNote);
            // 5) Stones relay what they saw.
            ctx.Get<StoneSystem>()?.RelayAll();
            // 6) The scene's lines (draft copy for the owner; GDD Ch1 beat: he recites the Code).
            _lines = Warm
                ? new[] { "callum|Sit. The fire's big enough for two.", "callum|\"Strike the ready. Spare the yielded. Never the back.\" My father's words. Tonight they feel lighter.",
                          "callum|You were... useful today. I noticed.", "sidekick~Was that a compliment?" }
                : new[] { "callum|We camp here. I'll take the first watch.", "callum|\"Strike the ready. Spare the yielded. Never the back.\" The Code. Learn it.",
                          _sawDishonour ? "callum|And keep your sand in your pockets." : "callum|Fortune favoured us today.", "sidekick~He hasn't looked at me once." };
            // 7) Level-up picks + loadout.
            var skills = sk.GetComponent<SidekickSkills>();
            if (autoPicks || UIRoot.Instance == null)
            {
                skills.System.AtCamp = true;
                foreach (var id in autoPickIds) if (picks > 0 && skills.Learn(id)) picks--;
                skills.System.AtCamp = false;
                Invoke(nameof(Finish), AutoSceneSeconds); // bots still sit through the scene (QA watches it)
            }
            else
            {
                // The scene first (it says what he makes of you), then the level-up; a confirm skips straight to it.
                _skills = skills;
                _pendingPicks = picks;
                _skipChip = SkipChip();
            }
        }

        RectTransform SkipChip()
        {
            var root = UIRoot.Instance;
            if (root == null) return null;
            var rt = UIKit.Rect(root.Overlay, "CampSkip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(420f, 40f), new Vector2(0f, 150f));
            var key = UIKit.Keycap(rt, "Key", KeyGlyphs.Label("confirm", KeyGlyphs.Current), 30f);
            key.anchorMin = key.anchorMax = new Vector2(0f, 0.5f);
            key.pivot = new Vector2(0f, 0.5f);
            key.anchoredPosition = Vector2.zero;
            var t = UIKit.Text(rt, "Text", "SKIP TO THE LEVEL-UP", UIKit.Mono, 20, UIKit.Dim, TMPro.TextAlignmentOptions.Left);
            t.rectTransform.offsetMin = new Vector2(key.sizeDelta.x + 12f, 0f);
            UIKit.Outline(t, 0.2f);
            return rt;
        }

        /// <summary>Open the level-up now (the confirm key during the fireside scene does this).</summary>
        public void SkipScene()
        {
            if (SceneDone || _finished) return;
            SceneDone = true;
            if (_lines != null) _line = _lines.Length; // the rest of the scene stays unsaid
            if (_skipChip != null) Destroy(_skipChip.gameObject);
            if (_skills == null || UIRoot.Instance == null) return;
            Picker = SkillPicker.Show(UIRoot.Instance, _skills.System, _pendingPicks, "» CAMP  ·  LEVEL UP", true, "CONTINUE  »  THE RIGGED DUEL");
            Picker.Done += Finish;
        }

        void Update()
        {
            if (_lines == null || _finished) return;
            _t += Time.deltaTime;
            if (_skills != null && !SceneDone && (_t >= SceneLength || GameInput.Instance.Confirm.WasPressedThisFrame())) SkipScene();
            if (_light != null) _light.intensity *= 1f + 0.06f * Mathf.Sin(_t * 17f) * Time.deltaTime * 10f;
            if (_line < _lines.Length && _t >= 0.6f + _line * 4.2f)
            {
                var l = _lines[_line++];
                var ctx = RunContext.Current;
                if (l.Contains("~")) ctx?.Events.RaiseThought(l.Split('~')[1]);
                else ctx?.Events.RaiseBark(l.Split('|')[0], l.Split('|')[1], 3.8f, 3);
            }
        }

        void OnDestroy()
        {
            if (_skipChip != null) Destroy(_skipChip.gameObject);
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_skipChip != null) Destroy(_skipChip.gameObject);
            if (_sk != null)
            {
                _sk.Presenter?.PlayAction("none");
                _sk.Commands = _savedCommands;
            }
            if (_rig != null)
            {
                _rig.LookAheadDistance = _savedLookAhead;
                _rig.RemoveFocus(_fire);
            }
            Finished?.Invoke();
        }
    }
}
