using UnityEditor;
using UnityEditor.SceneManagement;

namespace HS.EditorTools
{
    /// <summary>
    /// QA scenes (Scenes/QA_*.unity) are built and played by the QA tools. When a run ends the editor goes back to the
    /// game's Main scene, so the next press of Play plays the game, not the last QA run (the opening scrub, for one,
    /// fast-forwards the opening and then stops play mode).
    /// </summary>
    [InitializeOnLoad]
    public static class QaSceneGuard
    {
        const string QaPrefix = "Assets/_Game/Scenes/QA_";

        static QaSceneGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        // EnteredEditMode comes after the play session's scenes are restored (delayCall doesn't fire while the editor
        // sits in the background, so this runs right here).
        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredEditMode) BackToMain();
        }

        public static void BackToMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.StartsWith(QaPrefix) || scene.isDirty || EditorSceneManager.sceneCount > 1) return;
            EditorSceneManager.OpenScene(GameAssetsBuilder.MainScenePath);
        }
    }
}
