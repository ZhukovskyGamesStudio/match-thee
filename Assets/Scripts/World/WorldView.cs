using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Строит мир из конфига, двигает вью вслед за моделью, перевозит камеру между экранами,
// показывает только тот уровень рельефа, на котором стоит герой, держит курсор трона и интерфейс.
public class WorldView : MonoBehaviour {
    // Между слоями оставлен запас: предметы внутри своего слоя ещё разбираются по строкам карты.
    private const int GroundOrder = 0;
    private const int FloorOrder = 1000;
    private const int DecorOrder = 2000;
    private const int ObjectOrder = 10000;
    private const int HeroOrder = 20000;
    private const float ScrollDuration = 0.35f;

    [SerializeField]
    private WorldConfig _world;

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private Camera _camera;

    private readonly Dictionary<WorldEntity, ElementView> _views = new();
    private readonly Dictionary<ElementView, int> _oneLayer = new(); // вью, видимое только с одного уровня
    private Vector2Int _screen;
    private int _layer;
    private Vector3 _scrollFrom;
    private Vector3 _scrollTo;
    private float _scrollProgress = 1f;
    private int _lastWidth;
    private int _lastHeight;
    private bool _entryPending; // первый вход в комнату записываем кадром позже, когда ход целиком разрешился
    private int _vanishFrame = -1;  // кадр и задержка последнего исчезновения: возврат героя ждёт его конца
    private float _vanishDelay;

    public WorldModel Model { get; private set; }
    public CursorView Cursor { get; private set; }
    public HudView Hud { get; private set; }
    public SoundView Sound { get; private set; }
    public PauseView Pause { get; private set; }
    public Vector2Int Screen => _screen;
    public int Layer => _layer;
    public bool IsScrolling => _scrollProgress < 1f;

    private void Awake() {
        WorldGrid grid = _world.BuildGrid();
        int heroes = grid.Cells().Count(cell => grid.KindAt(cell) == ElementKind.Hero);
        if (heroes != 1) {
            Debug.LogWarning($"Match Thee: на карте {heroes} героев, нужен ровно один — управляется последний по порядку чтения карты");
        }

        List<Vector3Int> ambiguous = grid.FindAmbiguous();
        if (ambiguous.Count > 0) {
            Debug.LogWarning($"Match Thee: в {ambiguous.Count} клетках с одного уровня видны сразу две — переход поставлен там, где на соседнем уровне клетка не стёрта: {string.Join(", ", ambiguous.Take(8))}");
        }

        Model = new WorldModel(grid, _world.ScreenWidth, _world.ScreenHeight);
        Sound = gameObject.AddComponent<SoundView>();
        Sound.Init(Model);
        Model.EntityMoved += OnEntityMoved;
        Model.EntitiesMatched += OnEntitiesMatched;
        Model.HeroFormChanged += OnHeroFormChanged;
        Model.GameWon += OnGameWon;
        Model.Restored += OnRestored;

        foreach (WorldEntity ground in Model.Grounds) {
            Spawn(ground, GroundOrder);
        }

        foreach (WorldEntity floor in Model.Floors) {
            Spawn(floor, ElementRules.IsTall(floor.Kind) ? DecorOrder : FloorOrder);
        }

        SpawnTerrainDecor();

        foreach (WorldEntity entity in Model.Objects) {
            Spawn(entity, entity.Kind == ElementKind.Hero ? HeroOrder : ObjectOrder);
        }

        CreateCursor();
        CreateHud();
        SetupCamera();
        RefreshVisibility();
        UnityEngine.Cursor.visible = false;
        _entryPending = true;
    }

    private void OnDestroy() {
        if (Model != null) {
            Model.EntityMoved -= OnEntityMoved;
            Model.EntitiesMatched -= OnEntitiesMatched;
            Model.HeroFormChanged -= OnHeroFormChanged;
            Model.GameWon -= OnGameWon;
            Model.Restored -= OnRestored;
        }

    }

    private void Update() {
        if (UnityEngine.Screen.width != _lastWidth || UnityEngine.Screen.height != _lastHeight) {
            FitCamera();
        }

        if (_entryPending) {
            _entryPending = false;
            Model.RecordRoomEntry();
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
    // Шаг, меняющий уровень (вход в пещеру, лестница), сюда не попадает: герой должен пройти его сам.
    public bool TryScrollToNeighbor(Vector2Int direction) {
        if (Model.Hero == null || !Model.Grid.TryStep(Model.Hero.Position, direction, out Vector3Int target)) {
            return false;
        }

        if (target.z != Model.Hero.Position.z || Model.IsInScreen(_screen, target)) {
            return false;
        }

        Vector2Int screen = Model.ScreenContaining(target, _screen);
        if (screen == _screen) {
            return false;
        }

        EnterRoom(screen, _layer);
        return true;
    }

    // Неудачное смещение: элемент в клетке дёргается в сторону цели.
    public void Bump(Vector3Int cell, Vector2Int direction) {
        WorldEntity entity = Model.ObjectAt(cell);
        if (entity != null && _views.TryGetValue(entity, out ElementView view)) {
            view.Bump(direction);
            Sound.Play("bump");
        }
    }

    private ElementView Spawn(WorldEntity entity, int sortingOrder, string spriteName = null) {
        GameObject viewObject = new(entity.Kind.ToString(), typeof(SpriteRenderer), typeof(ElementView));
        viewObject.transform.SetParent(transform, false);

        ElementView view = viewObject.GetComponent<ElementView>();
        view.Init(entity, _elements, sortingOrder, spriteName ?? TerrainTiles.NameOf(Model.Grid, entity.Kind, entity.Position));
        _views[entity] = view;
        return view;
    }

    // Борт возвышенности: его не рисуют на карте, форма берётся из соседей.
    // Автору достаточно покрасить саму возвышенность — обрыв, углы и тень вырастают сами.
    private void SpawnTerrainDecor() {
        foreach (Vector3Int cell in Model.Grid.Cells()) {
            SpawnDecor(ElementKind.Ledge, cell, TerrainTiles.LedgeNameOf(Model.Grid, cell));
            SpawnPassageSides(cell);
        }
    }

    private void SpawnDecor(ElementKind kind, Vector3Int cell, string spriteName) {
        if (spriteName != null) {
            Spawn(new WorldEntity(kind, cell), DecorOrder, spriteName);
        }
    }

    // Переход виден с двух уровней, и выглядит с них по-разному: снаружи это дверь в скале,
    // изнутри — проём в стене пещеры. Кладём два вью и включаем то, что отвечает уровню героя.
    private void SpawnPassageSides(Vector3Int cell) {
        ElementKind kind = Model.Grid.KindAt(cell);
        if (!ElementRules.IsPassage(kind) || Model.Grid.LinkOf(cell) == WorldGrid.NoLink) {
            return;
        }

        WorldEntity floor = Model.FloorAt(cell);
        if (floor != null && _views.TryGetValue(floor, out ElementView outside)) {
            _oneLayer[outside] = Model.Grid.LinkOf(cell); // тайл вида имени — взгляд со связанного уровня
        }

        ElementView inside = Spawn(new WorldEntity(kind, cell), DecorOrder, $"{kind.ToString().ToLowerInvariant()}_in");
        _oneLayer[inside] = cell.z;
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
        Pause = PauseView.Create();
    }

    private void OnEntityMoved(WorldEntity entity) {
        // Комнату меняем до вью: иначе клетка героя на новом уровне считается невидимой,
        // вью на миг прячется и теряет проезд — шаг во вход в пещеру дёргался бы вместо шага.
        if (entity == Model.Hero) {
            Vector2Int screen = Model.ScreenContaining(entity.Position, _screen);
            if (screen != _screen || entity.Position.z != _layer) {
                EnterRoom(screen, entity.Position.z);
            }
        }

        if (_views.TryGetValue(entity, out ElementView view)) {
            view.MoveTo(entity.Position);
            SetVisible(entity, view);
        }
    }

    // Переход в комнату: экран и уровень. Камера едет только если сменился экран,
    // а видимость перестраивается, только если сменился уровень.
    private void EnterRoom(Vector2Int screen, int layer) {
        bool scrolled = screen != _screen;
        bool dug = layer != _layer;
        _screen = screen;
        _layer = layer;
        Model.SetRoom(screen, layer);
        if (scrolled) {
            _scrollFrom = _camera.transform.position;
            _scrollTo = CameraPosition(screen);
            _scrollProgress = 0f;
        }

        if (dug) {
            RefreshVisibility();
        }

        _entryPending = true; // вход в комнату: если первый — запоминаем клетку входа
        if (screen == Vector2Int.zero) {
            Hud.RevealRestart(); // левая нижняя комната открывает рестарт
        }
    }

    // На виду только клетки уровня героя и переходы, ведущие на него: пещеру снаружи не видно,
    // а изнутри не видно поверхности. Спрятанное вью не тикает — доводим его смещение сразу.
    private void RefreshVisibility() {
        foreach (KeyValuePair<WorldEntity, ElementView> pair in _views) {
            SetVisible(pair.Key, pair.Value);
        }
    }

    private void SetVisible(WorldEntity entity, ElementView view) {
        bool visible = Model.IsVisible(entity.Position)
            && (!_oneLayer.TryGetValue(view, out int layer) || layer == Model.CurrentLayer);
        if (view.gameObject.activeSelf == visible) {
            return;
        }

        if (!visible) {
            view.Snap();
        }

        view.gameObject.SetActive(visible);
    }

    // R: комната в первозданный вид, герой на клетку первого входа.
    public void RestoreRoom() {
        Model.RestartRoom();
    }

    // Комната перезапущена: вью предметов пересобираем с нуля.
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

        RefreshVisibility();
        Cursor.Hide();
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
    // Ряд, сложившийся на невидимом уровне (элемент затолкали в пещеру), убирается без анимации.
    private void OnEntitiesMatched(IReadOnlyList<WorldEntity> matched) {
        float delay = 0f;
        foreach (WorldEntity entity in matched) {
            if (_views.TryGetValue(entity, out ElementView view) && view.gameObject.activeSelf) {
                delay = Mathf.Max(delay, view.RemainingShiftTime);
            }
        }

        if (Model.Hero != null && _views.TryGetValue(Model.Hero, out ElementView heroView)) {
            delay = Mathf.Max(delay, heroView.RemainingFormPopTime);
        }

        bool seen = false;
        foreach (WorldEntity entity in matched) {
            if (!_views.Remove(entity, out ElementView view)) {
                continue;
            }

            if (view.gameObject.activeSelf) {
                view.Vanish(delay);
                seen = true;
            } else {
                Destroy(view.gameObject);
            }
        }

        if (seen) {
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
        _layer = Model.Hero?.Position.z ?? 0;
        _camera.transform.position = CameraPosition(_screen);
        Model.SetRoom(_screen, _layer);
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
