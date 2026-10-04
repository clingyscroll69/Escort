using HS.Core;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Off-route cache that pays the exploration share of the room's XP pot (GDD §4.1). Searching it (Interact, 0.8 s)
    /// shows its note in the System window — small pieces of the Curator's trail for those who look.
    /// </summary>
    public sealed class ExploreAnchor : MonoBehaviour, IInteractable
    {
        public string Title = "Abandoned satchel";
        [TextArea] public string Note = "";
        [Tooltip("Rations the sidekick finds here (a forage cache).")]
        public int Rations;
        public bool Searched { get; private set; }

        void OnEnable()
        {
            if (Application.isPlaying) Interactables.Register(this);
        }

        void OnDisable()
        {
            if (Application.isPlaying) Interactables.Unregister(this);
        }

        public Vector3 InteractPosition => transform.position;
        public string Prompt => "Search: " + Title;
        public float InteractDuration => 0.8f;
        public bool CanInteract(Agent who) => !Searched && who is HS.Sidekick.SidekickAgent;

        public void Interact(Agent who)
        {
            if (Searched) return;
            Searched = true;
            var ctx = RunContext.Current;
            var room = GetComponentInParent<RoomModule>();
            int found = Rations > 0 && who is HS.Sidekick.SidekickAgent sk ? sk.Rations.Give(Rations) : 0;
            string extra = found > 0 ? $"\n<size=80%>+{found} ration{(found > 1 ? "s" : "")}.</size>" : "";
            ctx?.Events.RaiseNotice($"{Title.ToUpperInvariant()}\n<size=80%>{Note}</size>{extra}");
            ctx?.Events.Explored?.Invoke(room != null ? room.RoomIndex : -1, Title);
        }
    }
}
