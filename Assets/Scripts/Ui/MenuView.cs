using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Главное меню: префаб Resources/Ui/MainMenu — название с короной, кнопки «Играть» и «Выход», громкость.
// Клик по «Играть» (или Enter/пробел) загружает игровую сцену. В WebGL кнопки «Выход» нет.
// Фоном играет королевская мелодия, громкость следует за слайдером.
public class MenuView : MonoBehaviour {
    private const string GameScene = "GameScene";
    private const float CursorCells = 18f; // курсор размером с клетку игрового экрана
    private const string MusicClip = "Audio/music_throne";

    [SerializeField]
    private ElementsConfig _elements;

    [SerializeField]
    private Button _play;

    [SerializeField]
    private Button _quit;

    private PixelCursor _pointer;
    private AudioSource _music;
    private int _lastHeight;

    private void Awake() {
        if (FindAnyObjectByType<EventSystem>() == null) {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        _play.onClick.AddListener(Play);
        _quit.onClick.AddListener(Application.Quit);
        _quit.gameObject.SetActive(Application.platform != RuntimePlatform.WebGLPlayer);
        _pointer = PixelCursor.Create(transform, _elements, Screen.height / CursorCells);
        _pointer.Visible = true;

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.clip = Resources.Load<AudioClip>(MusicClip);
        _music.volume = SoundView.MusicVolume;
        if (_music.clip != null) {
            _music.Play();
        }
    }

    private void Update() {
        if (Screen.height != _lastHeight) {
            _lastHeight = Screen.height;
            _pointer.SetSize(Screen.height / CursorCells);
        }

        _music.volume = SoundView.MusicVolume;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) {
            Play();
        }
    }

    private static void Play() {
        SceneManager.LoadScene(GameScene);
    }
}
