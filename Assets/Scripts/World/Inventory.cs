using System;
using System.Collections.Generic;
using System.Linq;

// Рюкзак игрока. Ресурсы (Counts) — расходуемые, по одному за ряд из четырёх.
// Открытые виды (Unlocked) — навсегда, за ряд из пяти: превращаться в них можно всегда и бесплатно.
public class Inventory {
    public event Action<ElementKind, int> Changed; // вид, итого
    public event Action<ElementKind, int> Added;   // вид, сколько пришло
    public event Action<ElementKind> Unlocked;

    private readonly Dictionary<ElementKind, int> _counts = new();
    private readonly HashSet<ElementKind> _unlocked = new();

    public IReadOnlyDictionary<ElementKind, int> Counts => _counts;
    public IReadOnlyCollection<ElementKind> UnlockedKinds => _unlocked;

    // Порядок предметов в рюкзаке (он же порядок превращений по Q): открытые навсегда, потом расходуемые.
    public IEnumerable<ElementKind> Kinds {
        get {
            foreach (ElementKind kind in _unlocked.OrderBy(kind => (int)kind)) {
                yield return kind;
            }

            foreach (KeyValuePair<ElementKind, int> pair in _counts.Where(pair => pair.Value > 0 && !_unlocked.Contains(pair.Key)).OrderBy(pair => (int)pair.Key)) {
                yield return pair.Key;
            }
        }
    }

    // Откат к снимку комнаты: без событий, интерфейс перестраивается по WorldModel.Restored.
    public void Restore(IReadOnlyDictionary<ElementKind, int> counts, IEnumerable<ElementKind> unlocked) {
        _counts.Clear();
        foreach (KeyValuePair<ElementKind, int> pair in counts) {
            _counts[pair.Key] = pair.Value;
        }

        _unlocked.Clear();
        _unlocked.UnionWith(unlocked);
    }

    public bool IsUnlocked(ElementKind kind) {
        return _unlocked.Contains(kind);
    }

    public void Unlock(ElementKind kind) {
        if (_unlocked.Add(kind)) {
            Unlocked?.Invoke(kind);
        }
    }

    public int Count(ElementKind kind) {
        return _counts.TryGetValue(kind, out int count) ? count : 0;
    }

    // Забрать без всплывашки «+N»: только Changed.
    public bool TryTake(ElementKind kind, int amount) {
        int count = Count(kind);
        if (count < amount) {
            return false;
        }

        _counts[kind] = count - amount;
        Changed?.Invoke(kind, count - amount);
        return true;
    }

    public void Add(ElementKind kind, int amount) {
        int count = Count(kind) + amount;
        _counts[kind] = count;
        Changed?.Invoke(kind, count);
        Added?.Invoke(kind, amount);
    }
}
