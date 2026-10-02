using UnityEngine;

namespace HS.Rooms
{
    /// <summary>Keeps only the hero's room ±1 active (the fixed camera never sees further) — perf headroom for 60 fps.</summary>
    public sealed class RoomStreamer : MonoBehaviour
    {
        public ChapterBuilder Chapter;
        public Transform Focus;
        int _last = -99;

        void LateUpdate()
        {
            if (Chapter == null || Focus == null || Chapter.Rooms.Count == 0) return;
            int idx = Chapter.RoomIndexAt(Focus.position.z);
            if (Focus.position.x > 150f) idx = -10; // at the campfire/boss: chain rooms off
            if (idx == _last) return;
            _last = idx;
            for (int i = 0; i < Chapter.Rooms.Count; i++)
                Chapter.Rooms[i].gameObject.SetActive(Mathf.Abs(i - idx) <= 1);
        }
    }
}
