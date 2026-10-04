using HS.Rooms;
using NUnit.Framework;

namespace HS.Tests
{
    /// <summary>Chapter 4's nemesis squad (campaign spec §4): one more cheat and 15% faster reloads per Curator Intel level.</summary>
    public class NemesisSquadTests
    {
        [Test]
        public void Each_Intel_Level_Sends_One_More_Cheat()
        {
            for (int level = 0; level <= CuratorIntel.MaxLevel; level++)
            {
                int spawned = 0;
                for (int minIntel = 0; minIntel <= CuratorIntel.MaxLevel; minIntel++)
                    if (NemesisSquad.Spawns(minIntel, level)) spawned++;
                Assert.AreEqual(level + 1, spawned, "Intel " + level + ": the base squad plus one per level");
            }
            Assert.IsTrue(NemesisSquad.Spawns(3, 9), "Intel caps at its top level");
            Assert.IsFalse(NemesisSquad.Spawns(1, -1));
        }

        [Test]
        public void Shooters_Reload_15_Percent_Faster_Per_Level()
        {
            Assert.AreEqual(1f, NemesisSquad.ReloadMul(0), 1e-5f);
            Assert.AreEqual(0.85f, NemesisSquad.ReloadMul(1), 1e-5f);
            Assert.AreEqual(0.85f * 0.85f * 0.85f, NemesisSquad.ReloadMul(3), 1e-5f);
            Assert.AreEqual(NemesisSquad.ReloadMul(3), NemesisSquad.ReloadMul(7), 1e-5f, "no faster than the top level");
            Assert.AreEqual(4, NemesisSquad.Chapter);
        }
    }
}
