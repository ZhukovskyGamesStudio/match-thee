using System;
using System.Collections.Generic;

// Ресурсы игрока: из исчезнувшего элемента выпадает один ресурс его вида (из куста — куст).
public class Inventory {
    public event Action<ElementKind, int> Changed;

    private readonly Dictionary<ElementKind, int> _counts = new();

    public IReadOnlyDictionary<ElementKind, int> Counts => _counts;

    public int Count(ElementKind kind) {
        return _counts.TryGetValue(kind, out int count) ? count : 0;
    }

    public void Add(ElementKind kind, int amount) {
        int count = Count(kind) + amount;
        _counts[kind] = count;
        Changed?.Invoke(kind, count);
    }
}
