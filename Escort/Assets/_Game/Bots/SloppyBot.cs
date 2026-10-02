using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.Bots
{
    /// <summary>
    /// Balance harness "Sloppy" bot (GDD §11.4): moves near the hero, uses skills at random, never aims (random spots
    /// around itself), swings the knife at whatever is close, dodges sometimes when hurt. Deterministic (seeded).
    /// </summary>
    public sealed class SloppyBot : MonoBehaviour, ISidekickCommands
    {
        DetRandom _rng;
        float _t, _nextSkill = 3f, _nextShuffle;
        Vector3 _offset = new Vector3(2.5f, 0f, -2.5f);
        float _lastHp;

        public SidekickCommand Next(SidekickAgent self)
        {
            var ctx = RunContext.Current;
            _rng ??= new DetRandom(ctx != null ? ctx.Seed * 31 + 7 : 7);
            var c = SidekickCommand.None;
            var hero = ctx != null ? ctx.Hero : null;
            if (hero == null || !hero.IsAlive) return c;
            _t += SimLoop.Dt;
            if (_t >= _nextShuffle)
            {
                _nextShuffle = _t + 2f;
                _offset = new Vector3((float)_rng.NextDouble() * 6f - 3f, 0f, -1.5f - (float)_rng.NextDouble() * 3f);
            }
            c.Move = BotUtil.MoveTo(self, hero.Position + _offset, 1f);
            if (_t >= _nextSkill)
            {
                _nextSkill = _t + 2.5f + (float)_rng.NextDouble() * 3.5f;
                c.Skill = _rng.Range(0, 4);
                c.AimPoint = self.Position + new Vector3((float)_rng.NextDouble() * 10f - 5f, 0f, (float)_rng.NextDouble() * 10f - 5f);
                c.HasAim = true;
            }
            if (AgentRegistry.Nearest(self.Position, 2f, a => a is HS.Enemies.EnemyAgent e && e.IsActive) != null && _rng.Chance(0.1)) c.Attack = true;
            if (self.Health.Current < _lastHp - 0.5f && _rng.Chance(0.3)) c.Dodge = true;
            _lastHp = self.Health.Current;
            return c;
        }
    }
}
