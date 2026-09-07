using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Рюкзак игрока. Ряд из четырёх даёт ресурс только на текущий экран: он остаётся в своей комнате.
// Ряд из пяти даёт общий ресурс: его можно потратить по одному разу НА КАЖДОМ экране — траты
// из общего запаса считаются отдельно по экранам. Тратятся сначала местные, потом общие.
public class Inventory {
    public event Action<ElementKind, int, bool> Added; // вид, сколько пришло, на все ли экраны
    public event Action<ElementKind, int> Changed;     // вид, сколько доступно здесь
    public event Action ScreenChanged;

    private readonly Dictionary<ElementKind, int> _global = new();
    private readonly Dictionary<Vector2Int, Dictionary<ElementKind, int>> _local = new();
    private readonly Dictionary<Vector2Int, Dictionary<ElementKind, int>> _spent = new(); // сколько общих потрачено на экране
    private Vector2Int _screen;

    // Экран, на котором стоит герой: определяет, какие местные ресурсы сейчас доступны.
    public Vector2Int Screen {
        get => _screen;
        set {
            if (_screen == value) {
                return;
            }

            _screen = value;
            ScreenChanged?.Invoke();
        }
    }

    // Общих осталось на этом экране: общий запас минус потраченное здесь.
    public int GlobalCount(ElementKind kind) {
        return Math.Max(0, TotalGlobal(kind) - CountIn(_spent, kind));
    }

    public int TotalGlobal(ElementKind kind) {
        return _global.TryGetValue(kind, out int count) ? count : 0;
    }

    public int LocalCount(ElementKind kind) {
        return CountIn(_local, kind);
    }

    private int CountIn(Dictionary<Vector2Int, Dictionary<ElementKind, int>> table, ElementKind kind) {
        return table.TryGetValue(_screen, out Dictionary<ElementKind, int> counts) && counts.TryGetValue(kind, out int count) ? count : 0;
    }

    // Сколько доступно на этом экране: общие плюс местные.
    public int Count(ElementKind kind) {
        return GlobalCount(kind) + LocalCount(kind);
    }

    // Порядок предметов в рюкзаке (он же порядок превращений по Q): всё, чего здесь больше нуля.
    public IEnumerable<ElementKind> Kinds {
        get {
            IEnumerable<ElementKind> localKinds = _local.TryGetValue(_screen, out Dictionary<ElementKind, int> counts) ? counts.Keys : Enumerable.Empty<ElementKind>();
            return _global.Keys.Concat(localKinds).Distinct().Where(kind => Count(kind) > 0).OrderBy(kind => (int)kind);
        }
    }

    public bool IsEmpty => !Kinds.Any();

    public void Add(ElementKind kind, int amount, bool everywhere) {
        if (everywhere) {
            _global[kind] = TotalGlobal(kind) + amount;
        } else {
            CountsIn(_local, _screen)[kind] = LocalCount(kind) + amount;
        }

        Changed?.Invoke(kind, Count(kind));
        Added?.Invoke(kind, amount, everywhere);
    }

    // Забрать без всплывашки: сначала местные, потом общие (трата общих записывается за этим экраном).
    public bool TryTake(ElementKind kind, int amount) {
        if (Count(kind) < amount) {
            return false;
        }

        int fromLocal = Math.Min(amount, LocalCount(kind));
        if (fromLocal > 0) {
            CountsIn(_local, _screen)[kind] = LocalCount(kind) - fromLocal;
        }

        int fromGlobal = amount - fromLocal;
        if (fromGlobal > 0) {
            CountsIn(_spent, _screen)[kind] = CountIn(_spent, kind) + fromGlobal;
        }

        Changed?.Invoke(kind, Count(kind));
        return true;
    }

    // Снимок для отката комнаты: копия общих, всех местных и трат общих по экранам.
    public State Save() {
        return new State {
            Global = new Dictionary<ElementKind, int>(_global),
            Local = Copy(_local),
            Spent = Copy(_spent),
        };
    }

    // Откат без событий: интерфейс перестраивается по WorldModel.Restored.
    public void Restore(State state) {
        _global.Clear();
        foreach (KeyValuePair<ElementKind, int> pair in state.Global) {
            _global[pair.Key] = pair.Value;
        }

        Fill(_local, state.Local);
        Fill(_spent, state.Spent);
    }

    private static Dictionary<ElementKind, int> CountsIn(Dictionary<Vector2Int, Dictionary<ElementKind, int>> table, Vector2Int screen) {
        if (!table.TryGetValue(screen, out Dictionary<ElementKind, int> counts)) {
            counts = new Dictionary<ElementKind, int>();
            table[screen] = counts;
        }

        return counts;
    }

    private static Dictionary<Vector2Int, Dictionary<ElementKind, int>> Copy(Dictionary<Vector2Int, Dictionary<ElementKind, int>> table) {
        return table.ToDictionary(pair => pair.Key, pair => new Dictionary<ElementKind, int>(pair.Value));
    }

    private static void Fill(Dictionary<Vector2Int, Dictionary<ElementKind, int>> table, Dictionary<Vector2Int, Dictionary<ElementKind, int>> source) {
        table.Clear();
        foreach (KeyValuePair<Vector2Int, Dictionary<ElementKind, int>> pair in source) {
            table[pair.Key] = new Dictionary<ElementKind, int>(pair.Value);
        }
    }

    public class State {
        public Dictionary<ElementKind, int> Global;
        public Dictionary<Vector2Int, Dictionary<ElementKind, int>> Local;
        public Dictionary<Vector2Int, Dictionary<ElementKind, int>> Spent;
    }
}
