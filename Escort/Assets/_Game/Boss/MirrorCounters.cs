using System;
using HS.Core;

namespace HS.Boss
{
    /// <summary>The Mirror's counter-moves: one for every S0 rule Callum still shares with it at the door (GDD §4.5a).</summary>
    [Flags]
    public enum MirrorCounter
    {
        None = 0,
        /// <summary>He still waits on the Unready: it feigns a stagger, he waits, its heavy blow lands unparried.</summary>
        Feint = 1,
        /// <summary>He still guards his Honor: every Habit Break he sees costs him Major Honor.</summary>
        Goad = 2,
        /// <summary>He is still bound by "no aid": at the Duet he refuses her, and the Link never opens.</summary>
        Terms = 4,
    }

    /// <summary>
    /// The Mirror's numbers and its counters by Stage (campaign spec §5). Pure data: the brain, the Duet and the tests read
    /// these, nothing else decides them.
    /// </summary>
    public static class MirrorCounters
    {
        // Counters
        public const float FeintEvery = 14f, FeintTime = 1.2f;

        // Habit Breaks: a stall or stagger with +100% damage taken.
        public const float BreakDamageMul = 2f;
        public const float EtiquetteCooldown = 12f, SaluteTime = 1.2f, SaluteWait = 3f;
        public const int NicheEngagers = 3, DecoyEngagers = 2;
        public const float EngageRadius = 4.6f, NicheHold = 8f, CollapseStagger = 6f;
        public const float DishonourStagger = 2f, DishonourCut = 0.25f, DishonourFloor = 0.5f;

        // Its own S0 rules
        public const float HeroBlowMul = 0.6f;       // his Stance II, worn by the copy: his blows on it land at 60%
        public const float RiposteCooldown = 5f, RiposteMul = 2f, RiposteStagger = 0.9f;
        public const float WaitUnready = 3f;

        // The Duet Finisher
        public const float Floor = 0.25f;            // its HP stops here for anything but the Duet
        public const float TellTime = 1.5f, RingReturn = 10f, MissRiposte = 0.12f;
        public const float HoldPleaseHold = 4f, SilentPartnerHeal = 0.25f;
        public const int DominoNeeds = 2;

        public static MirrorCounter For(Stage stage)
        {
            var c = MirrorCounter.None;
            if (stage <= Stage.S2) c |= MirrorCounter.Feint;
            if (stage <= Stage.S1) c |= MirrorCounter.Goad;
            if (stage == Stage.S0) c |= MirrorCounter.Terms;
            return c;
        }

        public static bool Has(Stage stage, MirrorCounter counter) => (For(stage) & counter) != 0;

        /// <summary>A ping on the Mirror: S0 is too busy fighting to hear it; S1–S2 re-challenge; S3 strikes during its salute.</summary>
        public static bool EtiquetteHeard(Stage stage) => stage >= Stage.S1;

        public static bool Dishonour(Stage stage) => stage >= Stage.S3;

        /// <summary>Engagers as the Mirror counts them: Callum one, her one while she is at it, a decoy close by two.</summary>
        public static int Engagers(bool heroClose, bool sidekickEngaged, bool decoyClose) =>
            (heroClose ? 1 : 0) + (sidekickEngaged ? 1 : 0) + (decoyClose ? DecoyEngagers : 0);

        /// <summary>The Link window by capstone (GDD §4.5a): Hold Please doubles it, Silent Partner widens it; none: 1 s.</summary>
        public static float LinkWindow(string capstoneId) => HS.Skills.Impl.CapstoneRules.LinkWindow(capstoneId);

        /// <summary>The Mirror's damage after its broken Honor (−25% a break, to −50%).</summary>
        public static float DamageAfterDishonour(int breaks) => Math.Max(DishonourFloor, 1f - DishonourCut * breaks);

        /// <summary>What the copy carries over its head: the rule it is running, from his S0 list.</summary>
        public static string Icon(MirrorBrain.Mode mode)
        {
            switch (mode)
            {
                case MirrorBrain.Mode.Salute: return "challenge";
                case MirrorBrain.Mode.Wait: return "wait";
                case MirrorBrain.Mode.Niche: return "fallback";
                case MirrorBrain.Mode.Reach: return "judgment";
                default: return "fight"; // a feint shows what it really is doing: fighting
            }
        }
    }
}
