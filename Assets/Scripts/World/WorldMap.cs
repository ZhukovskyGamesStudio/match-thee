using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Разбор текстовых карт мира в сетку элементов по уровням. Клетки-заполнители (стены из стеновой растительности)
// раскладываются детерминированно так, чтобы одинаковые элементы не стояли ближе WallSpacing клеток по прямой:
// тогда ни один толчок или обмен с трона не сложит из стены ряд, а игрок видов стены не получает вовсе.
// Учитываются и уже расставленные автором клетки (в том числе обычные кусты и деревья).
// Соседство считается по правилам сетки: через вход в пещеру соседние клетки лежат на разных уровнях.
public static class WorldMap {
    public const int MatchLength = 3;
    public const int WallSpacing = 2;
    private const int GroundSearch = 4; // на сколько клеток вокруг искать землю под предмет

    private static readonly Vector2Int[] Directions = {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up,
    };

    public static WorldGrid Parse(WorldConfig config) {
        int width = config.Width;
        int height = config.Height;
        WorldGrid grid = new(width, height, config.LayerCount);
        List<Vector3Int> pending = new();
        char filler = config.Filler;

        for (int layer = 0; layer < config.LayerCount; layer++) {
            string[] rows = config.RowsOf(layer);
            for (int row = 0; row < height; row++) {
                int y = height - 1 - row;
                for (int x = 0; x < width; x++) {
                    char symbol = rows[row][x];
                    if (config.IsLayered && symbol == WorldConfig.VoidSymbol) {
                        continue; // клетки на этом уровне нет
                    }

                    Vector3Int cell = new(x, y, layer);
                    if (symbol == filler) {
                        grid.Set(cell, ElementKind.None, true);
                        pending.Add(cell);
                        continue;
                    }

                    ElementKind kind = config.KindOf(symbol);
                    grid.Set(cell, kind, true);
                    int link = config.LinkOf(layer, kind);
                    if (link != WorldGrid.NoLink) {
                        grid.SetLink(cell, link);
                    }
                }
            }
        }

        ResolveFillers(grid, pending, config.FillerKinds, config.Seed);
        ResolveGround(grid);
        return grid;
    }

    // Карта задаёт клетку одним символом, поэтому под нарисованным предметом, троном или скалой земли нет.
    // Подставляем ближайшую землю того же уровня: предмет на плато стоит на плато, в пещере — на её полу,
    // а у породы земля просвечивает в скруглённых углах. В мире без рельефа земли нет вовсе — там всё как раньше.
    private static void ResolveGround(WorldGrid grid) {
        List<Vector3Int> pending = new();
        foreach (Vector3Int cell in grid.Cells()) {
            ElementKind kind = grid.KindAt(cell);
            if (ElementRules.IsGround(kind) || ElementRules.IsPassage(kind) || kind == ElementKind.Slope) {
                continue; // земля, переходы и склон рисуют себя целиком
            }

            // Сначала — какой земли больше вокруг: ящик на краю плато стоит на плато, а не в низине.
            ElementKind ground = CommonGround(grid, cell);
            if (ground != ElementKind.None) {
                grid.SetGround(cell, ground);
            } else {
                pending.Add(cell);
            }
        }

        foreach (Vector3Int cell in pending) {
            ElementKind ground = NearestGround(grid, cell);
            if (ground != ElementKind.None) {
                grid.SetGround(cell, ground);
            }
        }
    }

    // Земля, которой больше всего среди четырёх соседей; поровну — берём ту, что выше.
    private static ElementKind CommonGround(WorldGrid grid, Vector3Int cell) {
        ElementKind best = ElementKind.None;
        int bestCount = 0;
        foreach (Vector2Int direction in Directions) {
            if (!grid.TryNeighbour(cell, direction, out Vector3Int neighbour)) {
                continue;
            }

            ElementKind kind = grid.KindAt(neighbour);
            if (!ElementRules.IsGround(kind)) {
                continue;
            }

            int count = 0;
            foreach (Vector2Int other in Directions) {
                if (grid.TryNeighbour(cell, other, out Vector3Int candidate) && grid.KindAt(candidate) == kind) {
                    count++;
                }
            }

            bool better = count > bestCount
                || (count == bestCount && ElementRules.ElevationOf(kind) > ElementRules.ElevationOf(best));
            if (better) {
                best = kind;
                bestCount = count;
            }
        }

        return best;
    }

    private static ElementKind NearestGround(WorldGrid grid, Vector3Int cell) {
        for (int radius = 1; radius <= GroundSearch; radius++) {
            for (int dy = -radius; dy <= radius; dy++) {
                for (int dx = -radius; dx <= radius; dx++) {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) {
                        continue;
                    }

                    Vector3Int other = new(cell.x + dx, cell.y + dy, cell.z);
                    if (grid.Exists(other) && ElementRules.IsGround(grid.KindAt(other))) {
                        return grid.KindAt(other);
                    }
                }
            }
        }

        return ElementKind.None;
    }

    public static ElementKind[,,] ToKinds(this WorldGrid grid) {
        ElementKind[,,] kinds = new ElementKind[grid.Width, grid.Height, grid.Layers];
        foreach (Vector3Int cell in grid.Cells()) {
            kinds[cell.x, cell.y, cell.z] = grid.KindAt(cell);
        }

        return kinds;
    }

    public static List<Vector3Int> FindRuns(WorldGrid grid) {
        return FindRuns(grid, grid.ToKinds());
    }

    // Клетки, входящие в ряды из MatchLength и более одинаковых толкаемых элементов — ошибка авторской карты.
    public static List<Vector3Int> FindRuns(WorldGrid grid, ElementKind[,,] cells) {
        List<Vector3Int> result = new();
        foreach (Vector3Int cell in grid.Cells()) {
            ElementKind kind = cells[cell.x, cell.y, cell.z];
            if (ElementRules.IsPushable(kind) && MakesRun(grid, cells, cell, kind)) {
                result.Add(cell);
            }
        }

        return result;
    }

    private static void ResolveFillers(WorldGrid grid, List<Vector3Int> pending, IReadOnlyList<ElementKind> fillerKinds, int seed) {
        System.Random random = new(seed);
        ElementKind[,,] cells = grid.ToKinds();
        List<ElementKind> fallback = Enum.GetValues(typeof(ElementKind)).Cast<ElementKind>()
            .Where(kind => ElementRules.IsPushable(kind) && !fillerKinds.Contains(kind)).ToList();
        List<ElementKind> candidates = new();

        foreach (Vector3Int cell in pending.OrderBy(cell => cell.z).ThenByDescending(cell => cell.y).ThenBy(cell => cell.x)) {
            candidates.Clear();
            candidates.AddRange(fillerKinds.OrderBy(_ => random.Next()));

            // Лучше всего — стеновой вид без такого же в WallSpacing клетках по прямой; хуже — стеновой без ряда;
            // совсем на крайний случай — любой толкаемый без ряда. None не случается на реальных картах.
            ElementKind chosen = candidates.FirstOrDefault(kind => !HasSameWithin(grid, cells, cell, kind, WallSpacing));
            if (chosen == ElementKind.None) {
                chosen = candidates.Concat(fallback).FirstOrDefault(kind => !MakesRun(grid, cells, cell, kind));
            }

            cells[cell.x, cell.y, cell.z] = chosen;
            grid.Set(cell, chosen, true);
        }
    }

    // Есть ли такой же элемент не дальше distance клеток по прямой в любую из четырёх сторон.
    private static bool HasSameWithin(WorldGrid grid, ElementKind[,,] cells, Vector3Int cell, ElementKind kind, int distance) {
        foreach (Vector2Int direction in Directions) {
            if (HasSame(grid, cells, cell, direction, kind, distance)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasSame(WorldGrid grid, ElementKind[,,] cells, Vector3Int cell, Vector2Int direction, ElementKind kind, int distance) {
        for (int step = 0; step < distance; step++) {
            if (!grid.TryStep(cell, direction, out cell)) {
                return false;
            }

            if (cells[cell.x, cell.y, cell.z] == kind) {
                return true;
            }
        }

        return false;
    }

    // Образует ли элемент kind в клетке ряд из MatchLength одинаковых с уже известными соседями.
    private static bool MakesRun(WorldGrid grid, ElementKind[,,] cells, Vector3Int cell, ElementKind kind) {
        return 1 + Run(grid, cells, cell, Vector2Int.left, kind) + Run(grid, cells, cell, Vector2Int.right, kind) >= MatchLength
            || 1 + Run(grid, cells, cell, Vector2Int.down, kind) + Run(grid, cells, cell, Vector2Int.up, kind) >= MatchLength;
    }

    private static int Run(WorldGrid grid, ElementKind[,,] cells, Vector3Int cell, Vector2Int direction, ElementKind kind) {
        int count = 0;
        while (grid.TryStep(cell, direction, out cell) && cells[cell.x, cell.y, cell.z] == kind) {
            count++;
        }

        return count;
    }
}
