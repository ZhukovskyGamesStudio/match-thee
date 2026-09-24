using System;
using UnityEngine;

// Какой спрайт показать в клетке рельефа. Рельеф задаётся высотой, а не стенами: автор красит
// возвышенность, а обрыв, углы и тень вырастают сами по соседям — как в обычном блоб-тайлсете.
//
// В имени тайла — число, сложенное из битов соседей: N=1, NE=2, E=4, SE=8, S=16, SW=32, W=64, NW=128.
//   ledge_<маска>            борт возвышенности: стена только с юга (бок стены в такой проекции не виден),
//                            в маске шесть бит — стороны и нижние диагонали, отсюда 36 тайлов.
//   cliff_<блоб>, cavewall_<блоб>  сплошная порода: угловой бит считается, только если заняты обе
//                            соседние стороны — классические 47 сочетаний с внешними И внутренними углами.
// Земля — четыре варианта, выбранных хешем клетки: одинаковые тайлы не выстраиваются в сетку.
public static class TerrainTiles {
    public const int GroundVariants = 4;

    private const int North = 1;
    private const int NorthEast = 2;
    private const int East = 4;
    private const int SouthEast = 8;
    private const int South = 16;
    private const int SouthWest = 32;
    private const int West = 64;
    private const int NorthWest = 128;

    // null — у вида нет своих тайлов, берётся обычный спрайт по имени вида.
    public static string NameOf(WorldGrid grid, ElementKind kind, Vector3Int cell) {
        if (ElementRules.IsAutotiled(kind)) {
            return $"{Name(kind)}_{CanonWall(Mask(grid, cell, WallLike))}";
        }

        if (ElementRules.IsGround(kind)) {
            int variant = Variant(cell);
            return variant == 0 ? Name(kind) : $"{Name(kind)}_{(char)('b' + variant - 1)}";
        }

        return null;
    }

    // Борт возвышенности: стена рисуется только с той стороны, куда обрыв смотрит на нас (юг).
    // Боковых стен нет — при взгляде спереди-сверху бок стены не виден. В маске шесть бит:
    // север и юг — продолжается ли поверхность, восток и запад — продолжается ли стена,
    // а диагонали снизу говорят, что сосед стену не продолжит и здесь у неё внутренний угол.
    public static string LedgeNameOf(WorldGrid grid, Vector3Int cell) {
        if (grid.FloorKindAt(cell) != ElementKind.Highland) {
            return null;
        }

        int mask = 0;
        if (Raised(grid, Step(grid, cell, Vector2Int.up))) {
            mask |= North;
        }

        if (Raised(grid, Step(grid, cell, Vector2Int.down))) {
            mask |= South;
        }

        mask |= Side(grid, cell, Vector2Int.right, East, SouthEast);
        mask |= Side(grid, cell, Vector2Int.left, West, SouthWest);
        // Поверхность продолжается и вниз, и вверх — рисовать нечего.
        bool empty = (mask & South) != 0 && (mask & North) != 0;
        return empty ? null : $"{Name(ElementKind.Ledge)}_{mask}";
    }

    // Бит стороны и бит её нижней диагонали: стена продолжается вбок, но у соседа обрыва нет.
    private static int Side(WorldGrid grid, Vector3Int cell, Vector2Int direction, int sideBit, int cornerBit) {
        Vector3Int neighbour = Step(grid, cell, direction);
        if (!ContinuesFace(grid, neighbour)) {
            return 0;
        }

        return sideBit | (Raised(grid, Step(grid, neighbour, Vector2Int.down)) ? cornerBit : 0);
    }

    private static Vector3Int Step(WorldGrid grid, Vector3Int cell, Vector2Int direction) {
        return grid.TryNeighbour(cell, direction, out Vector3Int to) ? to : new Vector3Int(-1, -1, -1);
    }

    private static bool ContinuesFace(WorldGrid grid, Vector3Int cell) {
        return grid.Exists(cell) && ElementRules.ContinuesFace(grid.FloorKindAt(cell));
    }

    // Угол у сплошной породы считается, только если заняты обе соседние стороны: 256 -> 47 сочетаний.
    private static int CanonWall(int mask) {
        if ((mask & North) == 0 || (mask & East) == 0) {
            mask &= ~NorthEast;
        }

        if ((mask & South) == 0 || (mask & East) == 0) {
            mask &= ~SouthEast;
        }

        if ((mask & South) == 0 || (mask & West) == 0) {
            mask &= ~SouthWest;
        }

        if ((mask & North) == 0 || (mask & West) == 0) {
            mask &= ~NorthWest;
        }

        return mask & 0xFF;
    }

    private static int Mask(WorldGrid grid, Vector3Int cell, Func<WorldGrid, Vector3Int, bool> test) {
        int mask = 0;
        mask |= Bit(grid, cell, Vector2Int.up, test, North);
        mask |= Bit(grid, cell, Vector2Int.right, test, East);
        mask |= Bit(grid, cell, Vector2Int.down, test, South);
        mask |= Bit(grid, cell, Vector2Int.left, test, West);
        mask |= Corner(grid, cell, Vector2Int.up, Vector2Int.right, test, NorthEast);
        mask |= Corner(grid, cell, Vector2Int.down, Vector2Int.right, test, SouthEast);
        mask |= Corner(grid, cell, Vector2Int.down, Vector2Int.left, test, SouthWest);
        mask |= Corner(grid, cell, Vector2Int.up, Vector2Int.left, test, NorthWest);
        return mask;
    }

    private static int Bit(WorldGrid grid, Vector3Int cell, Vector2Int direction, Func<WorldGrid, Vector3Int, bool> test, int bit) {
        return grid.TryNeighbour(cell, direction, out Vector3Int to) && test(grid, to) ? bit : 0;
    }

    // Диагональ — два шага подряд: так она остаётся на том же уровне, что и путь до неё.
    private static int Corner(WorldGrid grid, Vector3Int cell, Vector2Int first, Vector2Int second, Func<WorldGrid, Vector3Int, bool> test, int bit) {
        if (grid.TryNeighbour(cell, first, out Vector3Int step) && grid.TryNeighbour(step, second, out Vector3Int corner)) {
            return test(grid, corner) ? bit : 0;
        }

        if (grid.TryNeighbour(cell, second, out step) && grid.TryNeighbour(step, first, out corner)) {
            return test(grid, corner) ? bit : 0;
        }

        return 0;
    }

    // Поверхность продолжается: возвышенность, склон или прорубленный в стене вход.
    private static bool Raised(WorldGrid grid, Vector3Int cell) {
        return grid.Exists(cell) && ElementRules.IsRaised(grid.FloorKindAt(cell));
    }

    // Порода: с ней смыкаются грани стен (вид клетки, а не земля под ней).
    private static bool WallLike(WorldGrid grid, Vector3Int cell) {
        return grid.Exists(cell) && ElementRules.IsWallLike(grid.KindAt(cell));
    }

    // Вариант земли — от координат клетки, а не от порядка обхода: карта одинакова при каждом запуске.
    private static int Variant(Vector3Int cell) {
        int hash = (cell.x * 73856093) ^ (cell.y * 19349663) ^ ((cell.z + 1) * 83492791);
        return (hash & 0x7FFFFFFF) % GroundVariants;
    }

    private static string Name(ElementKind kind) {
        return kind.ToString().ToLowerInvariant();
    }
}
