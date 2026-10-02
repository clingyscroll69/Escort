using HS.Core;
using UnityEngine;

namespace HS.Skills
{
    public enum SkillFamily { Fixer, Handler, Provisioner, Scholar, Combat, Capstone }
    public enum SkillType { Active, Passive, ActivePassive }

    /// <summary>
    /// Skill data (GDD §5). Numbers are rank-1/rank-2 starting values for the balance harness. Tooltips describe USES,
    /// never recommendations (GDD §4.7).
    /// </summary>
    [CreateAssetMenu(menuName = "HS/Skill", fileName = "Skill")]
    public sealed class SkillDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public SkillFamily family;
        public SkillType type;
        [TextArea] public string uses;
        public string icon;
        [Tooltip("Implemented in this build (the slice ships 6).")]
        public bool implemented;
        public bool capstone;
        public float[] cooldown = { 0f, 0f };
        [Tooltip("Primary value per rank (duration, damage, heal...). See skill behaviour for meaning.")]
        public float[] valueA = { 0f, 0f };
        [Tooltip("Secondary value per rank (range, count...).")]
        public float[] valueB = { 0f, 0f };
        [Tooltip("How Callum's code reads this if witnessed (GDD §6.1 Honor).")]
        public SabotageSeverity dishonor;

        public float Cooldown(int rank) => Pick(cooldown, rank);
        public float A(int rank) => Pick(valueA, rank);
        public float B(int rank) => Pick(valueB, rank);
        public bool UsesSlot => type != SkillType.Passive;

        static float Pick(float[] arr, int rank)
        {
            if (arr == null || arr.Length == 0) return 0f;
            return arr[Mathf.Clamp(rank - 1, 0, arr.Length - 1)];
        }
    }
}
