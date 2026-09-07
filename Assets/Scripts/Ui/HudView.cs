using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Рамка вокруг игрового экрана (поля под интерфейс) и рюкзак слева сверху. Собирается кодом.
// При получении ресурса рядом с рюкзаком всплывает его иконка с «+1», а сам рюкзак подпрыгивает.
// Q (или клик по рюкзаку) открывает панель со списком всех ресурсов внутри.
// Справа сверху кнопка рестарта (R): назад к моменту входа в комнату.
// Финал: мир плавно затемняется, всплывает пиксельная дрожащая надпись, затем кнопка «сыграть снова».
public class HudView : MonoBehaviour {
    private const float BorderThickness = 2f;
    private const float BackpackScale = 0.8f; // от высоты верхнего поля
    private const float PopupLifetime = 1.1f;
    private const float PopupAppear = 0.12f; // доля жизни на появление
    private const float PopupFade = 0.35f;   // доля жизни на исчезновение
    private const float PopupRise = 0.35f;   // подъём за жизнь, в размерах рюкзака
    private const float PunchDuration = 0.25f;
    private const float PunchScale = 1.25f;
    private static readonly Color FrameColor = new Color32(24, 24, 30, 255);
    private static readonly Color BorderColor = new Color32(70, 70, 82, 255);
    private static readonly Color PanelColor = new Color32(24, 24, 30, 255);
    private static readonly Color TextColor = new Color32(232, 232, 238, 255);
    private static readonly Color DimTextColor = new Color32(140, 140, 150, 255);
    private static readonly Color FormTextColor = new Color32(237, 226, 133, 255); // цвет короны: текущая форма героя
    private static readonly Color RowHoverColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color RowPressedColor = new Color(1f, 1f, 1f, 0.25f);
    private const string UnlockedLabel = "∞";
    private const float WobbleFps = 5f;
    private const float SpritePixels = 24f; // пикселей спрайта на клетку
    private const float WinBackdropAlpha = 0.72f;
    private const float WinBackdropDuration = 1.2f;
    private const float WinTextDelay = 0.4f;
    private const float WinTextDuration = 0.5f;
    private const float WinTextStartScale = 1.6f;
    private const float WinTextPixelScale = 5f;  // пикселей игры на пиксель надписи
    private const float ReplayDelay = 2.6f;
    private const float ReplayDuration = 0.4f;
    private const float ReplayPixelScale = 3f;
    private const float ReplayGap = 1.2f;         // отступ кнопки от надписи, в высотах надписи
    private static readonly Color ReplayHoverColor = new Color(1f, 1f, 0.75f);
    private static readonly Color ReplayPressedColor = new Color(0.7f, 0.7f, 0.7f);

    private class Popup {
        public RectTransform Rect;
        public CanvasGroup Group;
        public Vector2 Start;
        public float Age;
    }

    private ElementsConfig _elements;
    private WorldModel _model;
    private Inventory _inventory;
    private Font _font;
    private RectTransform _top;
    private RectTransform _bottom;
    private RectTransform _left;
    private RectTransform _right;
    private RectTransform _borderTop;
    private RectTransform _borderBottom;
    private RectTransform _borderLeft;
    private RectTransform _borderRight;
    private RectTransform _backpack;
    private RectTransform _restart;
    private RectTransform _panel;
    private RectTransform _panelInner;
    private Image _winBackdrop;
    private Image _winText;
    private Image _replay;
    private CanvasGroup _replayGroup;
    private Sprite[] _winFrames;
    private Sprite[] _replayFrames;
    private float _winTime = -1f; // >= 0 — финал идёт
    private System.Action _restartAction;
    private bool _replayShown;
    private readonly List<Popup> _popups = new();
    private float _backpackSize;
    private float _padding;
    private float _punchProgress = 1f;
    private bool _panelOpen;
    private float _aspect = 16f / 9f;
    private int _lastWidth;
    private int _lastHeight;

    public bool IsPanelOpen => _panelOpen;
    public bool WantsCursor => _panelOpen || _replayShown;

    public void Init(ElementsConfig elements, float aspect, WorldModel model, System.Action restart) {
        _elements = elements;
        _aspect = aspect;
        _model = model;
        _inventory = model.Inventory;
        _restartAction = restart;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // считаем в пикселях сами
        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        _top = CreatePanel(transform, "FrameTop", FrameColor);
        _bottom = CreatePanel(transform, "FrameBottom", FrameColor);
        _left = CreatePanel(transform, "FrameLeft", FrameColor);
        _right = CreatePanel(transform, "FrameRight", FrameColor);
        _borderTop = CreatePanel(transform, "BorderTop", BorderColor);
        _borderBottom = CreatePanel(transform, "BorderBottom", BorderColor);
        _borderLeft = CreatePanel(transform, "BorderLeft", BorderColor);
        _borderRight = CreatePanel(transform, "BorderRight", BorderColor);
        _backpack = CreateIconButton("Backpack", "backpack", TogglePanel);
        _restart = CreateIconButton("Restart", "restart", () => _restartAction?.Invoke());
        _panel = CreatePanel(transform, "InventoryPanel", BorderColor); // последним — поверх всплывашек
        _panelInner = CreatePanel(_panel, "Inner", PanelColor);
        Stretch(_panelInner, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.one * (-BorderThickness * 2f));
        _panel.gameObject.SetActive(false);
        Layout();

        _inventory.Added += OnResourceAdded;
        _inventory.Unlocked += OnKindUnlocked;
        _inventory.Changed += OnResourceChanged;
        _model.HeroFormChanged += OnHeroFormChanged;
        _model.Restored += OnRestored;
    }

    private void OnDestroy() {
        if (_inventory != null) {
            _inventory.Added -= OnResourceAdded;
            _inventory.Unlocked -= OnKindUnlocked;
            _inventory.Changed -= OnResourceChanged;
        }

        if (_model != null) {
            _model.HeroFormChanged -= OnHeroFormChanged;
            _model.Restored -= OnRestored;
        }
    }

    private void Update() {
        if (Screen.width != _lastWidth || Screen.height != _lastHeight) {
            Layout();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.qKey.wasPressedThisFrame) {
            TogglePanel();
        }

        UpdatePunch();
        UpdatePopups();
        if (_winTime >= 0f) {
            UpdateWin();
        }
    }

    // Конец игры: затемнение, надпись и кнопка создаются сразу, а проявляются по таймеру в UpdateWin.
    public void ShowWin() {
        if (_winTime >= 0f) {
            return;
        }

        _winTime = 0f;
        _winFrames = LoadFrames("win_text");
        _replayFrames = LoadFrames("replay_text");

        _winBackdrop = CreatePanel(transform, "WinBackdrop", Color.clear).GetComponent<Image>();
        Stretch(_winBackdrop.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);

        _winText = CreateImage(transform, "WinText", _winFrames[0]).GetComponent<Image>();
        _winText.color = Color.clear;
        Vector2 textSize = _winFrames[0].rect.size * PixelScale(WinTextPixelScale);
        PlaceCenter(_winText.rectTransform, Vector2.zero, textSize);

        GameObject replay = new("Replay", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        replay.transform.SetParent(transform, false);
        _replay = replay.GetComponent<Image>();
        _replay.sprite = _replayFrames[0];
        Vector2 replaySize = _replayFrames[0].rect.size * PixelScale(ReplayPixelScale);
        PlaceCenter(_replay.rectTransform, new Vector2(0f, -(textSize.y * ReplayGap + replaySize.y / 2f)), replaySize);
        Button button = replay.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = ReplayHoverColor;
        colors.pressedColor = ReplayPressedColor;
        button.colors = colors;
        button.onClick.AddListener(Replay);
        _replayGroup = replay.GetComponent<CanvasGroup>();
        _replayGroup.alpha = 0f;
        _replayGroup.interactable = false;
        _replayGroup.blocksRaycasts = false;
    }

    private void UpdateWin() {
        _winTime += Time.deltaTime;
        int frame = (int)(Time.time * WobbleFps) % _winFrames.Length;

        float backdrop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_winTime / WinBackdropDuration));
        _winBackdrop.color = new Color(0f, 0f, 0f, WinBackdropAlpha * backdrop);

        float t = Mathf.Clamp01((_winTime - WinTextDelay) / WinTextDuration);
        float ease = 1f - (1f - t) * (1f - t);
        _winText.sprite = _winFrames[frame];
        _winText.color = new Color(1f, 1f, 1f, ease);
        _winText.rectTransform.localScale = Vector3.one * Mathf.Lerp(WinTextStartScale, 1f, ease);

        float r = Mathf.Clamp01((_winTime - ReplayDelay) / ReplayDuration);
        _replay.sprite = _replayFrames[frame];
        _replayGroup.alpha = r;
        if (r >= 1f && !_replayShown) {
            _replayShown = true;
            _replayGroup.interactable = true;
            _replayGroup.blocksRaycasts = true;
        }
    }

    private static void Replay() {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Во сколько экранных пикселей рисовать один пиксель спрайта, чтобы надпись была кратна пикселю игры.
    private float PixelScale(float factor) {
        Rect inner = ScreenFrame.InnerRect(Screen.width, Screen.height, _aspect);
        float gamePixel = inner.height / (_model.ScreenHeight * SpritePixels);
        return Mathf.Max(1f, Mathf.Round(gamePixel * factor));
    }

    private Sprite[] LoadFrames(string name) {
        Sprite[] frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = _elements.GetFrame(name, i);
        }

        return frames;
    }

    private void Layout() {
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        Rect inner = ScreenFrame.InnerRect(Screen.width, Screen.height, _aspect);

        Stretch(_top, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, Screen.height - inner.yMax));
        Stretch(_bottom, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, inner.yMin));
        Stretch(_left, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(inner.xMin, 0f));
        Stretch(_right, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(Screen.width - inner.xMax, 0f));

        float t = BorderThickness;
        PlaceBottomLeft(_borderTop, new Vector2(inner.xMin - t, inner.yMax), new Vector2(inner.width + t * 2f, t));
        PlaceBottomLeft(_borderBottom, new Vector2(inner.xMin - t, inner.yMin - t), new Vector2(inner.width + t * 2f, t));
        PlaceBottomLeft(_borderLeft, new Vector2(inner.xMin - t, inner.yMin), new Vector2(t, inner.height));
        PlaceBottomLeft(_borderRight, new Vector2(inner.xMax, inner.yMin), new Vector2(t, inner.height));

        // Рюкзак в левом верхнем углу, по высоте вписан в верхнее поле. Пивот в центре — чтобы подпрыгивать на месте.
        float margin = Screen.height - inner.yMax;
        _backpackSize = margin * BackpackScale;
        _padding = (margin - _backpackSize) / 2f;
        _backpack.anchorMin = new Vector2(0f, 1f);
        _backpack.anchorMax = new Vector2(0f, 1f);
        _backpack.pivot = new Vector2(0.5f, 0.5f);
        _backpack.anchoredPosition = new Vector2(_padding + _backpackSize / 2f, -(_padding + _backpackSize / 2f));
        _backpack.sizeDelta = new Vector2(_backpackSize, _backpackSize);

        // Рестарт в правом верхнем углу, того же размера.
        _restart.anchorMin = new Vector2(1f, 1f);
        _restart.anchorMax = new Vector2(1f, 1f);
        _restart.pivot = new Vector2(0.5f, 0.5f);
        _restart.anchoredPosition = new Vector2(-(_padding + _backpackSize / 2f), -(_padding + _backpackSize / 2f));
        _restart.sizeDelta = new Vector2(_backpackSize, _backpackSize);

        if (_panelOpen) {
            RebuildPanel();
        }
    }

    // Кнопка-иконка со спрайтом (рюкзак с буквой Q, рестарт с буквой R).
    private RectTransform CreateIconButton(string name, string spriteName, UnityEngine.Events.UnityAction onClick) {
        GameObject button = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(transform, false);
        Image image = button.GetComponent<Image>();
        image.sprite = _elements.GetFrame(spriteName, 0);
        image.preserveAspect = true;
        button.GetComponent<Button>().onClick.AddListener(onClick);
        return button.GetComponent<RectTransform>();
    }

    // Откат комнаты: финал (если был) убираем, панель перестраиваем.
    private void OnRestored() {
        HideWin();
        if (_panelOpen) {
            RebuildPanel();
        }
    }

    private void HideWin() {
        if (_winTime < 0f) {
            return;
        }

        Destroy(_winBackdrop.gameObject);
        Destroy(_winText.gameObject);
        Destroy(_replay.gameObject);
        _winBackdrop = null;
        _winText = null;
        _replay = null;
        _replayGroup = null;
        _winTime = -1f;
        _replayShown = false;
    }

    private void OnResourceAdded(ElementKind kind, int amount) {
        ShowPopup(kind, $"+{amount}");
        _punchProgress = 0f;
    }

    // Вид открыт навсегда (ряд из пяти): всплывашка со знаком бесконечности.
    private void OnKindUnlocked(ElementKind kind) {
        ShowPopup(kind, UnlockedLabel);
        _punchProgress = 0f;
        if (_panelOpen) {
            RebuildPanel();
        }
    }

    private void OnResourceChanged(ElementKind kind, int count) {
        if (_panelOpen) {
            RebuildPanel();
        }
    }

    private void OnHeroFormChanged(ElementKind form) {
        if (_panelOpen) {
            RebuildPanel();
        }
    }

    // Превращение: клик по строке рюкзака превращает героя в этот предмет.
    private void OnItemClicked(ElementKind kind) {
        _model.TryTransform(kind);
    }

    // Всплывашка: иконка ресурса и подпись («+1» или «∞») справа от рюкзака; появляется, плывёт вверх и тает.
    private void ShowPopup(ElementKind kind, string label) {
        float size = _backpackSize * 0.6f;
        float width = size * 2.3f;

        GameObject root = new("Popup", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        root.transform.SetSiblingIndex(_panel.GetSiblingIndex()); // под панелью, над рамкой
        RectTransform rect = root.GetComponent<RectTransform>();
        Vector2 start = new(_padding + _backpackSize + _padding + _popups.Count * (width + _padding), -(_padding + (_backpackSize - size) / 2f));
        PlaceTopLeft(rect, start, new Vector2(width, size));

        PlaceTopLeft(CreateImage(root.transform, "Icon", _elements.GetFrame(kind, 0)), Vector2.zero, new Vector2(size, size));
        Text text = CreateText(root.transform, label, size * 0.75f, TextAnchor.MiddleLeft);
        PlaceTopLeft(text.rectTransform, new Vector2(size + size * 0.15f, 0f), new Vector2(width - size, size));

        _popups.Add(new Popup { Rect = rect, Group = root.GetComponent<CanvasGroup>(), Start = start });
    }

    private void UpdatePopups() {
        for (int i = _popups.Count - 1; i >= 0; i--) {
            Popup popup = _popups[i];
            popup.Age += Time.deltaTime;
            float t = popup.Age / PopupLifetime;
            if (t >= 1f) {
                Destroy(popup.Rect.gameObject);
                _popups.RemoveAt(i);
                continue;
            }

            float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / PopupAppear));
            popup.Rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, appear);
            popup.Rect.anchoredPosition = popup.Start + Vector2.up * (_backpackSize * PopupRise * t);
            popup.Group.alpha = t < 1f - PopupFade ? appear : (1f - t) / PopupFade;
        }
    }

    // Рюкзак подпрыгивает (раздувается и возвращается) при получении ресурса.
    private void UpdatePunch() {
        if (_punchProgress >= 1f) {
            return;
        }

        _punchProgress = Mathf.Min(1f, _punchProgress + Time.deltaTime / PunchDuration);
        _backpack.localScale = Vector3.one * (1f + (PunchScale - 1f) * Mathf.Sin(_punchProgress * Mathf.PI));
    }

    private void TogglePanel() {
        _panelOpen = !_panelOpen;
        _panel.gameObject.SetActive(_panelOpen);
        if (_panelOpen) {
            RebuildPanel();
        }
    }

    // Панель под рюкзаком: заголовок и по строке на каждый вид — иконка, имя, «∞» для открытых навсегда
    // или количество расходуемых. При включённом превращении строки — кнопки, текущая форма героя подсвечена.
    private void RebuildPanel() {
        foreach (Transform child in _panelInner) {
            Destroy(child.gameObject);
        }

        float row = _backpackSize * 0.55f;
        float pad = row * 0.35f;
        float width = _backpackSize * 4.5f;
        float y = -pad;

        Text title = CreateText(_panelInner, "Рюкзак", row * 0.6f, TextAnchor.MiddleLeft);
        PlaceTopLeft(title.rectTransform, new Vector2(pad, y), new Vector2(width - pad * 2f, row));
        y -= row;

        List<(ElementKind Kind, string Label)> items = _inventory.UnlockedKinds.OrderBy(kind => (int)kind)
            .Select(kind => (kind, UnlockedLabel))
            .Concat(_inventory.Counts.Where(pair => pair.Value > 0 && !_inventory.IsUnlocked(pair.Key)).OrderBy(pair => (int)pair.Key)
                .Select(pair => (pair.Key, $"×{pair.Value}")))
            .ToList();
        if (items.Count == 0) {
            Text empty = CreateText(_panelInner, "пусто", row * 0.5f, TextAnchor.MiddleLeft);
            empty.color = DimTextColor;
            PlaceTopLeft(empty.rectTransform, new Vector2(pad, y), new Vector2(width - pad * 2f, row));
            y -= row;
        }

        foreach ((ElementKind Kind, string Label) item in items) {
            RectTransform line = CreateRow(item.Kind, width, row);
            PlaceTopLeft(line, new Vector2(0f, y), new Vector2(width, row));
            bool isForm = Features.Transformation && _model.HeroForm == item.Kind;

            PlaceTopLeft(CreateImage(line, "Icon", _elements.GetFrame(item.Kind, 0)), new Vector2(pad, 0f), new Vector2(row, row));
            Text name = CreateText(line, item.Kind.ToString(), row * 0.5f, TextAnchor.MiddleLeft);
            PlaceTopLeft(name.rectTransform, new Vector2(pad + row + pad, 0f), new Vector2(width - row - pad * 3f, row));
            Text count = CreateText(line, item.Label, row * 0.5f, TextAnchor.MiddleRight);
            PlaceTopLeft(count.rectTransform, new Vector2(pad + row + pad, 0f), new Vector2(width - row - pad * 3f, row));
            if (isForm) {
                name.color = FormTextColor;
                count.color = FormTextColor;
            }

            y -= row;
        }

        PlaceTopLeft(_panel, new Vector2(_padding, -(_padding + _backpackSize + _padding)), new Vector2(width, -y + pad));
    }

    // Строка панели: при включённом превращении — кнопка с подсветкой при наведении.
    private RectTransform CreateRow(ElementKind kind, float width, float height) {
        GameObject line = new("Row", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(_panelInner, false);
        Image image = line.GetComponent<Image>();
        image.color = Features.Transformation ? Color.white : Color.clear; // цвет состояния кнопки умножается на этот
        image.raycastTarget = Features.Transformation;
        if (Features.Transformation) {
            Button button = line.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.clear;
            colors.selectedColor = Color.clear;
            colors.highlightedColor = RowHoverColor;
            colors.pressedColor = RowPressedColor;
            button.colors = colors;
            button.onClick.AddListener(() => OnItemClicked(kind));
        }

        return line.GetComponent<RectTransform>();
    }

    private static RectTransform CreatePanel(Transform parent, string name, Color color) {
        RectTransform rect = CreateImage(parent, name, null);
        rect.GetComponent<Image>().color = color;
        return rect;
    }

    private static RectTransform CreateImage(Transform parent, string name, Sprite sprite) {
        GameObject obj = new(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return obj.GetComponent<RectTransform>();
    }

    private Text CreateText(Transform parent, string content, float fontSize, TextAnchor anchor) {
        GameObject obj = new("Text", typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.font = _font;
        text.text = content;
        text.fontSize = Mathf.Max(1, Mathf.RoundToInt(fontSize));
        text.color = TextColor;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size) {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private static void PlaceBottomLeft(RectTransform rect, Vector2 position, Vector2 size) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void PlaceCenter(RectTransform rect, Vector2 offset, Vector2 size) {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    // Координаты от левого верхнего угла родителя, y вниз — отрицательный.
    private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size) {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void EnsureEventSystem() {
        if (Object.FindAnyObjectByType<EventSystem>() != null) {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
