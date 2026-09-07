using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class LegendEntry {
    public string Symbol;
    public ElementKind Kind;
}

// Весь мир одной текстовой картой: символ на клетку, верхняя строка — верх мира.
// Мир бесшовный и делится на экраны ScreenWidth x ScreenHeight — камера показывает один экран.
// FillerSymbol — стены из перемешанных элементов FillerKinds, раскладываются по сиду при загрузке.
[CreateAssetMenu(fileName = "World", menuName = "Scriptable Objects/WorldConfig")]
public class WorldConfig : ScriptableObject {
    [field: SerializeField]
    public int ScreenWidth { get; private set; } = 32;

    [field: SerializeField]
    public int ScreenHeight { get; private set; } = 18;

    [field: SerializeField]
    public int Seed { get; private set; } = 1;

    [field: SerializeField]
    public string FillerSymbol { get; private set; } = "%";

    [field: SerializeField]
    public List<ElementKind> FillerKinds { get; private set; } = new() { ElementKind.Tree, ElementKind.Bush, ElementKind.Rock };

    [field: SerializeField, TextArea(8, 40)]
    public string Map { get; private set; }

    [field: SerializeField]
    public List<LegendEntry> Legend { get; private set; } = new();

    public string[] Rows {
        get {
            string[] rows = (Map ?? string.Empty).Replace("\r", "").Split('\n').Select(row => row.TrimEnd()).Where(row => row.Length > 0).ToArray();
            int width = rows.Length > 0 ? rows.Max(row => row.Length) : 0;
            return rows.Select(row => row.PadRight(width, '.')).ToArray();
        }
    }

    public ElementKind KindOf(char symbol) {
        foreach (LegendEntry entry in Legend) {
            if (!string.IsNullOrEmpty(entry.Symbol) && entry.Symbol[0] == symbol) {
                return entry.Kind;
            }
        }

        return ElementKind.None;
    }

    public ElementKind[,] BuildCells() {
        return WorldMap.Parse(Rows, KindOf, Filler, FillerKinds, Seed);
    }

    public char Filler => string.IsNullOrEmpty(FillerSymbol) ? '%' : FillerSymbol[0];

    public string SymbolOf(ElementKind kind) {
        foreach (LegendEntry entry in Legend) {
            if (entry.Kind == kind && !string.IsNullOrEmpty(entry.Symbol)) {
                return entry.Symbol;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    public void Set(string map, IEnumerable<LegendEntry> legend, int screenWidth, int screenHeight, int seed) {
        Map = map;
        Legend = legend.ToList();
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Seed = seed;
    }

    public void SetMap(string map) {
        Map = map;
    }
#endif
}
