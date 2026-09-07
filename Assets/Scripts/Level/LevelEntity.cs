using UnityEngine;

public class LevelEntity {
    public ElementKind Kind { get; }
    public Vector2Int Position { get; set; }

    public LevelEntity(ElementKind kind, Vector2Int position) {
        Kind = kind;
        Position = position;
    }
}
