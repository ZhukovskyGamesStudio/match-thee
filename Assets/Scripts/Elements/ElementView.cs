using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ElementView : MonoBehaviour {
    private const float WobbleFps = 5f;
    private const float MoveSpeed = 14f; // клеток в секунду

    private SpriteRenderer _renderer;
    private Sprite[] _frames;
    private Vector3 _targetPosition;

    public LevelEntity Entity { get; private set; }

    public void Init(LevelEntity entity, ElementsConfig elements, int sortingOrder) {
        Entity = entity;
        _renderer = GetComponent<SpriteRenderer>();
        _renderer.sortingOrder = sortingOrder;

        _frames = new Sprite[ElementRules.WobbleFrames];
        for (int i = 0; i < _frames.Length; i++) {
            _frames[i] = elements.GetFrame(entity.Kind, i);
        }

        _targetPosition = ToWorld(entity.Position);
        transform.position = _targetPosition;
    }

    public void MoveTo(Vector2Int cell) {
        Vector3 next = ToWorld(cell);
        if (ElementRules.IsCreature(Entity.Kind) && !Mathf.Approximately(next.x, _targetPosition.x)) {
            _renderer.flipX = next.x < _targetPosition.x;
        }

        _targetPosition = next;
    }

    private void Update() {
        // Дрожание как в Baba Is You: все элементы переключают кадры синхронно.
        int frame = (int)(Time.time * WobbleFps) % _frames.Length;
        _renderer.sprite = _frames[frame];
        transform.position = Vector3.MoveTowards(transform.position, _targetPosition, MoveSpeed * Time.deltaTime);
    }

    public static Vector3 ToWorld(Vector2Int cell) {
        return new Vector3(cell.x, cell.y, 0f);
    }
}
