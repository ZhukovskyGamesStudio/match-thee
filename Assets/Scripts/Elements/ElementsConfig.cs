using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Все кадры всех спрайтов из Assets/Sprites/Elements; заполняется сборщиком сцены.
[CreateAssetMenu(fileName = "ElementsConfig", menuName = "Scriptable Objects/ElementsConfig")]
public class ElementsConfig : ScriptableObject {
    [field: SerializeField]
    public List<Sprite> Frames { get; private set; } = new();

    private Dictionary<string, Sprite> _byName;

    public Sprite GetFrame(ElementKind kind, int frame) {
        return GetFrame(kind.ToString().ToLowerInvariant(), frame);
    }

    // Для спрайтов, которые не элементы мира: курсор и прочая служебная графика.
    public Sprite GetFrame(string name, int frame) {
        _byName ??= Frames.Where(sprite => sprite != null).ToDictionary(sprite => sprite.name);
        return _byName.TryGetValue(ElementRules.SpriteName(name, frame), out Sprite result) ? result : null;
    }

#if UNITY_EDITOR
    public void SetFrames(IEnumerable<Sprite> frames) {
        Frames = frames.ToList();
        _byName = null;
    }
#endif
}
