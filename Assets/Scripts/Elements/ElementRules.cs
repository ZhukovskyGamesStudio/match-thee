public static class ElementRules {
    public const int WobbleFrames = 3;

    // Пол: по нему ходят, его не толкают и в ряды он не собирается. Трон — декоративная клетка пола.
    public static bool IsFloor(ElementKind kind) {
        return kind is ElementKind.Grass or ElementKind.Water or ElementKind.Lava or ElementKind.Ice or ElementKind.Sand
            or ElementKind.Throne or ElementKind.CastleFloor;
    }

    // Непроходимое и неподвижное: в ряды не собирается и не исчезает.
    public static bool IsSolid(ElementKind kind) {
        return kind is ElementKind.Wall or ElementKind.CastleWall or ElementKind.CastleWall2 or ElementKind.CastleWall3
            or ElementKind.CastleWindow;
    }

    // Стеновая растительность: из неё генератор складывает стены.
    public static bool IsWallPlant(ElementKind kind) {
        return kind is ElementKind.Spruce or ElementKind.BerryBush or ElementKind.DarkTree or ElementKind.Birch or ElementKind.Stump;
    }

    // Всё остальное — элементы три-в-ряд: герой их толкает, с трона меняет местами, три подряд исчезают.
    public static bool IsPushable(ElementKind kind) {
        return kind != ElementKind.None && kind != ElementKind.Hero && !IsFloor(kind) && !IsSolid(kind);
    }

    // Существа разворачиваются по направлению движения, предметы — нет.
    public static bool IsCreature(ElementKind kind) {
        return kind is ElementKind.Hero or ElementKind.Person or ElementKind.Ghost or ElementKind.Slime or ElementKind.Bat or ElementKind.Frog
            or ElementKind.Bird;
    }

    public static string SpriteName(ElementKind kind, int frame) {
        return SpriteName(kind.ToString().ToLowerInvariant(), frame);
    }

    public static string SpriteName(string name, int frame) {
        return $"{name}_{frame}";
    }
}
