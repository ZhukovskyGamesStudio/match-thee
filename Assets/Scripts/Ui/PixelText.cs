using UnityEngine;
using UnityEngine.UI;

// Пиксельная надпись из спрайта конфига: три кадра дрожания, размер — пиксели спрайта × масштаб.
// Имя спрайта и масштаб настраиваются в префабе.
[RequireComponent(typeof(Image))]
[ExecuteAlways]
public class PixelText : MonoBehaviour {
    private const float WobbleFps = 5f;

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private string _spriteName;

    [SerializeField, Min(1)]
    private int _pixelScale = 3;

    private Image _image;
    private Sprite[] _frames;

    public Vector2 Size => _frames != null && _frames[0] != null ? _frames[0].rect.size * _pixelScale : Vector2.zero;

    public void Setup(ElementsConfig elements, string spriteName, int pixelScale) {
        _elements = elements;
        _spriteName = spriteName;
        _pixelScale = pixelScale;
        Load();
    }

    private void OnEnable() {
        Load();
    }

    private void OnValidate() {
        Load();
    }

    private void Load() {
        _image = GetComponent<Image>();
        if (_elements == null || string.IsNullOrEmpty(_spriteName)) {
            return;
        }

        _frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < _frames.Length; i++) {
            _frames[i] = _elements.GetFrame(_spriteName, i);
        }

        if (_frames[0] == null) {
            return;
        }

        _image.sprite = _frames[0];
        _image.preserveAspect = true;
        RectTransform rect = (RectTransform)transform;
        rect.sizeDelta = Size;
        if (TryGetComponent(out LayoutElement layout)) {
            layout.preferredWidth = Size.x;
            layout.preferredHeight = Size.y;
        }
    }

    private void Update() {
        if (!Application.isPlaying || _frames == null || _frames[0] == null) {
            return;
        }

        _image.sprite = _frames[(int)(Time.unscaledTime * WobbleFps) % _frames.Length];
    }
}
