using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Окно рисования карты мира: слева палитра элементов, справа сетка с границами экранов.
// ЛКМ — рисовать выбранным элементом, ПКМ — стирать, Alt+ЛКМ — взять элемент из клетки.
// Правки сразу пишутся в World.asset (с Undo), на диск — кнопкой «Сохранить» или при закрытии окна.
// У многоуровневого мира сверху выбирается уровень: рисуем его карту, а уровень под ним виден тенью.
public class WorldPainterWindow : EditorWindow {
    private const string WorldConfigPath = "Assets/Configs/World.asset";
    private const string ElementsConfigPath = "Assets/Configs/ElementsConfig.asset";
    private const char Empty = '.';
    private const float PaletteWidth = 232f;
    private const float PaletteCell = 48f;
    private const float PaletteLabel = 14f;
    private const int PaletteColumns = 4;
    private const float SideButton = 20f;
    private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.08f);
    private static readonly Color PaletteCellColor = new(0.13f, 0.13f, 0.16f);
    private static readonly Color CellLineColor = new(1f, 1f, 1f, 0.05f);
    private static readonly Color SharedEdgeColor = new(0.45f, 0.6f, 1f, 0.18f);
    private static readonly Color RunColor = new(1f, 0.2f, 0.2f, 0.45f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.25f);
    private static readonly Color SelectedColor = new(1f, 0.85f, 0.3f, 0.7f);
    private static readonly Color FillerTint = new(1f, 1f, 1f, 0.55f);
    private static readonly Color GhostTint = new(1f, 1f, 1f, 0.2f);
    private static readonly Color VoidColor = new(0f, 0f, 0f, 0.8f);

    // Виды рельефа предлагаем в палитре всегда: мир может быть ещё без них в легенде.
    // Символ — привычный по тестовому миру; если он занят, берётся первый свободный.
    private static readonly (ElementKind Kind, char Symbol)[] ReliefKinds = {
        (ElementKind.Lowland, '-'), (ElementKind.Highland, '+'), (ElementKind.Cliff, '/'),
        (ElementKind.Slope, 'a'), (ElementKind.CaveMouth, '('), (ElementKind.CaveFloor, ';'),
        (ElementKind.CaveWall, '|'), (ElementKind.DeepFloor, ')'),
        (ElementKind.StairsDown, 'c'), (ElementKind.StairsUp, 'x'),
    };

    // С какого края карты растим или убираем клетки.
    private enum Side { Top, Bottom, Left, Right }

    private WorldConfig _config;
    private ElementsConfig _elements;
    private char[,] _cells; // [x, row], row 0 — верхняя строка карты
    private char[,] _ghost; // уровень под текущим: рисуется тенью, чтобы попадать пещерой под плато
    private int _layer;
    private int _width;
    private int _height;
    private char _brush = '%';
    private float _zoom = 24f;
    private Vector2 _paletteScroll;
    private Vector2 _mapScroll;
    private Vector2Int _hover = new(-1, -1);
    private readonly HashSet<Vector2Int> _runs = new(); // (x, row)
    private int _screensX = 1;
    private int _screensY = 1;
    private bool _stroke; // штрих начат нажатием внутри карты: только такие протяжки рисуют
    private WorldGrid _grid; // карта, разобранная как в игре: рельеф считается по ней
    private bool _relief = true; // показывать карту так, как её увидит игрок
    private readonly List<(int X, int Row, string Name)> _tiles = new(); // тайлы рельефа в порядке рисования
    private bool[,] _seen; // в клетке есть хоть что-то видимое с текущего уровня
    private int _family; // из какого пола сложен текущий уровень: поверхность, пещера, нижняя пещера
    private bool _grown; // текущий уровень вырастает из холмов уровня выше — его пол и стены не рисуют
    private readonly Dictionary<char, ElementKind> _offered = new(); // виды из палитры, которых нет в легенде

    [MenuItem("Match Thee/World Painter")]
    public static void Open() {
        WorldPainterWindow window = GetWindow<WorldPainterWindow>("World Painter");
        window.minSize = new Vector2(900f, 520f);
    }

    private void OnEnable() {
        wantsMouseMove = true;
        LoadConfigs();
        Reload();
        Undo.undoRedoPerformed += Reload;
    }

    private void OnDisable() {
        Undo.undoRedoPerformed -= Reload;
        AssetDatabase.SaveAssets();
    }

    private void OnFocus() {
        LoadConfigs();
        Reload();
    }

    private void LoadConfigs() {
        if (_config == null) {
            _config = AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath);
        }

        if (_elements == null) {
            _elements = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);
        }
    }

    private void Reload() {
        _runs.Clear();
        if (_config == null) {
            _cells = null;
            return;
        }

        _layer = Mathf.Clamp(_layer, 0, _config.LayerCount - 1);
        string[] rows = _config.RowsOf(_layer);
        _height = rows.Length;
        _width = _height > 0 ? rows[0].Length : 0;
        _cells = new char[_width, _height];
        for (int row = 0; row < _height; row++) {
            for (int x = 0; x < _width; x++) {
                _cells[x, row] = rows[row][x];
            }
        }

        _ghost = null;
        if (_layer > 0) {
            string[] under = _config.RowsOf(_layer - 1);
            _ghost = new char[_width, _height];
            for (int row = 0; row < _height; row++) {
                for (int x = 0; x < _width; x++) {
                    _ghost[x, row] = row < under.Length && x < under[row].Length ? under[row][x] : Empty;
                }
            }
        }

        _screensX = Mathf.Max(1, (_width - 1) / Mathf.Max(1, _config.ScreenWidth - 1));
        _screensY = Mathf.Max(1, (_height - 1) / Mathf.Max(1, _config.ScreenHeight - 1));
        BuildTerrain();
        Repaint();
    }

    // Карта глазами игрока: земля под предметами, автотайл породы, борт возвышенности со свесом.
    // Три прохода — три слоя сортировки во вью: пол, высокие тайлы и борт, предметы.
    // Считаем один раз на правку: в OnGUI остаётся только вывод спрайтов.
    private void BuildTerrain() {
        _tiles.Clear();
        _seen = new bool[Mathf.Max(1, _width), Mathf.Max(1, _height)];
        if (_config == null || _cells == null) {
            return;
        }

        _grid = _config.BuildGrid();
        _family = FamilyOfLayer(_layer);
        _grown = IsGrown(_layer);
        for (int pass = 0; pass < 3; pass++) {
            for (int row = 0; row < _height; row++) {
                for (int x = 0; x < _width; x++) {
                    foreach (Vector3Int cell in VisibleCells(x, row)) {
                        _seen[x, row] = true;
                        if (_relief) {
                            CollectTile(pass, x, row, cell);
                        }
                    }
                }
            }
        }
    }

    // Клетки всех уровней, которые видны с текущего: свой уровень и переходы, ведущие на него.
    private IEnumerable<Vector3Int> VisibleCells(int x, int row) {
        int y = _height - 1 - row;
        for (int layer = 0; layer < _config.LayerCount; layer++) {
            Vector3Int cell = new(x, y, layer);
            if (_grid.IsInside(cell) && _grid.IsVisible(cell, _layer)) {
                yield return cell;
            }
        }
    }

    private void CollectTile(int pass, int x, int row, Vector3Int cell) {
        ElementKind kind = _grid.KindAt(cell);
        bool passage = ElementRules.IsPassage(kind);
        if (pass == 0) {
            ElementKind ground = _grid.GroundAt(cell);
            if (ground != ElementKind.None) {
                AddTile(x, row, TerrainTiles.NameOf(_grid, ground, cell));
            }

            if (ElementRules.IsFloor(kind) && !ElementRules.IsTall(kind) && !passage) {
                AddTile(x, row, TerrainTiles.NameOf(_grid, kind, cell) ?? SpriteOf(kind));
            }

            return;
        }

        if (pass == 1) {
            // У перехода два вида: со связанного уровня — обычный, со своего — «изнутри».
            // Без связи вид один: это просто пол, и пещерным его рисовать незачем.
            int link = passage ? _grid.LinkOf(cell) : WorldGrid.NoLink;
            if (passage && link != WorldGrid.NoLink && cell.z == _layer) {
                AddTile(x, row, $"{SpriteOf(kind)}_in");
            } else if (passage || ElementRules.IsTall(kind)) {
                AddTile(x, row, SpriteOf(kind));
            }

            AddTile(x, row, TerrainTiles.LedgeNameOf(_grid, cell));
            return;
        }

        if (kind != ElementKind.None && !ElementRules.IsFloor(kind)) {
            AddTile(x, row, SpriteOf(kind));
        }
    }

    private void AddTile(int x, int row, string name) {
        if (!string.IsNullOrEmpty(name)) {
            _tiles.Add((x, row, name));
        }
    }

    private static string SpriteOf(ElementKind kind) {
        return kind.ToString().ToLowerInvariant();
    }

    // Пол уровня решает, чем на нём рисуют: холмы — только наверху, пол пещеры — только в пещере.
    // Скала пещеры годится обоим подземным уровням, у пустого уровня ограничений нет — с него начинают.
    private const int SurfaceFamily = 1;
    private const int CaveFamily = 2;
    private const int DeepFamily = 4;

    private static int FamilyOf(ElementKind kind) {
        return kind switch {
            ElementKind.Lowland or ElementKind.Highland or ElementKind.Cliff or ElementKind.Slope => SurfaceFamily,
            ElementKind.CaveFloor => CaveFamily,
            ElementKind.DeepFloor => DeepFamily,
            ElementKind.CaveWall => CaveFamily | DeepFamily,
            _ => 0,
        };
    }

    // Считаем по большинству: случайная клетка чужого пола не должна открывать на уровне чужой рельеф.
    private int FamilyOfLayer(int layer) {
        int[] counts = new int[3];
        foreach (string line in _config.RowsOf(layer)) {
            foreach (char symbol in line) {
                ElementKind kind = _config.KindOf(symbol);
                if (!ElementRules.IsGround(kind)) {
                    continue;
                }

                int family = FamilyOf(kind);
                for (int index = 0; index < counts.Length; index++) {
                    if ((family & (1 << index)) != 0) {
                        counts[index]++;
                    }
                }
            }
        }

        int best = 0;
        for (int index = 0; index < counts.Length; index++) {
            if (counts[index] > 0 && counts[index] >= counts[best]) {
                best = index;
            }
        }

        return counts[best] == 0 ? 0 : 1 << best;
    }

    // Проход рисуют с любой стороны, но живёт он на одном уровне — на том, где на него есть правило.
    // Ниоткуда не ведёт — значит, на этом уровне ему не место.
    private int OwnerLayer(ElementKind kind, int layer) {
        if (_config.LinkOf(layer, kind) != WorldGrid.NoLink) {
            return layer;
        }

        for (int other = 0; other < _config.LayerCount; other++) {
            if (_config.LinkOf(other, kind) == layer) {
                return other;
            }
        }

        return -1;
    }

    private bool AllowedOnLayer(ElementKind kind) {
        if (ElementRules.IsPassage(kind)) {
            return !_config.IsLayered || OwnerLayer(kind, _layer) >= 0;
        }

        int family = FamilyOf(kind);
        if (family == 0) {
            return true; // не рельеф: предметы ставят на любом уровне
        }

        if (_grown) {
            return false; // пол и стены пещеры вырастают из холмов — рисовать их нечем
        }

        return _family == 0 || (family & _family) != 0;
    }

    // Пещера — внутренность горы: наверху холм, под ним пустота в камне. Такой уровень не рисуют,
    // его карту выращивают из уровня выше. Родитель — тот уровень, куда ведут переходы отсюда наверх.
    private int ParentLayer(int layer) {
        int parent = -1;
        foreach (ElementKind kind in new[] { ElementKind.CaveMouth, ElementKind.StairsUp, ElementKind.StairsDown }) {
            int target = _config.LinkOf(layer, kind);
            if (target >= 0 && target < layer && target > parent) {
                parent = target;
            }
        }

        return parent;
    }

    // Выращенный — тот, чей родитель наверху сложен из поверхности: холмы там и задают ему форму.
    // Нижняя пещера растёт из пещеры, а не из холмов, — её по-прежнему рисуют руками.
    private bool IsGrown(int layer) {
        int parent = ParentLayer(layer);
        return parent >= 0 && FamilyOfLayer(parent) == SurfaceFamily;
    }

    // Карта пещеры по карте поверхности: под холмом пустота, по её краю — камень, снаружи холма
    // клетки нет вовсе. Переходы и поставленные предметы переживают пересборку.
    private void GrowCave(int layer) {
        int parent = ParentLayer(layer);
        char floor = SymbolFor(ElementKind.CaveFloor);
        char wall = SymbolFor(ElementKind.CaveWall);
        if (parent < 0 || floor == '\0' || wall == '\0') {
            return;
        }

        string[] rows = _config.RowsOf(layer);
        int height = rows.Length;
        int width = height > 0 ? rows[0].Length : 0;
        // Гору считаем по земле, как её видит игра: под деревом на плато тот же холм, а не дырка.
        WorldGrid grid = _config.BuildGrid();
        bool[,] hill = new bool[width, height];
        for (int row = 0; row < height; row++) {
            for (int x = 0; x < width; x++) {
                Vector3Int cell = new(x, height - 1 - row, parent);
                // Дырка от прохода — тоже гора: он прорублен в её боку, и камень под ним остаётся.
                hill[x, row] = grid.Exists(cell)
                    ? ElementRules.IsRaised(grid.FloorKindAt(cell))
                    : ElementRules.IsPassage(_config.KindOf(rows[row][x]));
            }
        }

        string[] grown = new string[height];
        for (int row = 0; row < height; row++) {
            char[] line = rows[row].ToCharArray();
            for (int x = 0; x < width; x++) {
                char next = hill[x, row]
                    ? (AtHillEdge(hill, x, row, width, height) ? wall : floor)
                    : WorldConfig.VoidSymbol;
                if (SeesPassageBelow(layer, x, row)) {
                    next = WorldConfig.VoidSymbol; // сквозь эту клетку виден спуск с уровня ниже
                }

                ElementKind here = _config.KindOf(line[x]);
                bool passage = ElementRules.IsPassage(here) && next != WorldConfig.VoidSymbol;
                bool thing = next == floor && here != ElementKind.None
                    && !ElementRules.IsGround(here) && !ElementRules.IsAutotiled(here);
                if (!passage && !thing) {
                    line[x] = next;
                }
            }

            grown[row] = new string(line);
        }

        _config.SetMap(layer, string.Join("\n", grown));
        EditorUtility.SetDirty(_config);
    }

    // На этой клетке снизу стоит переход, ведущий сюда: камнем её закладывать нельзя, иначе спуск
    // окажется завален и шаг с лестницы упрётся в собственный пол.
    private bool SeesPassageBelow(int layer, int x, int row) {
        for (int other = 0; other < _config.LayerCount; other++) {
            if (other == layer) {
                continue;
            }

            ElementKind kind = _config.KindOf(CellAt(other, x, row));
            if (ElementRules.IsPassage(kind) && _config.LinkOf(other, kind) == layer) {
                return true;
            }
        }

        return false;
    }

    private static bool AtHillEdge(bool[,] hill, int x, int row, int width, int height) {
        for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
                int nx = x + dx;
                int nrow = row + dy;
                if (nx < 0 || nx >= width || nrow < 0 || nrow >= height || !hill[nx, nrow]) {
                    return true;
                }
            }
        }

        return false;
    }

    // Вид, которым растят пещеру, мог ещё не попасть в легенду мира — заводим ему символ.
    private char SymbolFor(ElementKind kind) {
        string symbol = _config.SymbolOf(kind);
        if (symbol != null) {
            return symbol[0];
        }

        foreach ((ElementKind other, char preferred) in ReliefKinds) {
            if (other == kind) {
                char free = FreeSymbol(preferred);
                if (free != '\0') {
                    _config.AddLegend(kind, free);
                    return free;
                }
            }
        }

        return '\0';
    }

    // Проход цел, когда лежит на своём уровне, а на связанном под ним дырка.
    private bool PassageIsWhole(int x, int row, ElementKind kind) {
        if (OwnerLayer(kind, _layer) != _layer) {
            return false;
        }

        int linked = _config.LinkOf(_layer, kind);
        return linked == WorldGrid.NoLink || CellAt(linked, x, row) == WorldConfig.VoidSymbol;
    }

    private char CellAt(int layer, int x, int row) {
        string[] rows = _config.RowsOf(layer);
        return row < rows.Length && x < rows[row].Length ? rows[row][x] : WorldConfig.VoidSymbol;
    }

    private void SetCell(int layer, int x, int row, char symbol) {
        if (layer == _layer) {
            _cells[x, row] = symbol;
            WriteMap();
            return;
        }

        string[] rows = _config.RowsOf(layer);
        if (row >= rows.Length || x >= rows[row].Length) {
            return;
        }

        char[] line = rows[row].ToCharArray();
        line[x] = symbol;
        rows[row] = new string(line);
        _config.SetMap(layer, string.Join("\n", rows));
        EditorUtility.SetDirty(_config);
    }

    // Клетку закрыли — значит, прохода, который был сквозь неё виден с другого уровня, больше нет.
    private void ClearPassageBehind(int x, int row) {
        for (int other = 0; other < _config.LayerCount; other++) {
            ElementKind kind = _config.KindOf(CellAt(other, x, row));
            if (other != _layer && ElementRules.IsPassage(kind) && _config.LinkOf(other, kind) == _layer) {
                SetCell(other, x, row, WorldConfig.VoidSymbol);
            }
        }
    }

    // Проход убрали — дырка под него на связанном уровне не нужна: затягиваем её соседним полом.
    private void CloseHole(int x, int row, ElementKind was) {
        int linked = _config.LinkOf(_layer, was);
        if (linked == WorldGrid.NoLink || CellAt(linked, x, row) != WorldConfig.VoidSymbol) {
            return;
        }

        char fill = '\0';
        int best = 0;
        foreach (Vector2Int step in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }) {
            int nx = x + step.x;
            int nrow = row + step.y;
            if (nx < 0 || nx >= _width || nrow < 0 || nrow >= _height) {
                continue;
            }

            char symbol = CellAt(linked, nx, nrow);
            if (!ElementRules.IsGround(_config.KindOf(symbol))) {
                continue;
            }

            int count = 0;
            foreach (Vector2Int other in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }) {
                int ox = x + other.x;
                int orow = row + other.y;
                if (ox >= 0 && ox < _width && orow >= 0 && orow < _height && CellAt(linked, ox, orow) == symbol) {
                    count++;
                }
            }

            if (count > best) {
                best = count;
                fill = symbol;
            }
        }

        if (fill != '\0') {
            SetCell(linked, x, row, fill);
        }
    }

    private void OnGUI() {
        HandleShortcuts();
        DrawToolbar();
        if (_config == null || _elements == null || _cells == null) {
            EditorGUILayout.HelpBox("Нужны Assets/Configs/World.asset и ElementsConfig.asset — собери сцену через Match Thee/Build Game Scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawPalette();
        DrawMap();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(_grown
            ? "Пещера — внутренность горы: её пол и стены вырастают из холмов на поверхности, кистью тут ставят только предметы и проходы."
            : "ЛКМ — рисовать, ПКМ — стирать, Alt+ЛКМ или колёсико — взять элемент из клетки, F — следующий уровень. Синим подсвечены клетки, общие для соседних экранов.", EditorStyles.miniLabel);
    }

    // F — следующий уровень рельефа: рисуя пещеру, всё время прыгаешь между ней и поверхностью.
    private void HandleShortcuts() {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode != KeyCode.F || _config == null || _config.LayerCount < 2) {
            return;
        }

        _layer = (_layer + (e.shift ? _config.LayerCount - 1 : 1)) % _config.LayerCount;
        Reload();
        e.Use();
    }

    private void DrawToolbar() {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        _config = (WorldConfig)EditorGUILayout.ObjectField(_config, typeof(WorldConfig), false, GUILayout.Width(150f));
        if (EditorGUI.EndChangeCheck()) {
            Reload();
        }

        if (_config != null && _config.IsLayered) {
            GUILayout.Space(8f);
            GUILayout.Label("Уровень", GUILayout.Width(52f));
            string[] names = Enumerable.Range(0, _config.LayerCount).Select(index => $"{index}: {_config.LayerName(index)}").ToArray();
            int layer = EditorGUILayout.Popup(_layer, names, EditorStyles.toolbarPopup, GUILayout.Width(130f));
            if (layer != _layer) {
                _layer = layer;
                Reload();
            }
        }

        if (_config != null) {
            GUILayout.Space(8f);
            // Экраны делят крайнюю строку и столбец, поэтому сходятся при размере 1 + n * (экран - 1).
            int spareX = (_width - 1) % Mathf.Max(1, _config.ScreenWidth - 1);
            int spareY = (_height - 1) % Mathf.Max(1, _config.ScreenHeight - 1);
            string size = $"{_width}x{_height}";
            GUILayout.Label(spareX == 0 && spareY == 0 ? size : $"{size} — экраны не сходятся (+{spareX}/+{spareY})",
                EditorStyles.miniLabel, GUILayout.Width(190f));
        }

        GUILayout.Space(8f);
        bool relief = GUILayout.Toggle(_relief, "Рельеф", EditorStyles.toolbarButton, GUILayout.Width(58f));
        if (relief != _relief) {
            _relief = relief;
            BuildTerrain();
        }

        GUILayout.Space(8f);
        GUILayout.Label("Масштаб", GUILayout.Width(56f));
        _zoom = GUILayout.HorizontalSlider(_zoom, 12f, 48f, GUILayout.Width(90f));
        GUILayout.Space(12f);
        GUILayout.Label("Экраны", GUILayout.Width(48f));
        _screensX = Mathf.Max(1, EditorGUILayout.IntField(_screensX, GUILayout.Width(28f)));
        GUILayout.Label("x", GUILayout.Width(10f));
        _screensY = Mathf.Max(1, EditorGUILayout.IntField(_screensY, GUILayout.Width(28f)));
        if (GUILayout.Button("Применить размер", EditorStyles.toolbarButton)) {
            Resize();
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Проверить ряды", EditorStyles.toolbarButton)) {
            Validate();
        }

        if (GUILayout.Button("Сохранить", EditorStyles.toolbarButton)) {
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPalette() {
        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Width(PaletteWidth));
        int column = 0;
        EditorGUILayout.BeginHorizontal();
        foreach ((char symbol, string name, Sprite sprite) entry in PaletteEntries()) {
            if (column == PaletteColumns) {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                column = 0;
            }

            Rect rect = GUILayoutUtility.GetRect(PaletteCell, PaletteCell + PaletteLabel, GUILayout.Width(PaletteCell));
            DrawPaletteEntry(rect, entry.symbol, entry.name, entry.sprite);
            column++;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    private IEnumerable<(char, string, Sprite)> PaletteEntries() {
        _offered.Clear();
        yield return (Empty, "пусто", null);
        if (_config.IsLayered) {
            yield return (WorldConfig.VoidSymbol, "нет клетки", null);
        }

        yield return (_config.Filler, "стена", _elements.GetFrame(ElementKind.Spruce, 0));
        foreach (LegendEntry entry in _config.Legend) {
            if (!string.IsNullOrEmpty(entry.Symbol) && AllowedOnLayer(entry.Kind)) {
                yield return (entry.Symbol[0], entry.Kind.ToString(), IconOf(entry.Kind));
            }
        }

        // Рельеф рисуют в любом мире: виды, которых ещё нет в легенде, стоят в палитре со свободным
        // символом, а запись в легенду появляется, когда таким видом первый раз что-то нарисуют.
        foreach ((ElementKind kind, char symbol) in ReliefKinds) {
            if (_config.SymbolOf(kind) != null || !AllowedOnLayer(kind)) {
                continue;
            }

            char free = FreeSymbol(symbol);
            if (free != '\0') {
                _offered[free] = kind;
                yield return (free, kind.ToString(), IconOf(kind));
            }
        }
    }

    // Иконка вида: у породы своего спрайта нет, есть только тайлы по соседям — берём одиночный.
    private Sprite IconOf(ElementKind kind) {
        return _elements.GetFrame(kind, 0) ?? _elements.GetFrame($"{SpriteOf(kind)}_0", 0);
    }

    // Свободный символ для нового вида: сначала привычный, потом первый незанятый печатный.
    private char FreeSymbol(char preferred) {
        char symbol = preferred;
        while (symbol <= '~') {
            bool taken = symbol == Empty || symbol == WorldConfig.VoidSymbol || symbol == _config.Filler
                || _config.KindOf(symbol) != ElementKind.None || _offered.ContainsKey(symbol);
            if (!taken) {
                return symbol;
            }

            symbol = symbol == preferred ? '!' : (char)(symbol + 1);
        }

        return '\0';
    }

    private void DrawPaletteEntry(Rect rect, char symbol, string name, Sprite sprite) {
        Rect icon = new(rect.x, rect.y, PaletteCell, PaletteCell);
        EditorGUI.DrawRect(icon, symbol == _brush ? SelectedColor : PaletteCellColor);
        if (sprite != null) {
            DrawSprite(Inset(icon, 4f), sprite, symbol == _config.Filler ? FillerTint : Color.white);
        }

        string label = name.Length > 7 ? name.Substring(0, 7) : name;
        GUI.Label(new Rect(rect.x, rect.y + PaletteCell, PaletteCell, PaletteLabel), $"{symbol} {label}", EditorStyles.miniLabel);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition)) {
            _brush = symbol;
            e.Use();
            Repaint();
        }
    }

    private void DrawMap() {
        EditorGUILayout.BeginVertical();
        DrawSideButtons(Side.Top);
        EditorGUILayout.BeginHorizontal();
        DrawSideButtons(Side.Left);
        _mapScroll = EditorGUILayout.BeginScrollView(_mapScroll);
        Rect area = GUILayoutUtility.GetRect(_width * _zoom + 2f, _height * _zoom + 2f, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        HandleMapMouse(area);

        if (Event.current.type == EventType.Repaint) {
            EditorGUI.DrawRect(area, BackgroundColor);
            DrawSharedEdges(area);
            for (int row = 0; row < _height; row++) {
                for (int x = 0; x < _width; x++) {
                    Rect cell = CellRect(area, x, row);
                    char symbol = _cells[x, row];
                    if (_ghost != null && symbol == WorldConfig.VoidSymbol
                        && _ghost[x, row] != Empty && _ghost[x, row] != WorldConfig.VoidSymbol) {
                        DrawSymbol(cell, _ghost[x, row], GhostTint);
                    }

                    // В рельефе клетка бывает занята с другого уровня — тогда она не «дырка».
                    if (_config.IsLayered && symbol == WorldConfig.VoidSymbol) {
                        if (!_relief || _seen == null || !_seen[x, row]) {
                            EditorGUI.DrawRect(cell, VoidColor);
                        }
                    } else if (symbol != Empty && !_relief) {
                        DrawSymbol(cell, symbol, Color.white);
                    }
                }
            }

            if (_relief) {
                DrawTerrain(area);
            }

            if (_zoom >= 16f) {
                DrawCellGrid(area);
            }

            foreach (Vector2Int cell in _runs) {
                EditorGUI.DrawRect(CellRect(area, cell.x, cell.y), RunColor);
            }

            if (_hover.x >= 0) {
                EditorGUI.DrawRect(CellRect(area, _hover.x, _hover.y), HoverColor);
            }
        }

        EditorGUILayout.EndScrollView();
        DrawSideButtons(Side.Right);
        EditorGUILayout.EndHorizontal();
        DrawSideButtons(Side.Bottom);
        EditorGUILayout.EndVertical();
    }

    // Кнопки по краям карты: мир растёт и убывает с той стороны, с какой нажали, —
    // нарисованное остаётся на месте относительно этого края.
    private void DrawSideButtons(Side side) {
        bool column = side is Side.Left or Side.Right;
        if (column) {
            EditorGUILayout.BeginVertical(GUILayout.Width(SideButton));
        } else {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(SideButton));
        }

        GUILayout.FlexibleSpace();
        string what = column ? "столбец" : "строку";
        if (GUILayout.Button(new GUIContent("+", $"Добавить {what} с этого края"), GUILayout.Width(SideButton), GUILayout.Height(SideButton))) {
            Grow(side, 1);
        }

        if (GUILayout.Button(new GUIContent("-", $"Убрать {what} с этого края"), GUILayout.Width(SideButton), GUILayout.Height(SideButton))) {
            Grow(side, -1);
        }

        GUILayout.FlexibleSpace();
        if (column) {
            EditorGUILayout.EndVertical();
        } else {
            EditorGUILayout.EndHorizontal();
        }
    }

    // Строка или столбец с края: у всех уровней сразу, иначе карты разъедутся по размеру.
    private void Grow(Side side, int delta) {
        bool column = side is Side.Left or Side.Right;
        int width = _width + (column ? delta : 0);
        int height = _height + (column ? 0 : delta);
        if (width < 1 || height < 1) {
            return;
        }

        Undo.RecordObject(_config, "Resize World");
        char pad = _config.IsLayered ? WorldConfig.VoidSymbol : Empty;
        // Карты читаем все разом: строки добиваются до общего размера мира, а он меняется от первой же
        // записи — если читать по ходу дела, каждый следующий уровень вырастет ещё на столбец.
        List<string[]> maps = new();
        for (int layer = 0; layer < _config.LayerCount; layer++) {
            maps.Add(_config.RowsOf(layer));
        }

        for (int layer = 0; layer < maps.Count; layer++) {
            List<string> rows = new(maps[layer]);
            if (column) {
                for (int row = 0; row < rows.Count; row++) {
                    rows[row] = delta > 0
                        ? (side == Side.Left ? pad + rows[row] : rows[row] + pad)
                        : (side == Side.Left ? rows[row].Substring(1) : rows[row].Substring(0, width));
                }
            } else if (delta > 0) {
                rows.Insert(side == Side.Top ? 0 : rows.Count, new string(pad, width));
            } else {
                rows.RemoveAt(side == Side.Top ? 0 : rows.Count - 1);
            }

            _config.SetMap(layer, string.Join("\n", rows));
        }

        EditorUtility.SetDirty(_config);
        _runs.Clear();
        Reload();
    }

    private void HandleMapMouse(Rect area) {
        Event e = Event.current;
        if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) {
            _stroke = false;
        }

        if (!area.Contains(e.mousePosition)) {
            if (_hover.x >= 0 && e.type == EventType.MouseMove) {
                _hover = new Vector2Int(-1, -1);
                Repaint();
            }

            return;
        }

        int x = Mathf.Clamp((int)((e.mousePosition.x - area.x - 1f) / _zoom), 0, _width - 1);
        int row = Mathf.Clamp((int)((e.mousePosition.y - area.y - 1f) / _zoom), 0, _height - 1);
        if (e.type == EventType.MouseMove) {
            _hover = new Vector2Int(x, row);
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDown) {
            _stroke = true;
        } else if (e.type != EventType.MouseDrag || !_stroke) {
            // Протяжка после клика по палитре или из-за пределов карты — не рисуем.
            return;
        }

        _hover = new Vector2Int(x, row);
        if (e.button == 2 || (e.button == 0 && e.alt)) {
            _brush = _cells[x, row];
        } else if (e.button == 0) {
            Paint(x, row, _brush);
        } else if (e.button == 1) {
            Paint(x, row, _config.IsLayered ? WorldConfig.VoidSymbol : Empty);
        }

        e.Use();
        Repaint();
    }

    private void Paint(int x, int row, char symbol) {
        ElementKind kind = _config.KindOf(symbol);
        if (kind == ElementKind.None && _offered.TryGetValue(symbol, out ElementKind offered)) {
            kind = offered;
        }

        // Та же кисть по той же клетке — делать нечего. Кроме одного случая: проход лежит не на своём
        // уровне или без дырки на связанном (осталось от старых карт) — тогда повторный мазок его чинит.
        if (_cells[x, row] == symbol && !(ElementRules.IsPassage(kind) && !PassageIsWhole(x, row, kind))) {
            return;
        }

        if (!AllowedOnLayer(kind)) {
            return; // чужой для этого уровня рельеф: в пещере холмами не рисуют, наверху полом пещеры
        }

        Undo.RecordObject(_config, "Paint World");
        if (_config.KindOf(symbol) == ElementKind.None && kind != ElementKind.None) {
            _config.AddLegend(kind, symbol); // видом из палитры рисуют впервые — заводим ему запись в легенде
        }

        // Герой на карте один: новый '@' стирает прежнего.
        if (kind == ElementKind.Hero) {
            for (int otherRow = 0; otherRow < _height; otherRow++) {
                for (int otherX = 0; otherX < _width; otherX++) {
                    if (_config.KindOf(_cells[otherX, otherRow]) == ElementKind.Hero) {
                        _cells[otherX, otherRow] = Empty;
                    }
                }
            }
        }

        ElementKind was = _config.KindOf(_cells[x, row]);
        if (ElementRules.IsPassage(kind)) {
            // У прохода две стороны: сам он лежит на своём уровне, а на связанном на его месте дырка.
            // Рисуют его с любой стороны — обе появляются сразу.
            int owner = OwnerLayer(kind, _layer);
            if (owner < 0) {
                owner = _layer; // правила перехода нет — значит, это просто пол, лежит где нарисовали
            }

            int linked = _config.LinkOf(owner, kind);
            SetCell(owner, x, row, symbol);
            if (linked != WorldGrid.NoLink) {
                SetCell(linked, x, row, WorldConfig.VoidSymbol);
            }
        } else {
            if (_cells[x, row] == WorldConfig.VoidSymbol) {
                ClearPassageBehind(x, row);
            }

            _cells[x, row] = symbol;
            if (ElementRules.IsPassage(was)) {
                CloseHole(x, row, was);
            }
        }

        _runs.Clear();
        WriteMap();
        for (int layer = 0; layer < _config.LayerCount; layer++) {
            if (IsGrown(layer)) {
                GrowCave(layer); // холмы изменились — пещера под ними пересобирается
            }
        }

        Reload(); // правка задевает соседний уровень, а рельеф вырастает по соседям — перечитываем всё
    }

    private void WriteMap() {
        List<string> rows = new(_height);
        for (int row = 0; row < _height; row++) {
            char[] line = new char[_width];
            for (int x = 0; x < _width; x++) {
                line[x] = _cells[x, row];
            }

            rows.Add(new string(line));
        }

        _config.SetMap(_layer, string.Join("\n", rows));
        EditorUtility.SetDirty(_config);
    }

    // Новый размер в экранах: содержимое остаётся на месте, новые клетки пустые.
    private void Resize() {
        int width = 1 + _screensX * (_config.ScreenWidth - 1);
        int height = 1 + _screensY * (_config.ScreenHeight - 1);
        if (width == _width && height == _height) {
            return;
        }

        Undo.RecordObject(_config, "Resize World");
        char pad = _config.IsLayered ? WorldConfig.VoidSymbol : Empty;
        for (int layer = 0; layer < _config.LayerCount; layer++) {
            string[] rows = _config.RowsOf(layer);
            List<string> resized = new(height);
            for (int row = 0; row < height; row++) {
                string line = row < rows.Length ? rows[row] : string.Empty;
                resized.Add(line.Length >= width ? line.Substring(0, width) : line.PadRight(width, pad));
            }

            _config.SetMap(layer, string.Join("\n", resized));
        }

        EditorUtility.SetDirty(_config);
        _runs.Clear();
        Reload();
    }

    private void Validate() {
        _runs.Clear();
        WorldGrid grid = _config.BuildGrid();
        int elsewhere = 0;
        foreach (Vector3Int cell in WorldMap.FindRuns(grid)) {
            if (cell.z == _layer) {
                _runs.Add(new Vector2Int(cell.x, _height - 1 - cell.y));
            } else {
                elsewhere++;
            }
        }

        int ambiguous = grid.FindAmbiguous().Count;
        string message = _runs.Count == 0 && elsewhere == 0
            ? "Рядов из трёх нет"
            : $"Клеток в рядах из трёх: {_runs.Count} здесь, {elsewhere} на других уровнях";
        if (ambiguous > 0) {
            message += $", неоднозначных клеток: {ambiguous}";
        }

        ShowNotification(new GUIContent(message));
        Repaint();
    }

    private void DrawTerrain(Rect area) {
        foreach ((int x, int row, string name) in _tiles) {
            Sprite sprite = _elements.GetFrame(name, 0);
            if (sprite == null) {
                continue;
            }

            // Высокий тайл занимает свою клетку и свисает на клетку ниже — рисуем во всю высоту спрайта.
            Rect cell = CellRect(area, x, row);
            cell.height = cell.width * sprite.rect.height / Mathf.Max(1f, sprite.rect.width);
            DrawSprite(cell, sprite, Color.white);
        }
    }

    private void DrawSymbol(Rect rect, char symbol, Color tint) {
        if (symbol == _config.Filler) {
            DrawSprite(rect, _elements.GetFrame(ElementKind.Spruce, 0), FillerTint * tint);
            return;
        }

        ElementKind kind = _config.KindOf(symbol);
        Sprite sprite = kind != ElementKind.None ? IconOf(kind) : null;
        if (sprite != null) {
            DrawSprite(rect, sprite, tint);
        } else if (tint.a > 0.5f) {
            GUI.Label(rect, symbol.ToString(), EditorStyles.centeredGreyMiniLabel);
        }
    }

    // Общие клетки соседних экранов: крайние столбцы и строки сетки экранов.
    private void DrawSharedEdges(Rect area) {
        int strideX = _config.ScreenWidth - 1;
        int strideY = _config.ScreenHeight - 1;
        for (int x = 0; x < _width; x += strideX) {
            EditorGUI.DrawRect(new Rect(area.x + 1f + x * _zoom, area.y + 1f, _zoom, _height * _zoom), SharedEdgeColor);
        }

        for (int row = 0; row < _height; row += strideY) {
            EditorGUI.DrawRect(new Rect(area.x + 1f, area.y + 1f + row * _zoom, _width * _zoom, _zoom), SharedEdgeColor);
        }
    }

    private void DrawCellGrid(Rect area) {
        for (int x = 0; x <= _width; x++) {
            EditorGUI.DrawRect(new Rect(area.x + 1f + x * _zoom, area.y + 1f, 1f, _height * _zoom), CellLineColor);
        }

        for (int row = 0; row <= _height; row++) {
            EditorGUI.DrawRect(new Rect(area.x + 1f, area.y + 1f + row * _zoom, _width * _zoom, 1f), CellLineColor);
        }
    }

    private Rect CellRect(Rect area, int x, int row) {
        return new Rect(area.x + 1f + x * _zoom, area.y + 1f + row * _zoom, _zoom, _zoom);
    }

    private static Rect Inset(Rect rect, float amount) {
        return new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
    }

    private static void DrawSprite(Rect rect, Sprite sprite, Color tint) {
        Texture2D texture = sprite.texture;
        Rect source = sprite.textureRect;
        Rect uv = new(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
        Color previous = GUI.color;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(rect, texture, uv);
        GUI.color = previous;
    }
}
