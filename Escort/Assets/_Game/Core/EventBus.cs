using System;
using UnityEngine;

namespace HS.Core
{
    /// <summary>A sidekick action the hero might consider dishonourable (GDD §6.1 Honor / Caught).</summary>
    public enum SabotageSeverity { None, Minor, Major }

    public struct SabotageEvent
    {
        public string Tag;                 // skill/verb id: "pocket_sand", "loosen_bolt", "crossbow", "knife"
        public SabotageSeverity Severity;
        public Vector3 Position;           // where the dirty deed visibly happened (the effect, not the sidekick)
        public Vector3 ActorPosition;      // where the sidekick stood
        public Agent Victim;
        public float Time;
    }

    public struct PingInfo
    {
        public Vector3 Point;
        public Agent Target;               // null = place ping
        public string Meaning;             // "mark" (baseline); Signal Codes adds "hold","advance","fall_back"
    }

    public struct BarkInfo
    {
        public string SpeakerId;           // "callum", "sidekick", "ashgrave", "system"
        public string Text;
        public float Duration;
        public int Priority;
    }

    public enum DuelEndReason { TargetDied, TargetFled, HeroDied, Abandoned, Replaced }

    /// <summary>Per-run event hub. Fields (not C# events) so any system can raise them.</summary>
    public sealed class EventBus
    {
        public Action<DamageInfo, float> Damage;
        public Action<Agent, DamageInfo> Death;
        public Action<SabotageEvent> Sabotage;
        public Action<PingInfo> Ping;
        public Action<string, Agent> SkillUsed;
        public Action<Agent, Agent> DuelStarted;
        public Action<Agent, Agent, DuelEndReason> DuelEnded;
        public Action<Agent> SaluteFinished;
        public Action<BarkInfo> Bark;
        public Action<string> SystemNotice;
        public Action<string> ThoughtPopup;   // qualitative sidekick-side feedback ("Nobody saw that.")
        public Action<int> RoomEntered;       // room index
        public Action<int> RoomCleared;
        public Action<string> FlowChanged;    // game-flow state id
        public Action<float, float> CoverStory;     // (honour restore, penalty-halving window s)
        public Action<Agent, Agent> WoundTreated;   // (healer, patient)
        public Action<Agent, string> Revealed;      // hidden enemy revealed (by what)
        public Action<Vector3, string> PropCollapsed; // armable prop impact point, kind
        public Action<Agent, Agent> AttackResolving; // (attacker, victim) just before a melee hit lands — parry window
        public Action<Agent, string, bool> WoundChanged; // (who, wound type, added?)
        public Action<Vector3, string, Agent> HazardSprung; // (where, kind, victim)
        public Action<int, string> Explored;                // (room index, cache title) — the exploration XP share
        public Action<Agent, string> ProjectileFired;       // (shooter, tag) — presentation (audio)

        public void RaiseDamage(DamageInfo d, float applied) => Damage?.Invoke(d, applied);
        public void RaiseDeath(Agent a, DamageInfo d) => Death?.Invoke(a, d);
        public void RaiseSabotage(SabotageEvent e) => Sabotage?.Invoke(e);
        public void RaisePing(PingInfo p) => Ping?.Invoke(p);
        public void RaiseSkillUsed(string id, Agent user) => SkillUsed?.Invoke(id, user);

        public void RaiseBark(string speaker, string text, float duration = 2.6f, int priority = 0)
            => Bark?.Invoke(new BarkInfo { SpeakerId = speaker, Text = text, Duration = duration, Priority = priority });

        public void RaiseNotice(string text) => SystemNotice?.Invoke(text);
        public void RaiseThought(string text) => ThoughtPopup?.Invoke(text);
    }
}
