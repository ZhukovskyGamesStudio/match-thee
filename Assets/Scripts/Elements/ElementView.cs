using UnityEngine;

// Вью элемента: дрожание кадров и три анимации — смещение, неудачное смещение, уничтожение.
[RequireComponent(typeof(SpriteRenderer))]
public class ElementView : MonoBehaviour {
    private const float WobbleFps = 5f;
    private const float ShiftDuration = 0.12f; // на одну клетку
    private const float BumpDuration = 0.22f;
    private const float BumpDistance = 0.3f;
    private const float VanishDuration = 0.3f;
    private const float VanishPopScale = 1.3f;
    private const float FormPopDuration = 0.2f;
    private const float FormPopScale = 1.3f;

    private SpriteRenderer _renderer;
    private SpriteRenderer _crown; // только у героя: корона поверх формы превращения
    private ElementsConfig _elements;
    private Sprite[] _frames;
    private Sprite[] _crownFrames;
    private ElementKind _form = ElementKind.None;
    private float _formPopProgress = 1f;
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

    public void Init(WorldEntity entity, ElementsConfig elements, int sortingOrder) {
        Entity = entity;
        _elements = elements;
        _renderer = GetComponent<SpriteRenderer>();
        _renderer.sortingOrder = sortingOrder;
        _frames = LoadFrames(entity.Kind);

        if (entity.Kind == ElementKind.Hero) {
            GameObject crownObject = new("Crown", typeof(SpriteRenderer));
            crownObject.transform.SetParent(transform, false);
            _crown = crownObject.GetComponent<SpriteRenderer>();
            _crown.sortingOrder = sortingOrder + 1;
            _crown.enabled = false;
            _crownFrames = LoadFrames("crown");
        }

        _shiftFrom = _shiftTo = ToWorld(entity.Position);
        transform.position = _shiftTo;
    }

    // Превращение героя: кадры формы (или свои, если None) и корона сверху; коротко раздувается.
    public void SetForm(ElementKind form) {
        _form = form;
        _frames = LoadFrames(form == ElementKind.None ? Entity.Kind : form);
        if (_crown != null) {
            _crown.enabled = form != ElementKind.None;
        }

        _formPopProgress = 0f;
    }

    private Sprite[] LoadFrames(ElementKind kind) {
        return LoadFrames(kind.ToString().ToLowerInvariant());
    }

    private Sprite[] LoadFrames(string name) {
        Sprite[] frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = _elements.GetFrame(name, i);
        }

        return frames;
    }

    // Смещение: плавный проезд до новой клетки (толчок героем или обмен с трона).
    public void MoveTo(Vector2Int cell) {
        Vector3 next = ToWorld(cell);
        if (_form == ElementKind.None && ElementRules.IsCreature(Entity.Kind) && !Mathf.Approximately(next.x, _shiftTo.x)) {
            _renderer.flipX = next.x < _shiftTo.x;
        }

        _shiftFrom = BasePosition();
        _shiftTo = next;
        _shiftDuration = ShiftDuration * Mathf.Max(1f, Vector3.Distance(_shiftFrom, _shiftTo));
        _shiftProgress = 0f;
    }

    // Неудачное смещение: элемент дёргается к цели и возвращается.
    public void Bump(Vector2Int direction) {
        _bumpDirection = new Vector3(direction.x, direction.y, 0f) * BumpDistance;
        _bumpProgress = 0f;
    }

    // Уничтожение: после задержки (когда весь ряд доехал) подпрыгивает, сжимается в точку и тает.
    public void Vanish(float delay) {
        _vanishDelay = Mathf.Max(0f, delay);
        _renderer.sortingOrder += 5;
    }

    private void Update() {
        // Дрожание как в Baba Is You: все элементы переключают кадры синхронно.
        int frame = (int)(Time.time * WobbleFps) % _frames.Length;
        _renderer.sprite = _frames[frame];
        if (_crown != null && _crown.enabled) {
            _crown.sprite = _crownFrames[frame];
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

    public static Vector3 ToWorld(Vector2Int cell) {
        return new Vector3(cell.x, cell.y, 0f);
    }
}
