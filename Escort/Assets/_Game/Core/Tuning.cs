using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Every gameplay number lives here (GDD conventions: data-driven starting values tuned by the balance harness).
    /// Defaults are the GDD's starting values; the asset at Resources/Tuning overrides them.
    /// </summary>
    [CreateAssetMenu(menuName = "HS/Tuning", fileName = "Tuning")]
    public sealed class Tuning : ScriptableObject
    {
        public SidekickTuning sidekick = new SidekickTuning();
        public CallumTuning callum = new CallumTuning();
        public WoundTuning wounds = new WoundTuning();
        public RapportTuning rapport = new RapportTuning();
        public CameraTuning camera = new CameraTuning();
        public List<EnemyStats> enemies = EnemyStats.Defaults();

        static Tuning _default;

        public static Tuning LoadDefault()
        {
            if (_default != null) return _default;
            _default = Resources.Load<Tuning>("Tuning");
            if (_default == null) _default = CreateInstance<Tuning>();
            return _default;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _default = null;

        public EnemyStats Enemy(string id)
        {
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].id == id) return enemies[i];
            Debug.LogError($"[Tuning] unknown enemy archetype '{id}'");
            return enemies[0];
        }
    }

    [Serializable]
    public class SidekickTuning
    {
        [Header("Vitals (GDD 4.1)")]
        public float maxHp = 80f;
        public float hpPerLevel = 10f;

        [Header("Movement")]
        public float jogSpeed = 6.0f;
        public float walkSpeed = 1.5f;      // precise positioning; the stock walk cycle is a stroll
        public float crouchSpeed = 1.6f;    // Quiet Feet sneak: the stock sneak cycle plays at ~3.8x here and stays planted (1.8 hit the 4.2x clamp)
        public float accel = 45f;
        public float turnSpeed = 900f;

        [Header("Dodge roll (3 charges, 0.3s invulnerability)")]
        public int dodgeCharges = 3;
        public float dodgeRecharge = 1.8f;
        public float dodgeDuration = 0.42f;
        public float dodgeDistance = 4.2f;
        public float dodgeIFrames = 0.3f;

        [Header("Kitchen knife (8 damage)")]
        public float knifeDamage = 8f;
        public float knifeRange = 1.7f;
        public float knifeArc = 110f;
        public float knifeWindup = 0.12f;
        public float knifeCooldown = 0.42f;

        [Header("Support")]
        public float supportRange = 25f;
        public float abandonRange = 20f;
        public float interactRange = 2.2f;
        public float pingRange = 30f;
        public float pingCooldown = 0.35f;

        [Header("Being overlooked (unlisted)")]
        public float detectRadius = 4.5f;          // any direction, unaware enemy
        public float detectConeRange = 9f;         // in an unaware enemy's view cone
        public float detectConeAngle = 110f;
        public float detectRadiusQuietFeet = 2f;   // Quiet Feet: can't be detected beyond 2 m while crouched
    }

    [Serializable]
    public class CallumTuning
    {
        [Header("Chapter 1 power (x2.5 dmg, x2 HP per chapter)")]
        public float maxHp = 260f;
        public float damage = 26f;
        public float attackWindup = 0.45f;
        public float attackRecovery = 0.6f;
        public float attackRange = 2.1f;
        public float routeSpeed = 4.4f;
        public float combatSpeed = 5.0f;
        public float turnSpeed = 540f;

        [Header("Rules (GDD 6.1)")]
        public float challengeRange = 15f;
        public float saluteTime = 1.2f;
        public float waitUnreadyS0 = 3f;
        public float waitUnreadyS1 = 2f;
        [Tooltip("S2+: he waits only this long on a flagged cheater.")]
        public float waitUnreadyS2Cheater = 1f;
        public int fallbackEngagers = 3;
        public float engageRadius = 4.6f;   // swinging or circling at menace range (attack tokens)
        public float fallbackHold = 12f;    // holds the narrows (one attacker at a time) before re-evaluating

        [Header("Witness cone (120 deg, 12 m)")]
        public float witnessAngle = 120f;
        public float witnessRange = 12f;
        public float quietFeetAngleMul = 0.67f;
        public float quietFeetRangeMul = 0.6f;

        [Header("Honor")]
        public float honorMax = 100f;
        public float honorLossMajor = 20f;
        public float honorLossMinor = 8f;
        public float honorLow = 40f;
        public float honorLowDamageMul = 0.75f;
        public float honorRoomClearRegen = 15f;
        [Tooltip("Fraction of max HP recovered on reaching the next room (wounds remain).")]
        public float secondWind = 0.5f;
        public float coverStoryHonorRestore = 12f;

        [Header("Strike I: Riposte (parry counter 2x)")]
        public float riposteCooldown = 5f;
        public float riposteMultiplier = 2f;
        [Tooltip("Strike II (chapter 4): the counter lands for 3x.")]
        public float riposteMultiplierII = 3f;

        [Header("Stance: Unyielding (from his challenged opponent)")]
        public float stanceIMul = 0.7f;
        public float stanceIIMul = 0.6f;

        [Header("Finisher: Judgment (a charge, then one blow)")]
        public float finisherMul = 6f;
        public float finisherCharge = 3f;
        public float finisherChargeII = 2f;
        public float finisherCooldown = 20f;
        [Tooltip("A hit of this fraction of his max HP during the charge breaks it.")]
        public float finisherBreakFraction = 0.1f;
        [Tooltip("He reaches for it when his opponent is under this fraction of HP.")]
        public float finisherBelow = 0.6f;

        [Header("Route")]
        public float thresholdPause = 1.5f;
        public float thresholdMaxWait = 6f;
        public float thresholdSidekickNear = 9f;

        public float Damage(int chapter) => damage * ChapterTier.HeroDamage(chapter);
        public float MaxHp(int chapter) => maxHp * ChapterTier.HeroHp(chapter);
    }

    [Serializable]
    public class WoundTuning
    {
        public float woundThresholdFraction = 0.25f;
        public int crippledCount = 3;
        public float crippledSpeedMul = 0.7f;
        public float sprainSpeedMul = 0.85f;
        public float ribsMaxHpMul = 0.8f;
        public float armDamageMul = 0.8f;
        public float feverDrainFraction = 0.0045f; // ~1.2 HP/s at Ch1's 260 HP; never drains below feverFloor
        public float feverFloor = 0.15f;
        public float concussionDelay = 0.5f;
    }

    [Serializable]
    public class RapportTuning
    {
        [Tooltip("Points offered per chapter by the Opportunity Director (Ch1..Ch5 approach)")]
        public float[] chapterBudget = { 60f, 90f, 100f, 110f, 40f };
        public float penaltyCapFraction = 0.30f;
        public float s1 = 0.30f, s2 = 0.55f, s3 = 0.75f;

        [Header("Callum moment weights")]
        public float unseenAssist = 3f;
        public float avertedCheat = 4f;
        public float coveredLapse = 2f;
        public float chosenBlindness = 2f;
        public float duetStrike = 3f;
        public float woundTreatedAfterDuel = 2f;

        [Header("Penalties")]
        public float caught = 3f;
        public float caughtTwice = 6f;
        public float spoiledDuel = 2f;
        public float friendlyFire = 3f;
        public float friendlyFireHpFraction = 0.10f;
        public float abandon = 3f;
        public float abandonHeroHpFraction = 0.40f;
        public float coverStoryWindow = 3f;
        public float woundTreatWindow = 12f;
    }

    [Serializable]
    public class CameraTuning
    {
        public float pitch = 50f;
        public float yaw = 0f;
        public float fov = 32f;
        public float minDistance = 12f;
        public float maxDistance = 28f;          // keeps a 1.75 m character ≳100 px tall at 1080p
        public float framingPadding = 1.2f;
    }

    /// <summary>Enemy archetype numbers (chapter 1 bandits, Whisperwood's poachers, the rigged-duel cast).</summary>
    [Serializable]
    public class EnemyStats
    {
        public string id;
        public string displayName;
        public float maxHp;
        public float speed;
        public float damage;
        public DamageKind kind;
        public float windup;
        public float recovery;
        public float range;
        public float stagger;
        [Header("Ranged")]
        public bool ranged;
        public float projectileSpeed;
        public float aimTime;
        public float reload;
        [Header("Flags")]
        public bool cheater;              // archers, turncoats, ambushers: 'dirty' fighters Callum's code ignores
        public float surrenderAtHp;       // fraction; 0 = never
        public float cheapShotDamage;
        public bool startsHidden;
        public float ambushDamage;
        public float heavyDamage;          // brute smash / cheap shots that cause wounds
        public float heavyWindup;
        public float heavyEvery;          // every N-th attack is heavy (0 = never)
        [Tooltip("Metres a heavy blow shoves its victim (the bone bridge's shield-bearers). 0 = none.")]
        public float shove;

        // Ch1 tuning target (GDD §3: the hero solos ~70% of encounters): honest bandits are a nuisance to a knight; the
        // danger is the cheating — cheap shots, ambushes, shooters he won't chase, the brute's heavy — which is the
        // sidekick's job. Measured by the balance harness (docs/qa/balance).
        public static List<EnemyStats> Defaults() => new List<EnemyStats>
        {
            new EnemyStats { id = "thug", displayName = "Road Thug", maxHp = 80, speed = 4.2f, damage = 11, kind = DamageKind.Melee, windup = 0.55f, recovery = 1.0f, range = 1.8f, stagger = 0f },
            new EnemyStats { id = "brute", displayName = "Bandit Brute", maxHp = 170, speed = 3.5f, damage = 18, kind = DamageKind.Melee, windup = 0.7f, recovery = 1.0f, range = 2.1f, heavyDamage = 68, heavyWindup = 1.15f, heavyEvery = 4 },
            new EnemyStats { id = "crossbowman", displayName = "Crossbowman", maxHp = 50, speed = 4.0f, damage = 16, kind = DamageKind.Ranged, windup = 0f, recovery = 0.4f, range = 20f, ranged = true, projectileSpeed = 34f, aimTime = 0.9f, reload = 3.6f, cheater = true },
            new EnemyStats { id = "turncoat", displayName = "Turncoat", maxHp = 90, speed = 4.3f, damage = 11, kind = DamageKind.Melee, windup = 0.5f, recovery = 0.95f, range = 1.8f, surrenderAtHp = 0.36f, cheapShotDamage = 66, cheater = true },
            new EnemyStats { id = "ambusher", displayName = "Hedge Ambusher", maxHp = 70, speed = 4.6f, damage = 11, kind = DamageKind.Melee, windup = 0.45f, recovery = 0.95f, range = 1.8f, startsHidden = true, ambushDamage = 48, cheater = true },
            new EnemyStats { id = "archer", displayName = "Gallery Archer", maxHp = 45, speed = 3.8f, damage = 21, kind = DamageKind.Ranged, windup = 0f, recovery = 0.5f, range = 26f, ranged = true, projectileSpeed = 30f, aimTime = 1.0f, reload = 6.0f, cheater = true },
            // Whisperwood (chapter 2): poachers in the trees, woodsmen, ambushers in the ferns. Chapter-1 baselines; the tier scales.
            new EnemyStats { id = "poacher", displayName = "Poacher", maxHp = 45, speed = 4.0f, damage = 14, kind = DamageKind.Ranged, windup = 0f, recovery = 0.4f, range = 18f, ranged = true, projectileSpeed = 32f, aimTime = 1.0f, reload = 3.8f, cheater = true },
            new EnemyStats { id = "woodsman", displayName = "Woodsman", maxHp = 150, speed = 3.6f, damage = 16, kind = DamageKind.Melee, windup = 0.7f, recovery = 1.0f, range = 2.1f, heavyDamage = 60, heavyWindup = 1.15f, heavyEvery = 4 },
            new EnemyStats { id = "fern_ambusher", displayName = "Fern Ambusher", maxHp = 60, speed = 4.6f, damage = 10, kind = DamageKind.Melee, windup = 0.45f, recovery = 0.95f, range = 1.8f, startsHidden = true, ambushDamage = 44, cheater = true },
            // Catacombs of Ends (chapter 3): cultists doze in the crypt, tomb robbers feign surrender, the ward wakes guardians.
            new EnemyStats { id = "cultist", displayName = "Cultist", maxHp = 75, speed = 4.3f, damage = 12, kind = DamageKind.Melee, windup = 0.5f, recovery = 0.95f, range = 1.8f, cheater = true },
            new EnemyStats { id = "tomb_robber", displayName = "Tomb Robber", maxHp = 85, speed = 4.4f, damage = 11, kind = DamageKind.Melee, windup = 0.5f, recovery = 0.95f, range = 1.8f, surrenderAtHp = 0.36f, cheapShotDamage = 64, cheater = true },
            new EnemyStats { id = "ward_guardian", displayName = "Ward Guardian", maxHp = 190, speed = 3.4f, damage = 18, kind = DamageKind.Melee, windup = 0.75f, recovery = 1.0f, range = 2.2f, heavyDamage = 70, heavyWindup = 1.2f, heavyEvery = 3 },
            new EnemyStats { id = "shield_bearer", displayName = "Shield-Bearer", maxHp = 130, speed = 3.8f, damage = 14, kind = DamageKind.Melee, windup = 0.6f, recovery = 1.0f, range = 2.0f, heavyDamage = 24, heavyWindup = 0.9f, heavyEvery = 3, shove = 3f },
            new EnemyStats { id = "alcove_archer", displayName = "Alcove Archer", maxHp = 45, speed = 3.8f, damage = 18, kind = DamageKind.Ranged, windup = 0f, recovery = 0.5f, range = 22f, ranged = true, projectileSpeed = 30f, aimTime = 1.0f, reload = 4.4f, cheater = true },
            // The rigged duel must outlast the volley signal (T+25 s) by several volleys: a durable, measured duellist.
            new EnemyStats { id = "ashgrave", displayName = "Lord Ashgrave", maxHp = 1250, speed = 4.6f, damage = 9, kind = DamageKind.Blade, windup = 0.7f, recovery = 1.0f, range = 2.2f, heavyDamage = 26, heavyWindup = 1.1f, heavyEvery = 4 },
        };
    }
}
