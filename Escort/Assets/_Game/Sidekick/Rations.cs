using UnityEngine;

namespace HS.Sidekick
{
    /// <summary>What the sidekick carries to feed the hero (campaign spec §2): up to three rations.</summary>
    public sealed class Rations
    {
        public const int Max = 3;
        public int Count { get; private set; }

        /// <summary>Returns how many were actually taken on (a full pack leaves the rest).</summary>
        public int Give(int n)
        {
            int before = Count;
            Count = Mathf.Clamp(Count + Mathf.Max(0, n), 0, Max);
            return Count - before;
        }

        public bool Take()
        {
            if (Count <= 0) return false;
            Count--;
            return true;
        }

        public void Restore(int n) => Count = Mathf.Clamp(n, 0, Max);
    }
}
