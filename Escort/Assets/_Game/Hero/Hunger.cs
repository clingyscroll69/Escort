using System;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>
    /// Attrition (campaign spec §2, chapters 2–4): a meter on the hero that drains over about six minutes of road. At 0 he is
    /// Starving: −20% damage and no between-room recovery. A ration (fed by the sidekick) or the camp refills it. Plain C#.
    /// </summary>
    public sealed class Hunger
    {
        public const float Max = 100f, DrainPerSecond = Max / 360f, HungryBelow = 30f, StarvingDamageMul = 0.8f;

        public float Value { get; private set; } = Max;
        /// <summary>On for the chapters with hunger (schedule).</summary>
        public bool Enabled;
        /// <summary>Off the road (camp, door, boss): no drain.</summary>
        public bool Paused;
        public bool Hungry => Enabled && Value < HungryBelow;
        public bool Starving => Enabled && Value <= 0f;
        public float Fraction => Value / Max;
        /// <summary>(hungry, starving) whenever either changes.</summary>
        public event Action<bool, bool> Changed;

        public void Tick(float dt)
        {
            if (!Enabled || Paused || Value <= 0f) return;
            bool h = Hungry, s = Starving;
            Value = Mathf.Max(0f, Value - DrainPerSecond * dt);
            if (h != Hungry || s != Starving) Changed?.Invoke(Hungry, Starving);
        }

        public void Feed() => Set(Max);
        public void Restore(float v) => Set(v);

        void Set(float v)
        {
            bool h = Hungry, s = Starving;
            Value = Mathf.Clamp(v, 0f, Max);
            if (h != Hungry || s != Starving) Changed?.Invoke(Hungry, Starving);
        }
    }
}
