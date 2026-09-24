using UnityEngine;

// Сущность мира. Позиция — клетка (x, y, уровень): z говорит, на каком уровне рельефа она лежит.
public class WorldEntity {
    public ElementKind Kind { get; }
    public Vector3Int Position { get; set; }

    public WorldEntity(ElementKind kind, Vector3Int position) {
        Kind = kind;
        Position = position;
    }
}
