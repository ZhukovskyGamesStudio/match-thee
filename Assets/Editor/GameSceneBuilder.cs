using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

// Собирает прототип из спрайтов элементов: атлас, конфиг кадров, первый уровень и игровую сцену.
// Меню Match Thee/Build Game Scene. Пока игровой сцены нет, запускается сам при открытии проекта.
public static class GameSceneBuilder {
    private const string ElementsFolder = "Assets/Sprites/Elements";
    private const string AtlasPathV2 = "Assets/Sprites/ElementsAtlas.spriteatlasv2";
    private const string AtlasPathV1 = "Assets/Sprites/ElementsAtlas.spriteatlas";
    private const string ConfigsFolder = "Assets/Configs";
    private const string ElementsConfigPath = ConfigsFolder + "/ElementsConfig.asset";
    private const string LevelConfigPath = ConfigsFolder + "/Level_01.asset";
    private const string SceneTemplatePath = "Assets/Settings/Lit2DSceneTemplate.scenetemplate";
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const int ElementsCount = 50;
    private const double AutoBuildTimeout = 120;

    private const string FirstLevelMap =
@"################
#,,....R...,,,.#
#,@....T....,,.#
#......K..BB...#
#..~~~.........#
#..~~~..G..B...#
#.......X......#
#....R.R...S...#
#:::.......F.M.#
#:::..D.....C..#
#,,....H..O..,,#
################";

    private static readonly (string Symbol, ElementKind Kind)[] DefaultLegend = {
        ("@", ElementKind.Hero), ("#", ElementKind.Wall),
        (",", ElementKind.Grass), ("~", ElementKind.Water), ("^", ElementKind.Lava), ("*", ElementKind.Ice), (":", ElementKind.Sand),
        ("R", ElementKind.Rock), ("T", ElementKind.Tree), ("U", ElementKind.Bush), ("F", ElementKind.Flower),
        ("M", ElementKind.Mushroom), ("C", ElementKind.Cactus), ("O", ElementKind.Cloud), ("X", ElementKind.Crystal),
        ("K", ElementKind.Key), ("D", ElementKind.Door), ("H", ElementKind.Chest), ("B", ElementKind.Box), ("L", ElementKind.Barrel),
        ("G", ElementKind.Gem), ("$", ElementKind.Coin), ("S", ElementKind.Star), ("V", ElementKind.Heart), ("Q", ElementKind.Skull),
        ("!", ElementKind.Bomb), ("I", ElementKind.Torch), ("W", ElementKind.Book), ("P", ElementKind.Potion),
        ("A", ElementKind.Apple), ("E", ElementKind.Egg), ("N", ElementKind.Bone),
        ("b", ElementKind.Button), ("l", ElementKind.Lever), ("g", ElementKind.Gear), ("s", ElementKind.Spring), ("p", ElementKind.Spikes),
        (">", ElementKind.Arrow), ("o", ElementKind.Portal), ("m", ElementKind.Magnet), ("e", ElementKind.Bell),
        ("z", ElementKind.Ghost), ("j", ElementKind.Slime), ("v", ElementKind.Bat), ("f", ElementKind.Frog), ("d", ElementKind.Bird),
        ("u", ElementKind.Sun), ("n", ElementKind.Moon), ("y", ElementKind.Lightning), ("r", ElementKind.Fire),
    };

    private static double _nextCheckTime;
    private static double _autoBuildDeadline;

    [MenuItem("Match Thee/Build Game Scene")]
    public static void Build() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }

        BuildAtlas();
        BuildElementsConfig();
        BuildLevelConfig();
        AssetDatabase.SaveAssets();
        BuildScene();
        AssetDatabase.SaveAssets();
        Debug.Log($"Match Thee: игровая сцена собрана — {ScenePath}");
    }

    [InitializeOnLoadMethod]
    private static void BuildOnFirstLoad() {
        if (File.Exists(ScenePath)) {
            return;
        }

        _autoBuildDeadline = EditorApplication.timeSinceStartup + AutoBuildTimeout;
        EditorApplication.update += WaitForSpritesThenBuild;
    }

    // Ждём, пока импортёр нарежет все полоски: при первом импорте текстуры могут прийти позже компиляции.
    private static void WaitForSpritesThenBuild() {
        if (EditorApplication.timeSinceStartup < _nextCheckTime) {
            return;
        }

        _nextCheckTime = EditorApplication.timeSinceStartup + 0.5;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
            return;
        }

        if (File.Exists(ScenePath)) {
            EditorApplication.update -= WaitForSpritesThenBuild;
            return;
        }

        if (EditorApplication.timeSinceStartup > _autoBuildDeadline) {
            EditorApplication.update -= WaitForSpritesThenBuild;
            Debug.LogWarning("Match Thee: спрайты элементов не нарезаны, сцена не собрана. Запусти Match Thee/Build Game Scene вручную.");
            return;
        }

        if (!AreSpritesSliced()) {
            return;
        }

        EditorApplication.update -= WaitForSpritesThenBuild;
        Build();
    }

    private static string[] ElementTexturePaths() {
        if (!Directory.Exists(ElementsFolder)) {
            return new string[0];
        }

        return Directory.GetFiles(ElementsFolder, "*.png").Select(path => path.Replace('\\', '/')).OrderBy(path => path).ToArray();
    }

    private static bool AreSpritesSliced() {
        string[] paths = ElementTexturePaths();
        return paths.Length >= ElementsCount
            && paths.All(path => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().Count() == ElementRules.WobbleFrames);
    }

    private static void BuildAtlas() {
        Object folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ElementsFolder);
        SpriteAtlasPackingSettings packing = new() {
            padding = 2,
            enableRotation = false,
            enableTightPacking = false,
            enableAlphaDilation = false
        };
        SpriteAtlasTextureSettings texture = new() {
            filterMode = FilterMode.Point,
            generateMipMaps = false,
            sRGB = true,
            readable = false
        };

        bool isV2 = EditorSettings.spritePackerMode is SpritePackerMode.SpriteAtlasV2 or SpritePackerMode.SpriteAtlasV2Build;
        if (isV2) {
            if (!File.Exists(AtlasPathV2)) {
                SpriteAtlasAsset atlas = new();
                atlas.Add(new[] { folder });
                SpriteAtlasAsset.Save(atlas, AtlasPathV2);
                AssetDatabase.ImportAsset(AtlasPathV2);
            }

            if (AssetImporter.GetAtPath(AtlasPathV2) is SpriteAtlasImporter importer) {
                importer.packingSettings = packing;
                importer.textureSettings = texture;
                importer.SaveAndReimport();
            }

            return;
        }

        if (!File.Exists(AtlasPathV1)) {
            SpriteAtlas created = new();
            created.Add(new[] { folder });
            AssetDatabase.CreateAsset(created, AtlasPathV1);
        }

        SpriteAtlas atlasV1 = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPathV1);
        atlasV1.SetPackingSettings(packing);
        atlasV1.SetTextureSettings(texture);
        EditorUtility.SetDirty(atlasV1);
    }

    private static ElementsConfig BuildElementsConfig() {
        EnsureFolder(ConfigsFolder);
        ElementsConfig config = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);
        if (config == null) {
            config = ScriptableObject.CreateInstance<ElementsConfig>();
            AssetDatabase.CreateAsset(config, ElementsConfigPath);
        }

        config.SetFrames(ElementTexturePaths().SelectMany(path => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>()));
        EditorUtility.SetDirty(config);
        return config;
    }

    // Уже существующий уровень не трогаем: там могут быть правки руками.
    private static LevelConfig BuildLevelConfig() {
        EnsureFolder(ConfigsFolder);
        LevelConfig config = AssetDatabase.LoadAssetAtPath<LevelConfig>(LevelConfigPath);
        if (config != null) {
            return config;
        }

        config = ScriptableObject.CreateInstance<LevelConfig>();
        config.Set(FirstLevelMap, DefaultLegend.Select(entry => new LevelLegendEntry { Symbol = entry.Symbol, Kind = entry.Kind }));
        AssetDatabase.CreateAsset(config, LevelConfigPath);
        return config;
    }

    // Ссылки на конфиги берём по пути уже после создания сцены: инстанцирование шаблона
    // переимпортирует свежесозданные ассеты, и старые managed-ссылки на них становятся пустыми.
    private static void BuildScene() {
        Scene scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single) : CreateScene();
        LevelConfig level = AssetDatabase.LoadAssetAtPath<LevelConfig>(LevelConfigPath);
        ElementsConfig elements = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);

        LevelView view = Object.FindAnyObjectByType<LevelView>();
        GameObject levelObject = view != null ? view.gameObject : new GameObject("Level", typeof(LevelView), typeof(HeroInput));
        SceneManager.MoveGameObjectToScene(levelObject, scene);
        view = levelObject.GetComponent<LevelView>();
        if (!levelObject.TryGetComponent(out HeroInput _)) {
            levelObject.AddComponent<HeroInput>();
        }

        SerializedObject viewObject = new(view);
        viewObject.FindProperty("_level").objectReferenceValue = level;
        viewObject.FindProperty("_elements").objectReferenceValue = elements;
        viewObject.FindProperty("_camera").objectReferenceValue = FindCamera();
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject inputObject = new(levelObject.GetComponent<HeroInput>());
        inputObject.FindProperty("_level").objectReferenceValue = view;
        inputObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    // Сцена из шаблона URP 2D (камера + Global Light 2D); без глобального света lit-спрайты чёрные.
    private static Scene CreateScene() {
        SceneTemplateAsset template = AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(SceneTemplatePath);
        if (template != null) {
            InstantiationResult result = SceneTemplateService.Instantiate(template, false, ScenePath);
            if (result != null) {
                ConfigureCamera();
                return result.scene;
            }
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        cameraObject.GetComponent<Camera>().GetUniversalAdditionalCameraData();

        GameObject lightObject = new("Global Light 2D", typeof(Light2D));
        Light2D light = lightObject.GetComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;

        ConfigureCamera();
        return scene;
    }

    private static void ConfigureCamera() {
        Camera camera = FindCamera();
        if (camera == null) {
            return;
        }

        camera.orthographic = true;
        camera.orthographicSize = 7f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(16, 16, 20, 255);
        camera.transform.position = new Vector3(0f, 0f, -10f);
    }

    private static Camera FindCamera() {
        return Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
    }

    private static void EnsureFolder(string path) {
        if (AssetDatabase.IsValidFolder(path)) {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
