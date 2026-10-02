using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HS.Skills
{
    /// <summary>All skills (Resources/SkillCatalog). The level-up System window lists these with family filters.</summary>
    [CreateAssetMenu(menuName = "HS/Skill Catalog", fileName = "SkillCatalog")]
    public sealed class SkillCatalog : ScriptableObject
    {
        public List<SkillDefinition> skills = new List<SkillDefinition>();

        static SkillCatalog _instance;
        public static SkillCatalog Load() => _instance != null ? _instance : _instance = Resources.Load<SkillCatalog>("SkillCatalog");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        public SkillDefinition Get(string id) => skills.FirstOrDefault(s => s != null && s.id == id);
        public IEnumerable<SkillDefinition> Implemented => skills.Where(s => s != null && s.implemented && !s.capstone);
    }
}
