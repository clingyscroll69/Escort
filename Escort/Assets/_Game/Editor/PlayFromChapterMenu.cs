using UnityEditor;
using UnityEditor.SceneManagement;

namespace HS.EditorTools
{
    /// <summary>Owner's shortcut: press Play straight into chapter N with the kit a thorough player would have by then.</summary>
    public static class PlayFromChapterMenu
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("Tools/HS/Play from chapter/2 · Whisperwood")] static void Ch2() => Play(2);
        [MenuItem("Tools/HS/Play from chapter/3 · Catacombs of Ends")] static void Ch3() => Play(3);
        [MenuItem("Tools/HS/Play from chapter/4 · The Sunken Bastion")] static void Ch4() => Play(4);
        [MenuItem("Tools/HS/Play from chapter/5 · The Gallery")] static void Ch5() => Play(5);

        static void Play(int chapter)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorSceneManager.GetActiveScene().path != MainScene) EditorSceneManager.OpenScene(MainScene);
            SessionState.SetInt(HS.Flow.GameFlow.PlayFromChapterKey, chapter);
            EditorApplication.isPlaying = true;
        }
    }
}
