using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Название из отдельных букв: каждая дрожит своим кадром. Как в три-в-ряд, свайп по букве меняет её
// с соседней; пробел — пустая клетка, свайп в его сторону только дёргает букву. Корона входит в спрайт T.
public class TitleView : MonoBehaviour {
    private const float WobbleFps = 5f;
    private const float ShiftDuration = 0.15f;
    private const float SwipeThreshold = 0.35f; // доля ширины буквы
    private const float BumpDuration = 0.22f;
    private const float BumpDistance = 0.3f;    // доля ширины буквы
    private const int LetterGap = 1;  // пикселей спрайта между буквами
    private const int SpaceWidth = 3; // пикселей спрайта на пробел
    private const int GlyphPad = 2;   // запас обводки в спрайте буквы с каждой стороны

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private string _text = "Match Thee";

    [SerializeField]
    private int _crownIndex = 6; // индекс буквы с короной (T в Thee)

    [SerializeField, Min(1)]
    private int _pixelScale = 11;

    private readonly List<Letter> _letters = new();
    private readonly List<Vector2> _cells = new(); // центры всех клеток, включая пробелы
    private readonly List<bool> _cellIsSpace = new();
    private RectTransform _rect;
    private float _letterWidth;

    private class Letter {
        public RectTransform Rect;
        public Image Image;
        public Sprite[] Frames;
        public int Cell;
        public int Index;
        public Vector2 DragStart;
        public bool Swiped;
        public float BumpProgress = 1f;
        public float BumpDirection;
    }

    private void Awake() {
        _rect = (RectTransform)transform;
        Build();
    }

    private void Build() {
        float x = 0f;
        List<(Sprite[] frames, float x, float width, bool space)> layout = new();
        for (int i = 0; i < _text.Length; i++) {
            char ch = _text[i];
            if (ch == ' ') {
                float spaceWidth = SpaceWidth * _pixelScale;
                layout.Add((null, x, spaceWidth, true));
                x += spaceWidth + LetterGap * _pixelScale;
                continue;
            }

            Sprite[] frames = LoadFrames(SpriteName(ch, i == _crownIndex));
            float width = (frames[0].rect.width - GlyphPad * 2f) * _pixelScale; // ширина самой буквы без запаса
            layout.Add((frames, x, width, false));
            x += width + LetterGap * _pixelScale;
            _letterWidth = Mathf.Max(_letterWidth, width);
        }

        float total = x - LetterGap * _pixelScale;
        float height = 0f;
        foreach ((Sprite[] frames, float _, float _, bool space) in layout) {
            if (!space) {
                height = frames[0].rect.height * _pixelScale;
                break;
            }
        }

        _rect.sizeDelta = new Vector2(total, height);

        int letterIndex = 0;
        for (int i = 0; i < layout.Count; i++) {
            (Sprite[] frames, float left, float width, bool space) = layout[i];
            Vector2 center = new(left + width / 2f - total / 2f, 0f);
            _cells.Add(center);
            _cellIsSpace.Add(space);
            if (space) {
                continue;
            }

            GameObject obj = new($"Letter{letterIndex}", typeof(RectTransform), typeof(Image), typeof(LetterDrag));
            obj.transform.SetParent(transform, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = frames[0];
            image.preserveAspect = true;
            RectTransform rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = frames[0].rect.size * _pixelScale;
            rect.anchoredPosition = center;

            Letter letter = new() { Rect = rect, Image = image, Frames = frames, Cell = i, Index = letterIndex };
            obj.GetComponent<LetterDrag>().Init(this, letter);
            _letters.Add(letter);
            letterIndex++;
        }
    }

    private void Update() {
        int frame = (int)(Time.unscaledTime * WobbleFps);
        foreach (Letter letter in _letters) {
            // Соседние буквы берут разные кадры — дрожат вразнобой.
            letter.Image.sprite = letter.Frames[(frame + letter.Index) % letter.Frames.Length];

            // Плавный проезд к своей клетке плюс «дёрганье» при неудачном свайпе.
            Vector2 target = _cells[letter.Cell];
            Vector2 bump = Vector2.zero;
            if (letter.BumpProgress < 1f) {
                letter.BumpProgress = Mathf.Min(1f, letter.BumpProgress + Time.unscaledDeltaTime / BumpDuration);
                bump = Vector2.right * (letter.BumpDirection * _letterWidth * BumpDistance * Mathf.Sin(letter.BumpProgress * Mathf.PI));
            }

            Vector2 current = letter.Rect.anchoredPosition - bump;
            float step = Vector2.Distance(current, target) * Time.unscaledDeltaTime / ShiftDuration + 1f;
            letter.Rect.anchoredPosition = Vector2.MoveTowards(current, target, step) + bump;
        }
    }

    private void BeginDrag(Letter letter, PointerEventData eventData) {
        letter.Swiped = false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out letter.DragStart);
    }

    // Свайп: сдвиг мыши больше порога — одна попытка обмена с соседом в эту сторону, до следующего нажатия.
    private void Drag(Letter letter, PointerEventData eventData) {
        if (letter.Swiped || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out Vector2 local)) {
            return;
        }

        float delta = local.x - letter.DragStart.x;
        if (Mathf.Abs(delta) < _letterWidth * SwipeThreshold) {
            return;
        }

        letter.Swiped = true;
        Swipe(letter, delta > 0f ? 1 : -1);
    }

    private void Swipe(Letter letter, int direction) {
        int target = letter.Cell + direction;
        Letter other = target >= 0 && target < _cells.Count && !_cellIsSpace[target] ? _letters.Find(candidate => candidate.Cell == target) : null;
        if (other == null) {
            letter.BumpProgress = 0f;
            letter.BumpDirection = direction;
            return;
        }

        other.Cell = letter.Cell;
        letter.Cell = target;
    }

    private Sprite[] LoadFrames(string name) {
        Sprite[] frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = _elements.GetFrame(name, i);
        }

        return frames;
    }

    // Имя спрайта буквы: glyph_<буква>, заглавные — glyph_cap_<буква>, с короной — суффикс _crown.
    private static string SpriteName(char ch, bool crown) {
        string name = "glyph_" + (char.IsUpper(ch) ? "cap_" : string.Empty) + char.ToLowerInvariant(ch);
        return crown ? name + "_crown" : name;
    }

    // Буква ловит перетаскивание и передаёт его названию.
    private class LetterDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
        private TitleView _title;
        private Letter _letter;

        public void Init(TitleView title, Letter letter) {
            _title = title;
            _letter = letter;
        }

        public void OnBeginDrag(PointerEventData eventData) {
            _title.BeginDrag(_letter, eventData);
        }

        public void OnDrag(PointerEventData eventData) {
            _title.Drag(_letter, eventData);
        }

        public void OnEndDrag(PointerEventData eventData) {
            _letter.Swiped = false;
        }
    }
}
