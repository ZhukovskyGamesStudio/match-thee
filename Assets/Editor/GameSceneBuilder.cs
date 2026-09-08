using System;
using System.Collections.Generic;
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

// Собирает прототип из спрайтов элементов: атлас, конфиг кадров, карту мира и игровую сцену.
// Меню Match Thee/Build Game Scene. Пока игровой сцены нет, запускается сам при открытии проекта.
public static class GameSceneBuilder {
    private const string ElementsFolder = "Assets/Sprites/Elements";
    private const string AtlasPathV2 = "Assets/Sprites/ElementsAtlas.spriteatlasv2";
    private const string AtlasPathV1 = "Assets/Sprites/ElementsAtlas.spriteatlas";
    private const string ConfigsFolder = "Assets/Configs";
    private const string ElementsConfigPath = ConfigsFolder + "/ElementsConfig.asset";
    private const string WorldConfigPath = ConfigsFolder + "/World.asset";
    private const string SceneTemplatePath = "Assets/Settings/Lit2DSceneTemplate.scenetemplate";
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const double AutoBuildTimeout = 120;

    private const int ScreenWidth = 32;
    private const int ScreenHeight = 18;
    private const int WorldSeed = 1;

    // Мир 2x2 экрана. `%` — стена из перемешанных деревьев, кустов и камней (раскладывается при загрузке),
    // `#` — неподвижный блок (в наборе на будущее, сейчас не используется). Проходы между экранами — разрывы в `%`.
    private const string ScreenTopLeft =
@"%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
%,,,..........................,%
%,@....R.......K....BB.......,,%
%,.............................%
%.....~~~~......X..............%
%.....~~~~.........G...........%
%.....~~~~......................
%........R......................
%.......R.R.....................
%:::..........S................%
%:::.......................D...%
%:::.....B.....................%
%..............H....t.MFM......%
%.....T................M......,%
%............................,,%
%,,...........................,%
%..............................%
%%%%%%%%%%%%%%%....%%%%%%%%%%%%%";

    private const string ScreenTopRight =
@"%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
%..............................%
%...........~~~~~~.............%
%..........~~~~~~~~......K.....%
%..........~~~~~~~~............%
%...........~~~~~~.............%
......B........................%
...............................%
..........R....................%
%....................%%........%
%...................%..%.......%
%....G..............%.G.%......%
%...................%..%.......%
%....................%%........%
%..:::.........................%
%..:::.....................T...%
%..............................%
%%%%%%%%%%%....%%%%%%%%%%%%%%%%%";

    private const string ScreenBottomLeft =
@"%%%%%%%%%%%%%%%....%%%%%%%%%%%%%
%..............................%
%....**********................%
%....**********......B.........%
%....**********................%
%....**********................%
%..............................%
%.........K....................%
%......^^^^.....................
%......^^^^.....................
%......^^^^.....R...............
%..............................%
%....................X.........%
%..............................%
%,,,,.......................,,,%
%,,,,,.....................,,,,%
%,,,..........$..............,,%
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%";

    private const string ScreenBottomRight =
@"%%%%%%%%%%%....%%%%%%%%%%%%%%%%%
%..............................%
%..........................,,,,%
%.......B..................,,,,%
%..............................%
%.......................%%%%...%
%.......................%..%...%
%.......................%.H%...%
........................%..%...%
........................%%%%...%
...............................%
%.....~~~~.....................%
%.....~~~~.....................%
%..............................%
%..........S...................%
%..............................%
%..............................%
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%";

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
        ("t", ElementKind.Throne), ("&", ElementKind.Person),
        ("Y", ElementKind.Spruce), ("i", ElementKind.BerryBush), ("q", ElementKind.DarkTree), ("k", ElementKind.Birch), ("h", ElementKind.Stump),
        ("1", ElementKind.CastleWall), ("2", ElementKind.CastleWall2), ("3", ElementKind.CastleWall3), ("0", ElementKind.CastleWindow),
        ("=", ElementKind.CastleFloor),
        ("4", ElementKind.KeyW), ("5", ElementKind.KeyA), ("6", ElementKind.KeyS), ("7", ElementKind.KeyD),
        ("{", ElementKind.ArrowUp), ("[", ElementKind.ArrowLeft), ("}", ElementKind.ArrowDown), ("]", ElementKind.ArrowRight),
        ("?", ElementKind.MouseClick),
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
        BuildWorldConfig();
        AssetDatabase.SaveAssets();
        BuildScene();
        AssetDatabase.SaveAssets();
        ValidateWorld();
        Debug.Log($"Match Thee: игровая сцена собрана — {ScenePath}");
    }

    // Перезаписывает карту и легенду в существующем конфиге встроенными экранами (GUID ассета сохраняется).
    [MenuItem("Match Thee/Reset World Map")]
    public static void ResetWorldMap() {
        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        if (world == null) {
            BuildWorldConfig();
            AssetDatabase.SaveAssets();
            ValidateWorld();
            return;
        }

        world.Set(DefaultMap(), DefaultLegendEntries(), ScreenWidth, ScreenHeight, WorldSeed);
        EditorUtility.SetDirty(world);
        AssetDatabase.SaveAssets();
        ValidateWorld();
        Debug.Log($"Match Thee: карта мира перезаписана — {WorldConfigPath}");
    }

    // Ищет на карте ряды из трёх одинаковых элементов: такие бы сразу соединились и исчезли.
    [MenuItem("Match Thee/Validate World")]
    public static void ValidateWorld() {
        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        if (world == null) {
            Debug.LogWarning($"Match Thee: нет конфига мира {WorldConfigPath}");
            return;
        }

        List<Vector2Int> runs = WorldMap.FindRuns(world.BuildCells());
        if (runs.Count > 0) {
            Debug.LogWarning($"Match Thee: на карте {runs.Count} клеток в рядах из трёх одинаковых: {string.Join(", ", runs.Take(12))}");
        } else {
            Debug.Log("Match Thee: карта мира без рядов из трёх одинаковых");
        }
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
        return paths.Length > 0
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
                // Пиксель-арт без сжатия: блочное сжатие размазывает мелкие детали (корона, обводки) и мерцает по кадрам.
                foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "WebGL" }) {
                    TextureImporterPlatformSettings settings = importer.GetPlatformSettings(platform);
                    settings.overridden = true;
                    settings.textureCompression = TextureImporterCompression.Uncompressed;
                    settings.format = TextureImporterFormat.RGBA32;
                    importer.SetPlatformSettings(settings);
                }

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

    private static void BuildElementsConfig() {
        EnsureFolder(ConfigsFolder);
        ElementsConfig config = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);
        if (config == null) {
            config = ScriptableObject.CreateInstance<ElementsConfig>();
            AssetDatabase.CreateAsset(config, ElementsConfigPath);
        }

        config.SetFrames(ElementTexturePaths().SelectMany(path => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>()));
        EditorUtility.SetDirty(config);
    }

    // Уже существующую карту не трогаем: там могут быть правки руками.
    private static void BuildWorldConfig() {
        EnsureFolder(ConfigsFolder);
        if (AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath) != null) {
            return;
        }

        WorldConfig config = ScriptableObject.CreateInstance<WorldConfig>();
        config.Set(DefaultMap(), DefaultLegendEntries(), ScreenWidth, ScreenHeight, WorldSeed);
        AssetDatabase.CreateAsset(config, WorldConfigPath);
    }

    private static string DefaultMap() {
        return StitchScreens(new[,] {
            { ScreenTopLeft, ScreenTopRight },
            { ScreenBottomLeft, ScreenBottomRight },
        });
    }

    private static IEnumerable<LegendEntry> DefaultLegendEntries() {
        return DefaultLegend.Select(entry => new LegendEntry { Symbol = entry.Symbol, Kind = entry.Kind });
    }

    // Склеивает экраны [ряд, колонка] в одну карту. Соседние экраны делят крайний столбец и крайнюю
    // строку, поэтому стена между ними в одну клетку; при расхождении общей клетки берётся левый/верхний экран.
    private static string StitchScreens(string[,] screens) {
        int screenRows = screens.GetLength(0);
        int screenColumns = screens.GetLength(1);
        string[][][] lines = new string[screenRows][][];
        for (int screenRow = 0; screenRow < screenRows; screenRow++) {
            lines[screenRow] = new string[screenColumns][];
            for (int screenColumn = 0; screenColumn < screenColumns; screenColumn++) {
                string[] screen = screens[screenRow, screenColumn].Replace("\r", "").Split('\n');
                if (screen.Length != ScreenHeight) {
                    throw new InvalidOperationException($"экран [{screenRow},{screenColumn}]: строк {screen.Length}, нужно {ScreenHeight}");
                }

                for (int line = 0; line < ScreenHeight; line++) {
                    if (screen[line].Length != ScreenWidth) {
                        throw new InvalidOperationException($"экран [{screenRow},{screenColumn}], строка {line}: длина {screen[line].Length}, нужно {ScreenWidth}");
                    }
                }

                lines[screenRow][screenColumn] = screen;
            }
        }

        List<string> rows = new();
        for (int screenRow = 0; screenRow < screenRows; screenRow++) {
            for (int line = 0; line < ScreenHeight; line++) {
                if (screenRow > 0 && line == 0) {
                    continue; // строка общая с экраном выше
                }

                System.Text.StringBuilder row = new(lines[screenRow][0][line]);
                for (int screenColumn = 1; screenColumn < screenColumns; screenColumn++) {
                    string current = lines[screenRow][screenColumn][line];
                    if (current[0] != lines[screenRow][screenColumn - 1][line][ScreenWidth - 1]) {
                        Debug.LogWarning($"Match Thee: экраны [{screenRow},{screenColumn - 1}] и [{screenRow},{screenColumn}], строка {line}: общая клетка задана по-разному, взята левая");
                    }

                    row.Append(current, 1, ScreenWidth - 1);
                }

                rows.Add(row.ToString());
            }

            if (screenRow > 0) {
                for (int screenColumn = 0; screenColumn < screenColumns; screenColumn++) {
                    if (lines[screenRow][screenColumn][0] != lines[screenRow - 1][screenColumn][ScreenHeight - 1]) {
                        Debug.LogWarning($"Match Thee: экраны [{screenRow - 1},{screenColumn}] и [{screenRow},{screenColumn}]: общая строка задана по-разному, взята верхняя");
                    }
                }
            }
        }

        return string.Join("\n", rows);
    }

    // Ссылки на конфиги берём по пути уже после создания сцены: инстанцирование шаблона
    // переимпортирует свежесозданные ассеты, и старые managed-ссылки на них становятся пустыми.
    private static void BuildScene() {
        Scene scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single) : CreateScene();
        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        ElementsConfig elements = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);

        GameObject stale = GameObject.Find("Level");
        if (stale != null) {
            Object.DestroyImmediate(stale);
        }

        WorldView view = Object.FindAnyObjectByType<WorldView>();
        GameObject worldObject = view != null ? view.gameObject : new GameObject("World", typeof(WorldView), typeof(PlayerInput));
        SceneManager.MoveGameObjectToScene(worldObject, scene);
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(worldObject);
        view = worldObject.GetComponent<WorldView>();
        if (!worldObject.TryGetComponent(out PlayerInput _)) {
            worldObject.AddComponent<PlayerInput>();
        }

        SerializedObject viewObject = new(view);
        viewObject.FindProperty("_world").objectReferenceValue = world;
        viewObject.FindProperty("_elements").objectReferenceValue = elements;
        viewObject.FindProperty("_camera").objectReferenceValue = FindCamera();
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject inputObject = new(worldObject.GetComponent<PlayerInput>());
        inputObject.FindProperty("_world").objectReferenceValue = view;
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
        camera.orthographicSize = ScreenHeight / 2f;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false; // постобработки нет, в WebGL её шейдеры вырезаны
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
