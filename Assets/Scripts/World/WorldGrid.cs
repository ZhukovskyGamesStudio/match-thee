using System.Collections.Generic;
using UnityEngine;

// Сетка мира по уровням: клетка — это (x, y, уровень). Уровень 0 — поверхность, дальше пещеры и этажи.
// В одном столбце (x, y) клетки разных уровней — разные места: под плато может лежать пещера.
// Часть клеток — переходы: они лежат на своём уровне, но видны и проходимы ещё с одного (Link).
// Вход в пещеру — переход уровня пещеры со ссылкой на поверхность: снаружи он виден дырой в обрыве,
// изнутри — проёмом, а шаг сквозь него переносит героя (и толкаемый элемент) с уровня на уровень.
public class WorldGrid {
    public const int NoLink = -1;

    public int Width { get; }
    public int Height { get; }
    public int Layers { get; }

    private readonly ElementKind[,,] _kinds;
    private readonly ElementKind[,,] _ground; // земля под предметом: карта задаёт одну клетку одним символом
    private readonly bool[,,] _exists;
    private readonly int[,,] _links;

    public WorldGrid(int width, int height, int layers) {
        Width = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        Layers = Mathf.Max(1, layers);
        _kinds = new ElementKind[Width, Height, Layers];
        _ground = new ElementKind[Width, Height, Layers];
        _exists = new bool[Width, Height, Layers];
        _links = new int[Width, Height, Layers];
        for (int layer = 0; layer < Layers; layer++) {
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    _links[x, y, layer] = NoLink;
                }
            }
        }
    }

    public bool IsInside(Vector3Int cell) {
        return cell.x >= 0 && cell.y >= 0 && cell.z >= 0 && cell.x < Width && cell.y < Height && cell.z < Layers;
    }

    // Есть ли клетка на этом уровне. Клетки нет — там пусто: ни пройти, ни увидеть.
    public bool Exists(Vector3Int cell) {
        return IsInside(cell) && _exists[cell.x, cell.y, cell.z];
    }

    public ElementKind KindAt(Vector3Int cell) {
        return IsInside(cell) ? _kinds[cell.x, cell.y, cell.z] : ElementKind.None;
    }

    // Вид земли в клетке: сама клетка, если это земля, иначе подставленная под предмет.
    // По нему считается высота: ящик на плато стоит на плато, а не висит над низиной.
    public ElementKind FloorKindAt(Vector3Int cell) {
        ElementKind ground = GroundAt(cell);
        return ground != ElementKind.None ? ground : KindAt(cell);
    }

    // Земля, подставленная под предмет (None — предмет стоит прямо на фоне, как в мире без рельефа).
    public ElementKind GroundAt(Vector3Int cell) {
        return IsInside(cell) ? _ground[cell.x, cell.y, cell.z] : ElementKind.None;
    }

    public void SetGround(Vector3Int cell, ElementKind kind) {
        if (IsInside(cell)) {
            _ground[cell.x, cell.y, cell.z] = kind;
        }
    }

    // Уровень, с которого клетка тоже доступна и видна (NoLink — только со своего).
    public int LinkOf(Vector3Int cell) {
        return IsInside(cell) ? _links[cell.x, cell.y, cell.z] : NoLink;
    }

    public bool IsLink(Vector3Int cell) {
        return LinkOf(cell) != NoLink;
    }

    // Видно с уровня layer: своя клетка уровня или переход, ведущий на него.
    public bool IsVisible(Vector3Int cell, int layer) {
        return Exists(cell) && (cell.z == layer || LinkOf(cell) == layer);
    }

    public void Set(Vector3Int cell, ElementKind kind, bool exists) {
        if (!IsInside(cell)) {
            return;
        }

        _kinds[cell.x, cell.y, cell.z] = kind;
        _exists[cell.x, cell.y, cell.z] = exists;
    }

    public void SetLink(Vector3Int cell, int layer) {
        if (IsInside(cell)) {
            _links[cell.x, cell.y, cell.z] = layer;
        }
    }

    // Клетка столбца (x, y), в которую можно шагнуть с уровня layer: своя клетка этого уровня,
    // а если её нет — переход, ведущий на него. Больше одной такой клетки в столбце быть не должно.
    public bool TryCell(int x, int y, int layer, out Vector3Int cell) {
        cell = new Vector3Int(x, y, layer);
        if (Exists(cell)) {
            return true;
        }

        for (int other = 0; other < Layers; other++) {
            Vector3Int candidate = new(x, y, other);
            if (Exists(candidate) && LinkOf(candidate) == layer) {
                cell = candidate;
                return true;
            }
        }

        return false;
    }

    // Соседняя клетка по сетке: сначала свой уровень, потом тот, на который ведёт переход под ногами.
    // Так с входа в пещеру шаг внутрь идёт по пещере, а шаг наружу выводит на поверхность.
    // Высоту здесь не смотрим: форма рельефа считается по соседям, даже если туда не шагнуть.
    public bool TryNeighbour(Vector3Int from, Vector2Int direction, out Vector3Int to) {
        int x = from.x + direction.x;
        int y = from.y + direction.y;
        if (TryCell(x, y, from.z, out to)) {
            return true;
        }

        int link = LinkOf(from);
        return link != NoLink && TryCell(x, y, link, out to);
    }

    // Шаг: сосед по сетке, на который пускает рельеф. Разница высот — стена обрыва,
    // пройти её можно только через склон или переход между уровнями.
    public bool TryStep(Vector3Int from, Vector2Int direction, out Vector3Int to) {
        return TryNeighbour(from, direction, out to) && CanCross(from, to);
    }

    public bool CanCross(Vector3Int from, Vector3Int to) {
        if (IsRampCell(from) || IsRampCell(to)) {
            return true;
        }

        return ElementRules.ElevationOf(FloorKindAt(from)) == ElementRules.ElevationOf(FloorKindAt(to));
    }

    private bool IsRampCell(Vector3Int cell) {
        return ElementRules.IsRamp(KindAt(cell)) || ElementRules.IsRamp(FloorKindAt(cell));
    }

    public IEnumerable<Vector3Int> Cells() {
        for (int layer = 0; layer < Layers; layer++) {
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    Vector3Int cell = new(x, y, layer);
                    if (_exists[x, y, layer]) {
                        yield return cell;
                    }
                }
            }
        }
    }

    // Ошибка карты: в столбец (x, y) с одного уровня ведут сразу две клетки — шаг стал бы неоднозначным.
    // Так бывает, если переход поставили туда, где на соседнем уровне клетка не стёрта.
    public List<Vector3Int> FindAmbiguous() {
        List<Vector3Int> result = new();
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                for (int layer = 0; layer < Layers; layer++) {
                    int reachable = 0;
                    for (int other = 0; other < Layers; other++) {
                        Vector3Int cell = new(x, y, other);
                        if (Exists(cell) && (other == layer || LinkOf(cell) == layer)) {
                            reachable++;
                        }
                    }

                    if (reachable > 1) {
                        result.Add(new Vector3Int(x, y, layer));
                    }
                }
            }
        }

        return result;
    }
}
