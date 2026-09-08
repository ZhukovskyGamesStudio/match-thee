using UnityEngine;
using UnityEngine.UI;

// Слайдер громкости (музыка или звуки): при показе берёт значение из настроек, при движении пишет их.
public class VolumeSlider : MonoBehaviour {
    [SerializeField]
    private Slider _slider;

    [SerializeField]
    private bool _music;

    private void OnEnable() {
        _slider.SetValueWithoutNotify(_music ? SoundView.MusicVolume : SoundView.SoundVolume);
        _slider.onValueChanged.AddListener(OnChanged);
    }

    private void OnDisable() {
        _slider.onValueChanged.RemoveListener(OnChanged);
    }

    private void OnChanged(float value) {
        if (_music) {
            SoundView.MusicVolume = value;
        } else {
            SoundView.SoundVolume = value;
        }
    }
}
