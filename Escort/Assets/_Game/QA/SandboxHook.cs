using HS.Core;
using HS.Hero;
using HS.Sidekick;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Sandbox wiring: registers hero/sidekick in the RunContext (the real game does this in GameFlow).</summary>
    public sealed class SandboxHook : MonoBehaviour
    {
        void Start()
        {
            var ctx = RunContext.Current;
            if (ctx == null) return;
            ctx.Hero = FindAnyObjectByType<HeroAgent>();
            ctx.Sidekick = FindAnyObjectByType<SidekickAgent>();
        }
    }
}
