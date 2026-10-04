using System.Collections.Generic;
using HS.Core;
using HS.Sidekick;
using HS.Skills.Impl;
using UnityEngine;

namespace HS.Skills
{
    /// <summary>Owns the sidekick's SkillSystem, wires slots to input, applies passives and publishes feedback.</summary>
    [RequireComponent(typeof(SidekickAgent))]
    public sealed class SidekickSkills : MonoBehaviour, ISimTickable
    {
        public string[] StartingSkills = new string[0];
        public SkillSystem System { get; private set; }
        public int TickOrder => TickOrders.Skills;
        SidekickAgent _sk;

        public static Dictionary<string, ISkillBehaviour> SliceBehaviours() => new Dictionary<string, ISkillBehaviour>
        {
            { "pocket_sand", new PocketSandSkill() },
            { "loosen_bolt", new LoosenBoltSkill() },
            { "crossbow", new CrossbowSkill() },
            { "bandage", new BandageSkill() },
            { "cover_story", new CoverStorySkill() },
            { "splint_and_stitch", new SplintAndStitchSkill() },
            { "pull_back", new PullBackSkill() },
            { "sling", new SlingSkill() },
            { "read_the_room", new ReadTheRoomSkill() },
            { "read_runes", new ReadRunesSkill() },
            { "lockpick", new LockpickSkill() },
            { "map_sketch", new MapSketchSkill() },
            { "buckler", new BucklerSkill() },
            { "bait_and_switch", new BaitAndSwitchSkill() },
            { "smoke_bomb", new SmokeBombSkill() },
            { "pep_talk", new PepTalkSkill() },
            { "shoulder_check", new ShoulderCheckSkill() },
            // Capstones (one per run, revealed at the chapter 4 campfire; their own key).
            { "domino_effect", new DominoEffectSkill() },
            { "crossfire", new CrossfireSkill() },
            { "hold_please", new HoldPleaseSkill() },
            { "silent_partner", new SilentPartnerSkill() },
        };

        /// <summary>The capstone key was pressed (whatever came of it): the Duet listens for this.</summary>
        public event System.Action<string> CapstonePressed;
        float _partnerT;

        void Awake()
        {
            _sk = GetComponent<SidekickAgent>();
            System = new SkillSystem(SliceBehaviours());
            System.Learned += OnLearned;
            _sk.SkillHandler = Use;
        }

        void Start()
        {
            var cat = SkillCatalog.Load();
            if (cat == null) return;
            foreach (var id in StartingSkills) Learn(id);
        }

        void OnEnable() => SimLoop.Register(this);
        void OnDisable() => SimLoop.Unregister(this);

        public bool Learn(string id)
        {
            var def = SkillCatalog.Load()?.Get(id);
            return def != null && System.Learn(def);
        }

        bool Use(int slot, SidekickCommand cmd)
        {
            if (_sk.IsChanneling || _sk.IsDodging || !_sk.IsAlive) return false;
            var ctx = new SkillUseContext { User = _sk, AimPoint = _sk.AimPoint, Run = RunContext.Current };
            if (slot == SkillSystem.CapstoneSlot) return UseCapstone(ctx);
            bool ok = System.TryActivate(slot, ctx);
            var run = RunContext.Current;
            if (ok)
            {
                run?.Events.RaiseSkillUsed(System.InSlot(slot).Id, _sk);
            }
            else if (run != null && System.LastFailReason != null && System.LastFailReason != "cooldown" && System.LastFailReason != "empty slot")
            {
                run.Events.RaiseThought(System.LastFailReason);
            }
            return ok;
        }

        /// <summary>The capstone key: inside an open Link window it is the Duet; otherwise its normal use.</summary>
        bool UseCapstone(in SkillUseContext ctx)
        {
            var cap = System.Capstone;
            var run = ctx.Run;
            if (cap == null)
            {
                run?.Events.RaiseThought("Nothing there yet. Something will come.");
                return false;
            }
            CapstonePressed?.Invoke(cap.Id);
            var link = run != null ? run.Get<ILinkWindow>() : null;
            if (link != null && link.Open && cap.Ready && link.TryLink(cap.Id))
            {
                cap.CooldownRemaining = cap.Def.Cooldown(cap.Rank);
                run.Events.RaiseSkillUsed(cap.Id, _sk);
                return true;
            }
            bool ok = System.TryActivate(SkillSystem.CapstoneSlot, ctx);
            if (ok) run?.Events.RaiseSkillUsed(cap.Id, _sk);
            else if (run != null && System.LastFailReason != null && System.LastFailReason != "cooldown") run.Events.RaiseThought(System.LastFailReason);
            return ok;
        }

        void OnLearned(SkillState s)
        {
            if (s.Id == "quiet_feet")
            {
                _sk.HasQuietFeet = true;
                _sk.QuietFeetRadius = s.Def.A(s.Rank);
                _sk.QuietFeetConeMul = s.Def.B(s.Rank);
            }
        }

        public void SimTick(float dt)
        {
            System.Tick(dt);
            var cap = System.Capstone;
            if (cap != null && cap.Id == "silent_partner") SilentPartnerSkill.Tick(_sk, ref _partnerT, dt);
        }
    }
}
