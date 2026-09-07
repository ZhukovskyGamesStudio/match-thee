using UnityEngine;

// Рамка на клетке под курсором мыши в режиме трона.
[RequireComponent(typeof(SpriteRenderer))]
public class CursorView : MonoBehaviour {
    private const int SortingOrder = 30;
    private const float WobbleFps = 5f;

    private SpriteRenderer _renderer;
    private Sprite[] _frames;

    public void Init(ElementsConfig elements) {
        _frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < _frames.Length; i++) {
            _frames[i] = elements.GetFrame("cursor", i);
        }

        _renderer = GetComponent<SpriteRenderer>();
        _renderer.sortingOrder = SortingOrder;
        Hide();
    }

    public void Show(Vector2Int cell) {
        _renderer.enabled = true;
        transform.position = ElementView.ToWorld(cell);
    }

    public void Hide() {
        _renderer.enabled = false;
    }

    private void Update() {
        _renderer.sprite = _frames[(int)(Time.time * WobbleFps) % _frames.Length];
    }
}
