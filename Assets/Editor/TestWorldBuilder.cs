using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Тестовый мир с рельефом: поверхность (низина и возвышенность), пещера под плато и нижняя пещера.
// Меню Match Thee/Build Test World — собирает Configs/WorldTest.asset и Scenes/TestScene.unity.
// Основной мир и список сцен сборки не трогает: сцену открывать вручную.
//
// Как читать карты ниже (символ на клетку, верхняя строка — верх мира):
//   -  низина          +  возвышенность    a  пологий склон наверх   _  клетки на этом уровне нет
//   ;  пол пещеры      |  скала пещеры     )  пол нижней пещеры
//   (  вход в пещеру (ведёт на поверхность)   x  лестница на плато   c  спуск в нижнюю пещеру
//
// Обрыв НЕ рисуют: борт, углы и тень вырастают сами по краю возвышенности (TerrainTiles).
// Между разными высотами не шагнуть — только через склон или переход.
//
// Правило переходов: клетка перехода лежит на своём уровне, но видна и проходима ещё с того,
// на который ведёт. Поэтому там, где стоит переход, на соседнем уровне должно быть «клетки нет»:
// у входов в пещеру и у лестницы наверх поверхность прорезана (_), а спуск в нижнюю пещеру стоит
// в южной стене пещеры — снаружи над ним край плато.
public static class TestWorldBuilder {
    private const string WorldConfigPath = "Assets/Configs/WorldTest.asset";
    private const string ElementsConfigPath = "Assets/Configs/ElementsConfig.asset";
    private const string ScenePath = "Assets/Scenes/TestScene.unity";
    private const string ConfigsFolder = "Assets/Configs";

    private const int ScreenWidth = 32;
    private const int ScreenHeight = 18;
    private const int WorldSeed = 1;

    // Поверхность: низина внизу, возвышенность сверху, её язык посередине и склон на южном конце языка.
    // Дыры у подножия — входы в пещеру, дыра в плато — колодец лестницы: там клетки уровня нет.
    private const string SurfaceMap =
@"+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
++++++++++++++++++++++K+++++++++++++++++R++++++++++++++++++++++
++++++++++++++++++++++++++++++++++++++++++++_++++++++++++++++++
++++++++++++++++R++++++++++++++++++++++++++++++++++++++++++++++
++++++++++++++++++++++++++++++++++++++++++++++++++T++++++++++++
++++++++++++T++++++++++++++++++++++++++++++++++++++++++++++++++
+++++++++++++++++++++++++++++++M++++++++++++++++++++++++U++++++
+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
++++++++_+++++++++++++++++++++++++++++++++++++++++++++_++++++++
---------------------------+++++++++---------------------------
---------B--------------H--+++++++++---------------------------
--------B------------------+++F+++++--------B-------F----------
------------t---------L----+++++++++----R----------------------
-------R--------@----------+++++++++------------T--------------
---------------------------++++a++++---------------------------
--------------------M---------------U--------------------------
---------------------------------------------------------------
---------------------------------------------------------------";

    // Пещера под плато: скала по краю, два входа у подножия (стена под ними прорублена),
    // лестница наверх в северной стене и спуск в нижнюю пещеру в южной.
    private const string CaveMap =
@"_______________________________________________________________
_______________________________________________________________
___|||||||||||||||||||||||||||||||||||||||||x|||||||||||||||___
___|;;;;;;;;X;;;;;;;;;;;;;;;L;;;$;;;;;;;;;I;;;;;S;;;;;;;;;;|___
___|;;;;;;;;;;;;;;;;|||||||;;;;;;;;;;;;;;;;;;;;;;;G;;;;;;;;|___
___|;;;;;;;;;$;;X;;;;;;;;;|;;;;;;;;;||||||||;;;;;;;;;;;;;;;|___
___|;;;;;;$;;;;;;;;;;;I;;;|;;;G;;;;;|;;;t;;;;;;;;;;;;;;;;;;|___
___|;;;;;;;;;;;;;S;;;;;;;;;;;;;;;;B;|;;;;;;;;;X;;;;;;;;;;;;|___
___|||||(|||||||||||c|||||||||||||||||||||||||||||||||(|||||___
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________";

    // Нижняя пещера: лежит под низиной, попасть в неё можно только спуском из пещеры.
    private const string DeepMap =
@"_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
____________||||||||)||||||||||________________________________
____________|)))))))))))))))))|________________________________
____________|)$)))))H)))|)))))|________________________________
____________|)))))))))))|))X))|________________________________
____________|))))G))))))|)))))|________________________________
____________|)))))))))V)))))K)|________________________________
____________|||||||||||||||||||________________________________
_______________________________________________________________
_______________________________________________________________";

    private static readonly (string Symbol, ElementKind Kind)[] TerrainLegend = {
        ("-", ElementKind.Lowland), ("+", ElementKind.Highland), ("/", ElementKind.Cliff), ("a", ElementKind.Slope),
        ("(", ElementKind.CaveMouth), (";", ElementKind.CaveFloor), ("|", ElementKind.CaveWall),
        (")", ElementKind.DeepFloor), ("x", ElementKind.StairsUp), ("c", ElementKind.StairsDown),
    };

    [MenuItem("Match Thee/Build Test World")]
    public static void Build() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }

        GameSceneBuilder.BuildAtlas();
        GameSceneBuilder.BuildElementsConfig();
        BuildWorldConfig();
        AssetDatabase.SaveAssets();
        BuildScene();
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log($"Match Thee: тестовый мир собран — {ScenePath} (открой сцену и запусти)");
    }

    // Перезаписывает карты уровней встроенными (GUID ассета сохраняется, правки в World Painter теряются).
    [MenuItem("Match Thee/Reset Test World Map")]
    public static void ResetMap() {
        BuildWorldConfig(reset: true);
        AssetDatabase.SaveAssets();
        Validate();
    }

    // Ряды из трёх одинаковых на карте и клетки, в которые с одного уровня ведут сразу две дороги.
    [MenuItem("Match Thee/Validate Test World")]
    public static void Validate() {
        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        if (world == null) {
            Debug.LogWarning($"Match Thee: нет конфига {WorldConfigPath} — сначала Match Thee/Build Test World");
            return;
        }

        WorldGrid grid = world.BuildGrid();
        List<Vector3Int> runs = WorldMap.FindRuns(grid);
        List<Vector3Int> ambiguous = grid.FindAmbiguous();
        if (runs.Count > 0) {
            Debug.LogWarning($"Match Thee: в тестовом мире {runs.Count} клеток в рядах из трёх одинаковых: {string.Join(", ", runs.Take(12))}");
        }

        if (ambiguous.Count > 0) {
            Debug.LogWarning($"Match Thee: в тестовом мире {ambiguous.Count} клеток, куда с одного уровня ведут сразу две дороги: {string.Join(", ", ambiguous.Take(12))}");
        }

        if (runs.Count == 0 && ambiguous.Count == 0) {
            Debug.Log($"Match Thee: тестовый мир {grid.Width}x{grid.Height}, уровней {grid.Layers} — карта в порядке");
        }
    }

    private static void BuildWorldConfig(bool reset = false) {
        GameSceneBuilder.EnsureFolder(ConfigsFolder);
        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        if (world == null) {
            world = ScriptableObject.CreateInstance<WorldConfig>();
            AssetDatabase.CreateAsset(world, WorldConfigPath);
        } else if (!reset) {
            return; // карту не трогаем: там могут быть правки руками
        }

        world.SetLayered(Layers(), Legend(), ScreenWidth, ScreenHeight, WorldSeed);
        EditorUtility.SetDirty(world);
    }

    private static IEnumerable<WorldLayer> Layers() {
        yield return new WorldLayer { Name = "поверхность", Map = SurfaceMap };
        yield return new WorldLayer {
            Name = "пещера",
            Map = CaveMap,
            Links = new List<LinkRule> {
                new() { Kind = ElementKind.CaveMouth, Layer = 0 },
                new() { Kind = ElementKind.StairsUp, Layer = 0 },
            },
        };
        // Спуск принадлежит нижней пещере, а в пещере над ним дырка: стоя на лестнице, ты уже внизу.
        // Так переход не зависит от того, докуда дорос пол пещеры вокруг.
        yield return new WorldLayer {
            Name = "нижняя пещера",
            Map = DeepMap,
            Links = new List<LinkRule> {
                new() { Kind = ElementKind.StairsDown, Layer = 1 },
            },
        };
    }

    // Легенда тестового мира: та же, что у основного, плюс рельеф.
    private static IEnumerable<LegendEntry> Legend() {
        return GameSceneBuilder.DefaultLegend.Concat(TerrainLegend)
            .Select(entry => new LegendEntry { Symbol = entry.Symbol, Kind = entry.Kind });
    }

    // Сцена как игровая, но со своим конфигом мира. Ссылки на ассеты перечитываем по пути:
    // инстанцирование шаблона переимпортирует свежесозданные ассеты, и managed-ссылки на них пустеют.
    private static void BuildScene() {
        Scene scene = File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : GameSceneBuilder.CreateScene(ScenePath);

        WorldConfig world = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        ElementsConfig elements = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);

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
        viewObject.FindProperty("_camera").objectReferenceValue = GameSceneBuilder.FindCamera();
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject inputObject = new(worldObject.GetComponent<PlayerInput>());
        inputObject.FindProperty("_world").objectReferenceValue = view;
        inputObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
