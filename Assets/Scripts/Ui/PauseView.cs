using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Пауза по Escape: префаб Resources/Ui/PausePanel (свой канвас поверх всего). Пока пауза, время игры стоит,
// музыка играет. Слайдеры громкости и кнопки «Продолжить», «В меню», «Выход» настраиваются в префабе.
// В WebGL кнопки «Выход» нет: браузер не закрыть.
public class PauseView : MonoBehaviour {
    private const string PrefabPath = "Ui/PausePanel";
    private const string MenuScene = "MenuScene";

    [SerializeField]
    private Button _resume;

    [SerializeField]
    private Button _menu;

    [SerializeField]
    private Button _quit;

    public bool IsPaused { get; private set; }

    public static PauseView Create() {
        GameObject prefab = Resources.Load<GameObject>(PrefabPath);
        PauseView pause = Instantiate(prefab).GetComponent<PauseView>();
        pause.gameObject.SetActive(false);
        return pause;
    }

    private void Awake() {
        _resume.onClick.AddListener(Resume);
        _menu.onClick.AddListener(ToMenu);
        _quit.onClick.AddListener(Quit);
        _quit.gameObject.SetActive(Application.platform != RuntimePlatform.WebGLPlayer);
    }

    public void Toggle() {
        if (IsPaused) {
            Resume();
        } else {
            Pause();
        }
    }

    private void Pause() {
        IsPaused = true;
        Time.timeScale = 0f;
        gameObject.SetActive(true);
    }

    private void Resume() {
        IsPaused = false;
        Time.timeScale = 1f;
        gameObject.SetActive(false);
    }

    private void ToMenu() {
        Resume();
        SceneManager.LoadScene(MenuScene);
    }

    private static void Quit() {
        Time.timeScale = 1f;
        Application.Quit();
    }

    private void OnDestroy() {
        Time.timeScale = 1f;
    }
}
