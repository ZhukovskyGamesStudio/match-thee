using UnityEngine;

public class WorldEntity {
    public ElementKind Kind { get; }
    public Vector2Int Position { get; set; }

    public WorldEntity(ElementKind kind, Vector2Int position) {
        Kind = kind;
        Position = position;
    }
}
