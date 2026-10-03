using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Flow;
using HS.Skills;
using HS.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS.Tests
{
    /// <summary>Shared helpers for UI and tutorial tests: real clicks through the EventSystem, waits, clean-up.</summary>
    public static class TestUi
    {
        /// <summary>Click the middle of a UI element through the EventSystem: whatever is drawn on top there takes it.</summary>
        public static void Click(string name)
        {
            var target = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
                .FirstOrDefault(r => r.name == name && r.gameObject.activeInHierarchy);
            Assert.IsNotNull(target, name + " is on screen");
            var ped = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center)),
                button = PointerEventData.InputButton.Left,
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            Assert.IsNotEmpty(hits, name + " takes clicks");
            Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(target), $"{name} is under {hits[0].gameObject.name}");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, ped, ExecuteEvents.pointerClickHandler);
        }

        public static IEnumerator WaitUntil(Func<bool> cond, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!cond())
            {
                if (Time.realtimeSinceStartup > until) Assert.Fail($"timed out after {seconds:0.#} s waiting for {what}");
                yield return null;
            }
        }

        /// <summary>Everything a flow or a UI test can leave behind.</summary>
        public static void TearDownAll()
        {
            foreach (var a in UnityEngine.Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a) UnityEngine.Object.DestroyImmediate(a.gameObject);
            foreach (var n in new[]
                     {
                         "TutorialDirector", "OpeningStreet", "Chapter", "OpportunityDirector", "StoneSystem", "UIRoot", "CameraRig", "Encounters",
                         "Main Camera", "GameFlow", "AudioDirector", "Campfire", "RiggedDuel", "TestSun", "SkillDemoStage",
                     })
            {
                var g = GameObject.Find(n);
                while (g)
                {
                    UnityEngine.Object.DestroyImmediate(g);
                    g = GameObject.Find(n);
                }
            }
            foreach (var p in UnityEngine.Object.FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(p.gameObject);
            var ctx = UnityEngine.Object.FindAnyObjectByType<RunContext>();
            if (ctx) UnityEngine.Object.DestroyImmediate(ctx.gameObject);
            if (SimLoop.Instance) UnityEngine.Object.DestroyImmediate(SimLoop.Instance.gameObject);
            ModalGate.Clear();
            RunState.Clear();
        }

        /// <summary>
        /// A player's chapter (no AutoPlay, so the tutorial is on), skipping the opening by resuming from a chapter-start
        /// Restore Point that already knows Pocket Sand.
        /// </summary>
        public static GameFlow StartChapterAsPlayer(params string[] skills)
        {
            var point = new RunState.Point { Seed = 1, Level = 1, Xp = 0, HeroStage = Stage.S0 };
            foreach (var id in skills.Length > 0 ? skills : new[] { "pocket_sand" })
            {
                point.Skills.Add((id, 1));
                var def = SkillCatalog.Load().Get(id);
                if (def != null && def.UsesSlot) point.Loadout.Add(id);
            }
            RunState.ChapterStart = point;
            RunState.Resume = "chapter";
            return new GameObject("GameFlow").AddComponent<GameFlow>();
        }

        /// <summary>Hand the sidekick to a script (the real keyboard can't interfere).</summary>
        public static HS.Sidekick.ScriptedCommands Script(GameFlow flow)
        {
            var sk = flow.Chapter.Sidekick;
            var pc = sk.GetComponent<HS.Sidekick.PlayerCommands>();
            if (pc != null) pc.enabled = false;
            var s = new HS.Sidekick.ScriptedCommands();
            sk.Commands = s;
            return s;
        }
    }
}
