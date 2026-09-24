public static class ElementRules {
    public const int WobbleFrames = 3;

    // Пол: по нему ходят, его не толкают и в ряды он не собирается. Трон — декоративная клетка пола.
    public static bool IsFloor(ElementKind kind) {
        return kind is ElementKind.Grass or ElementKind.Water or ElementKind.Lava or ElementKind.Ice or ElementKind.Sand
            or ElementKind.Throne or ElementKind.CastleFloor or ElementKind.Slope
            || IsTutorialDecor(kind) || IsGround(kind) || IsPassage(kind) || IsTerrainDecor(kind);
    }

    // Земля рельефа: сплошной пол уровня. Ею же засыпается клетка под предметом, чтобы он не висел
    // над пустотой. Склон сюда не входит: он ведёт с низины на плато, и подставлять его под предмет незачем.
    public static bool IsGround(ElementKind kind) {
        return kind is ElementKind.Lowland or ElementKind.Highland or ElementKind.CaveFloor or ElementKind.DeepFloor;
    }

    // Переход между уровнями: пол, на котором шаг в сторону может увести на связанный уровень.
    // Склон в этот список не входит: он поднимает на плато в пределах одного уровня.
    public static bool IsPassage(ElementKind kind) {
        return kind is ElementKind.CaveMouth or ElementKind.StairsDown or ElementKind.StairsUp;
    }

    // Высота клетки. Между разными высотами не шагнуть: с низины на плато только по склону.
    // Обрыв не рисуют — он вырастает сам по краю возвышенности (TerrainTiles).
    public static int ElevationOf(ElementKind kind) {
        return kind == ElementKind.Highland ? 1 : 0;
    }

    // Клетка, через которую высоту меняют: пологий склон и лестницы.
    // Вход в пещеру сюда не входит: он прорублен в подножии стены и лежит на уровне низины,
    // поэтому сверху, с плато, в него шагнуть нельзя — только снизу и изнутри.
    public static bool IsRamp(ElementKind kind) {
        return kind is ElementKind.Slope or ElementKind.StairsUp or ElementKind.StairsDown;
    }

    // Поверхность продолжается: над такой клеткой обрыва не рисуют. Переходы сюда входят все:
    // вход прорублен в самой стене, а колодец лестницы — дыра в плато, и стены над ними быть не должно.
    public static bool IsRaised(ElementKind kind) {
        return kind is ElementKind.Highland or ElementKind.Slope || IsPassage(kind);
    }

    // Стена обрыва продолжается вбок: в соседнюю возвышенность, в прорубленный вход
    // и в подъём — у него по краям клетки та же стена во всю высоту, стык получается встык.
    public static bool ContinuesFace(ElementKind kind) {
        return kind is ElementKind.Highland or ElementKind.CaveMouth or ElementKind.Slope;
    }

    // Высоту меняют только склон и лестницы. Вход в пещеру лежит на уровне низины:
    // снизу и изнутри в него шагают, сверху с плато — нет.

    // Борт — не элемент карты, а украшение поверх земли: вью создаёт его сам.
    public static bool IsTerrainDecor(ElementKind kind) {
        return kind == ElementKind.Ledge;
    }

    // Тайл выше клетки: стена свисает на клетку ниже. Такой тайл кладут поверх её земли —
    // в одном слое сортировки порядок между ними не определён, и свес то виден, то пропадает.
    public static bool IsTall(ElementKind kind) {
        return kind is ElementKind.Ledge or ElementKind.Slope or ElementKind.CaveMouth;
    }

    // Подсказки обучения: клавиши, стрелки, мышка. Пол, ни на что не влияет.
    public static bool IsTutorialDecor(ElementKind kind) {
        return kind is ElementKind.KeyW or ElementKind.KeyA or ElementKind.KeyS or ElementKind.KeyD
            or ElementKind.ArrowUp or ElementKind.ArrowLeft or ElementKind.ArrowDown or ElementKind.ArrowRight
            or ElementKind.MouseClick;
    }

    // Непроходимое и неподвижное: в ряды не собирается и не исчезает. Стены замка — обычные элементы,
    // а обрыв и скала пещеры — рельеф: их не толкают и не собирают.
    public static bool IsSolid(ElementKind kind) {
        return kind is ElementKind.Wall or ElementKind.Cliff or ElementKind.CaveWall;
    }

    // Порода с автотайлом: обрыв и скала пещеры сами подбирают вид по соседям (TerrainTiles).
    public static bool IsAutotiled(ElementKind kind) {
        return kind is ElementKind.Cliff or ElementKind.CaveWall;
    }

    // С чем смыкается грань стены: с такой же породой и с прорубленными в ней переходами.
    public static bool IsWallLike(ElementKind kind) {
        return IsAutotiled(kind) || IsPassage(kind);
    }

    // Дерево крупнее клетки и стоит в ней со сдвигом на пару пикселей: лес не выстраивается по
    // линейке, а кроны слегка задевают соседние клетки — мир читается цельным, а не сеткой.
    public static bool IsJittered(ElementKind kind) {
        return kind is ElementKind.Tree or ElementKind.Spruce or ElementKind.DarkTree or ElementKind.Birch;
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
