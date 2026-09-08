using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// WASD/стрелки шагают героем по клеткам. Пока герой стоит на троне, включается мышь:
// курсор подсвечивает клетку, свайп по элементу меняет его с соседом, как в три-в-ряд.
public class PlayerInput : MonoBehaviour {
    private const float SwipeThreshold = 0.35f; // в клетках

    [SerializeField]
    private WorldView _world;

    [SerializeField]
    private float _repeatDelay = 0.22f;

    [SerializeField]
    private float _repeatInterval = 0.1f;

    private Vector2Int _heldDirection;
    private float _nextRepeatTime;
    private bool _throneMode;
    private Vector2Int? _dragCell;
    private Vector3 _dragStart;

    private void Update() {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _world == null || _world.Model == null) {
            return;
        }

        WorldModel model = _world.Model;
        if (keyboard.escapeKey.wasPressedThisFrame && _world.Pause != null) {
            _world.Pause.Toggle();
        }

        if (_world.Pause != null && _world.Pause.IsPaused) {
            _world.Hud.Pointer.Visible = true;
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame && _world.Hud != null && _world.Hud.RestartAvailable) {
            _dragCell = null;
            _world.Hud.StartRestart();
            return;
        }

        if (!_world.IsScrolling) {
            Vector2Int direction = ReadStep(keyboard);
            if (direction != Vector2Int.zero && !_world.TryScrollToNeighbor(direction)) {
                model.TryMoveHero(direction);
            }
        }

        bool throne = model.IsHeroOnThrone;
        if (throne != _throneMode) {
            _throneMode = throne;
            _dragCell = null;
            if (!throne) {
                _world.Cursor.Hide();
            }
        }

        // Пиксельный курсор виден на троне и когда на финале появилась кнопка; системный скрыт всегда.
        if (_world.Hud != null) {
            _world.Hud.Pointer.Visible = throne || _world.Hud.WantsCursor;
        }

        if (throne) {
            UpdateMouse(model);
        }
    }

    private void UpdateMouse(WorldModel model) {
        Mouse mouse = Mouse.current;
        if (mouse == null) {
            return;
        }

        Vector3 worldPosition = _world.ScreenToWorld(mouse.position.ReadValue());
        Vector2Int cell = new(Mathf.RoundToInt(worldPosition.x), Mathf.RoundToInt(worldPosition.y));
        bool onScreen = model.IsInScreen(_world.Screen, cell);
        if (onScreen) {
            _world.Cursor.Show(cell);
        } else {
            _world.Cursor.Hide();
        }

        if (mouse.leftButton.wasPressedThisFrame) {
            WorldEntity entity = onScreen ? model.ObjectAt(cell) : null;
            if (entity != null && ElementRules.IsPushable(entity.Kind)) {
                _dragCell = cell;
                _dragStart = worldPosition;
            }

            return;
        }

        if (mouse.leftButton.wasReleasedThisFrame) {
            _dragCell = null;
            return;
        }

        if (!_dragCell.HasValue || !mouse.leftButton.isPressed) {
            return;
        }

        Vector3 delta = worldPosition - _dragStart;
        if (Mathf.Abs(delta.x) < SwipeThreshold && Mathf.Abs(delta.y) < SwipeThreshold) {
            return;
        }

        Vector2Int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? (delta.x > 0f ? Vector2Int.right : Vector2Int.left)
            : (delta.y > 0f ? Vector2Int.up : Vector2Int.down);
        Swipe(model, _dragCell.Value, direction);
        _dragCell = null; // одно движение — одна попытка, до следующего нажатия
    }

    private void Swipe(WorldModel model, Vector2Int from, Vector2Int direction) {
        Vector2Int to = from + direction;
        switch (model.Swap(from, to)) {
            case SwapResult.Swapped:
                break;
            case SwapResult.NoMatch:
                _world.Bump(from, direction);
                _world.Bump(to, -direction);
                break;
            default:
                _world.Bump(from, direction);
                break;
        }
    }

    // Нажатие — сразу шаг, удержание — повтор с задержкой.
    private Vector2Int ReadStep(Keyboard keyboard) {
        Vector2Int pressed = ReadDirection(keyboard, pressedThisFrame: true);
        if (pressed != Vector2Int.zero) {
            _heldDirection = pressed;
            _nextRepeatTime = Time.time + _repeatDelay;
            return pressed;
        }

        Vector2Int held = ReadDirection(keyboard, pressedThisFrame: false);
        if (held == Vector2Int.zero) {
            _heldDirection = Vector2Int.zero;
            return Vector2Int.zero;
        }

        if (held != _heldDirection) {
            _heldDirection = held;
            _nextRepeatTime = Time.time + _repeatDelay;
            return Vector2Int.zero;
        }

        if (Time.time < _nextRepeatTime) {
            return Vector2Int.zero;
        }

        _nextRepeatTime = Time.time + _repeatInterval;
        return held;
    }

    private static Vector2Int ReadDirection(Keyboard keyboard, bool pressedThisFrame) {
        if (IsActive(keyboard.wKey, keyboard.upArrowKey)) {
            return Vector2Int.up;
        }

        if (IsActive(keyboard.sKey, keyboard.downArrowKey)) {
            return Vector2Int.down;
        }

        if (IsActive(keyboard.aKey, keyboard.leftArrowKey)) {
            return Vector2Int.left;
        }

        if (IsActive(keyboard.dKey, keyboard.rightArrowKey)) {
            return Vector2Int.right;
        }

        return Vector2Int.zero;

        bool IsActive(KeyControl key, KeyControl alternative) {
            return pressedThisFrame
                ? key.wasPressedThisFrame || alternative.wasPressedThisFrame
                : key.isPressed || alternative.isPressed;
        }
    }
}
