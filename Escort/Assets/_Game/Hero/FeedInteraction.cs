using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>
    /// Feed him (campaign spec §2): next to the hero, out of a fight, the sidekick spends a ration (Interact, 1.5 s). He
    /// holds still for it, the meter refills and he gets 10% of his HP back.
    /// </summary>
    public sealed class FeedInteraction : MonoBehaviour, IInteractable, ISimTickable
    {
        public const float Duration = 1.5f, HealFraction = 0.1f, Reach = 2.6f;
        public const string Label = "Feed him (ration)";
        HeroAgent _hero;
        public int TickOrder => TickOrders.Hero - 1;

        void Awake() => _hero = GetComponent<HeroAgent>();

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Interactables.Register(this);
            SimLoop.Register(this);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            Interactables.Unregister(this);
            SimLoop.Unregister(this);
        }

        public Vector3 InteractPosition => _hero.Position;
        public string Prompt => Label;
        public float InteractDuration => Duration;

        /// <summary>No fight on him: no duel, nobody pressing him.</summary>
        public bool Lull => !(_hero.Module is HS.Hero.Callum.CallumModule cm) || (cm.Challenged == null && cm.Engagers == 0);

        public bool CanInteract(Agent who) =>
            who is SidekickAgent sk && sk.Rations.Count > 0 && _hero.IsAlive && _hero.Hunger.Enabled && _hero.Hunger.Value < Hunger.Max - 5f && Lull;

        public void Interact(Agent who)
        {
            if (!(who is SidekickAgent sk) || !CanInteract(who) || !sk.Rations.Take()) return;
            _hero.Hunger.Feed();
            _hero.Health.Heal(_hero.Health.Max * HealFraction);
            HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Heal, _hero.Position + Vector3.up * 0.9f);
            _hero.Presenter?.PlayAction("consume", 1.2f);
            var ctx = RunContext.Current;
            ctx?.Events.RaiseSkillUsed("feed", sk);
            if (_hero.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(HS.Hero.Callum.CallumModule.FedLines, 1);
        }

        /// <summary>He stops walking while she hands it over (a hero on his route would leave reach mid-ration).</summary>
        public void SimTick(float dt)
        {
            var sk = RunContext.Current != null ? RunContext.Current.Sidekick as SidekickAgent : null;
            bool feeding = sk != null && sk.IsChanneling && sk.ChannelLabel == Label && Geo.FlatDistance(sk.Position, _hero.Position) <= Reach + 1.5f;
            if (feeding) _hero.TreatmentHold = true;
            else if (_holding) _hero.TreatmentHold = false;
            _holding = feeding;
        }

        bool _holding;
    }
}
