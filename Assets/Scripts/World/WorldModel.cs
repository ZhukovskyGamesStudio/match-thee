using System;
using System.Collections.Generic;
using System.Linq;
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

    // Что даёт группа по длине самого длинного прямого ряда в ней: три — просто исчезает,
    // четыре — один расходуемый ресурс, пять — вид открыт для превращения навсегда.
    public const int ResourceRunLength = 4;
    public const int UnlockRunLength = 5;

    public int Width { get; }
    public int Height { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public WorldEntity Hero { get; private set; }
    // Превращение: во что сейчас превращён герой (None — сам собой). В сетке рядов его клетка считается этим видом.
    public ElementKind HeroForm { get; private set; } = ElementKind.None;
    public bool IsHeroTransformed => HeroForm != ElementKind.None;
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
    public event Action<ElementKind> HeroFormChanged;
    public event Action GameWon; // герой соединился с персонажами и исчез
    public event Action Restored; // мир откатился к снимку комнаты: вью пересобираются заново

    // Снимок комнаты: слой предметов, форма героя и рюкзак на момент входа. R возвращает к нему.
    private class Snapshot {
        public ElementKind[,] Objects;
        public ElementKind HeroForm;
        public Dictionary<ElementKind, int> Counts;
        public HashSet<ElementKind> Unlocked;
    }

    private Snapshot _snapshot;

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

                if (kind == ElementKind.Hero) {
                    Hero = entity; // до Place: в сетке рядов клетка героя считается персонажем
                }

                Place(entity, entity.Position);
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

    // Превращение: герой принимает вид предмета из рюкзака. Если он сразу оказался в ряду — ряд исчезает.
    public bool TryTransform(ElementKind kind) {
        if (!Features.Transformation || Hero == null || !ElementRules.IsPushable(kind) || kind == HeroForm) {
            return false;
        }

        bool unlocked = Inventory.IsUnlocked(kind);
        if (!unlocked && Inventory.Count(kind) <= 0) {
            return false;
        }

        if (!unlocked && Features.TransformationCostsResource && !Inventory.TryTake(kind, 1)) {
            return false;
        }

        SetHeroForm(kind);
        ResolveMatches();
        return true;
    }

    private void SetHeroForm(ElementKind kind) {
        HeroForm = kind;
        _cells[Hero.Position.x, Hero.Position.y] = CellKind(Hero);
        HeroFormChanged?.Invoke(kind);
    }

    // Герой в сетке рядов: его форма превращения, а без неё — персонаж (ряд с персонажами завершает игру).
    private ElementKind CellKind(WorldEntity entity) {
        if (entity != Hero) {
            return entity.Kind;
        }

        return IsHeroTransformed ? HeroForm : ElementKind.Person;
    }

    public void SaveSnapshot() {
        ElementKind[,] objects = new ElementKind[Width, Height];
        foreach (WorldEntity entity in _objects.Values) {
            objects[entity.Position.x, entity.Position.y] = entity.Kind;
        }

        _snapshot = new Snapshot {
            Objects = objects,
            HeroForm = HeroForm,
            Counts = Inventory.Counts.ToDictionary(pair => pair.Key, pair => pair.Value),
            Unlocked = new HashSet<ElementKind>(Inventory.UnlockedKinds),
        };
    }

    // Откат к снимку: предметы пересоздаются заново (старые сущности больше не действительны).
    public bool Restore() {
        if (_snapshot == null) {
            return false;
        }

        _objects.Clear();
        Array.Clear(_cells, 0, _cells.Length);
        Hero = null;
        HeroForm = ElementKind.None;
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                ElementKind kind = _snapshot.Objects[x, y];
                if (kind == ElementKind.None) {
                    continue;
                }

                WorldEntity entity = new(kind, new Vector2Int(x, y));
                if (kind == ElementKind.Hero) {
                    Hero = entity;
                }

                Place(entity, entity.Position);
            }
        }

        HeroForm = _snapshot.HeroForm;
        if (Hero != null) {
            _cells[Hero.Position.x, Hero.Position.y] = CellKind(Hero);
        }

        Inventory.Restore(_snapshot.Counts, _snapshot.Unlocked);
        Restored?.Invoke();
        return true;
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
        _cells[cell.x, cell.y] = CellKind(entity);
    }

    private void Remove(WorldEntity entity) {
        _objects.Remove(entity.Position);
        _cells[entity.Position.x, entity.Position.y] = ElementKind.None;
    }

    // Ряды из трёх и более одинаковых элементов исчезают. Связная группа одного вида
    // (ряд, крест, уголок) награждает по длине самого длинного прямого ряда в ней (см. константы выше).
    // Группа с превращённым героем награды не даёт: остальные элементы исчезают, герой становится собой.
    // Группа персонажей с героем — конец игры: герой исчезает вместе с ними.
    private void ResolveMatches() {
        List<Vector2Int> runs = WorldMap.FindRuns(_cells);
        if (runs.Count == 0) {
            return;
        }

        HashSet<Vector2Int> runCells = new(runs);
        HashSet<Vector2Int> visited = new();
        List<WorldEntity> matched = new();
        Stack<Vector2Int> stack = new();
        HashSet<Vector2Int> group = new();
        bool won = false;

        foreach (Vector2Int start in runs) {
            if (!visited.Add(start)) {
                continue;
            }

            ElementKind kind = _cells[start.x, start.y];
            bool withHero = false;
            group.Clear();
            stack.Push(start);
            while (stack.Count > 0) {
                Vector2Int cell = stack.Pop();
                group.Add(cell);
                foreach (Vector2Int neighbor in Neighbors(cell)) {
                    if (runCells.Contains(neighbor) && _cells[neighbor.x, neighbor.y] == kind && visited.Add(neighbor)) {
                        stack.Push(neighbor);
                    }
                }

                WorldEntity entity = ObjectAt(cell);
                if (entity == Hero) {
                    withHero = true;
                } else if (entity != null) {
                    Remove(entity);
                    matched.Add(entity);
                }
            }

            if (!withHero) {
                int longest = LongestRun(group);
                if (longest >= UnlockRunLength) {
                    Inventory.Unlock(kind);
                } else if (longest >= ResourceRunLength) {
                    Inventory.Add(kind, 1);
                }
            } else if (kind == ElementKind.Person) {
                Remove(Hero);
                matched.Add(Hero);
                Hero = null;
                HeroForm = ElementKind.None;
                won = true;
            } else {
                SetHeroForm(ElementKind.None);
            }
        }

        EntitiesMatched?.Invoke(matched);
        if (won) {
            GameWon?.Invoke();
        }
    }

    // Самый длинный прямой ряд (горизонтальный или вертикальный) внутри группы клеток.
    private static int LongestRun(HashSet<Vector2Int> group) {
        int longest = 0;
        foreach (Vector2Int cell in group) {
            if (!group.Contains(cell + Vector2Int.left)) {
                longest = Math.Max(longest, RunLength(group, cell, Vector2Int.right));
            }

            if (!group.Contains(cell + Vector2Int.down)) {
                longest = Math.Max(longest, RunLength(group, cell, Vector2Int.up));
            }
        }

        return longest;
    }

    private static int RunLength(HashSet<Vector2Int> group, Vector2Int start, Vector2Int step) {
        int length = 0;
        for (Vector2Int cell = start; group.Contains(cell); cell += step) {
            length++;
        }

        return length;
    }

    private static IEnumerable<Vector2Int> Neighbors(Vector2Int cell) {
        yield return cell + Vector2Int.right;
        yield return cell + Vector2Int.left;
        yield return cell + Vector2Int.up;
        yield return cell + Vector2Int.down;
    }
}
