using System;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Presentation;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// The Duet Finisher (GDD §4.5a step 3). At 25% the Mirror reaches for the code (a 1.5 s tell); Callum glances at her
    /// and a Link ring closes around her for 1.0 s (Hold Please 2.0 s, Silent Partner 1.6 s) while his Judgment charges.
    /// Her capstone inside it — with none ready, a ping on the Mirror — is the Duet: the capstone's Duet form, then
    /// Judgment takes whatever the Mirror has left. A miss: its riposte (12% of his max HP and a wound), and the ring
    /// returns 10 s later. At S0 the Terms counter closes it: "No aid."
    /// </summary>
    public sealed class DuetFinisher : ILinkWindow
    {
        public enum Step { Idle, Tell, Ring, Linked, Done }

        public Step Current { get; private set; }
        public float Remaining { get; private set; }
        public float Window { get; private set; }
        public float Cooldown { get; private set; }
        public int Misses { get; private set; }
        public int Refusals { get; private set; }
        public string LinkedWith { get; private set; }
        public bool Open => Current == Step.Ring;
        public event Action<string> Linked;
        public event Action Missed;
        public event Action RingOpened;

        readonly MirrorBrain _brain;
        readonly HeroAgent _hero;
        readonly CallumModule _cm;
        readonly SidekickAgent _sk;
        readonly RunContext _ctx;
        GameObject _ring;
        MaterialPropertyBlock _mpb;

        public DuetFinisher(MirrorBrain brain, HeroAgent hero, SidekickAgent sk)
        {
            _brain = brain;
            _hero = hero;
            _cm = hero != null ? hero.Module as CallumModule : null;
            _sk = sk;
            _ctx = RunContext.Current;
            if (_ctx != null)
            {
                _ctx.Register<ILinkWindow>(this);
                _ctx.Events.Ping += OnPing;
            }
            if (_cm != null) _cm.FinisherLanded += OnFinisherLanded;
        }

        public void Dispose()
        {
            if (_ctx != null)
            {
                if (_ctx.Get<ILinkWindow>() == this) _ctx.Register<ILinkWindow>(null);
                _ctx.Events.Ping -= OnPing;
            }
            if (_cm != null)
            {
                _cm.FinisherLanded -= OnFinisherLanded;
                _cm.DuetSanctioned = false;
            }
            ShowRing(false, 0f);
            if (_ring != null) UnityEngine.Object.Destroy(_ring);
        }

        EnemyAgent Mirror => _brain.Self;
        Stage Stage => _hero != null ? _hero.Stage : Stage.S0;

        SkillState Capstone()
        {
            var skills = _sk != null ? _sk.GetComponent<SidekickSkills>() : null;
            return skills != null ? skills.System.Capstone : null;
        }

        string CapstoneId() => Capstone()?.Id;

        public void Tick(float dt)
        {
            if (Cooldown > 0f) Cooldown -= dt;
            if (Mirror == null || !Mirror.IsAlive)
            {
                if (Current != Step.Done) ShowRing(false, 0f);
                Current = Step.Done;
                return;
            }
            switch (Current)
            {
                case Step.Idle:
                    if (Cooldown <= 0f && _brain.AtFloor && _brain.Current == MirrorBrain.Mode.Fight && _hero.IsAlive)
                    {
                        Current = Step.Tell;
                        Remaining = MirrorCounters.TellTime;
                        _brain.BeginReach();
                        Vfx.Burst(VfxKind.Glint, Mirror.Position + Vector3.up * 1.35f, 1.6f); // the pendant
                        _ctx?.Events.RaiseBark("mirror", "By the Code—", 1.6f, 2);
                    }
                    return;
                case Step.Tell:
                    if ((Remaining -= dt) > 0f) return;
                    BeginRing();
                    return;
                case Step.Ring:
                    Remaining -= dt;
                    ShowRing(true, Mathf.Clamp01(Remaining / Mathf.Max(0.01f, Window)));
                    if (Remaining <= 0f) Miss();
                    return;
                case Step.Linked:
                    // Judgment takes the rest when it lands; if it can't (he was knocked out of it), the Duet still does.
                    if ((Remaining -= dt) <= 0f) FinishMirror();
                    return;
            }
        }

        void BeginRing()
        {
            if (MirrorCounters.Has(Stage, MirrorCounter.Terms))
            {
                // Counter: the Terms. He is still bound by "no aid", and he will not take her hand.
                Refusals++;
                _cm?.Bark(NoAidLines, 3);
                Miss();
                return;
            }
            EnsureJudgment();
            if (_cm != null) _cm.DuetSanctioned = true;
            Window = MirrorCounters.LinkWindow(CapstoneId());
            Remaining = Window;
            Current = Step.Ring;
            _cm?.Bark(GlanceLines(Stage), 3);
            ShowRing(true, 1f);
            HS.Audio.AudioDirector.Instance?.Play("ui_confirm", _sk != null ? _sk.Position : (Vector3?)null, 0.7f, 0.05f, 0f);
            RingOpened?.Invoke();
        }

        /// <summary>He charges Judgment on the Mirror for the Duet (challenging it first if he must).</summary>
        void EnsureJudgment()
        {
            if (_cm == null || _cm.FinisherCharging) return;
            if (_cm.Challenged != Mirror && _cm.Challenged == null) _cm.StartChallenge(Mirror);
            if (_cm.Challenged == Mirror) _cm.BeginFinisher();
        }

        /// <summary>The capstone key inside the ring: the Duet.</summary>
        public bool TryLink(string capstoneId)
        {
            if (!Open) return false;
            if (capstoneId == "domino_effect" && !DominoReady()) return false;
            Link(capstoneId);
            return true;
        }

        void OnPing(PingInfo p)
        {
            // With no capstone ready (none chosen, or still resting), her ping on the Mirror is the Duet: the ring's timing is
            // the Mirror's, and a trick spent a minute ago must not lock the finish away.
            var cap = Capstone();
            if (Open && p.Target == Mirror && (cap == null || !cap.Ready)) Link(null);
        }

        void Link(string capstoneId)
        {
            LinkedWith = capstoneId ?? "ping";
            Current = Step.Linked;
            ShowRing(false, 0f);
            _brain.Finishing = true;
            Form(capstoneId);
            EnsureJudgment();
            Remaining = (_cm != null && _cm.FinisherCharging ? _cm.FinisherChargeTime * (1f - _cm.FinisherProgress) : 0f) + 0.6f;
            if (_brain.Current == MirrorBrain.Mode.Reach) _brain.HoldFor(Remaining + 0.5f);
            Vfx.Burst(VfxKind.Glint, _sk != null ? _sk.Position + Vector3.up * 1.4f : Mirror.Position, 1.8f);
            _ctx?.Events.RaiseBark("sidekick", "Now!", 1.4f, 2);
            Linked?.Invoke(LinkedWith);
        }

        /// <summary>The capstone's Duet form (Plan 4's table).</summary>
        void Form(string capstoneId)
        {
            int ch = _ctx != null ? _ctx.Chapter : 5;
            switch (capstoneId)
            {
                case "domino_effect":
                {
                    // Every armed prop comes down on it, the nearest idle ones armed first. A prop over him stays up.
                    var props = new System.Collections.Generic.List<ArmableProp>();
                    foreach (var p in ArmableProp.Instances) if (p != null && p.isActiveAndEnabled) props.Add(p);
                    props.Sort((a, b) => Geo.FlatDistance(a.ImpactPoint, Mirror.Position).CompareTo(Geo.FlatDistance(b.ImpactPoint, Mirror.Position)));
                    int armedNow = 0;
                    foreach (var p in props)
                        if (p.State == ArmableProp.PropState.Idle && armedNow < CapstoneRules.DominoAutoArm)
                        {
                            p.Arm();
                            armedNow++;
                        }
                    foreach (var p in props)
                    {
                        if (p.State != ArmableProp.PropState.Armed) continue;
                        if (_hero == null || Geo.FlatDistance(_hero.Position, p.ImpactPoint) > p.Anchor.ImpactRadius + 0.3f) p.Trigger();
                        Mirror.TakeDamage(DamageInfo.Make(_sk, Mirror, p.Anchor.Damage, DamageKind.Heavy, "duet", 1f));
                    }
                    Vfx.Burst(VfxKind.Dust, Mirror.Position, 1.6f);
                    break;
                }
                case "crossfire":
                {
                    var sys = _sk != null ? _sk.GetComponent<SidekickSkills>()?.System : null;
                    _sk?.Motor.FaceInstant(Geo.DirTo(_sk.Position, Mirror.Position));
                    _sk?.Presenter?.PlayAction("shoot", 0.5f);
                    _cm?.JoinFinisher(CapstoneRules.CrossfireBolt(sys) * 3f * ChapterTier.SidekickDamage(ch));
                    Vfx.Burst(VfxKind.Sparks, Mirror.Position + Vector3.up * 1.3f, 1.4f);
                    break;
                }
                case "hold_please":
                    // Held for the blow (the window was already doubled).
                    _brain.HoldFor(MirrorCounters.HoldPleaseHold);
                    _ctx?.Events.RaiseBark("sidekick", "Hold, please.", 1.6f, 2);
                    break;
                case "silent_partner":
                    if (_hero != null) _hero.Health.Heal(_hero.Health.Max * MirrorCounters.SilentPartnerHeal);
                    Vfx.Burst(VfxKind.Glint, _hero.Position + Vector3.up * 1.4f, 1.4f);
                    break;
            }
        }

        bool DominoReady()
        {
            int armed = 0, idle = 0;
            foreach (var p in ArmableProp.Instances)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                if (p.State == ArmableProp.PropState.Armed) armed++;
                else if (p.State == ArmableProp.PropState.Idle) idle++;
            }
            if (armed + Mathf.Min(idle, CapstoneRules.DominoAutoArm) >= MirrorCounters.DominoNeeds) return true;
            _ctx?.Events.RaiseThought("Not enough up there to bring down on it.");
            return false;
        }

        void Miss()
        {
            Misses++;
            ShowRing(false, 0f);
            Current = Step.Idle;
            Cooldown = MirrorCounters.RingReturn;
            if (_cm != null) _cm.DuetSanctioned = false;
            _cm?.CancelFinisher();
            _brain.EndReach();
            // Its riposte: the opening it was reading for.
            if (_hero != null && _hero.IsAlive)
            {
                Mirror.Motor.FaceInstant(Geo.DirTo(Mirror.Position, _hero.Position));
                Mirror.Presenter?.PlayAction("attack3", 0.5f);
                _hero.TakeDamage(DamageInfo.Make(null, _hero, _hero.Health.Max * MirrorCounters.MissRiposte, DamageKind.Blade, "mirror_riposte", 0.6f));
                if (_hero.IsAlive) _hero.Wounds.Add(WoundSet.TypeFor(DamageKind.Blade));
                _hero.ApplyWoundEffects();
            }
            _ctx?.Events.RaiseBark("mirror", "Alone, then. As always.", 2f, 2);
            Missed?.Invoke();
        }

        void OnFinisherLanded(EnemyAgent target, float dmg)
        {
            if (Current == Step.Linked && target == Mirror) FinishMirror();
        }

        void FinishMirror()
        {
            if (Current == Step.Done) return;
            Current = Step.Done;
            if (Mirror != null && Mirror.IsAlive)
                Mirror.TakeDamage(DamageInfo.Make(_hero, Mirror, Mirror.Health.Current + 1f, DamageKind.Blade, "duet", 1f));
        }

        // ------------------------------------------------------------------ the ring

        void ShowRing(bool on, float fraction)
        {
            if (!on)
            {
                if (_ring != null) _ring.SetActive(false);
                return;
            }
            if (_ring == null)
            {
                _ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(_ring.GetComponent<Collider>());
                _ring.name = "LinkRing";
                var r = _ring.GetComponent<Renderer>();
                r.sharedMaterial = Resources.Load<Material>("FX/FX_Ring");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _mpb = new MaterialPropertyBlock();
            }
            _ring.SetActive(true);
            var at = _sk != null ? _sk.Position : Mirror.Position;
            _ring.transform.SetPositionAndRotation(at + Vector3.up * 0.06f, Quaternion.Euler(90f, 0f, 0f));
            // It closes on her: from 3.2 m across to her feet as the window runs out.
            _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 3.2f, fraction);
            var rr = _ring.GetComponent<Renderer>();
            _mpb.SetColor("_Color", Color.Lerp(new Color(1f, 0.35f, 0.3f, 0.95f), new Color(1f, 0.85f, 0.45f, 0.95f), fraction));
            rr.SetPropertyBlock(_mpb);
        }

        static string[] GlanceLines(Stage s) =>
            s >= Stage.S3 ? new[] { "Together. Now!" } : s == Stage.S2 ? new[] { "Now, friend!" } : new[] { "If you have something — now." };

        static readonly string[] NoAidLines = { "No aid. I gave my word.", "Stay back! No aid!" };
    }
}
