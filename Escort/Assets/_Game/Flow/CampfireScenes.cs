using HS.Core;

namespace HS.Flow
{
    public enum CampfireVariant { Cold, Neutral, Warm }

    /// <summary>
    /// The fireside scene per chapter and variant (GDD §4.6: warm, neutral or cold by Stage and recent penalties; §6.1
    /// campfire beats). Lines: "speaker|text" for a bark, "sidekick~text" for her thought. Draft copy for the owner.
    /// </summary>
    public static class CampfireScenes
    {
        public static CampfireVariant VariantFor(int chapter, Stage stage, bool penalisedThisChapter)
        {
            switch (chapter)
            {
                case 1: return penalisedThisChapter ? CampfireVariant.Cold : CampfireVariant.Neutral;
                case 2: return stage >= Stage.S1 ? CampfireVariant.Warm : CampfireVariant.Cold;
                default: return stage >= Stage.S2 ? CampfireVariant.Warm : stage == Stage.S1 ? CampfireVariant.Neutral : CampfireVariant.Cold;
            }
        }

        const string Code = "\"Strike the ready. Spare the yielded. Never the back.\"";

        public static string[] Lines(int chapter, CampfireVariant v, bool sawDishonour)
        {
            switch (chapter)
            {
                case 1:
                    return v == CampfireVariant.Cold
                        ? new[] { "callum|We camp here. I'll take the first watch.", "callum|" + Code + " The Code. Learn it.", sawDishonour ? "callum|And keep your sand in your pockets." : "callum|Fortune favoured us today.", "sidekick~He hasn't looked at me once." }
                        : new[] { "callum|We camp here. The road was kind today.", "callum|" + Code + " My father's words. Learn them; they'll keep you alive.", "callum|Fortune favoured us.", "sidekick~Fortune. Sure." };
                case 2:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Sit. The fire's big enough for two.", "callum|" + Code + " Tonight they feel lighter.", "callum|That archer in the trees fell before he could loose. ...You were near him, weren't you.", "sidekick~Was that almost a thank-you?" }
                        : new[] { "callum|Did you see that archer fall? Fortune favours the just.", "callum|The wood is hungry. So am I. Keep the rations coming.", "callum|First watch is mine.", "sidekick~Fortune, again." };
                case 3:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Down there, when the robber went down behind me...", "callum|I was looking at the sky.", "callum|Some things a knight does not need to see. Sit closer; the crypt is cold.", "sidekick~He looked away. On purpose." }
                        : v == CampfireVariant.Neutral
                            ? new[] { "callum|The dead keep strange company. We were lucky in that vault.", "callum|Lucky more than once, now that I count.", "callum|Get some sleep. I'll wake you.", "sidekick~He's counting." }
                            : new[] { "callum|Luck carried us through those tombs.", "callum|" + Code + " That is what carried us. That, and luck.", "callum|I'll take the watch.", "sidekick~It wasn't luck." };
                default:
                    return v == CampfireVariant.Warm
                        ? new[] { "callum|Tomorrow, the Gallery.", "callum|Stand where I can't see you. Whatever you do there... I'd rather not have to judge it.", "callum|And come back.", "sidekick~He's asking me to cheat. Kind of." }
                        : v == CampfireVariant.Neutral
                            ? new[] { "callum|Tomorrow, the Gallery.", "callum|Whoever waits there has studied me. I can feel it.", "callum|Stay close. Not too close.", "sidekick~Close. Not too close. Got it." }
                            : new[] { "callum|Tomorrow, the Gallery. Single combat, I expect.", "callum|Stay out of it. Whatever happens.", "callum|" + Code, "sidekick~He still thinks this is a fair fight." };
            }
        }

        /// <summary>The night he learns Recall (GDD §4.2: his first reaction is an S0 or S1 line).</summary>
        public static string RecallLine(Stage stage) => stage >= Stage.S1
            ? "callum|If you fall out there... I'll come back for you. That's in the Code. Somewhere."
            : "callum|If you get yourself knocked flat, I suppose I'll have to drag you up. Try not to.";
    }
}
