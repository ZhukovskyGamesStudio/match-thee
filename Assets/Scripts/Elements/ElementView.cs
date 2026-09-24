using UnityEngine;

// Вью элемента: дрожание кадров и три анимации — смещение, неудачное смещение, уничтожение.
[RequireComponent(typeof(SpriteRenderer))]
public class ElementView : MonoBehaviour {
    private const float WobbleFps = 5f;
    private const float ShiftDuration = 0.12f; // на одну клетку
    private const float BumpDuration = 0.22f;
    private const float BumpDistance = 0.3f;
    public const float VanishDuration = 0.3f;
    private const float VanishPopScale = 1.3f;
    private const int RowStep = 10;     // шаг порядка между строками: ближняя строка рисуется поверх дальней
    private const int VanishLift = 5000; // исчезающий элемент всплывает над соседями
    private const float Pixel = 1f / 24f;
    private const float FormPopDuration = 0.35f;
    private const float FormPopScale = 1.3f;

    private SpriteRenderer _renderer;
    private SpriteRenderer _crown; // только у героя: корона поверх формы превращения
    private ElementsConfig _elements;
    private Sprite[] _frames;
    private Sprite[] _crownFrames;
    private string _spriteName; // обычно имя вида, у рельефа — вариант тайла по соседям
    private int _order; // порядок слоя без поправки на строку
    private ElementKind _form = ElementKind.None;
    private float _formPopProgress = 1f;
    private ElementKind _pendingForm;
    private float _pendingFormDelay = -1f; // >= 0 — смена формы отложена
    private Vector3 _shiftFrom;
    private Vector3 _shiftTo;
    private float _shiftDuration = ShiftDuration;
    private float _shiftProgress = 1f;
    private Vector3 _bumpDirection;
    private float _bumpProgress = 1f;
    private float _vanishDelay = -1f;    // >= 0 — уничтожение назначено
    private float _vanishProgress = -1f; // >= 0 — уничтожение идёт

    public WorldEntity Entity { get; private set; }
    public bool IsShifting => _shiftProgress < 1f;
    public float RemainingShiftTime => IsShifting ? (1f - _shiftProgress) * _shiftDuration : 0f;
    public float RemainingFormPopTime => _formPopProgress < 1f ? (1f - _formPopProgress) * FormPopDuration : 0f;

    public void Init(WorldEntity entity, ElementsConfig elements, int sortingOrder, string spriteName = null) {
        Entity = entity;
        _elements = elements;
        _renderer = GetComponent<SpriteRenderer>();
        _order = sortingOrder;
        _spriteName = string.IsNullOrEmpty(spriteName) ? Name(entity.Kind) : spriteName;
        _frames = LoadFrames(_spriteName);
        if (_frames[0] == null) {
            _spriteName = Name(entity.Kind); // варианта нет — показываем обычный вид
            _frames = LoadFrames(_spriteName);
        }

        if (entity.Kind == ElementKind.Hero) {
            GameObject crownObject = new("Crown", typeof(SpriteRenderer));
            crownObject.transform.SetParent(transform, false);
            _crown = crownObject.GetComponent<SpriteRenderer>();
            _crown.enabled = false;
            _crownFrames = LoadFrames("crown");
        }

        ApplyOrder(entity.Position);
        _shiftFrom = _shiftTo = Place(entity.Position);
        transform.position = _shiftTo;
        _renderer.sprite = _frames[0]; // кадр сразу: спрятанное вью не тикает, а показаться должно уже готовым
    }

    // Смена формы с задержкой: герой остаётся предметом, пока соседи по ряду исчезают.
    public void SetForm(ElementKind form, float delay) {
        if (delay <= 0f) {
            SetForm(form);
            return;
        }

        _pendingForm = form;
        _pendingFormDelay = delay;
    }

    // Превращение героя: кадры формы (или свои, если None) и корона сверху; коротко раздувается.
    public void SetForm(ElementKind form) {
        _pendingFormDelay = -1f;
        _form = form;
        _frames = LoadFrames(form == ElementKind.None ? _spriteName : Name(form));
        if (_crown != null) {
            _crown.enabled = form != ElementKind.None;
        }

        _formPopProgress = 0f;
    }

    private static string Name(ElementKind kind) {
        return kind.ToString().ToLowerInvariant();
    }

    private Sprite[] LoadFrames(string name) {
        Sprite[] frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = _elements.GetFrame(name, i);
        }

        return frames;
    }

    // Смещение: плавный проезд до новой клетки (толчок героем или обмен с трона).
    public void MoveTo(Vector3Int cell) {
        Vector3 next = Place(cell);
        ApplyOrder(cell);
        if (_form == ElementKind.None && ElementRules.IsCreature(Entity.Kind) && !Mathf.Approximately(next.x, _shiftTo.x)) {
            _renderer.flipX = next.x < _shiftTo.x;
        }

        _shiftFrom = BasePosition();
        _shiftTo = next;
        _shiftDuration = ShiftDuration * Mathf.Max(1f, Vector3.Distance(_shiftFrom, _shiftTo));
        _shiftProgress = 0f;
    }

    // Клетка ушла с видимого уровня: вью прячут, а спрятанное не тикает — доезжаем сразу.
    public void Snap() {
        _shiftFrom = _shiftTo;
        _shiftProgress = 1f;
        _bumpProgress = 1f;
        transform.position = _shiftTo;
    }

    // Неудачное смещение: элемент дёргается к цели и возвращается.
    public void Bump(Vector2Int direction) {
        _bumpDirection = new Vector3(direction.x, direction.y, 0f) * BumpDistance;
        _bumpProgress = 0f;
    }

    // Уничтожение: после задержки (когда весь ряд доехал) подпрыгивает, сжимается в точку и тает.
    public void Vanish(float delay) {
        _vanishDelay = Mathf.Max(0f, delay);
        _renderer.sortingOrder += VanishLift;
    }

    // Предметы крупнее клетки залезают на соседние, поэтому порядок между ними задаём строкой:
    // кто ниже — тот ближе к камере. Внутри строки чередуем по столбцу, чтобы у соседей
    // порядок не совпадал: при равном порядке Unity рисует их как придётся.
    private void ApplyOrder(Vector3Int cell) {
        _renderer.sortingOrder = ElementRules.IsFloor(Entity.Kind)
            ? _order
            : _order - cell.y * RowStep - (cell.x & 1);
        if (_crown != null) {
            _crown.sortingOrder = _renderer.sortingOrder + 1;
        }
    }

    // Дерево стоит в клетке не по линейке. Сдвиг считается от координат клетки: при перерисовке
    // дерево остаётся на месте, а после толчка встаёт по-новому — уже по новой клетке.
    private Vector3 Place(Vector3Int cell) {
        if (!ElementRules.IsJittered(Entity.Kind)) {
            return ToWorld(cell);
        }

        int hash = ((cell.x * 73856093) ^ (cell.y * 19349663) ^ ((cell.z + 1) * 83492791)) & 0x7FFFFFFF;
        return ToWorld(cell) + new Vector3((hash % 5 - 2) * Pixel, (hash / 5 % 3 - 1) * Pixel, 0f);
    }

    private void Update() {
        // Дрожание как в Baba Is You: все элементы переключают кадры синхронно.
        int frame = (int)(Time.time * WobbleFps) % _frames.Length;
        _renderer.sprite = _frames[frame];
        if (_crown != null && _crown.enabled) {
            _crown.sprite = _crownFrames[frame];
        }

        if (_pendingFormDelay >= 0f) {
            _pendingFormDelay -= Time.deltaTime;
            if (_pendingFormDelay < 0f) {
                SetForm(_pendingForm);
            }
        }

        if (_formPopProgress < 1f) {
            _formPopProgress = Mathf.Min(1f, _formPopProgress + Time.deltaTime / FormPopDuration);
            transform.localScale = Vector3.one * (1f + (FormPopScale - 1f) * Mathf.Sin(_formPopProgress * Mathf.PI));
        }

        if (_vanishProgress >= 0f) {
            UpdateVanish();
            return;
        }

        if (IsShifting) {
            _shiftProgress = Mathf.Min(1f, _shiftProgress + Time.deltaTime / _shiftDuration);
        }

        Vector3 bump = Vector3.zero;
        if (_bumpProgress < 1f) {
            _bumpProgress = Mathf.Min(1f, _bumpProgress + Time.deltaTime / BumpDuration);
            bump = _bumpDirection * Mathf.Sin(_bumpProgress * Mathf.PI);
        }

        transform.position = BasePosition() + bump;

        if (_vanishDelay >= 0f) {
            _vanishDelay -= Time.deltaTime;
            if (_vanishDelay < 0f && !IsShifting) {
                _vanishProgress = 0f;
            }
        }
    }

    private Vector3 BasePosition() {
        return Vector3.Lerp(_shiftFrom, _shiftTo, Mathf.SmoothStep(0f, 1f, _shiftProgress));
    }

    private void UpdateVanish() {
        _vanishProgress += Time.deltaTime / VanishDuration;
        float t = Mathf.Clamp01(_vanishProgress);
        float scale = t < 0.3f
            ? Mathf.Lerp(1f, VanishPopScale, t / 0.3f)
            : Mathf.Lerp(VanishPopScale, 0f, (t - 0.3f) / 0.7f);
        transform.localScale = Vector3.one * scale;

        Color color = _renderer.color;
        color.a = 1f - t * t;
        _renderer.color = color;

        if (t >= 1f) {
            Destroy(gameObject);
        }
    }

    // Уровень (z клетки) на положение не влияет: уровни лежат один поверх другого, виден всегда один.
    public static Vector3 ToWorld(Vector3Int cell) {
        return new Vector3(cell.x, cell.y, 0f);
    }
}
