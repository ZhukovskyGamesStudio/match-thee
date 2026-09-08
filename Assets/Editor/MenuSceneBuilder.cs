using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Собирает сцену меню Scenes/MenuScene: камера с тёмным фоном и экземпляр префаба Resources/Ui/MainMenu.
// Ставит её первой в сборку. Меню Match Thee/Build Menu Scene (префабы — Match Thee/Build UI Prefabs).
public static class MenuSceneBuilder {
    private const string ScenePath = "Assets/Scenes/MenuScene.unity";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string MenuPrefabPath = "Assets/Resources/Ui/MainMenu.prefab";

    [MenuItem("Match Thee/Build Menu Scene")]
    public static void Build() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }

        GameObject menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
        if (menuPrefab == null) {
            Debug.LogWarning($"Match Thee: нет префаба {MenuPrefabPath} — сначала Match Thee/Build UI Prefabs");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(16, 16, 20, 255);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        PrefabUtility.InstantiatePrefab(menuPrefab, scene);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        Debug.Log($"Match Thee: сцена меню собрана — {ScenePath}, в сборке первая");
    }
}
