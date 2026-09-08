using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Собирает префабы интерфейса в Assets/Resources/Ui: кнопку, слайдер громкости, панель паузы и главное меню.
// Меню Match Thee/Build UI Prefabs. Дальше их можно править в инспекторе; пересборка перезаписывает префабы.
public static class UiPrefabBuilder {
    private const string Folder = "Assets/Resources/Ui";
    private const string ElementsConfigPath = "Assets/Configs/ElementsConfig.asset";
    private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
    private static readonly Color PanelColor = new Color32(24, 24, 30, 255);
    private static readonly Color BorderColor = new Color32(70, 70, 82, 255);
    private static readonly Color TrackColor = new Color32(70, 70, 82, 255);
    private static readonly Color FillColor = new Color32(232, 232, 238, 255);
    private static readonly Color HoverColor = new Color(1f, 1f, 0.75f);
    private static readonly Color PressedColor = new Color(0.7f, 0.7f, 0.7f);
    private const int ButtonPixel = 5;
    private const int LabelPixel = 3;
    private const int TitlePixel = 11;
    private const float SliderWidth = 320f;
    private const float RowHeight = 48f;

    private static ElementsConfig _elements;

    [MenuItem("Match Thee/Build UI Prefabs")]
    public static void Build() {
        _elements = AssetDatabase.LoadAssetAtPath<ElementsConfig>(ElementsConfigPath);
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(Folder)) {
            AssetDatabase.CreateFolder("Assets/Resources", "Ui");
        }

        GameObject button = SavePrefab(BuildButton("PixelButton", "play_text"), "PixelButton");
        GameObject slider = SavePrefab(BuildVolumeRow("VolumeSlider", "music_label", true), "VolumeSlider");
        SavePrefab(BuildPausePanel(button, slider), "PausePanel");
        SavePrefab(BuildMainMenu(button, slider), "MainMenu");
        AssetDatabase.SaveAssets();
        Debug.Log($"Match Thee: префабы интерфейса собраны в {Folder}");
    }

    // Кнопка: пиксельная надпись, подсветка при наведении.
    private static GameObject BuildButton(string name, string spriteName) {
        GameObject obj = new(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(PixelText));
        obj.GetComponent<PixelText>().Setup(_elements, spriteName, ButtonPixel);
        Button button = obj.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = HoverColor;
        colors.pressedColor = PressedColor;
        button.colors = colors;
        return obj;
    }

    private static GameObject Instance(GameObject prefab, Transform parent, string name) {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        obj.name = name;
        return obj;
    }

    private static GameObject ButtonInstance(GameObject prefab, Transform parent, string name, string spriteName) {
        GameObject obj = Instance(prefab, parent, name);
        obj.GetComponent<PixelText>().Setup(_elements, spriteName, ButtonPixel);
        return obj;
    }

    // Строка громкости: подпись слева, слайдер справа.
    private static GameObject BuildVolumeRow(string name, string labelSprite, bool music) {
        GameObject row = new(name, typeof(RectTransform), typeof(LayoutElement), typeof(VolumeSlider));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(SliderWidth + 200f, RowHeight);
        LayoutElement layout = row.GetComponent<LayoutElement>();
        layout.preferredWidth = rowRect.sizeDelta.x;
        layout.preferredHeight = RowHeight;

        GameObject label = new("Label", typeof(RectTransform), typeof(Image), typeof(PixelText));
        label.transform.SetParent(row.transform, false);
        label.GetComponent<Image>().raycastTarget = false;
        label.GetComponent<PixelText>().Setup(_elements, labelSprite, LabelPixel);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;

        Slider slider = BuildSlider(row.transform);
        RectTransform sliderRect = slider.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(1f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.pivot = new Vector2(1f, 0.5f);
        sliderRect.anchoredPosition = Vector2.zero;
        sliderRect.sizeDelta = new Vector2(SliderWidth, RowHeight * 0.6f);

        SerializedObject volume = new(row.GetComponent<VolumeSlider>());
        volume.FindProperty("_slider").objectReferenceValue = slider;
        volume.FindProperty("_music").boolValue = music;
        volume.ApplyModifiedPropertiesWithoutUndo();
        return row;
    }

    private static GameObject VolumeInstance(GameObject prefab, Transform parent, string name, string labelSprite, bool music) {
        GameObject obj = Instance(prefab, parent, name);
        obj.transform.Find("Label").GetComponent<PixelText>().Setup(_elements, labelSprite, LabelPixel);
        SerializedObject volume = new(obj.GetComponent<VolumeSlider>());
        volume.FindProperty("_music").boolValue = music;
        volume.ApplyModifiedPropertiesWithoutUndo();
        return obj;
    }

    // Слайдер: тёмная дорожка, светлое заполнение, квадратная ручка.
    private static Slider BuildSlider(Transform parent) {
        GameObject root = new("Slider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);
        float height = RowHeight * 0.6f;

        RectTransform track = Image(root.transform, "Track", TrackColor, true);
        Stretch(track, new Vector2(0f, -height * 0.6f));

        RectTransform fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(root.transform, false);
        Stretch(fillArea, new Vector2(-height, -height * 0.6f));
        RectTransform fill = Image(fillArea, "Fill", FillColor, false);
        Stretch(fill, Vector2.zero);

        RectTransform handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(root.transform, false);
        Stretch(handleArea, new Vector2(-height, 0f));
        RectTransform handle = Image(handleArea, "Handle", FillColor, true);
        handle.sizeDelta = new Vector2(height, 0f);

        Slider slider = root.GetComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.5f;
        ColorBlock colors = slider.colors;
        colors.highlightedColor = HoverColor;
        colors.pressedColor = PressedColor;
        slider.colors = colors;
        return slider;
    }

    // Панель паузы: свой канвас, затемнение, панель с колонкой строк.
    private static GameObject BuildPausePanel(GameObject buttonPrefab, GameObject sliderPrefab) {
        GameObject root = Canvas("PausePanel", 200, typeof(PauseView));
        RectTransform backdrop = Image(root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.72f), true);
        Stretch(backdrop, Vector2.zero);

        RectTransform panel = Image(root.transform, "Panel", PanelColor, true);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = BorderColor;
        outline.effectDistance = new Vector2(2f, -2f);
        VerticalLayoutGroup column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(48, 48, 40, 40);
        column.spacing = 24f;
        column.childAlignment = TextAnchor.MiddleCenter;
        column.childControlWidth = false;
        column.childControlHeight = false;
        column.childForceExpandWidth = false;
        column.childForceExpandHeight = false;
        ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;

        VolumeInstance(sliderPrefab, panel, "Music", "music_label", true);
        VolumeInstance(sliderPrefab, panel, "Sounds", "sounds_label", false);
        GameObject resume = ButtonInstance(buttonPrefab, panel, "Resume", "resume_text");
        GameObject menu = ButtonInstance(buttonPrefab, panel, "Menu", "menu_text");
        GameObject quit = ButtonInstance(buttonPrefab, panel, "Quit", "quit_text");

        SerializedObject pause = new(root.GetComponent<PauseView>());
        pause.FindProperty("_resume").objectReferenceValue = resume.GetComponent<Button>();
        pause.FindProperty("_menu").objectReferenceValue = menu.GetComponent<Button>();
        pause.FindProperty("_quit").objectReferenceValue = quit.GetComponent<Button>();
        pause.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // Главное меню: название, две кнопки, две строки громкости внизу.
    private static GameObject BuildMainMenu(GameObject buttonPrefab, GameObject sliderPrefab) {
        GameObject root = Canvas("MainMenu", 100, typeof(MenuView));

        // Название из отдельных букв (TitleView собирает их при запуске): их можно таскать мышью.
        GameObject title = new("Title", typeof(RectTransform), typeof(TitleView));
        title.transform.SetParent(root.transform, false);
        SerializedObject titleView = new(title.GetComponent<TitleView>());
        titleView.FindProperty("_elements").objectReferenceValue = _elements;
        titleView.FindProperty("_pixelScale").intValue = TitlePixel;
        titleView.ApplyModifiedPropertiesWithoutUndo();
        Center(title.GetComponent<RectTransform>(), new Vector2(0f, 130f));

        GameObject play = ButtonInstance(buttonPrefab, root.transform, "Play", "play_text");
        Center(play.GetComponent<RectTransform>(), new Vector2(0f, -108f));
        GameObject quit = ButtonInstance(buttonPrefab, root.transform, "Quit", "quit_text");
        Center(quit.GetComponent<RectTransform>(), new Vector2(0f, -260f));

        GameObject music = VolumeInstance(sliderPrefab, root.transform, "Music", "music_label", true);
        Center(music.GetComponent<RectTransform>(), new Vector2(0f, -400f));
        GameObject sounds = VolumeInstance(sliderPrefab, root.transform, "Sounds", "sounds_label", false);
        Center(sounds.GetComponent<RectTransform>(), new Vector2(0f, -460f));

        SerializedObject menu = new(root.GetComponent<MenuView>());
        menu.FindProperty("_elements").objectReferenceValue = _elements;
        menu.FindProperty("_play").objectReferenceValue = play.GetComponent<Button>();
        menu.FindProperty("_quit").objectReferenceValue = quit.GetComponent<Button>();
        menu.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static GameObject Canvas(string name, int sortingOrder, System.Type component) {
        GameObject root = new(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), component);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.matchWidthOrHeight = 1f;
        return root;
    }

    private static RectTransform Image(Transform parent, string name, Color color, bool raycast) {
        GameObject obj = new(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return obj.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect, Vector2 sizeDelta) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }

    private static void Center(RectTransform rect, Vector2 offset) {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
    }

    private static GameObject SavePrefab(GameObject obj, string name) {
        string path = $"{Folder}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(obj, path);
        Object.DestroyImmediate(obj);
        return prefab;
    }
}
