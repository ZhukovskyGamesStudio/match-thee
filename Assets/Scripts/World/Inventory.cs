using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Рюкзак игрока. Ряд из четырёх даёт ресурс только в текущую комнату: он остаётся в ней.
// Ряд из пяти даёт общий ресурс: его можно потратить по одному разу В КАЖДОЙ комнате — траты
// из общего запаса считаются отдельно по комнатам. Тратятся сначала местные, потом общие.
// Комната — это экран на своём уровне рельефа (Vector3Int: экран по x, y и уровень в z):
// пещера под плато — отдельная комната со своими местными ресурсами.
public class Inventory {
    public event Action<ElementKind, int, bool> Added; // вид, сколько пришло, на все ли экраны
    public event Action<ElementKind, int> Changed;     // вид, сколько доступно здесь
    public event Action RoomChanged;

    private readonly Dictionary<ElementKind, int> _global = new();
    private readonly Dictionary<Vector3Int, Dictionary<ElementKind, int>> _local = new();
    private readonly Dictionary<Vector3Int, Dictionary<ElementKind, int>> _spent = new();  // сколько общих потрачено в комнате
    private readonly Dictionary<Vector3Int, Dictionary<ElementKind, int>> _gained = new(); // сколько общих добыто в комнате
    private Vector3Int _room;

    // Комната, в которой стоит герой: определяет, какие местные ресурсы сейчас доступны.
    public Vector3Int Room {
        get => _room;
        set {
            if (_room == value) {
                return;
            }

            _room = value;
            RoomChanged?.Invoke();
        }
    }

    // Общих осталось в этой комнате: общий запас минус потраченное здесь.
    public int GlobalCount(ElementKind kind) {
        return Math.Max(0, TotalGlobal(kind) - CountIn(_spent, kind));
    }

    public int TotalGlobal(ElementKind kind) {
        return _global.TryGetValue(kind, out int count) ? count : 0;
    }

    public int LocalCount(ElementKind kind) {
        return CountIn(_local, kind);
    }

    private int CountIn(Dictionary<Vector3Int, Dictionary<ElementKind, int>> table, ElementKind kind) {
        return table.TryGetValue(_room, out Dictionary<ElementKind, int> counts) && counts.TryGetValue(kind, out int count) ? count : 0;
    }

    // Сколько доступно в этой комнате: общие плюс местные.
    public int Count(ElementKind kind) {
        return GlobalCount(kind) + LocalCount(kind);
    }

    // Порядок предметов в рюкзаке (он же порядок превращений по Q): всё, чего здесь больше нуля.
    public IEnumerable<ElementKind> Kinds {
        get {
            IEnumerable<ElementKind> localKinds = _local.TryGetValue(_room, out Dictionary<ElementKind, int> counts) ? counts.Keys : Enumerable.Empty<ElementKind>();
            return _global.Keys.Concat(localKinds).Distinct().Where(kind => Count(kind) > 0).OrderBy(kind => (int)kind);
        }
    }

    public bool IsEmpty => !Kinds.Any();

    public void Add(ElementKind kind, int amount, bool everywhere) {
        if (everywhere) {
            _global[kind] = TotalGlobal(kind) + amount;
            CountsIn(_gained, _room)[kind] = CountIn(_gained, kind) + amount;
        } else {
            CountsIn(_local, _room)[kind] = LocalCount(kind) + amount;
        }

        Changed?.Invoke(kind, Count(kind));
        Added?.Invoke(kind, amount, everywhere);
    }

    // Забрать без всплывашки: сначала местные, потом общие (трата общих записывается за этой комнатой).
    public bool TryTake(ElementKind kind, int amount) {
        if (Count(kind) < amount) {
            return false;
        }

        int fromLocal = Math.Min(amount, LocalCount(kind));
        if (fromLocal > 0) {
            CountsIn(_local, _room)[kind] = LocalCount(kind) - fromLocal;
        }

        int fromGlobal = amount - fromLocal;
        if (fromGlobal > 0) {
            CountsIn(_spent, _room)[kind] = CountIn(_spent, kind) + fromGlobal;
        }

        Changed?.Invoke(kind, Count(kind));
        return true;
    }

    // Местные ресурсы комнаты на момент первого входа — для рестарта комнаты.
    public Dictionary<ElementKind, int> SaveLocal(Vector3Int room) {
        return _local.TryGetValue(room, out Dictionary<ElementKind, int> counts) ? new Dictionary<ElementKind, int>(counts) : new Dictionary<ElementKind, int>();
    }

    // Рестарт комнаты: общие ресурсы, добытые здесь, отнимаются; траты общих здесь возвращаются;
    // местные — как при первом входе. Без событий: интерфейс перестраивается по WorldModel.Restored.
    public void RestartRoom(Vector3Int room, Dictionary<ElementKind, int> localAtEntry) {
        if (_gained.TryGetValue(room, out Dictionary<ElementKind, int> gained)) {
            foreach (KeyValuePair<ElementKind, int> pair in gained) {
                _global[pair.Key] = Math.Max(0, TotalGlobal(pair.Key) - pair.Value);
            }

            gained.Clear();
        }

        _spent.Remove(room);
        _local[room] = new Dictionary<ElementKind, int>(localAtEntry);
    }

    private static Dictionary<ElementKind, int> CountsIn(Dictionary<Vector3Int, Dictionary<ElementKind, int>> table, Vector3Int room) {
        if (!table.TryGetValue(room, out Dictionary<ElementKind, int> counts)) {
            counts = new Dictionary<ElementKind, int>();
            table[room] = counts;
        }

        return counts;
    }

}
