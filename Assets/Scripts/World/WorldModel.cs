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

// Состояние мира без Unity-объектов: сетка по уровням, предметы, герой, комнаты, ходы, свопы с трона и сбор рядов.
// Клетка — это (x, y, уровень): мир многоуровневый, и под плато может лежать пещера. Соседство и шаги
// считает WorldGrid: через вход в пещеру шаг уводит на другой уровень, вместе с толкаемым элементом.
public class WorldModel {
    // Своп с трона только между соседними клетками, как в классических три-в-ряд.
    public const bool ThroneSwapAdjacentOnly = true;

    // Что даёт группа по длине самого длинного прямого ряда в ней: три — просто исчезает,
    // четыре — один ресурс только в этой комнате, пять — один ресурс во всех комнатах.
    public const int LocalRunLength = 4;
    public const int GlobalRunLength = 5;

    public WorldGrid Grid { get; }
    public int Width => Grid.Width;
    public int Height => Grid.Height;
    public int LayerCount => Grid.Layers;
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public WorldEntity Hero { get; private set; }
    // Превращение: во что сейчас превращён герой (None — сам собой). В сетке рядов его клетка считается этим видом.
    public ElementKind HeroForm { get; private set; } = ElementKind.None;
    public bool IsHeroTransformed => HeroForm != ElementKind.None;
    public Vector2Int CurrentScreen { get; private set; }
    // Уровень, на котором стоит герой: видно только его — и переходы, ведущие на него.
    public int CurrentLayer { get; private set; }
    // Комната — экран на своём уровне: пещера под плато и само плато считаются разными комнатами.
    public Vector3Int CurrentRoom => new(CurrentScreen.x, CurrentScreen.y, CurrentLayer);
    public Inventory Inventory { get; } = new();
    public IEnumerable<WorldEntity> Grounds => _grounds.Values;
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
    public event Action Restored; // комната перезапущена: вью предметов пересобираются заново

    // Первый вход в комнату: где герой в неё вошёл, в какой форме и что местного лежало в рюкзаке.
    // Рестарт возвращает комнату в вид из карты и героя на эту клетку.
    private class RoomEntry {
        public Vector3Int HeroCell;
        public ElementKind HeroForm;
        public Dictionary<ElementKind, int> Local;
    }

    private readonly Dictionary<Vector3Int, RoomEntry> _roomEntries = new();

    private readonly Dictionary<Vector3Int, WorldEntity> _grounds = new(); // земля уровня под всем остальным
    private readonly Dictionary<Vector3Int, WorldEntity> _floors = new();
    private readonly Dictionary<Vector3Int, WorldEntity> _objects = new();
    private readonly ElementKind[,,] _cells; // слой предметов (без пола) для поиска рядов

    public WorldModel(WorldGrid grid, int screenWidth, int screenHeight) {
        Grid = grid;
        ScreenWidth = Math.Max(2, screenWidth);
        ScreenHeight = Math.Max(2, screenHeight);
        _cells = new ElementKind[Width, Height, LayerCount];

        foreach (Vector3Int cell in Grid.Cells()) {
            // Под предметом и полом-декором лежит земля уровня: они не висят над пустотой
            // и не оставляют дыру, когда уходят.
            ElementKind ground = Grid.GroundAt(cell);
            if (ground != ElementKind.None) {
                _grounds[cell] = new WorldEntity(ground, cell);
            }

            ElementKind kind = Grid.KindAt(cell);
            if (kind == ElementKind.None) {
                continue;
            }

            WorldEntity entity = new(kind, cell);
            if (ElementRules.IsFloor(kind)) {
                _floors[cell] = entity;
                continue;
            }

            if (kind == ElementKind.Hero) {
                Hero = entity; // до Place: в сетке рядов клетка героя считается персонажем
            }

            Place(entity, cell);
        }
    }

    public bool IsInside(Vector3Int cell) {
        return Grid.Exists(cell);
    }

    public WorldEntity ObjectAt(Vector3Int cell) {
        return _objects.TryGetValue(cell, out WorldEntity entity) ? entity : null;
    }

    public WorldEntity FloorAt(Vector3Int cell) {
        return _floors.TryGetValue(cell, out WorldEntity entity) ? entity : null;
    }

    // Видна ли клетка с уровня, где стоит герой: чужие уровни скрыты, переходы на наш — видны.
    public bool IsVisible(Vector3Int cell) {
        return Grid.IsVisible(cell, CurrentLayer);
    }

    // Клетка столбца (x, y), которую игрок сейчас видит: нужна, чтобы попадать мышью с трона.
    public bool TryVisibleCell(int x, int y, out Vector3Int cell) {
        cell = new Vector3Int(x, y, CurrentLayer);
        if (Grid.Exists(cell)) {
            return true;
        }

        for (int layer = 0; layer < LayerCount; layer++) {
            Vector3Int candidate = new(x, y, layer);
            if (Grid.IsVisible(candidate, CurrentLayer)) {
                cell = candidate;
                return true;
            }
        }

        return false;
    }

    public Vector2Int ScreenOrigin(Vector2Int screen) {
        return new Vector2Int(screen.x * ScreenStrideX, screen.y * ScreenStrideY);
    }

    public bool IsInScreen(Vector2Int screen, Vector3Int cell) {
        Vector2Int origin = ScreenOrigin(screen);
        return cell.x >= origin.x && cell.x < origin.x + ScreenWidth && cell.y >= origin.y && cell.y < origin.y + ScreenHeight;
    }

    // Экран для клетки. Клетка на общем крае принадлежит обоим экранам, поэтому текущий экран в приоритете.
    public Vector2Int ScreenContaining(Vector3Int cell, Vector2Int preferred) {
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
    // Шаг через вход в пещеру или лестницу уводит на другой уровень — и героя, и то, что он толкает.
    public bool TryMoveHero(Vector2Int direction) {
        if (Hero == null || !Grid.TryStep(Hero.Position, direction, out Vector3Int target)) {
            return false;
        }

        WorldEntity blocker = ObjectAt(target);
        if (blocker != null) {
            if (!ElementRules.IsPushable(blocker.Kind)) {
                return false;
            }

            if (!Grid.TryStep(target, direction, out Vector3Int beyond) || ObjectAt(beyond) != null) {
                return false;
            }

            Move(blocker, beyond);
        }

        Move(Hero, target);
        ResolveMatches();
        return true;
    }

    // Своп с трона: два соседних элемента меняются местами, только если после этого сложится ряд.
    // Вторая клетка ищется по правилам сетки, поэтому менять можно и через вход в пещеру.
    public SwapResult Swap(Vector3Int from, Vector2Int direction, out Vector3Int to) {
        to = from;
        if (!IsInside(from) || !Grid.TryStep(from, direction, out to)) {
            return SwapResult.Invalid;
        }

        WorldEntity first = ObjectAt(from);
        WorldEntity second = ObjectAt(to);
        if (first == null || !ElementRules.IsPushable(first.Kind)) {
            return SwapResult.Invalid;
        }

        if (second == null) {
            return SwapResult.Empty;
        }

        if (!ElementRules.IsPushable(second.Kind)) {
            return SwapResult.Invalid;
        }

        Place(first, to);
        Place(second, from);
        if (WorldMap.FindRuns(Grid, _cells).Count == 0) {
            Place(first, from);
            Place(second, to);
            return SwapResult.NoMatch;
        }

        EntityMoved?.Invoke(first);
        EntityMoved?.Invoke(second);
        ResolveMatches();
        return SwapResult.Swapped;
    }

    // Превращение: герой принимает вид предмета из рюкзака. Предмет остаётся в рюкзаке (он «надет»),
    // тратится только если герой в этой форме исчез в ряду. Если герой сразу оказался в ряду — ряд исчезает.
    public bool TryTransform(ElementKind kind) {
        if (!Features.Transformation || Hero == null || !ElementRules.IsPushable(kind) || kind == HeroForm) {
            return false;
        }

        if (Inventory.Count(kind) <= 0) {
            return false;
        }

        SetHeroForm(kind);
        ResolveMatches();
        return true;
    }

    // Обратно в себя: предмет возвращается в рюкзак.
    public bool TransformBack() {
        if (Hero == null || !IsHeroTransformed) {
            return false;
        }

        SetHeroForm(ElementKind.None);
        ResolveMatches();
        return true;
    }

    // Q: по кругу — предметы рюкзака по порядку, затем снова сам герой.
    public bool TransformNext() {
        if (!Features.Transformation || Hero == null) {
            return false;
        }

        List<ElementKind> kinds = Inventory.Kinds.ToList();
        if (kinds.Count == 0) {
            return false;
        }

        int index = IsHeroTransformed ? kinds.IndexOf(HeroForm) : -1;
        return index + 1 < kinds.Count ? TryTransform(kinds[index + 1]) : TransformBack();
    }

    private void SetHeroForm(ElementKind kind) {
        HeroForm = kind;
        _cells[Hero.Position.x, Hero.Position.y, Hero.Position.z] = CellKind(Hero);
        HeroFormChanged?.Invoke(kind);
    }

    // Герой в сетке рядов: его форма превращения, а без неё — персонаж (ряд с персонажами завершает игру).
    private ElementKind CellKind(WorldEntity entity) {
        if (entity != Hero) {
            return entity.Kind;
        }

        return IsHeroTransformed ? HeroForm : ElementKind.Person;
    }

    // Комната, в которой стоит герой: экран и уровень. От неё зависят доступные местные ресурсы.
    // Если предмета формы героя в новой комнате нет (местный ресурс не переносится) — герой становится собой.
    public void SetRoom(Vector2Int screen, int layer) {
        CurrentScreen = screen;
        CurrentLayer = layer;
        Inventory.Room = CurrentRoom;
        if (Hero != null && IsHeroTransformed && Inventory.Count(HeroForm) <= 0) {
            SetHeroForm(ElementKind.None);
            ResolveMatches();
        }
    }

    // Запомнить первый вход в текущую комнату (повторные входы ничего не меняют).
    public void RecordRoomEntry() {
        if (Hero == null || _roomEntries.ContainsKey(CurrentRoom)) {
            return;
        }

        _roomEntries[CurrentRoom] = new RoomEntry {
            HeroCell = Hero.Position,
            HeroForm = HeroForm,
            Local = Inventory.SaveLocal(CurrentRoom),
        };
    }

    // Рестарт комнаты: предметы в её прямоугольнике на её уровне — как на карте, герой на клетке первого входа,
    // общие ресурсы, добытые здесь, отняты, потраченные здесь — возвращены, местные — как при первом входе.
    // Предметы пересоздаются заново (старые сущности комнаты больше не действительны).
    public bool RestartRoom() {
        if (!_roomEntries.TryGetValue(CurrentRoom, out RoomEntry entry)) {
            return false;
        }

        // Героя снимаем с сетки первым: иначе Remove(Hero) стёр бы восстановленный предмет на его клетке.
        if (Hero != null) {
            Remove(Hero);
        } else {
            Hero = new WorldEntity(ElementKind.Hero, entry.HeroCell);
        }

        foreach (WorldEntity entity in _objects.Values.ToList()) {
            if (entity.Position.z == CurrentLayer && IsInScreen(CurrentScreen, entity.Position)) {
                Remove(entity);
            }
        }

        Vector2Int origin = ScreenOrigin(CurrentScreen);
        for (int y = origin.y; y < origin.y + ScreenHeight; y++) {
            for (int x = origin.x; x < origin.x + ScreenWidth; x++) {
                Vector3Int cell = new(x, y, CurrentLayer);
                if (!Grid.Exists(cell)) {
                    continue;
                }

                ElementKind kind = Grid.KindAt(cell);
                if (kind == ElementKind.None || kind == ElementKind.Hero || ElementRules.IsFloor(kind) || cell == entry.HeroCell) {
                    continue;
                }

                Place(new WorldEntity(kind, cell), cell);
            }
        }

        WorldEntity occupant = ObjectAt(entry.HeroCell);
        if (occupant != null) {
            Remove(occupant);
        }

        Inventory.RestartRoom(CurrentRoom, entry.Local);
        HeroForm = ElementKind.None;
        Place(Hero, entry.HeroCell);
        if (entry.HeroForm != ElementKind.None && Inventory.Count(entry.HeroForm) > 0) {
            HeroForm = entry.HeroForm;
            _cells[Hero.Position.x, Hero.Position.y, Hero.Position.z] = CellKind(Hero);
        }

        Restored?.Invoke();
        return true;
    }

    private void Move(WorldEntity entity, Vector3Int to) {
        _objects.Remove(entity.Position);
        _cells[entity.Position.x, entity.Position.y, entity.Position.z] = ElementKind.None;
        Place(entity, to);
        EntityMoved?.Invoke(entity);
    }

    private void Place(WorldEntity entity, Vector3Int cell) {
        entity.Position = cell;
        _objects[cell] = entity;
        _cells[cell.x, cell.y, cell.z] = CellKind(entity);
    }

    private void Remove(WorldEntity entity) {
        _objects.Remove(entity.Position);
        _cells[entity.Position.x, entity.Position.y, entity.Position.z] = ElementKind.None;
    }

    // Ряды из трёх и более одинаковых элементов исчезают. Связная группа одного вида
    // (ряд, крест, уголок) награждает по длине самого длинного прямого ряда в ней (см. константы выше).
    // В группе с превращённым героем соседи исчезают, герой становится собой; награда — см. ветку ниже.
    // Группа персонажей с героем — конец игры: герой исчезает вместе с ними.
    private void ResolveMatches() {
        List<Vector3Int> runs = WorldMap.FindRuns(Grid, _cells);
        if (runs.Count == 0) {
            return;
        }

        HashSet<Vector3Int> runCells = new(runs);
        HashSet<Vector3Int> visited = new();
        List<WorldEntity> matched = new();
        Stack<Vector3Int> stack = new();
        HashSet<Vector3Int> group = new();
        bool won = false;
        bool revertHero = false;

        foreach (Vector3Int start in runs) {
            if (!visited.Add(start)) {
                continue;
            }

            ElementKind kind = _cells[start.x, start.y, start.z];
            bool withHero = false;
            group.Clear();
            stack.Push(start);
            while (stack.Count > 0) {
                Vector3Int cell = stack.Pop();
                group.Add(cell);
                foreach (Vector3Int neighbor in Neighbors(cell)) {
                    if (runCells.Contains(neighbor) && _cells[neighbor.x, neighbor.y, neighbor.z] == kind && visited.Add(neighbor)) {
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
                if (longest >= GlobalRunLength) {
                    Inventory.Add(kind, 1, everywhere: true);
                } else if (longest >= LocalRunLength) {
                    Inventory.Add(kind, 1, everywhere: false);
                }
            } else if (kind == ElementKind.Person) {
                Remove(Hero);
                matched.Add(Hero);
                Hero = null;
                HeroForm = ElementKind.None;
                won = true;
            } else {
                // Ряд с героем: тройка съедает «надетый» предмет, четвёрка возвращает его (ничего не тратится),
                // пятёрка тоже ничего не тратит и даёт +1 на всех экранах.
                int longest = LongestRun(group);
                if (longest >= GlobalRunLength) {
                    Inventory.Add(kind, 1, everywhere: true);
                } else if (longest < LocalRunLength && Features.TransformationCostsResource) {
                    Inventory.TryTake(kind, 1);
                }

                revertHero = true;
            }
        }

        // Сначала событие об исчезнувших, потом возврат героя: вью успевает отложить возврат до конца исчезновения.
        EntitiesMatched?.Invoke(matched);
        if (revertHero) {
            SetHeroForm(ElementKind.None);
        }

        if (won) {
            GameWon?.Invoke();
        }
    }

    // Самый длинный прямой ряд (горизонтальный или вертикальный) внутри группы клеток.
    private int LongestRun(HashSet<Vector3Int> group) {
        int longest = 0;
        foreach (Vector3Int cell in group) {
            foreach (Vector2Int step in new[] { Vector2Int.right, Vector2Int.up }) {
                // Считаем от начала ряда: клетки с соседом группы позади пропускаем.
                if (Grid.TryStep(cell, -step, out Vector3Int back) && group.Contains(back)) {
                    continue;
                }

                longest = Math.Max(longest, RunLength(group, cell, step));
            }
        }

        return longest;
    }

    private int RunLength(HashSet<Vector3Int> group, Vector3Int start, Vector2Int step) {
        int length = 0;
        Vector3Int cell = start;
        while (group.Contains(cell)) {
            length++;
            if (!Grid.TryStep(cell, step, out cell)) {
                break;
            }
        }

        return length;
    }

    private IEnumerable<Vector3Int> Neighbors(Vector3Int cell) {
        if (Grid.TryStep(cell, Vector2Int.right, out Vector3Int right)) {
            yield return right;
        }

        if (Grid.TryStep(cell, Vector2Int.left, out Vector3Int left)) {
            yield return left;
        }

        if (Grid.TryStep(cell, Vector2Int.up, out Vector3Int up)) {
            yield return up;
        }

        if (Grid.TryStep(cell, Vector2Int.down, out Vector3Int down)) {
            yield return down;
        }
    }
}
