using HS.Tutorial;
using UnityEditor;

namespace HS.EditorTools
{
    /// <summary>
    /// In the editor each Play starts as a first-time player, so the whole tutorial shows every time. Tick this to keep
    /// what has been seen between Plays, as a build does (in game: Pause ▸ Settings ▸ Reset Tutorial clears it).
    /// </summary>
    public static class TutorialMenu
    {
        const string Item = "Tools/HS/Tutorial/Remember Lessons Between Plays";

        [MenuItem(Item)]
        static void Toggle() => TutorialProgress.RememberInEditor = !TutorialProgress.RememberInEditor;

        [MenuItem(Item, true)]
        static bool Validate()
        {
            Menu.SetChecked(Item, TutorialProgress.RememberInEditor);
            return true;
        }
    }
}
