using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Собирает сцену меню Scenes/MenuScene (камера с тёмным фоном и MenuView) и ставит её первой в сборку.
// Меню Match Thee/Build Menu Scene.
public static class MenuSceneBuilder {
    private const string ScenePath = "Assets/Scenes/MenuScene.unity";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string ElementsConfigPath = "Assets/Configs/ElementsConfig.asset";

    [MenuItem("Match Thee/Build Menu Scene")]
    public static void Build() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(16, 16, 20, 255);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        GameObject menuObject = new("Menu", typeof(RectTransform), typeof(MenuView));
        SerializedObject menu = new(menuObject.GetComponent<MenuView>());
        menu.FindProperty("_elements").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);
        menu.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        Debug.Log($"Match Thee: сцена меню собрана — {ScenePath}, в сборке первая");
    }
}
