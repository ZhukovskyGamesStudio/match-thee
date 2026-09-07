public static class ElementRules {
    public const int WobbleFrames = 3;

    // Пол: по нему ходят, его не толкают.
    public static bool IsFloor(ElementKind kind) {
        return kind is ElementKind.Grass or ElementKind.Water or ElementKind.Lava or ElementKind.Ice or ElementKind.Sand;
    }

    // Непроходимое и неподвижное.
    public static bool IsSolid(ElementKind kind) {
        return kind == ElementKind.Wall;
    }

    // Всё остальное герой толкает перед собой.
    public static bool IsPushable(ElementKind kind) {
        return kind != ElementKind.None && kind != ElementKind.Hero && !IsFloor(kind) && !IsSolid(kind);
    }

    // Существа разворачиваются по направлению движения, предметы — нет.
    public static bool IsCreature(ElementKind kind) {
        return kind is ElementKind.Hero or ElementKind.Ghost or ElementKind.Slime or ElementKind.Bat or ElementKind.Frog or ElementKind.Bird;
    }

    public static string SpriteName(ElementKind kind, int frame) {
        return $"{kind.ToString().ToLowerInvariant()}_{frame}";
    }
}
