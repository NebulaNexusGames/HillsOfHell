using CarGame.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Панель настроек: громкость, чувствительность и инверсия обзора.
    /// Работает с <see cref="GameSettings"/> напрямую, поэтому одна и та же
    /// панель годится и для главного меню, и для паузы.
    ///
    /// Строит панель инструмент Tools/CarGame/Menus/Create ... — здесь только
    /// связи: на старте контролы подтягивают текущие значения, а изменения
    /// сразу уходят в настройки.
    /// </summary>
    [DisallowMultipleComponent]
    public class SettingsPanel : MonoBehaviour
    {
        [Header("Контролы")]
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private Button resetButton;

        [Header("Подписи значений")]
        [SerializeField] private TMP_Text volumeValue;
        [SerializeField] private TMP_Text sensitivityValue;

        [Header("Язык и управление")]
        [Tooltip("Кнопка переключения языка")]
        [SerializeField] private Button languageButton;
        [Tooltip("Надпись на кнопке языка: РУССКИЙ / ENGLISH")]
        [SerializeField] private TMP_Text languageText;
        [Tooltip("Кнопка, открывающая панель управления")]
        [SerializeField] private Button controlsButton;
        [Tooltip("Корень панели управления (ControlsPanel), по умолчанию скрыт")]
        [SerializeField] private GameObject controlsPanel;
        [Tooltip("Кнопка, открывающая панель графики")]
        [SerializeField] private Button graphicsButton;
        [Tooltip("Корень панели графики (GraphicsPanel), по умолчанию скрыт")]
        [SerializeField] private GameObject graphicsPanel;

        private bool initialised;

        private void Awake()
        {
            GameSettings.Load();
            SyncControls();

            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            if (sensitivitySlider != null) sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            if (invertYToggle != null) invertYToggle.onValueChanged.AddListener(OnInvertChanged);
            if (resetButton != null) resetButton.onClick.AddListener(OnReset);
            if (languageButton != null) languageButton.onClick.AddListener(ToggleLanguage);
            if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
            if (graphicsButton != null) graphicsButton.onClick.AddListener(OpenGraphics);

            Localization.Changed += OnLanguageChanged;

            initialised = true;
            UpdateLabels();
        }

        private void OnDestroy()
        {
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
            if (sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
            if (invertYToggle != null) invertYToggle.onValueChanged.RemoveListener(OnInvertChanged);
            if (resetButton != null) resetButton.onClick.RemoveListener(OnReset);
            if (languageButton != null) languageButton.onClick.RemoveListener(ToggleLanguage);
            if (controlsButton != null) controlsButton.onClick.RemoveListener(OpenControls);
            if (graphicsButton != null) graphicsButton.onClick.RemoveListener(OpenGraphics);

            Localization.Changed -= OnLanguageChanged;
        }

        private void SyncControls()
        {
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
                volumeSlider.value = GameSettings.MasterVolume;
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GameSettings.MinSensitivity;
                sensitivitySlider.maxValue = GameSettings.MaxSensitivity;
                sensitivitySlider.value = GameSettings.Sensitivity;
            }

            if (invertYToggle != null) invertYToggle.isOn = GameSettings.InvertY;
        }

        private void OnVolumeChanged(float value)
        {
            if (!initialised) return;
            GameSettings.SetVolume(value);
            UpdateLabels();
        }

        private void OnSensitivityChanged(float value)
        {
            if (!initialised) return;
            GameSettings.SetSensitivity(value);
            UpdateLabels();
        }

        private void OnInvertChanged(bool value)
        {
            if (!initialised) return;
            GameSettings.SetInvertY(value);
        }

        private void OnReset()
        {
            GameSettings.ResetToDefaults();
            SyncControls();
            UpdateLabels();
        }

        private void ToggleLanguage()
        {
            Localization.Toggle();
        }

        private void OnLanguageChanged()
        {
            UpdateLanguageLabel();
        }

        private void UpdateLanguageLabel()
        {
            if (languageText == null) return;
            languageText.text = Localization.IsEnglish ? "ENGLISH" : "РУССКИЙ";
        }

        private void OpenControls()
        {
            if (controlsPanel != null) controlsPanel.SetActive(true);
        }

        private void OpenGraphics()
        {
            if (graphicsPanel != null) graphicsPanel.SetActive(true);
        }

        private void UpdateLabels()
        {
            if (volumeValue != null)
                volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";

            if (sensitivityValue != null)
                sensitivityValue.text = GameSettings.Sensitivity.ToString("0.00") + "x";

            UpdateLanguageLabel();
        }
    }
}
