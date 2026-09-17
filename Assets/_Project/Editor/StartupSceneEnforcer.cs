#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Pressing Play in ANY scene always boots the game from 00_MainMenu,
// exactly like a built player would. Toggle it via the RPG menu in the toolbar.
[InitializeOnLoad]
public static class StartupSceneEnforcer
{
    private const string MenuPath = "RPG/Always Start At Main Menu";
    private const string PrefKey = "RPG_StartAtMainMenu";

    [InitializeOnLoadMethod]
    private static void Apply()
    {
        bool enabled = EditorPrefs.GetBool(PrefKey, true);
        Menu.SetChecked(MenuPath, enabled);
        EditorSceneManager.playModeStartScene = enabled ? LoadMainMenu() : null;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(PrefKey, !EditorPrefs.GetBool(PrefKey, true));
        Apply();
    }

    private static SceneAsset LoadMainMenu()
    {
        // Finds 00_MainMenu in the Build Profiles scene list
        // (falls back to the first listed scene if names ever change).
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled) continue;
            if (scene.path.Contains("00_MainMenu"))
                return AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
        }
        if (EditorBuildSettings.scenes.Length > 0)
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);
        return null;
    }
}
#endif