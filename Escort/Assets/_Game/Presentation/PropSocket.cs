using UnityEngine;

namespace HS.Presentation
{
    /// <summary>Held-prop socket metadata written by CharacterPrefabBuilder (axes in socket space).</summary>
    public sealed class PropSocket : MonoBehaviour
    {
        public string PropName;
        /// <summary>Blade/barrel direction (pommel → tip) in socket space.</summary>
        public Vector3 BladeAxis = Vector3.forward;
        /// <summary>Flat-of-blade normal in socket space.</summary>
        public Vector3 FlatNormal = Vector3.right;
    }
}
