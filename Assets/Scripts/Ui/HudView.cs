using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Рамка вокруг игрового экрана (поля под интерфейс) и кнопка рюкзака слева сверху. Собирается кодом.
public class HudView : MonoBehaviour {
    private const float BorderThickness = 2f;
    private const float BackpackScale = 0.8f; // от высоты верхнего поля
    private static readonly Color FrameColor = new Color32(24, 24, 30, 255);
    private static readonly Color BorderColor = new Color32(70, 70, 82, 255);

    private ElementsConfig _elements;
    private RectTransform _top;
    private RectTransform _bottom;
    private RectTransform _left;
    private RectTransform _right;
    private RectTransform _borderTop;
    private RectTransform _borderBottom;
    private RectTransform _borderLeft;
    private RectTransform _borderRight;
    private RectTransform _backpack;
    private float _aspect = 16f / 9f;
    private int _lastWidth;
    private int _lastHeight;

    public void Init(ElementsConfig elements, float aspect) {
        _elements = elements;
        _aspect = aspect;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // считаем в пикселях сами
        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        _top = CreatePanel("FrameTop", FrameColor);
        _bottom = CreatePanel("FrameBottom", FrameColor);
        _left = CreatePanel("FrameLeft", FrameColor);
        _right = CreatePanel("FrameRight", FrameColor);
        _borderTop = CreatePanel("BorderTop", BorderColor);
        _borderBottom = CreatePanel("BorderBottom", BorderColor);
        _borderLeft = CreatePanel("BorderLeft", BorderColor);
        _borderRight = CreatePanel("BorderRight", BorderColor);
        _backpack = CreateBackpackButton();
        Layout();
    }

    private void Update() {
        if (Screen.width != _lastWidth || Screen.height != _lastHeight) {
            Layout();
        }
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

        // Рюкзак в левом верхнем углу, по высоте вписан в верхнее поле.
        float margin = Screen.height - inner.yMax;
        float size = margin * BackpackScale;
        float padding = (margin - size) / 2f;
        _backpack.anchorMin = new Vector2(0f, 1f);
        _backpack.anchorMax = new Vector2(0f, 1f);
        _backpack.pivot = new Vector2(0f, 1f);
        _backpack.anchoredPosition = new Vector2(padding, -padding);
        _backpack.sizeDelta = new Vector2(size, size);
    }

    private RectTransform CreatePanel(string name, Color color) {
        GameObject panel = new(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        Image image = panel.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return panel.GetComponent<RectTransform>();
    }

    // Кнопка рюкзака: спрайт с большой буквой Q. Пока ничего не делает.
    private RectTransform CreateBackpackButton() {
        GameObject button = new("Backpack", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(transform, false);
        Image image = button.GetComponent<Image>();
        image.sprite = _elements.GetFrame("backpack", 0);
        image.preserveAspect = true;
        return button.GetComponent<RectTransform>();
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

    private static void EnsureEventSystem() {
        if (Object.FindAnyObjectByType<EventSystem>() != null) {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
