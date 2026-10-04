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
        };

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

        void OnLearned(SkillState s)
        {
            if (s.Id == "quiet_feet")
            {
                _sk.HasQuietFeet = true;
                _sk.QuietFeetRadius = s.Def.A(s.Rank);
                _sk.QuietFeetConeMul = s.Def.B(s.Rank);
            }
        }

        public void SimTick(float dt) => System.Tick(dt);
    }
}
