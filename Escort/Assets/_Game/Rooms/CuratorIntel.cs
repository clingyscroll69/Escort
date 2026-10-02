using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Curator Intel (GDD §4.3): 0–3, hidden. Rises as active stones record the hero's flaw-revealing behaviour; it scales
    /// the chapter 4 nemesis squads (it never blocks the boss). Clips held by a stone are pending until relayed at the
    /// chapter's end (campfire); breaking the stone first destroys them. Two clips per level.
    /// </summary>
    public sealed class CuratorIntel
    {
        public const int ClipsPerLevel = 2, MaxLevel = 3;
        public int Relayed { get; private set; }
        public int Forged { get; private set; }

        public int Level(int pending) => Mathf.Clamp((Relayed + pending) / ClipsPerLevel - Forged, 0, MaxLevel);

        public void Relay(int clips) => Relayed += Mathf.Max(0, clips);

        /// <summary>Forgery (later chapters): −1 Intel once per chapter.</summary>
        public void Forge() => Forged++;

        public (int relayed, int forged) Snapshot() => (Relayed, Forged);

        public void Restore((int relayed, int forged) s)
        {
            Relayed = s.relayed;
            Forged = s.forged;
        }
    }
}
