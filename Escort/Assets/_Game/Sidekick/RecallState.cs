namespace HS.Sidekick
{
    /// <summary>
    /// Recall (GDD §4.2, decided): learned at the chapter 2 campfire; from then on the sidekick is Downed at 0 HP and the
    /// hero may Recall her, twice per chapter. Registered in the RunContext; snapshotted with the Restore Points.
    /// </summary>
    public sealed class RecallState
    {
        public const int PerChapter = 2;
        public bool Learned;
        public int UsesLeft = PerChapter;

        public void BeginChapter() => UsesLeft = PerChapter;

        public bool Use()
        {
            if (!Learned || UsesLeft <= 0) return false;
            UsesLeft--;
            return true;
        }
    }
}
