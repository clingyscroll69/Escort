using UnityEditor;
using UnityEngine;

namespace HS.Agent
{
    /// <summary>One-time project settings the agent workflow relies on.</summary>
    public static class AgentSetup
    {
        [MenuItem("Tools/Agent/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.companyName = "Unlisted";
            PlayerSettings.productName = "Hero's Sidekick";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Faster play-mode entry and the MCP bridge's domain survives entering play mode.
            // All gameplay statics reset via [RuntimeInitializeOnLoadMethod(SubsystemRegistration)].
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            // Keep the editor ticking while unfocused so agent-driven play sessions progress.
            EditorPrefs.SetInt("InteractionMode", 1); // 1 = no throttling
            AssetDatabase.SaveAssets();
            AgentBridge.Write("setup.json", "{\"ok\":true}");
            Debug.Log("[Agent] project settings applied");
        }
    }
}
