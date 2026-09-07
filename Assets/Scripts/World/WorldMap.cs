using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Разбор текстовой карты мира в сетку элементов. Клетки-заполнители (стены из стеновой растительности)
// раскладываются детерминированно так, чтобы одинаковые элементы не стояли ближе WallSpacing клеток по прямой:
// тогда ни один толчок или обмен с трона не сложит из стены ряд, а игрок видов стены не получает вовсе.
// Учитываются и уже расставленные автором клетки (в том числе обычные кусты и деревья).
public static class WorldMap {
    public const int MatchLength = 3;
    public const int WallSpacing = 2;

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

                // Лучше всего — стеновой вид без такого же в WallSpacing клетках по прямой; хуже — стеновой без ряда;
                // совсем на крайний случай — любой толкаемый без ряда. None не случается на реальных картах.
                ElementKind chosen = candidates.FirstOrDefault(kind => !HasSameWithin(cells, x, y, kind, WallSpacing));
                if (chosen == ElementKind.None) {
                    chosen = candidates.Concat(fallback).FirstOrDefault(kind => !MakesRun(cells, x, y, kind));
                }

                cells[x, y] = chosen;
                pending[x, y] = false;
            }
        }
    }

    // Есть ли такой же элемент не дальше distance клеток по прямой в любую из четырёх сторон.
    private static bool HasSameWithin(ElementKind[,] cells, int x, int y, ElementKind kind, int distance) {
        return Run(cells, x, y, -1, 0, kind, distance) || Run(cells, x, y, 1, 0, kind, distance)
            || Run(cells, x, y, 0, -1, kind, distance) || Run(cells, x, y, 0, 1, kind, distance);
    }

    private static bool Run(ElementKind[,] cells, int x, int y, int dx, int dy, ElementKind kind, int distance) {
        for (int step = 1; step <= distance; step++) {
            int cx = x + dx * step;
            int cy = y + dy * step;
            if (cx < 0 || cy < 0 || cx >= cells.GetLength(0) || cy >= cells.GetLength(1)) {
                return false;
            }

            if (cells[cx, cy] == kind) {
                return true;
            }
        }

        return false;
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
