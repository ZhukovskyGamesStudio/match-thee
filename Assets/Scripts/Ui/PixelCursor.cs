using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Пиксельный курсор мыши: стрелка с короной, дрожит как всё в игре. Системный курсор скрыт,
// картинка идёт за мышью по канвасу и всегда рисуется поверх остального.
[RequireComponent(typeof(RectTransform))]
public class PixelCursor : MonoBehaviour {
    private const float WobbleFps = 5f;
    private const string SpriteName = "pointer";

    private Image _image;
    private Sprite[] _frames;
    private RectTransform _rect;
    private bool _visible;

    public bool Visible {
        get => _visible;
        set {
            _visible = value;
            _image.enabled = value && Mouse.current != null;
        }
    }

    public static PixelCursor Create(Transform canvas, ElementsConfig elements, float size) {
        GameObject obj = new("PixelCursor", typeof(RectTransform), typeof(Image), typeof(PixelCursor));
        obj.transform.SetParent(canvas, false);
        PixelCursor cursor = obj.GetComponent<PixelCursor>();
        cursor.Init(elements, size);
        return cursor;
    }

    private void Init(ElementsConfig elements, float size) {
        Cursor.visible = false;
        _frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < _frames.Length; i++) {
            _frames[i] = elements.GetFrame(SpriteName, i);
        }

        _image = GetComponent<Image>();
        _image.sprite = _frames[0];
        _image.raycastTarget = false;
        _image.enabled = false;

        // Острие стрелки — левый верхний пиксель спрайта: он и стоит в точке мыши.
        _rect = GetComponent<RectTransform>();
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.zero;
        _rect.pivot = new Vector2(0f, 1f);
        _rect.sizeDelta = new Vector2(size, size);
    }

    public void SetSize(float size) {
        _rect.sizeDelta = new Vector2(size, size);
    }

    private void LateUpdate() {
        Cursor.visible = false;
        Mouse mouse = Mouse.current;
        if (!_visible || mouse == null) {
            _image.enabled = false;
            return;
        }

        _image.enabled = true;
        _image.sprite = _frames[(int)(Time.time * WobbleFps) % _frames.Length];
        _rect.anchoredPosition = mouse.position.ReadValue();
        transform.SetAsLastSibling();
    }
}
