using System;
using System.Collections.Generic;

namespace HS.Core
{
    /// <summary>
    /// Deterministic, platform-independent RNG (xorshift64*). The ONLY randomness in the game is the run seed used
    /// to assemble levels (GDD §2, conventions). Never use UnityEngine.Random or System.Random in gameplay.
    /// </summary>
    public sealed class DetRandom
    {
        ulong _state;

        public DetRandom(int seed)
        {
            // SplitMix64 scramble so small seeds still diverge quickly.
            ulong z = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            _state = z ^ (z >> 31);
            if (_state == 0) _state = 0x2545F4914F6CDD1DUL;
        }

        public ulong NextULong()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>[min, maxExclusive)</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }

        public bool Chance(double p) => NextDouble() < p;

        public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Derive an independent stream (e.g. per room) without disturbing this one.</summary>
        public DetRandom Fork(int salt) => new DetRandom(unchecked((int)(NextULong() >> 32) ^ salt * 7919));

        public static int HashString(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h;
            }
        }
    }
}
