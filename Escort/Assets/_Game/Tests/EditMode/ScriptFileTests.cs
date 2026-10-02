using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HS.Tests
{
    /// <summary>
    /// Guard: Unity can only serialise a MonoBehaviour/ScriptableObject whose class lives in a file of the same name.
    /// Twice a class hidden in another file produced silent "missing script" bugs (RouteMarker, HeroRuleSetDef).
    /// </summary>
    public class ScriptFileTests
    {
        [Test]
        public void Every_Serializable_Unity_Class_Has_A_Matching_Script_File()
        {
            var scripted = MonoImporter.GetAllRuntimeMonoScripts().Select(m => m.GetClass()).Where(c => c != null).ToHashSet();
            var asm = typeof(HS.Core.Agent).Assembly;
            var offenders = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsGenericType && (typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t)))
                .Where(t => !scripted.Contains(t))
                .Select(t => t.FullName)
                .ToList();
            Assert.IsEmpty(offenders, "move these into files named after the class: " + string.Join(", ", offenders));
        }
    }
}
