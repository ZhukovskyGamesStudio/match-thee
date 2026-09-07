using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Окно рисования карты мира: слева палитра элементов, справа сетка с границами экранов.
// ЛКМ — рисовать выбранным элементом, ПКМ — стирать, Alt+ЛКМ — взять элемент из клетки.
// Правки сразу пишутся в World.asset (с Undo), на диск — кнопкой «Сохранить» или при закрытии окна.
public class WorldPainterWindow : EditorWindow {
    private const string WorldConfigPath = "Assets/Configs/World.asset";
    private const string ElementsConfigPath = "Assets/Configs/ElementsConfig.asset";
    private const char Empty = '.';
    private const float PaletteWidth = 232f;
    private const float PaletteCell = 48f;
    private const float PaletteLabel = 14f;
    private const int PaletteColumns = 4;
    private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.08f);
    private static readonly Color PaletteCellColor = new(0.13f, 0.13f, 0.16f);
    private static readonly Color CellLineColor = new(1f, 1f, 1f, 0.05f);
    private static readonly Color SharedEdgeColor = new(0.45f, 0.6f, 1f, 0.18f);
    private static readonly Color RunColor = new(1f, 0.2f, 0.2f, 0.45f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.25f);
    private static readonly Color SelectedColor = new(1f, 0.85f, 0.3f, 0.7f);
    private static readonly Color FillerTint = new(1f, 1f, 1f, 0.55f);

    private WorldConfig _config;
    private ElementsConfig _elements;
    private char[,] _cells; // [x, row], row 0 — верхняя строка карты
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

        string[] rows = _config.Rows;
        _height = rows.Length;
        _width = _height > 0 ? rows[0].Length : 0;
        _cells = new char[_width, _height];
        for (int row = 0; row < _height; row++) {
            for (int x = 0; x < _width; x++) {
                _cells[x, row] = rows[row][x];
            }
        }

        _screensX = Mathf.Max(1, (_width - 1) / Mathf.Max(1, _config.ScreenWidth - 1));
        _screensY = Mathf.Max(1, (_height - 1) / Mathf.Max(1, _config.ScreenHeight - 1));
        Repaint();
    }

    private void OnGUI() {
        DrawToolbar();
        if (_config == null || _elements == null || _cells == null) {
            EditorGUILayout.HelpBox("Нужны Assets/Configs/World.asset и ElementsConfig.asset — собери сцену через Match Thee/Build Game Scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawPalette();
        DrawMap();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("ЛКМ — рисовать, ПКМ — стирать, Alt+ЛКМ — взять элемент из клетки. Синим подсвечены клетки, общие для соседних экранов.", EditorStyles.miniLabel);
    }

    private void DrawToolbar() {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        _config = (WorldConfig)EditorGUILayout.ObjectField(_config, typeof(WorldConfig), false, GUILayout.Width(150f));
        if (EditorGUI.EndChangeCheck()) {
            Reload();
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
        yield return (Empty, "пусто", null);
        yield return (_config.Filler, "стена", _elements.GetFrame(ElementKind.Spruce, 0));
        foreach (LegendEntry entry in _config.Legend) {
            if (!string.IsNullOrEmpty(entry.Symbol)) {
                yield return (entry.Symbol[0], entry.Kind.ToString(), _elements.GetFrame(entry.Kind, 0));
            }
        }
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
        _mapScroll = EditorGUILayout.BeginScrollView(_mapScroll);
        Rect area = GUILayoutUtility.GetRect(_width * _zoom + 2f, _height * _zoom + 2f, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        HandleMapMouse(area);

        if (Event.current.type == EventType.Repaint) {
            EditorGUI.DrawRect(area, BackgroundColor);
            DrawSharedEdges(area);
            for (int row = 0; row < _height; row++) {
                for (int x = 0; x < _width; x++) {
                    if (_cells[x, row] != Empty) {
                        DrawSymbol(CellRect(area, x, row), _cells[x, row]);
                    }
                }
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
    }

    private void HandleMapMouse(Rect area) {
        Event e = Event.current;
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

        if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) {
            return;
        }

        _hover = new Vector2Int(x, row);
        if (e.button == 0 && e.alt) {
            _brush = _cells[x, row];
        } else if (e.button == 0) {
            Paint(x, row, _brush);
        } else if (e.button == 1) {
            Paint(x, row, Empty);
        }

        e.Use();
        Repaint();
    }

    private void Paint(int x, int row, char symbol) {
        if (_cells[x, row] == symbol) {
            return;
        }

        Undo.RecordObject(_config, "Paint World");
        // Герой на карте один: новый '@' стирает прежнего.
        if (_config.KindOf(symbol) == ElementKind.Hero) {
            for (int otherRow = 0; otherRow < _height; otherRow++) {
                for (int otherX = 0; otherX < _width; otherX++) {
                    if (_config.KindOf(_cells[otherX, otherRow]) == ElementKind.Hero) {
                        _cells[otherX, otherRow] = Empty;
                    }
                }
            }
        }

        _cells[x, row] = symbol;
        _runs.Clear();
        WriteMap();
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

        _config.SetMap(string.Join("\n", rows));
        EditorUtility.SetDirty(_config);
    }

    // Новый размер в экранах: содержимое остаётся на месте, новые клетки пустые.
    private void Resize() {
        int width = 1 + _screensX * (_config.ScreenWidth - 1);
        int height = 1 + _screensY * (_config.ScreenHeight - 1);
        if (width == _width && height == _height) {
            return;
        }

        char[,] cells = new char[width, height];
        for (int row = 0; row < height; row++) {
            for (int x = 0; x < width; x++) {
                cells[x, row] = x < _width && row < _height ? _cells[x, row] : Empty;
            }
        }

        Undo.RecordObject(_config, "Resize World");
        _cells = cells;
        _width = width;
        _height = height;
        _runs.Clear();
        WriteMap();
    }

    private void Validate() {
        _runs.Clear();
        foreach (Vector2Int cell in WorldMap.FindRuns(_config.BuildCells())) {
            _runs.Add(new Vector2Int(cell.x, _height - 1 - cell.y));
        }

        ShowNotification(new GUIContent(_runs.Count == 0 ? "Рядов из трёх нет" : $"Клеток в рядах из трёх: {_runs.Count}"));
        Repaint();
    }

    private void DrawSymbol(Rect rect, char symbol) {
        if (symbol == _config.Filler) {
            DrawSprite(rect, _elements.GetFrame(ElementKind.Spruce, 0), FillerTint);
            return;
        }

        ElementKind kind = _config.KindOf(symbol);
        Sprite sprite = kind != ElementKind.None ? _elements.GetFrame(kind, 0) : null;
        if (sprite != null) {
            DrawSprite(rect, sprite, Color.white);
        } else {
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
