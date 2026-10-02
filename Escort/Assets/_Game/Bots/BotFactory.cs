using HS.Sidekick;

namespace HS.Bots
{
    /// <summary>Bots by name (harness, QA scenes): idle, sloppy, supportive, follow — plus named builds.</summary>
    public static class BotFactory
    {
        /// <summary>Build name → (bot, opening picks, camp picks). Builds all use the Supportive bot's priorities.</summary>
        public static (string bot, string[] opening, string[] camp) Build(string name)
        {
            switch (name)
            {
                case "supportive": return ("supportive", new[] { "pocket_sand", "bandage" }, new[] { "crossbow", "quiet_feet", "cover_story" });
                case "fixer": return ("supportive", new[] { "quiet_feet", "pocket_sand" }, new[] { "bandage", "cover_story", "crossbow" });
                case "handler": return ("supportive", new[] { "pocket_sand", "cover_story" }, new[] { "bandage", "crossbow", "quiet_feet" });
                case "shadow": return ("supportive", new[] { "quiet_feet", "crossbow" }, new[] { "pocket_sand", "bandage", "cover_story" });
                default: return (name, new[] { "pocket_sand", "crossbow" }, new[] { "bandage", "quiet_feet", "cover_story", "loosen_bolt" });
            }
        }

        public static ISidekickCommands Make(string name, SidekickAgent sk)
        {
            switch (name)
            {
                case "idle": return sk.gameObject.AddComponent<IdleBot>();
                case "sloppy": return sk.gameObject.AddComponent<SloppyBot>();
                case "supportive": return sk.gameObject.AddComponent<SupportiveBot>();
                default: return sk.gameObject.AddComponent<FollowBot>();
            }
        }
    }
}
