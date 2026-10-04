using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Chapter 4's nemesis squad, "the Rigged Gauntlet" (GDD §6.1; campaign spec §4): the Curator sends what his footage
    /// says will work. The base squad comes at Intel 0; every Intel level adds one more cheat (a <see cref="SpawnMarker"/>
    /// with <see cref="SpawnMarker.MinIntel"/>), and the squad's shooters reload 15% faster per level. Pure rules.
    /// </summary>
    public static class NemesisSquad
    {
        public const int Chapter = 4;
        public const float ReloadStep = 0.85f;

        /// <summary>Does a marker that needs <paramref name="minIntel"/> appear at this Intel level?</summary>
        public static bool Spawns(int minIntel, int intelLevel) => minIntel <= Mathf.Clamp(intelLevel, 0, CuratorIntel.MaxLevel);

        /// <summary>Reload time multiplier for the squad's shooters.</summary>
        public static float ReloadMul(int intelLevel) => Mathf.Pow(ReloadStep, Mathf.Clamp(intelLevel, 0, CuratorIntel.MaxLevel));
    }
}
