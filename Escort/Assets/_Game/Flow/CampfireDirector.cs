using System;
using System.Linq;
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
    /// The campfire at the end of chapters 1–4 (GDD §4.6). On arrival the chapter's hidden Stage check runs, if it has one
    /// (end of Ch2: max S1; end of Ch3: max S2), and the scene shows where he stands: cold — he sits apart, back to you, a
    /// small fire; neutral — by the fire, facing it; warm — a bigger fire, he faces you, and notices. Rest treats his worst
    /// wound and heals you both; stones relay their clips; level-up picks and the loadout; then the next chapter.
    /// </summary>
    public sealed class CampfireDirector : MonoBehaviour
    {
        public sealed class Options
        {
            public int Chapter = 2;
            /// <summary>The hidden Stage check this campfire runs (null: none).</summary>
            public StageCheck? Check = StageCheck.Chapter2;
            public int Picks;
            public bool AutoPicks;
            public string[] AutoPickIds = new string[0];
            public string ContinueLabel = "CONTINUE";
            /// <summary>Chapter 2: he learns Recall here (GDD §4.6), and says so in his own Stage's words.</summary>
            public bool LearnsRecall;
            /// <summary>Chapter 3: the dossier scrap is read by the fire.</summary>
            public bool ReadsDossier;
            /// <summary>Chapter 4: after the level-up, the capstone is revealed (one of four, for the rest of the run).</summary>
            public bool CapstoneReveal;
            /// <summary>AutoPlay's capstone (bots, the harness).</summary>
            public string AutoCapstone = "hold_please";
        }

        public CapstonePicker Capstones { get; private set; }

        public CampfireVariant Variant { get; private set; }
        public bool Warm => Variant == CampfireVariant.Warm;
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
        Options _options;
        string[] _lines;

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>The slice's call: its check acts as the chapter 2 one (max S1).</summary>
        public void Begin(RoomModule camp, HeroAgent hero, SidekickAgent sk, int picks, bool autoPicks, string[] autoPickIds) =>
            Begin(camp, hero, sk, new Options { Chapter = 2, Check = StageCheck.Chapter2, Picks = picks, AutoPicks = autoPicks, AutoPickIds = autoPickIds ?? new string[0] });

        public void Begin(RoomModule camp, HeroAgent hero, SidekickAgent sk, Options o)
        {
            var ctx = RunContext.Current;
            _hero = hero;
            _sk = sk;
            _options = o;
            var ledger = ctx.Get<RapportLedger>();
            // 1) The hidden Stage check (only the chapters that have one: GDD §4.4, end of Ch2 and Ch3).
            if (o.Check.HasValue)
            {
                float rate = ledger != null ? ledger.CaptureRate : 0f;
                hero.ApplyStage(StageEvaluator.Evaluate(hero.Stage, rate, o.Check.Value));
            }
            StageAfter = hero.Stage;
            bool penalised = ledger != null && ledger.RawPenaltiesIn(o.Chapter) > 0f;
            Variant = CampfireScenes.VariantFor(o.Chapter, StageAfter, penalised);
            var cm = hero.Module as CallumModule;
            _sawDishonour = cm != null && cm.Honor < cm.T.honorMax;
            // 2) Places: warm — by the fire, facing you; neutral — by the fire, facing it; cold — apart, facing the dark.
            var heroSeat = Find(camp.transform, "HeroSeat");
            var skSeat = Find(camp.transform, "SidekickSeat");
            var fire = Find(camp.transform, "Fire");
            var heroPos = Variant == CampfireVariant.Cold ? heroSeat.position + (heroSeat.position - fire.position).normalized * 2.6f : heroSeat.position;
            hero.Route.SetNodes(new System.Collections.Generic.List<HS.Hero.RouteNode>());
            hero.Motor.Teleport(heroPos + Vector3.up * 0.05f);
            var face = Variant == CampfireVariant.Warm ? Geo.DirTo(heroPos, skSeat.position)
                : Variant == CampfireVariant.Neutral ? Geo.DirTo(heroPos, fire.position) : Geo.DirTo(fire.position, heroPos);
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
            float fireScale = Variant == CampfireVariant.Warm ? 1.25f : Variant == CampfireVariant.Neutral ? 1f : 0.7f;
            float warmth = (fireScale - 0.7f) / 0.55f;
            _flame = HS.Presentation.Vfx.FireAt(fire, fireScale);
            var lgo = new GameObject("FireLight");
            lgo.transform.SetParent(fire, false);
            lgo.transform.localPosition = Vector3.up * 0.6f;
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.62f, 0.3f);
            _light.range = 5f + 4f * warmth;
            _light.intensity = 1.4f + 1.8f * warmth;
            // 4) Rest.
            var tw = hero.Wounds.All.Count > 0 ? hero.Wounds.All[0] : (WoundType?)null;
            bool treated = hero.Wounds.TreatWorst();
            hero.Hunger.Feed();
            hero.Health.Heal(99999f);
            sk.Health.Heal(99999f);
            RestNote = treated ? "Rest: one of Callum's wounds is tended. You are both rested." : "Rest: you are both rested.";
            ctx.Events.RaiseNotice(RestNote);
            // 5) Stones relay what they saw.
            var stones = ctx.Get<StoneSystem>();
            stones?.RelayAll();
            // Scouts nobody caught send their reports too (GDD §4.3).
            foreach (var scout in HS.Curator.Scout.All.ToArray()) scout.Report(stones);
            // 6) The scene's lines (draft copy for the owner; GDD §6.1 campfire beats).
            _lines = CampfireScenes.Lines(o.Chapter, Variant, _sawDishonour);
            if (o.ReadsDossier)
            {
                ctx.Get<HS.Curator.Dossier>()?.Add(HS.Curator.Dossier.CallumScrap);
                ctx.Events.RaiseNotice("A PAGE FROM THE VAULT, IN A COLLECTOR'S HAND:\n<size=80%>" + HS.Curator.Dossier.CallumScrap + "</size>");
                var l = new System.Collections.Generic.List<string>(_lines);
                l.Insert(Mathf.Min(1, l.Count), CampfireScenes.DossierLine(StageAfter));
                _lines = l.ToArray();
            }
            if (o.LearnsRecall)
            {
                var l = new System.Collections.Generic.List<string>(_lines);
                l.Insert(l.Count - 1, CampfireScenes.RecallLine(StageAfter));
                _lines = l.ToArray();
            }
            // 7) Level-up picks + loadout.
            var skills = sk.GetComponent<SidekickSkills>();
            int picks = o.Picks;
            if (o.AutoPicks || UIRoot.Instance == null)
            {
                skills.System.AtCamp = true;
                foreach (var id in o.AutoPickIds) if (picks > 0 && skills.Learn(id)) picks--;
                skills.System.AtCamp = false;
                if (o.CapstoneReveal && skills.System.Capstone == null)
                    skills.System.LearnCapstone(SkillCatalog.Load()?.Get(o.AutoCapstone) ?? SkillCatalog.Load()?.Get(CapstonePicker.Offered[0]));
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
            Picker = SkillPicker.Show(UIRoot.Instance, _skills.System, _pendingPicks, "» CAMP  ·  LEVEL UP", true, _options.ContinueLabel);
            Picker.Done += AfterLevelUp;
        }

        /// <summary>Chapter 4: one last trick, chosen once for the run (GDD §5 capstones), before the road goes on.</summary>
        void AfterLevelUp()
        {
            if (!_options.CapstoneReveal || _skills.System.Capstone != null || UIRoot.Instance == null)
            {
                Finish();
                return;
            }
            Capstones = CapstonePicker.Show(UIRoot.Instance, _skills.System);
            Capstones.Chosen += id =>
            {
                RunContext.Current?.Events.RaiseNotice("NEW TRICK: " + (SkillCatalog.Load()?.Get(id)?.displayName ?? id).ToUpperInvariant()
                    + "\n<size=80%>" + KeyGlyphs.Format("On its own key ({capstone}).") + "</size>");
                Finish();
            };
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
