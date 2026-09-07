using System.Collections.Generic;
using System.Linq;
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
    private bool _snapshotPending; // снимок берём кадром позже входа, когда ход целиком разрешился
    private int _vanishFrame = -1;  // кадр и задержка последнего исчезновения: возврат героя ждёт его конца
    private float _vanishDelay;
    private Vector2Int _snapshotScreen;

    public WorldModel Model { get; private set; }
    public CursorView Cursor { get; private set; }
    public HudView Hud { get; private set; }
    public SoundView Sound { get; private set; }
    public Vector2Int Screen => _screen;
    public bool IsScrolling => _scrollProgress < 1f;

    private void Awake() {
        ElementKind[,] cells = _world.BuildCells();
        int heroes = cells.Cast<ElementKind>().Count(kind => kind == ElementKind.Hero);
        if (heroes != 1) {
            Debug.LogWarning($"Match Thee: на карте {heroes} героев, нужен ровно один — управляется последний по порядку чтения карты");
        }

        Model = new WorldModel(cells, _world.ScreenWidth, _world.ScreenHeight);
        Sound = gameObject.AddComponent<SoundView>();
        Sound.Init(Model);
        Model.EntityMoved += OnEntityMoved;
        Model.EntitiesMatched += OnEntitiesMatched;
        Model.HeroFormChanged += OnHeroFormChanged;
        Model.GameWon += OnGameWon;
        Model.Restored += OnRestored;

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
        _snapshotPending = true;
    }

    private void OnDestroy() {
        if (Model != null) {
            Model.EntityMoved -= OnEntityMoved;
            Model.EntitiesMatched -= OnEntitiesMatched;
            Model.HeroFormChanged -= OnHeroFormChanged;
            Model.GameWon -= OnGameWon;
            Model.Restored -= OnRestored;
        }

        UnityEngine.Cursor.visible = true;
    }

    private void Update() {
        if (UnityEngine.Screen.width != _lastWidth || UnityEngine.Screen.height != _lastHeight) {
            FitCamera();
        }

        if (_snapshotPending) {
            _snapshotPending = false;
            Model.SaveSnapshot();
            _snapshotScreen = _screen;
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

    // Герой стоит на общей крайней клетке и шагает наружу текущего экрана: камера переезжает
    // на соседний экран, а герой остаётся на месте. Следующий шаг уже обычный.
    public bool TryScrollToNeighbor(Vector2Int direction) {
        if (Model.Hero == null) {
            return false;
        }

        Vector2Int target = Model.Hero.Position + direction;
        if (!Model.IsInside(target) || Model.IsInScreen(_screen, target)) {
            return false;
        }

        Vector2Int screen = Model.ScreenContaining(target, _screen);
        if (screen == _screen) {
            return false;
        }

        ScrollTo(screen);
        return true;
    }

    // Неудачное смещение: элемент в клетке дёргается в сторону цели.
    public void Bump(Vector2Int cell, Vector2Int direction) {
        WorldEntity entity = Model.ObjectAt(cell);
        if (entity != null && _views.TryGetValue(entity, out ElementView view)) {
            view.Bump(direction);
            Sound.Play("bump");
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
        Hud = hudObject.AddComponent<HudView>();
        Hud.Init(_elements, (float)Model.ScreenWidth / Model.ScreenHeight, Model, RestoreRoom);
    }

    private void OnEntityMoved(WorldEntity entity) {
        if (_views.TryGetValue(entity, out ElementView view)) {
            view.MoveTo(entity.Position);
        }

        if (entity != Model.Hero) {
            return;
        }

        // Герой оказался вне экрана — камера переезжает на экран с ним, мир бесшовный.
        Vector2Int screen = Model.ScreenContaining(entity.Position, _screen);
        if (screen != _screen) {
            ScrollTo(screen);
        }
    }

    private void ScrollTo(Vector2Int screen) {
        _screen = screen;
        _scrollFrom = _camera.transform.position;
        _scrollTo = CameraPosition(screen);
        _scrollProgress = 0f;
        _snapshotPending = true; // вход в комнату: сохраняемся
        if (screen == Vector2Int.zero) {
            Hud.RevealRestart(); // левая нижняя комната открывает рестарт
        }
    }

    // R: назад к моменту входа в комнату.
    public void RestoreRoom() {
        Model.Restore();
    }

    // Модель откатилась: вью предметов пересобираем с нуля, камера сразу на экран входа.
    private void OnRestored() {
        foreach (KeyValuePair<WorldEntity, ElementView> pair in _views.ToList()) {
            if (!ElementRules.IsFloor(pair.Key.Kind)) {
                Destroy(pair.Value.gameObject);
                _views.Remove(pair.Key);
            }
        }

        foreach (WorldEntity entity in Model.Objects) {
            Spawn(entity, entity.Kind == ElementKind.Hero ? HeroOrder : ObjectOrder);
        }

        if (Model.Hero != null && Model.IsHeroTransformed && _views.TryGetValue(Model.Hero, out ElementView heroView)) {
            heroView.SetForm(Model.HeroForm);
        }

        Cursor.Hide();
        _screen = _snapshotScreen;
        _scrollProgress = 1f;
        _camera.transform.position = CameraPosition(_screen);
    }

    private void OnGameWon() {
        Hud.ShowWin();
    }

    // Возврат в себя после ряда с героем: ждём, пока соседи исчезнут. Обычная смена формы — сразу.
    private void OnHeroFormChanged(ElementKind form) {
        float delay = form == ElementKind.None && _vanishFrame == Time.frameCount ? _vanishDelay + ElementView.VanishDuration : 0f;
        if (Model.Hero != null && _views.TryGetValue(Model.Hero, out ElementView view)) {
            view.SetForm(form, delay);
        }

        Sound.PlayDelayed(form == ElementKind.None ? "untransform" : "transform", delay);
    }

    // Уничтожение начинается, когда все элементы ряда доехали до своих клеток,
    // а если ряд сложился превращением — когда герой закончил превращаться.
    private void OnEntitiesMatched(IReadOnlyList<WorldEntity> matched) {
        float delay = 0f;
        foreach (WorldEntity entity in matched) {
            if (_views.TryGetValue(entity, out ElementView view)) {
                delay = Mathf.Max(delay, view.RemainingShiftTime);
            }
        }

        if (Model.Hero != null && _views.TryGetValue(Model.Hero, out ElementView heroView)) {
            delay = Mathf.Max(delay, heroView.RemainingFormPopTime);
        }

        foreach (WorldEntity entity in matched) {
            if (_views.Remove(entity, out ElementView view)) {
                view.Vanish(delay);
            }
        }

        if (matched.Count > 0) {
            _vanishFrame = Time.frameCount;
            _vanishDelay = delay;
            Sound.PlayDelayed("vanish", delay);
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
        if (_screen == Vector2Int.zero) {
            Hud.RevealRestart();
        }
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
