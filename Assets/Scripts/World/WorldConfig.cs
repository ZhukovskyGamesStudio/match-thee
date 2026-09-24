using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class LegendEntry {
    public string Symbol;
    public ElementKind Kind;
}

// Тайл-переход на этом уровне: любая клетка такого вида ведёт на уровень Layer.
// Вход в пещеру на уровне пещеры ведёт на 0 (поверхность), спуск в нижнюю пещеру — на 2, и так далее.
[Serializable]
public class LinkRule {
    public ElementKind Kind;
    public int Layer;
}

// Уровень мира: своя текстовая карта того же размера, что и у остальных уровней.
// Символ WorldConfig.VoidSymbol — клетки на этом уровне нет (там пусто: ни пройти, ни увидеть).
[Serializable]
public class WorldLayer {
    public string Name;

    [TextArea(8, 40)]
    public string Map;

    public List<LinkRule> Links = new();
}

// Весь мир одной текстовой картой: символ на клетку, верхняя строка — верх мира.
// Мир бесшовный и делится на экраны ScreenWidth x ScreenHeight — камера показывает один экран.
// FillerSymbol — стены из перемешанных элементов FillerKinds, раскладываются по сиду при загрузке.
// Если заполнен список Layers, мир многоуровневый: карта Map не используется, каждый уровень задан своей
// картой, а переходы между уровнями — тайлами из Links. Пустой список — обычный одноуровневый мир.
[CreateAssetMenu(fileName = "World", menuName = "Scriptable Objects/WorldConfig")]
public class WorldConfig : ScriptableObject {
    public const char VoidSymbol = '_';
    public const char EmptySymbol = '.';

    [field: SerializeField]
    public int ScreenWidth { get; private set; } = 32;

    [field: SerializeField]
    public int ScreenHeight { get; private set; } = 18;

    [field: SerializeField]
    public int Seed { get; private set; } = 1;

    [field: SerializeField]
    public string FillerSymbol { get; private set; } = "%";

    [field: SerializeField]
    public List<ElementKind> FillerKinds { get; private set; } = new() {
        ElementKind.Spruce, ElementKind.BerryBush, ElementKind.DarkTree, ElementKind.Birch, ElementKind.Stump,
    };

    [field: SerializeField, TextArea(8, 40)]
    public string Map { get; private set; }

    [field: SerializeField]
    public List<WorldLayer> Layers { get; private set; } = new();

    [field: SerializeField]
    public List<LegendEntry> Legend { get; private set; } = new();

    public bool IsLayered => Layers is { Count: > 0 };

    public int LayerCount => IsLayered ? Layers.Count : 1;

    public string LayerName(int layer) {
        if (!IsLayered) {
            return "мир";
        }

        string name = layer >= 0 && layer < Layers.Count ? Layers[layer].Name : null;
        return string.IsNullOrEmpty(name) ? $"уровень {layer}" : name;
    }

    // Строки одноуровневой карты: как раньше, недостающее добивается пустыми клетками.
    public string[] Rows => RowsOf(0);

    // Строки уровня, дополненные до общего для всех уровней размера.
    // В многоуровневом мире добивка — «клетки нет», в одноуровневом — пустая проходимая клетка.
    public string[] RowsOf(int layer) {
        char pad = IsLayered ? VoidSymbol : EmptySymbol;
        string[] rows = SplitRows(MapOf(layer));
        int width = Width;
        int height = Height;
        string[] result = new string[height];
        for (int row = 0; row < height; row++) {
            result[row] = row < rows.Length ? rows[row].PadRight(width, pad) : new string(pad, width);
        }

        return result;
    }

    public int Width => AllMaps().Select(map => SplitRows(map).Select(row => row.Length).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();

    public int Height => AllMaps().Select(map => SplitRows(map).Length).DefaultIfEmpty(0).Max();

    public string MapOf(int layer) {
        if (!IsLayered) {
            return Map;
        }

        return layer >= 0 && layer < Layers.Count ? Layers[layer].Map : string.Empty;
    }

    // На какой уровень ведёт этот вид тайла, если его поставить на уровень layer (NoLink — никуда).
    public int LinkOf(int layer, ElementKind kind) {
        if (!IsLayered || layer < 0 || layer >= Layers.Count) {
            return WorldGrid.NoLink;
        }

        foreach (LinkRule rule in Layers[layer].Links) {
            if (rule.Kind == kind) {
                return rule.Layer;
            }
        }

        return WorldGrid.NoLink;
    }

    private IEnumerable<string> AllMaps() {
        if (!IsLayered) {
            yield return Map;
            yield break;
        }

        foreach (WorldLayer layer in Layers) {
            yield return layer.Map;
        }
    }

    private static string[] SplitRows(string map) {
        return (map ?? string.Empty).Replace("\r", "").Split('\n').Select(row => row.TrimEnd()).Where(row => row.Length > 0).ToArray();
    }

    public ElementKind KindOf(char symbol) {
        foreach (LegendEntry entry in Legend) {
            if (!string.IsNullOrEmpty(entry.Symbol) && entry.Symbol[0] == symbol) {
                return entry.Kind;
            }
        }

        return ElementKind.None;
    }

    public WorldGrid BuildGrid() {
        return WorldMap.Parse(this);
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

    // Новый вид на карте: рисовать им можно, только когда у вида есть символ в легенде.
    public void AddLegend(ElementKind kind, char symbol) {
        if (SymbolOf(kind) == null) {
            Legend.Add(new LegendEntry { Symbol = symbol.ToString(), Kind = kind });
        }
    }

    public void SetMap(string map) {
        SetMap(0, map);
    }

    public void SetMap(int layer, string map) {
        if (IsLayered) {
            Layers[Mathf.Clamp(layer, 0, Layers.Count - 1)].Map = map;
            return;
        }

        Map = map;
    }

    // Многоуровневый мир: карты уровней вместо одной общей карты.
    public void SetLayered(IEnumerable<WorldLayer> layers, IEnumerable<LegendEntry> legend, int screenWidth, int screenHeight, int seed) {
        Map = string.Empty;
        Layers = layers.ToList();
        Legend = legend.ToList();
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Seed = seed;
    }
#endif
}
