using System.Collections.Generic;
using UnityEngine;

// Строит уровень из конфига и двигает вью вслед за моделью.
public class LevelView : MonoBehaviour {
    private const int FloorOrder = 0;
    private const int ObjectOrder = 10;
    private const int HeroOrder = 20;

    [SerializeField]
    private LevelConfig _level;

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private Camera _camera;

    private readonly Dictionary<LevelEntity, ElementView> _views = new();

    public LevelModel Model { get; private set; }

    private void Awake() {
        Model = new LevelModel(_level.Rows, _level.KindOf);
        Model.EntityMoved += OnEntityMoved;

        foreach (LevelEntity floor in Model.Floors) {
            Spawn(floor, FloorOrder);
        }

        foreach (LevelEntity entity in Model.Objects) {
            Spawn(entity, entity.Kind == ElementKind.Hero ? HeroOrder : ObjectOrder);
        }

        FitCamera();
    }

    private void OnDestroy() {
        if (Model != null) {
            Model.EntityMoved -= OnEntityMoved;
        }
    }

    private void Spawn(LevelEntity entity, int sortingOrder) {
        GameObject viewObject = new(entity.Kind.ToString(), typeof(SpriteRenderer), typeof(ElementView));
        viewObject.transform.SetParent(transform, false);

        ElementView view = viewObject.GetComponent<ElementView>();
        view.Init(entity, _elements, sortingOrder);
        _views[entity] = view;
    }

    private void OnEntityMoved(LevelEntity entity) {
        if (_views.TryGetValue(entity, out ElementView view)) {
            view.MoveTo(entity.Position);
        }
    }

    // Камера смотрит в центр уровня и вмещает его целиком при любом соотношении сторон.
    private void FitCamera() {
        if (_camera == null) {
            _camera = Camera.main;
        }

        if (_camera == null) {
            return;
        }

        _camera.orthographic = true;
        _camera.transform.position = new Vector3((Model.Width - 1) / 2f, (Model.Height - 1) / 2f, -10f);

        float halfHeight = Model.Height / 2f;
        float halfWidth = Model.Width / 2f / _camera.aspect;
        _camera.orthographicSize = Mathf.Max(halfHeight, halfWidth) + 1f;
    }
}
