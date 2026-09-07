using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Разбор текстовой карты мира в сетку элементов. Клетки-заполнители (стены из перемешанных деревьев,
// кустов и камней) раскладываются детерминированно так, чтобы три одинаковых элемента не стояли подряд
// ни по горизонтали, ни по вертикали: иначе они бы сразу соединились и исчезли.
public static class WorldMap {
    public const int MatchLength = 3;

    public static ElementKind[,] Parse(string[] rows, Func<char, ElementKind> kindOf, char fillerSymbol, IReadOnlyList<ElementKind> fillerKinds, int seed) {
        int height = rows.Length;
        int width = height > 0 ? rows.Max(row => row.Length) : 0;
        ElementKind[,] cells = new ElementKind[width, height];
        bool[,] pending = new bool[width, height];

        for (int row = 0; row < height; row++) {
            int y = height - 1 - row;
            for (int x = 0; x < rows[row].Length; x++) {
                char symbol = rows[row][x];
                if (symbol == fillerSymbol) {
                    pending[x, y] = true;
                } else {
                    cells[x, y] = kindOf(symbol);
                }
            }
        }

        ResolveFillers(cells, pending, fillerKinds, seed);
        return cells;
    }

    // Клетки, входящие в ряды из MatchLength и более одинаковых толкаемых элементов — ошибка авторской карты.
    public static List<Vector2Int> FindRuns(ElementKind[,] cells) {
        List<Vector2Int> result = new();
        for (int y = 0; y < cells.GetLength(1); y++) {
            for (int x = 0; x < cells.GetLength(0); x++) {
                ElementKind kind = cells[x, y];
                if (ElementRules.IsPushable(kind) && MakesRun(cells, x, y, kind)) {
                    result.Add(new Vector2Int(x, y));
                }
            }
        }

        return result;
    }

    private static void ResolveFillers(ElementKind[,] cells, bool[,] pending, IReadOnlyList<ElementKind> fillerKinds, int seed) {
        System.Random random = new(seed);
        List<ElementKind> fallback = Enum.GetValues(typeof(ElementKind)).Cast<ElementKind>()
            .Where(kind => ElementRules.IsPushable(kind) && !fillerKinds.Contains(kind)).ToList();
        List<ElementKind> candidates = new();

        for (int y = cells.GetLength(1) - 1; y >= 0; y--) {
            for (int x = 0; x < cells.GetLength(0); x++) {
                if (!pending[x, y]) {
                    continue;
                }

                candidates.Clear();
                candidates.AddRange(fillerKinds.OrderBy(_ => random.Next()));
                candidates.AddRange(fallback);

                // None остаётся только если не подошёл ни один толкаемый элемент; на реальных картах не случается.
                cells[x, y] = candidates.FirstOrDefault(kind => !MakesRun(cells, x, y, kind));
                pending[x, y] = false;
            }
        }
    }

    // Образует ли элемент kind в клетке (x, y) ряд из MatchLength одинаковых с уже известными соседями.
    private static bool MakesRun(ElementKind[,] cells, int x, int y, ElementKind kind) {
        return 1 + Run(cells, x, y, -1, 0, kind) + Run(cells, x, y, 1, 0, kind) >= MatchLength
            || 1 + Run(cells, x, y, 0, -1, kind) + Run(cells, x, y, 0, 1, kind) >= MatchLength;
    }

    private static int Run(ElementKind[,] cells, int x, int y, int dx, int dy, ElementKind kind) {
        int count = 0;
        x += dx;
        y += dy;
        while (x >= 0 && y >= 0 && x < cells.GetLength(0) && y < cells.GetLength(1) && cells[x, y] == kind) {
            count++;
            x += dx;
            y += dy;
        }

        return count;
    }
}
