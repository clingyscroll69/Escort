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
                case "tripwire": l.Add("He watched his opponent, never the ground."); break;
                case "bolt":
                case "arrow": l.Add("He would not leave a fair fight to chase a coward on a roof."); break;
                case "fever": l.Add("He fought on with a wound he would not let anyone dress."); break;
                default: l.Add("He fought them fairly, one at a time. They did not return the courtesy."); break;
            }
            l.Add("And you... I have no page for you. How curious.");
            return l;
        }

        /// <summary>Callum's line after a win: S0 credits luck, S1 begins to notice (GDD tone: early luck → later "we").</summary>
        public static string VictoryLine(Stage stage, int archersSilenced) =>
            stage >= Stage.S1
                ? archersSilenced > 0 ? "The gallery fell quiet. ...Fortune had help today, didn't it." : "A rigged duel, and still we stand. Hm. We."
                : archersSilenced > 0 ? "Did you see? The gallery lost its nerve. Fortune favours the just!" : "Fortune favours the just.";
    }
}
