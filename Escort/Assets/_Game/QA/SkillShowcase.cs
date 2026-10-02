using System.Collections;
using HS.Core;
using HS.Enemies;
using HS.Sidekick;
using HS.Skills;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Play-mode QA: scripted sidekick demonstrates each slice skill in a real room; timed captures.</summary>
    public sealed class SkillShowcase : MonoBehaviour, ISidekickCommands
    {
        public Vector3 CamPos = new Vector3(0f, 16f, 8f);
        public Vector3 CamLook = new Vector3(-2f, 0f, 22f);
        SidekickCommand _next = SidekickCommand.None;
        SidekickAgent _sk;
        SidekickSkills _skills;

        public SidekickCommand Next(SidekickAgent self)
        {
            var c = _next;
            _next.Skill = -1;
            _next.Ping = false;
            _next.Attack = false;
            _next.Dodge = false;
            _next.CrouchToggle = false;
            return c;
        }

        IEnumerator Start()
        {
            _sk = FindAnyObjectByType<SidekickAgent>();
            _skills = _sk.GetComponent<SidekickSkills>();
            _sk.Commands = this;
            var ctx = RunContext.Current;
            ctx.Sidekick = _sk;
            ctx.Hero = FindAnyObjectByType<HS.Hero.HeroAgent>();
            yield return new WaitForSeconds(0.5f);
            foreach (var id in new[] { "loosen_bolt", "pocket_sand", "crossbow", "bandage" }) _skills.Learn(id);
            // 1) Arm the barrel stack (slot 0 = loosen_bolt)
            _next = new SidekickCommand { Skill = 0, AimPoint = _sk.Position, HasAim = true };
            yield return new WaitForSeconds(0.8f);
            Shot("task7_arming");
            yield return new WaitForSeconds(1.2f);
            Shot("task7_armed_ring");
            // 2) Pocket sand the crossbowman group (slot 1)
            _next = new SidekickCommand { Skill = 1, AimPoint = new Vector3(-2.6f, 0f, 23f), HasAim = true };
            yield return new WaitForSeconds(0.55f);
            Shot("task7_pocket_sand");
            yield return new WaitForSeconds(1.5f);
            // 3) Crossbow at the far enemy (slot 2)
            _next = new SidekickCommand { Skill = 2, AimPoint = new Vector3(5.6f, 0f, 28.8f), HasAim = true };
            yield return new WaitForSeconds(0.2f);
            Shot("task7_crossbow_flight");
            yield return new WaitForSeconds(1.0f);
            // 4) Ping the armed stack → collapse
            _next = new SidekickCommand { Ping = true, AimPoint = new Vector3(-2.6f, 0f, 23f), HasAim = true, Skill = -1 };
            yield return new WaitForSeconds(0.62f);
            Shot("task7_collapse");
            yield return new WaitForSeconds(1.2f);
            Shot("task7_aftermath");
            QaFlags.Done = true;
        }

        void Shot(string name)
        {
            var go = new GameObject("QaCam");
            var cam = go.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.fieldOfView = 34f;
            cam.transform.position = CamPos;
            cam.transform.LookAt(CamLook);
            QaCapture.Capture(cam, name, 1600, 900);
            Destroy(go);
        }
    }
}

namespace HS.QA
{
    /// <summary>Runtime flag QA scripts set when finished.</summary>
    public static class QaFlags { public static bool Done; }
}
