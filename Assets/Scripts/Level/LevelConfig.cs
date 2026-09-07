using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class LevelLegendEntry {
    public string Symbol;
    public ElementKind Kind;
}

// Уровень как текстовая карта: один символ — одна клетка, верхняя строка — верх уровня.
[CreateAssetMenu(fileName = "Level", menuName = "Scriptable Objects/LevelConfig")]
public class LevelConfig : ScriptableObject {
    [field: SerializeField, TextArea(8, 40)]
    public string Map { get; private set; }

    [field: SerializeField]
    public List<LevelLegendEntry> Legend { get; private set; } = new();

    public string[] Rows {
        get {
            string[] rows = (Map ?? string.Empty).Replace("\r", "").Split('\n').Select(row => row.TrimEnd()).Where(row => row.Length > 0).ToArray();
            int width = rows.Length > 0 ? rows.Max(row => row.Length) : 0;
            return rows.Select(row => row.PadRight(width, '.')).ToArray();
        }
    }

    public ElementKind KindOf(char symbol) {
        foreach (LevelLegendEntry entry in Legend) {
            if (!string.IsNullOrEmpty(entry.Symbol) && entry.Symbol[0] == symbol) {
                return entry.Kind;
            }
        }

        return ElementKind.None;
    }

#if UNITY_EDITOR
    public void Set(string map, IEnumerable<LevelLegendEntry> legend) {
        Map = map;
        Legend = legend.ToList();
    }
#endif
}
