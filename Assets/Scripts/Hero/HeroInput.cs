using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Шаг по клетке на WASD и стрелки: нажатие — сразу шаг, удержание — повтор с задержкой.
public class HeroInput : MonoBehaviour {
    [SerializeField]
    private LevelView _level;

    [SerializeField]
    private float _repeatDelay = 0.22f;

    [SerializeField]
    private float _repeatInterval = 0.1f;

    private Vector2Int _heldDirection;
    private float _nextRepeatTime;

    private void Update() {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _level == null || _level.Model == null) {
            return;
        }

        Vector2Int pressed = ReadDirection(keyboard, pressedThisFrame: true);
        if (pressed != Vector2Int.zero) {
            Step(pressed);
            _heldDirection = pressed;
            _nextRepeatTime = Time.time + _repeatDelay;
            return;
        }

        Vector2Int held = ReadDirection(keyboard, pressedThisFrame: false);
        if (held == Vector2Int.zero) {
            _heldDirection = Vector2Int.zero;
            return;
        }

        if (held != _heldDirection) {
            _heldDirection = held;
            _nextRepeatTime = Time.time + _repeatDelay;
            return;
        }

        if (Time.time >= _nextRepeatTime) {
            Step(held);
            _nextRepeatTime = Time.time + _repeatInterval;
        }
    }

    private void Step(Vector2Int direction) {
        _level.Model.TryMoveHero(direction);
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
