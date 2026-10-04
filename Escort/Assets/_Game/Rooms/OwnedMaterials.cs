using UnityEngine;

namespace HS.Rooms
{
    /// <summary>A renderer's own material instances (tints, the glitch), destroyed with it.</summary>
    public sealed class OwnedMaterials : MonoBehaviour
    {
        Material[] _owned;

        public static Material[] Of(Renderer r)
        {
            var keeper = r.GetComponent<OwnedMaterials>();
            if (keeper == null) keeper = r.gameObject.AddComponent<OwnedMaterials>();
            if (keeper._owned == null) keeper._owned = r.materials; // Unity copies the shared materials once
            return keeper._owned;
        }

        void OnDestroy()
        {
            if (_owned == null) return;
            foreach (var m in _owned) if (m != null) Destroy(m);
        }
    }
}
