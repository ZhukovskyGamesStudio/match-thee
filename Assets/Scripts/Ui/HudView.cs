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
// Под рюкзаком всегда виден список предметов (иконка и число); Q или клик по рюкзаку превращает героя
// в следующий предмет по кругу, пока герой превращён — в списке есть и он сам. Пустой рюкзак на Q трясётся и краснеет.
// Рюкзак появляется с первым собранным предметом.
// Справа сверху кнопка рестарта (R): назад к моменту входа в комнату; появляется в левой нижней комнате.
// Рестарт идёт под пиксельным переходом (шейдер PixelWipe): мир откатывается, пока экран закрыт.
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
    private static readonly Color GlobalPopupColor = new Color32(255, 208, 64, 255); // ресурс на все экраны — золотом
    private const string LocalColorTag = "<color=#8c8c96>";                             // местная часть счёта — тусклее
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
    private static readonly Color ShakeColor = new Color(1f, 0.35f, 0.3f);
    private const float ShakeDuration = 0.45f;
    private const float ShakeAmplitude = 0.12f; // в размерах рюкзака
    private const float ShakeCycles = 3f;
    private const float TransitionDuration = 0.7f;
    private const string TransitionShader = "Shaders/PixelWipe";

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
    private Image _backpackImage;
    private Vector2 _backpackHome;
    private bool _backpackRevealed;
    private bool _restartRevealed;
    private float _shakeProgress = 1f;
    private Image _transition;
    private Material _transitionMaterial;
    private float _transitionProgress = -1f; // >= 0 — переход идёт
    private bool _transitionRestored;
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
    private float _aspect = 16f / 9f;
    private int _lastWidth;
    private int _lastHeight;

    public bool WantsCursor => _replayShown;
    public PixelCursor Pointer { get; private set; }
    public bool RestartAvailable => _restartRevealed;

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
        _backpack = CreateIconButton("Backpack", "backpack", OnBackpackPressed);
        _backpackImage = _backpack.GetComponent<Image>();
        _backpack.gameObject.SetActive(false);
        _restart = CreateIconButton("Restart", "restart", StartRestart);
        _restart.gameObject.SetActive(false);
        _panel = CreatePanel(transform, "InventoryPanel", BorderColor); // последним — поверх всплывашек
        _panelInner = CreatePanel(_panel, "Inner", PanelColor);
        Stretch(_panelInner, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.one * (-BorderThickness * 2f));
        _panel.gameObject.SetActive(false);
        _transition = CreateImage(transform, "Transition", null).GetComponent<Image>();
        _transitionMaterial = new Material(Resources.Load<Shader>(TransitionShader));
        _transition.material = _transitionMaterial;
        _transition.raycastTarget = true;
        Stretch(_transition.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        _transition.gameObject.SetActive(false);
        Pointer = PixelCursor.Create(transform, _elements, 1f);
        Layout();

        _inventory.Added += OnResourceAdded;
        _inventory.Changed += OnResourceChanged;
        _inventory.ScreenChanged += RebuildPanel;
        _model.HeroFormChanged += OnHeroFormChanged;
        _model.Restored += OnRestored;
    }

    private void OnDestroy() {
        if (_inventory != null) {
            _inventory.Added -= OnResourceAdded;
            _inventory.Changed -= OnResourceChanged;
            _inventory.ScreenChanged -= RebuildPanel;
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
            OnBackpackPressed();
        }

        UpdatePunch();
        UpdateShake();
        UpdateTransition();
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
        Pointer.SetSize(inner.height / _model.ScreenHeight); // курсор размером с клетку
        _backpackHome = new Vector2(_padding + _backpackSize / 2f, -(_padding + _backpackSize / 2f));
        _backpack.anchoredPosition = _backpackHome;
        _backpack.sizeDelta = new Vector2(_backpackSize, _backpackSize);

        // Рестарт в правом верхнем углу, того же размера.
        _restart.anchorMin = new Vector2(1f, 1f);
        _restart.anchorMax = new Vector2(1f, 1f);
        _restart.pivot = new Vector2(0.5f, 0.5f);
        _restart.anchoredPosition = new Vector2(-(_padding + _backpackSize / 2f), -(_padding + _backpackSize / 2f));
        _restart.sizeDelta = new Vector2(_backpackSize, _backpackSize);
        RebuildPanel();
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
        RebuildPanel();
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

    // Всплывашка «+N»: за ряд из пяти (на все экраны) — золотом, за ряд из четырёх (только здесь) — белым.
    private void OnResourceAdded(ElementKind kind, int amount, bool everywhere) {
        RevealBackpack();
        ShowPopup(kind, $"+{amount}", everywhere ? GlobalPopupColor : TextColor);
        _punchProgress = 0f;
    }

    // Рюкзак виден с первого собранного предмета и дальше уже не прячется.
    private void RevealBackpack() {
        if (_backpackRevealed) {
            return;
        }

        _backpackRevealed = true;
        _backpack.gameObject.SetActive(true);
        _punchProgress = 0f;
        RebuildPanel();
    }

    // Кнопка рестарта появляется, когда игрок впервые попал в левую нижнюю комнату.
    public void RevealRestart() {
        if (_restartRevealed) {
            return;
        }

        _restartRevealed = true;
        _restart.gameObject.SetActive(true);
    }

    private bool IsInventoryEmpty => _inventory.IsEmpty;

    // Q или клик: пустой рюкзак трясётся и краснеет, иначе герой превращается в следующий предмет по кругу.
    private void OnBackpackPressed() {
        if (!_backpackRevealed) {
            return;
        }

        if (IsInventoryEmpty) {
            _shakeProgress = 0f;
            return;
        }

        _model.TransformNext();
    }

    private void UpdateShake() {
        if (_shakeProgress >= 1f) {
            return;
        }

        _shakeProgress = Mathf.Min(1f, _shakeProgress + Time.deltaTime / ShakeDuration);
        float fade = 1f - _shakeProgress;
        float offset = Mathf.Sin(_shakeProgress * Mathf.PI * 2f * ShakeCycles) * fade * _backpackSize * ShakeAmplitude;
        _backpack.anchoredPosition = _backpackHome + new Vector2(offset, 0f);
        _backpackImage.color = Color.Lerp(Color.white, ShakeColor, fade);
    }

    // Рестарт: запускаем переход; сам откат мира случится на его середине, когда экран закрыт.
    public void StartRestart() {
        if (!_restartRevealed || _transitionProgress >= 0f) {
            return;
        }

        _transitionProgress = 0f;
        _transitionRestored = false;
        _transition.transform.SetAsLastSibling();
        _transition.gameObject.SetActive(true);
        _transitionMaterial.SetFloat("_Progress", 0f);
    }

    private void UpdateTransition() {
        if (_transitionProgress < 0f) {
            return;
        }

        _transitionProgress += Time.deltaTime / TransitionDuration;
        _transitionMaterial.SetFloat("_Progress", Mathf.Clamp01(_transitionProgress));
        if (_transitionProgress >= 0.5f && !_transitionRestored) {
            _transitionRestored = true;
            _restartAction?.Invoke();
        }

        if (_transitionProgress >= 1f) {
            _transitionProgress = -1f;
            _transition.gameObject.SetActive(false);
        }
    }

    private void OnResourceChanged(ElementKind kind, int count) {
        RebuildPanel();
    }

    private void OnHeroFormChanged(ElementKind form) {
        RebuildPanel();
    }

    // Всплывашка: иконка ресурса и подпись («+1» или «∞») справа от рюкзака; появляется, плывёт вверх и тает.
    private void ShowPopup(ElementKind kind, string label, Color color) {
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
        text.color = color;
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

    // Панель под рюкзаком, видна всегда, пока в рюкзаке что-то есть: по строке на предмет — иконка и счёт:
    // общие «×N» и тусклее местные «+M» этого экрана. Пока герой превращён, первой строкой он сам.
    // Текущая форма подсвечена цветом короны.
    private void RebuildPanel() {
        if (_panelInner == null) {
            return;
        }

        foreach (Transform child in _panelInner) {
            Destroy(child.gameObject);
        }

        List<(ElementKind Kind, string Label)> items = new();
        if (_model.IsHeroTransformed) {
            items.Add((ElementKind.Hero, string.Empty));
        }

        foreach (ElementKind kind in _inventory.Kinds) {
            int global = _inventory.GlobalCount(kind);
            int local = _inventory.LocalCount(kind);
            string label = global > 0 ? $"×{global}" : string.Empty;
            if (local > 0) {
                label += $"{LocalColorTag}+{local}</color>";
            }

            items.Add((kind, label));
        }

        bool visible = _backpackRevealed && items.Count > 0;
        _panel.gameObject.SetActive(visible);
        if (!visible) {
            return;
        }

        float row = _backpackSize * 0.55f;
        float pad = row * 0.2f;
        float width = _backpackSize * 1.2f;
        float y = -pad;
        foreach ((ElementKind Kind, string Label) item in items) {
            bool isForm = Features.Transformation && _model.HeroForm == item.Kind;
            PlaceTopLeft(CreateImage(_panelInner, "Icon", _elements.GetFrame(item.Kind, 0)), new Vector2(pad, y), new Vector2(row, row));
            Text count = CreateText(_panelInner, item.Label, row * 0.5f, TextAnchor.MiddleLeft);
            PlaceTopLeft(count.rectTransform, new Vector2(pad + row + pad * 0.5f, y), new Vector2(width - row - pad * 2.5f, row));
            if (isForm) {
                count.color = FormTextColor;
            }

            y -= row;
        }

        PlaceTopLeft(_panel, new Vector2(_padding, -(_padding + _backpackSize + _padding)), new Vector2(width, -y + pad));
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
