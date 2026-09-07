using System.Collections.Generic;
using UnityEngine;

// Строит мир из конфига, двигает вью вслед за моделью, перевозит камеру между экранами,
// держит курсор трона и интерфейс.
public class WorldView : MonoBehaviour {
    private const int FloorOrder = 0;
    private const int ObjectOrder = 10;
    private const int HeroOrder = 20;
    private const float ScrollDuration = 0.35f;

    [SerializeField]
    private WorldConfig _world;

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private Camera _camera;

    private readonly Dictionary<WorldEntity, ElementView> _views = new();
    private Vector2Int _screen;
    private Vector3 _scrollFrom;
    private Vector3 _scrollTo;
    private float _scrollProgress = 1f;
    private int _lastWidth;
    private int _lastHeight;

    public WorldModel Model { get; private set; }
    public CursorView Cursor { get; private set; }
    public Vector2Int Screen => _screen;
    public bool IsScrolling => _scrollProgress < 1f;

    private void Awake() {
        Model = new WorldModel(_world.BuildCells(), _world.ScreenWidth, _world.ScreenHeight);
        Model.EntityMoved += OnEntityMoved;
        Model.EntitiesMatched += OnEntitiesMatched;

        foreach (WorldEntity floor in Model.Floors) {
            Spawn(floor, FloorOrder);
        }

        foreach (WorldEntity entity in Model.Objects) {
            Spawn(entity, entity.Kind == ElementKind.Hero ? HeroOrder : ObjectOrder);
        }

        CreateCursor();
        CreateHud();
        SetupCamera();
        UnityEngine.Cursor.visible = false;
    }

    private void OnDestroy() {
        if (Model != null) {
            Model.EntityMoved -= OnEntityMoved;
            Model.EntitiesMatched -= OnEntitiesMatched;
        }

        UnityEngine.Cursor.visible = true;
    }

    private void Update() {
        if (UnityEngine.Screen.width != _lastWidth || UnityEngine.Screen.height != _lastHeight) {
            FitCamera();
        }

        if (!IsScrolling) {
            return;
        }

        _scrollProgress = Mathf.Min(1f, _scrollProgress + Time.deltaTime / ScrollDuration);
        _camera.transform.position = Vector3.Lerp(_scrollFrom, _scrollTo, Mathf.SmoothStep(0f, 1f, _scrollProgress));
    }

    public Vector3 ScreenToWorld(Vector2 screenPosition) {
        Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -_camera.transform.position.z));
        world.z = 0f;
        return world;
    }

    // Неудачное смещение: элемент в клетке дёргается в сторону цели.
    public void Bump(Vector2Int cell, Vector2Int direction) {
        WorldEntity entity = Model.ObjectAt(cell);
        if (entity != null && _views.TryGetValue(entity, out ElementView view)) {
            view.Bump(direction);
        }
    }

    private void Spawn(WorldEntity entity, int sortingOrder) {
        GameObject viewObject = new(entity.Kind.ToString(), typeof(SpriteRenderer), typeof(ElementView));
        viewObject.transform.SetParent(transform, false);

        ElementView view = viewObject.GetComponent<ElementView>();
        view.Init(entity, _elements, sortingOrder);
        _views[entity] = view;
    }

    private void CreateCursor() {
        GameObject cursorObject = new("Cursor", typeof(SpriteRenderer), typeof(CursorView));
        cursorObject.transform.SetParent(transform, false);
        Cursor = cursorObject.GetComponent<CursorView>();
        Cursor.Init(_elements);
    }

    private void CreateHud() {
        GameObject hudObject = new("Hud", typeof(RectTransform));
        hudObject.AddComponent<HudView>().Init(_elements, (float)Model.ScreenWidth / Model.ScreenHeight);
    }

    private void OnEntityMoved(WorldEntity entity) {
        if (_views.TryGetValue(entity, out ElementView view)) {
            view.MoveTo(entity.Position);
        }

        if (entity != Model.Hero) {
            return;
        }

        // Герой ушёл за край экрана — камера переезжает на соседний экран, мир бесшовный.
        Vector2Int screen = Model.ScreenContaining(entity.Position, _screen);
        if (screen != _screen) {
            _screen = screen;
            _scrollFrom = _camera.transform.position;
            _scrollTo = CameraPosition(screen);
            _scrollProgress = 0f;
        }
    }

    // Уничтожение начинается, когда все элементы ряда доехали до своих клеток.
    private void OnEntitiesMatched(IReadOnlyList<WorldEntity> matched) {
        float delay = 0f;
        foreach (WorldEntity entity in matched) {
            if (_views.TryGetValue(entity, out ElementView view)) {
                delay = Mathf.Max(delay, view.RemainingShiftTime);
            }
        }

        foreach (WorldEntity entity in matched) {
            if (_views.Remove(entity, out ElementView view)) {
                view.Vanish(delay);
            }
        }
    }

    private void SetupCamera() {
        if (_camera == null) {
            _camera = Camera.main;
        }

        if (_camera == null) {
            return;
        }

        _camera.orthographic = true;
        _screen = Model.Hero != null ? Model.ScreenContaining(Model.Hero.Position, Vector2Int.zero) : Vector2Int.zero;
        _camera.transform.position = CameraPosition(_screen);
        FitCamera();
    }

    // Экран целиком занимает внутренний прямоугольник рамки; вокруг остаются поля под интерфейс.
    private void FitCamera() {
        _lastWidth = UnityEngine.Screen.width;
        _lastHeight = UnityEngine.Screen.height;
        if (_camera == null) {
            return;
        }

        Rect inner = ScreenFrame.InnerRect(_lastWidth, _lastHeight, (float)Model.ScreenWidth / Model.ScreenHeight);
        _camera.orthographicSize = Model.ScreenHeight / 2f * (_lastHeight / inner.height);
    }

    private Vector3 CameraPosition(Vector2Int screen) {
        Vector2 center = Model.ScreenCenter(screen);
        return new Vector3(center.x, center.y, -10f);
    }
}
