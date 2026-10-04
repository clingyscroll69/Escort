using System.Collections.Generic;
using HS.Core;

namespace HS.Boss
{
    /// <summary>
    /// The Curator's diagnosis after a loss (GDD §8: calm, collector-like; names the flaw precisely; ends with a line
    /// that shows he has no entry for you). Draft copy for the owner to rewrite (GDD: write barks yourself).
    /// </summary>
    public static class CuratorDiagnosis
    {
        public static List<string> For(Stage stage, string cause, bool sidekickDied)
        {
            var l = new List<string> { "SUBJECT: CALLUM, called 'the Honorable'." };
            if (sidekickDied)
            {
                l.Add("The help fell first. He kept to the terms over your body.");
                l.Add("A man who will not cheat can always be cheated. Even of his friends.");
            }
            else if (cause == "arrows")
            {
                l.Add(stage >= Stage.S1
                    ? "He raised his guard at the first arrow. Interesting — someone has been teaching him to look up."
                    : "He agreed to my terms. He saluted. He never once looked up at the gallery.");
                l.Add("A man who will not cheat can always be cheated. I only had to make it formal.");
            }
            else if (cause == "ashgrave")
            {
                l.Add("He fought fairly, and he very nearly won. A fair fight was never on offer.");
                l.Add("He will keep a bargain after it is broken. That is the whole of him.");
            }
            else
            {
                l.Add("He kept faith with the terms long after I had broken them.");
                l.Add("A man who will not cheat can always be cheated.");
            }
            l.Add("And you... I have no page for you. How curious.");
            return l;
        }

        /// <summary>A death on the road (before the duel): one line that names the flaw, then the line about you.</summary>
        public static List<string> ForRoad(string lastHitTag, bool sidekickDied)
        {
            var l = new List<string> { "SUBJECT: CALLUM, called 'the Honorable'." };
            if (sidekickDied) l.Add("The help fell first. The help always does.");
            else switch (lastHitTag)
            {
                case "cheap_shot": l.Add("He waited for a yielded man to rise. The man did not wait for him."); break;
                case "ambush": l.Add("He walked past a hedge he had no reason to fear."); break;
                case "spike_plate":
                case "snare":
                case "tripwire": l.Add("He watched his opponent, never the ground."); break;
                case "bolt":
                case "arrow": l.Add("He would not leave a fair fight to chase a coward on a roof."); break;
                case "fever": l.Add("He fought on with a wound he would not let anyone dress."); break;
                default: l.Add("He fought them fairly, one at a time. They did not return the courtesy."); break;
            }
            l.Add("And you... I have no page for you. How curious.");
            return l;
        }

        // ------------------------------------------------------------------ the Gallery (chapter 5)

        /// <summary>Phase 0 (GDD §4.5): the figure with the dull pendant names the flaw, then the line that has no page for you.</summary>
        public static List<string> Opening(Stage stage) => new List<string>
        {
            "Sir Callum. I have read every page of you.",
            "A man who will not cheat can always be cheated. A man who keeps his word can be held to it.",
            stage >= Stage.S1
                ? "So I have written terms, and you will keep them. ...Though lately someone has been writing in your margins."
                : "So I have written terms, and you will keep them. You always do.",
            "And you. There is no page for you. No matter: scenery needs no page.",
        };

        /// <summary>Phase 3's unmasking: the persona falls away and the Curator speaks as himself.</summary>
        public static readonly string[] Unmasking =
        {
            "You are not the first I have read. You are the first I could not finish.",
            "So. Here is everything you were. Let us see which of you I keep.",
        };

        /// <summary>A loss in the Gallery, by phase and cause (GDD §11.3: attributable).</summary>
        public static List<string> ForGallery(Stage stage, GalleryBoss.Phase phase, string cause, bool sidekickDied)
        {
            if (phase != GalleryBoss.Phase.Mirror && phase != GalleryBoss.Phase.Unmasking) return For(stage, cause, sidekickDied);
            var l = new List<string> { "SUBJECT: CALLUM, called 'the Honorable'." };
            if (sidekickDied) l.Add("The help fell first. He never saw it: he was busy fighting himself.");
            else switch (cause)
            {
                case "terms":
                    l.Add("He fought himself to a standstill, and would not take the hand that could end it.");
                    l.Add("\"No aid.\" He said it to the last. I had only to wait.");
                    break;
                case "feint":
                    l.Add("I staggered, and he waited. He always waits. I wrote that on the first page.");
                    break;
                case "duet_missed":
                    l.Add("He looked to the scenery for help. The scenery was late.");
                    break;
                default:
                    l.Add("Equal arms, equal habits. A man cannot outfight himself; he can only outgrow himself.");
                    break;
            }
            l.Add("And you... still no page. I begin to think that is the point.");
            return l;
        }

        /// <summary>His "we" line (GDD §8 ending): S0 still credits fortune; by S3 the stones will write two names.</summary>
        public static string WeLine(Stage stage)
        {
            switch (stage)
            {
                case Stage.S3: return "Write two names, stones. We did this.";
                case Stage.S2: return "We. I'll say it plainly, and to anyone: we.";
                case Stage.S1: return "We— we did that. Didn't we. ...We.";
                default: return "Fortune... no. That was not fortune, was it.";
            }
        }

        /// <summary>Callum's line after a win: S0 credits luck, S1 begins to notice (GDD tone: early luck → later "we").</summary>
        public static string VictoryLine(Stage stage, int archersSilenced) =>
            stage >= Stage.S1
                ? archersSilenced > 0 ? "The gallery fell quiet. ...Fortune had help today, didn't it." : "A rigged duel, and still we stand. Hm. We."
                : archersSilenced > 0 ? "Did you see? The gallery lost its nerve. Fortune favours the just!" : "Fortune favours the just.";
    }
}
