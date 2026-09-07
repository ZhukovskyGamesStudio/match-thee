using System.Collections.Generic;
using UnityEngine;

// 8-битные звуки и музыка из Resources/Audio: подписывается на события модели, музыка крутится по кругу.
// На троне обычная мелодия плавно сменяется королевской и обратно. Клипы грузятся заранее, чтобы не запаздывать.
// Один и тот же звук в одном кадре играет один раз (обмен с трона двигает два элемента сразу).
public class SoundView : MonoBehaviour {
    private const string Folder = "Audio/";
    private const float SoundVolume = 0.6f;
    private const float MusicVolume = 0.3f;
    private const float MusicFadeDuration = 1.5f;
    private const int DspBufferSize = 256; // «лучшая задержка»
    private static readonly string[] AllClips = {
        "step", "push", "bump", "vanish", "pickup", "unlock", "transform", "untransform", "win", "restart", "music", "music_throne",
    };

    private readonly Dictionary<string, AudioClip> _clips = new();
    private readonly Dictionary<string, int> _lastFrame = new();
    private AudioSource _sounds;
    private AudioSource _music;
    private AudioSource _throneMusic;
    private WorldModel _model;

    public void Init(WorldModel model) {
        _model = model;
        EnsureLowLatency();
        _sounds = gameObject.AddComponent<AudioSource>();
        _sounds.playOnAwake = false;

        foreach (string name in AllClips) {
            AudioClip clip = Load(name);
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) {
                clip.LoadAudioData();
            }
        }

        _music = CreateMusic("music", MusicVolume);
        _throneMusic = CreateMusic("music_throne", 0f);

        model.EntityMoved += OnEntityMoved;
        model.GameWon += OnGameWon;
        model.Restored += OnRestored;
        model.Inventory.Added += OnResourceAdded;
    }

    private void OnDestroy() {
        if (_model == null) {
            return;
        }

        _model.EntityMoved -= OnEntityMoved;
        _model.GameWon -= OnGameWon;
        _model.Restored -= OnRestored;
        _model.Inventory.Added -= OnResourceAdded;
    }

    // Звук шага должен идти в момент нажатия: буфер звукового движка минимальный. Reset останавливает
    // все источники, поэтому вызывается до их создания и только если буфер действительно больше.
    private static void EnsureLowLatency() {
        AudioConfiguration config = AudioSettings.GetConfiguration();
        if (config.dspBufferSize <= DspBufferSize) {
            return;
        }

        config.dspBufferSize = DspBufferSize;
        AudioSettings.Reset(config);
    }

    // Обе мелодии крутятся всё время, меняется только громкость — переход плавный, без стыков.
    private void Update() {
        if (_model == null) {
            return;
        }

        bool throne = _model.IsHeroOnThrone;
        float step = Time.deltaTime / MusicFadeDuration * MusicVolume;
        _music.volume = Mathf.MoveTowards(_music.volume, throne ? 0f : MusicVolume, step);
        _throneMusic.volume = Mathf.MoveTowards(_throneMusic.volume, throne ? MusicVolume : 0f, step);
    }

    private AudioSource CreateMusic(string name, float volume) {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.volume = volume;
        source.clip = Load(name);
        if (source.clip != null) {
            source.Play();
        }

        return source;
    }

    public void PlayDelayed(string name, float delay) {
        if (delay <= 0f) {
            Play(name);
            return;
        }

        StartCoroutine(PlayAfter(name, delay));
    }

    private System.Collections.IEnumerator PlayAfter(string name, float delay) {
        yield return new WaitForSeconds(delay);
        Play(name);
    }

    public void Play(string name) {
        if (_lastFrame.TryGetValue(name, out int frame) && frame == Time.frameCount) {
            return;
        }

        _lastFrame[name] = Time.frameCount;
        AudioClip clip = Load(name);
        if (clip != null) {
            _sounds.PlayOneShot(clip, SoundVolume);
        }
    }

    private AudioClip Load(string name) {
        if (!_clips.TryGetValue(name, out AudioClip clip)) {
            clip = Resources.Load<AudioClip>(Folder + name);
            _clips[name] = clip;
        }

        return clip;
    }

    private void OnEntityMoved(WorldEntity entity) {
        Play(entity == _model.Hero ? "step" : "push");
    }

    private void OnGameWon() {
        Play("win");
    }

    private void OnRestored() {
        Play("restart");
    }

    // Ряд из пяти (ресурс на все экраны) звучит как фанфара, ряд из четырёх — обычный «+1».
    private void OnResourceAdded(ElementKind kind, int amount, bool everywhere) {
        Play(everywhere ? "unlock" : "pickup");
    }
}
