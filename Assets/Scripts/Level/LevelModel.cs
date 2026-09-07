using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Состояние уровня без Unity-объектов: сетка, предметы, герой и правила хода.
public class LevelModel {
    public int Width { get; }
    public int Height { get; }
    public LevelEntity Hero { get; private set; }
    public IReadOnlyList<LevelEntity> Floors => _floors;
    public IEnumerable<LevelEntity> Objects => _objects.Values;

    public event Action<LevelEntity> EntityMoved;

    private readonly List<LevelEntity> _floors = new();
    private readonly Dictionary<Vector2Int, LevelEntity> _objects = new();

    public LevelModel(string[] rows, Func<char, ElementKind> kindOf) {
        Height = rows.Length;
        Width = rows.Length > 0 ? rows.Max(row => row.Length) : 0;

        for (int row = 0; row < Height; row++) {
            for (int x = 0; x < rows[row].Length; x++) {
                ElementKind kind = kindOf(rows[row][x]);
                if (kind == ElementKind.None) {
                    continue;
                }

                LevelEntity entity = new(kind, new Vector2Int(x, Height - 1 - row));
                if (ElementRules.IsFloor(kind)) {
                    _floors.Add(entity);
                    continue;
                }

                _objects[entity.Position] = entity;
                if (kind == ElementKind.Hero) {
                    Hero = entity;
                }
            }
        }
    }

    public bool IsInside(Vector2Int cell) {
        return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
    }

    public LevelEntity ObjectAt(Vector2Int cell) {
        return _objects.TryGetValue(cell, out LevelEntity entity) ? entity : null;
    }

    // Герой шагает на соседнюю клетку. Если там толкаемый предмет — толкает его на клетку дальше.
    // Толкнуть можно ровно один предмет: если за ним стоит ещё что-то (предмет, стена, край) — хода нет.
    public bool TryMoveHero(Vector2Int direction) {
        if (Hero == null || !CanEnter(Hero.Position + direction)) {
            return false;
        }

        Vector2Int target = Hero.Position + direction;
        LevelEntity blocker = ObjectAt(target);
        if (blocker != null) {
            if (!ElementRules.IsPushable(blocker.Kind)) {
                return false;
            }

            Vector2Int beyond = target + direction;
            if (!CanEnter(beyond) || ObjectAt(beyond) != null) {
                return false;
            }

            Move(blocker, beyond);
        }

        Move(Hero, target);
        return true;
    }

    private bool CanEnter(Vector2Int cell) {
        if (!IsInside(cell)) {
            return false;
        }

        LevelEntity occupant = ObjectAt(cell);
        return occupant == null || !ElementRules.IsSolid(occupant.Kind);
    }

    private void Move(LevelEntity entity, Vector2Int to) {
        _objects.Remove(entity.Position);
        entity.Position = to;
        _objects[to] = entity;
        EntityMoved?.Invoke(entity);
    }
}
