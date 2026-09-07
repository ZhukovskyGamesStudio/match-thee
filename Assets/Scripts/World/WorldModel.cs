using System;
using System.Collections.Generic;
using UnityEngine;

public enum SwapResult {
    Swapped, // элементы поменялись местами и сложился ряд
    NoMatch, // оба на месте: ряд не сложился
    Empty,   // вторая клетка пуста
    Invalid, // клетка вне мира, не соседняя или там не элемент (стена, герой)
}

// Состояние мира без Unity-объектов: сетка, предметы, герой, экраны, ходы, свопы с трона и сбор рядов.
public class WorldModel {
    // Своп с трона только между соседними клетками, как в классических три-в-ряд.
    public const bool ThroneSwapAdjacentOnly = true;

    public int Width { get; }
    public int Height { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public WorldEntity Hero { get; private set; }
    public Inventory Inventory { get; } = new();
    public IEnumerable<WorldEntity> Floors => _floors.Values;
    public IEnumerable<WorldEntity> Objects => _objects.Values;
    public bool IsHeroOnThrone => Hero != null && FloorAt(Hero.Position)?.Kind == ElementKind.Throne;

    // Соседние экраны делят крайний столбец и крайнюю строку: шаг сетки экранов на клетку меньше экрана.
    public int ScreenStrideX => ScreenWidth - 1;
    public int ScreenStrideY => ScreenHeight - 1;
    public int ScreensX => Math.Max(1, (Width - 1) / ScreenStrideX);
    public int ScreensY => Math.Max(1, (Height - 1) / ScreenStrideY);

    public event Action<WorldEntity> EntityMoved;
    public event Action<IReadOnlyList<WorldEntity>> EntitiesMatched;

    private readonly Dictionary<Vector2Int, WorldEntity> _floors = new();
    private readonly Dictionary<Vector2Int, WorldEntity> _objects = new();
    private readonly ElementKind[,] _cells; // слой предметов (без пола) для поиска рядов

    public WorldModel(ElementKind[,] cells, int screenWidth, int screenHeight) {
        Width = cells.GetLength(0);
        Height = cells.GetLength(1);
        ScreenWidth = Math.Max(2, screenWidth);
        ScreenHeight = Math.Max(2, screenHeight);
        _cells = new ElementKind[Width, Height];

        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                ElementKind kind = cells[x, y];
                if (kind == ElementKind.None) {
                    continue;
                }

                WorldEntity entity = new(kind, new Vector2Int(x, y));
                if (ElementRules.IsFloor(kind)) {
                    _floors[entity.Position] = entity;
                    continue;
                }

                Place(entity, entity.Position);
                if (kind == ElementKind.Hero) {
                    Hero = entity;
                }
            }
        }
    }

    public bool IsInside(Vector2Int cell) {
        return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
    }

    public WorldEntity ObjectAt(Vector2Int cell) {
        return _objects.TryGetValue(cell, out WorldEntity entity) ? entity : null;
    }

    public WorldEntity FloorAt(Vector2Int cell) {
        return _floors.TryGetValue(cell, out WorldEntity entity) ? entity : null;
    }

    public Vector2Int ScreenOrigin(Vector2Int screen) {
        return new Vector2Int(screen.x * ScreenStrideX, screen.y * ScreenStrideY);
    }

    public bool IsInScreen(Vector2Int screen, Vector2Int cell) {
        Vector2Int origin = ScreenOrigin(screen);
        return cell.x >= origin.x && cell.x < origin.x + ScreenWidth && cell.y >= origin.y && cell.y < origin.y + ScreenHeight;
    }

    // Экран для клетки. Клетка на общем крае принадлежит обоим экранам, поэтому текущий экран в приоритете.
    public Vector2Int ScreenContaining(Vector2Int cell, Vector2Int preferred) {
        if (IsInScreen(preferred, cell)) {
            return preferred;
        }

        return new Vector2Int(Math.Min(cell.x / ScreenStrideX, ScreensX - 1), Math.Min(cell.y / ScreenStrideY, ScreensY - 1));
    }

    public Vector2 ScreenCenter(Vector2Int screen) {
        Vector2Int origin = ScreenOrigin(screen);
        return new Vector2(origin.x + (ScreenWidth - 1) / 2f, origin.y + (ScreenHeight - 1) / 2f);
    }

    // Герой шагает на соседнюю клетку. Если там элемент — толкает его на клетку дальше.
    // Толкнуть можно ровно один элемент: если за ним стоит ещё что-то (элемент, стена, край мира) — хода нет.
    public bool TryMoveHero(Vector2Int direction) {
        if (Hero == null || !CanEnter(Hero.Position + direction)) {
            return false;
        }

        Vector2Int target = Hero.Position + direction;
        WorldEntity blocker = ObjectAt(target);
        if (blocker != null) {
            if (!ElementRules.IsPushable(blocker.Kind)) {
                return false;
            }

            Vector2Int beyond = target + direction;
            if (!CanEnter(beyond) || ObjectAt(beyond) != null) {
                return false;
            }

            Move(blocker, beyond);
        }

        Move(Hero, target);
        ResolveMatches();
        return true;
    }

    // Своп с трона: два элемента меняются местами, только если после этого сложится ряд.
    public SwapResult Swap(Vector2Int a, Vector2Int b) {
        if (a == b || !IsInside(a) || !IsInside(b)) {
            return SwapResult.Invalid;
        }

        if (ThroneSwapAdjacentOnly && Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) != 1) {
            return SwapResult.Invalid;
        }

        WorldEntity first = ObjectAt(a);
        WorldEntity second = ObjectAt(b);
        if (first == null || !ElementRules.IsPushable(first.Kind)) {
            return SwapResult.Invalid;
        }

        if (second == null) {
            return SwapResult.Empty;
        }

        if (!ElementRules.IsPushable(second.Kind)) {
            return SwapResult.Invalid;
        }

        Place(first, b);
        Place(second, a);
        if (WorldMap.FindRuns(_cells).Count == 0) {
            Place(first, a);
            Place(second, b);
            return SwapResult.NoMatch;
        }

        EntityMoved?.Invoke(first);
        EntityMoved?.Invoke(second);
        ResolveMatches();
        return SwapResult.Swapped;
    }

    private bool CanEnter(Vector2Int cell) {
        if (!IsInside(cell)) {
            return false;
        }

        WorldEntity occupant = ObjectAt(cell);
        return occupant == null || !ElementRules.IsSolid(occupant.Kind);
    }

    private void Move(WorldEntity entity, Vector2Int to) {
        _objects.Remove(entity.Position);
        _cells[entity.Position.x, entity.Position.y] = ElementKind.None;
        Place(entity, to);
        EntityMoved?.Invoke(entity);
    }

    private void Place(WorldEntity entity, Vector2Int cell) {
        entity.Position = cell;
        _objects[cell] = entity;
        _cells[cell.x, cell.y] = entity.Kind;
    }

    private void Remove(WorldEntity entity) {
        _objects.Remove(entity.Position);
        _cells[entity.Position.x, entity.Position.y] = ElementKind.None;
    }

    // Ряды из трёх и более одинаковых элементов исчезают. Связная группа одного вида
    // (ряд, крест, уголок) даёт один ресурс своего вида.
    private void ResolveMatches() {
        List<Vector2Int> runs = WorldMap.FindRuns(_cells);
        if (runs.Count == 0) {
            return;
        }

        HashSet<Vector2Int> runCells = new(runs);
        HashSet<Vector2Int> visited = new();
        List<WorldEntity> matched = new();
        Stack<Vector2Int> stack = new();

        foreach (Vector2Int start in runs) {
            if (!visited.Add(start)) {
                continue;
            }

            ElementKind kind = _cells[start.x, start.y];
            stack.Push(start);
            while (stack.Count > 0) {
                Vector2Int cell = stack.Pop();
                foreach (Vector2Int neighbor in Neighbors(cell)) {
                    if (runCells.Contains(neighbor) && _cells[neighbor.x, neighbor.y] == kind && visited.Add(neighbor)) {
                        stack.Push(neighbor);
                    }
                }

                WorldEntity entity = ObjectAt(cell);
                if (entity != null) {
                    Remove(entity);
                    matched.Add(entity);
                }
            }

            Inventory.Add(kind, 1);
        }

        EntitiesMatched?.Invoke(matched);
    }

    private static IEnumerable<Vector2Int> Neighbors(Vector2Int cell) {
        yield return cell + Vector2Int.right;
        yield return cell + Vector2Int.left;
        yield return cell + Vector2Int.up;
        yield return cell + Vector2Int.down;
    }
}
