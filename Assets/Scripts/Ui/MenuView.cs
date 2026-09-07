using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Главное меню: пиксельное название с короной на T и кнопка «Играть». Собирается кодом из спрайтов конфига.
// Клик по кнопке (или Enter) загружает игровую сцену.
public class MenuView : MonoBehaviour {
    private const string GameScene = "GameScene";
    private const float WobbleFps = 5f;
    private const float TitlePixel = 1f / 96f;   // экранных пикселей на пиксель названия, доля высоты окна
    private const float ButtonPixel = 1f / 200f; // то же для кнопки
    private const float TitleOffset = 0.12f;      // сдвиг названия вверх от центра, доля высоты окна
    private const float ButtonOffset = -0.1f;     // сдвиг кнопки вниз
    private static readonly Color HoverColor = new Color(1f, 1f, 0.75f);
    private static readonly Color PressedColor = new Color(0.7f, 0.7f, 0.7f);

    [SerializeField]
    private ElementsConfig _elements;

    private Image _title;
    private Image _button;
    private Sprite[] _titleFrames;
    private Sprite[] _buttonFrames;
    private int _lastWidth;
    private int _lastHeight;

    private void Awake() {
        Cursor.visible = true;
        _titleFrames = LoadFrames("title");
        _buttonFrames = LoadFrames("play_text");

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        gameObject.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<EventSystem>() == null) {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        _title = CreateImage("Title", _titleFrames[0], false);
        _button = CreateImage("Play", _buttonFrames[0], true);
        Button button = _button.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = HoverColor;
        colors.pressedColor = PressedColor;
        button.colors = colors;
        button.onClick.AddListener(Play);
        Layout();
    }

    private void Update() {
        if (Screen.width != _lastWidth || Screen.height != _lastHeight) {
            Layout();
        }

        int frame = (int)(Time.time * WobbleFps) % ElementRules.WobbleFrames;
        _title.sprite = _titleFrames[frame];
        _button.sprite = _buttonFrames[frame];

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) {
            Play();
        }
    }

    private static void Play() {
        SceneManager.LoadScene(GameScene);
    }

    private void Layout() {
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        float titlePixel = Mathf.Max(1f, Mathf.Round(Screen.height * TitlePixel));
        float buttonPixel = Mathf.Max(1f, Mathf.Round(Screen.height * ButtonPixel));
        Place(_title.rectTransform, new Vector2(0f, Screen.height * TitleOffset), _titleFrames[0].rect.size * titlePixel);
        Place(_button.rectTransform, new Vector2(0f, Screen.height * ButtonOffset), _buttonFrames[0].rect.size * buttonPixel);
    }

    private Image CreateImage(string name, Sprite sprite, bool raycast) {
        GameObject obj = new(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(transform, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = raycast;
        return image;
    }

    private Sprite[] LoadFrames(string name) {
        Sprite[] frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = _elements.GetFrame(name, i);
        }

        return frames;
    }

    private static void Place(RectTransform rect, Vector2 offset, Vector2 size) {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }
}
