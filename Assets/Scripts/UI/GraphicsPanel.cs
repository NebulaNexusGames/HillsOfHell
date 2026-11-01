using CarGame.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Панель графических настроек: PSX-эффект, световые блики, качество
    /// виньетки, тряска камеры, сглаживание, вертикальная синхронизация и лимит
    /// кадров. Работает с <see cref="GameSettings"/> напрямую, поэтому одна и та
    /// же панель годится и для главного меню, и для паузы.
    ///
    /// Строит панель инструмент Tools/CarGame/Menus/Create ... — здесь только
    /// связи: на старте контролы подтягивают текущие значения, а изменения сразу
    /// уходят в настройки.
    ///
    /// Как и <see cref="ControlsPanel"/>, это верхнеуровневое окно со своим слоем
    /// затемнения: пока оно открыто, остальные меню скрыты, игра на паузе, а Esc
    /// не должен снимать паузу (см. <see cref="IsOpen"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public class GraphicsPanel : MonoBehaviour
    {
        [Header("Ссылки (заполняет инструмент сборки меню)")]
        [Tooltip("Слой затемнения позади меню. Включается вместе с меню")]
        [SerializeField] private GameObject dimLayer;
        [SerializeField] private Toggle psxToggle;
        [SerializeField] private Toggle flareToggle;
        [SerializeField] private Button vignetteButton;
        [SerializeField] private TMP_Text vignetteValue;
        [SerializeField] private Toggle shakeToggle;
        [SerializeField] private Button aaButton;
        [SerializeField] private TMP_Text aaValue;
        [SerializeField] private Toggle vsyncToggle;
        [SerializeField] private Button fpsButton;
        [SerializeField] private TMP_Text fpsValue;
        [SerializeField] private Button backButton;

        /// <summary>Почти непрозрачный чёрный фон: за меню не должно быть видно других окон.</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.92f);

        private bool wired;
        private bool initialised;
        private float savedTimeScale = 1f;

        /// <summary>Открыто ли меню графики. Пока открыто, Esc не должен снимать паузу.</summary>
        public static bool IsOpen { get; private set; }

        private void Awake()
        {
            Wire();
        }

        private void OnEnable()
        {
            IsOpen = true;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (dimLayer != null)
            {
                dimLayer.SetActive(true);
                Image dimImage = dimLayer.GetComponent<Image>();
                if (dimImage != null) dimImage.color = DimColor;
            }

            // Прячем остальные меню: за графикой не должно просвечивать
            // ни настроек, ни паузы, ни главного меню.
            MenuManager.HideOthers(transform, dimLayer);

            SyncControls();

            Localization.Changed += RefreshLabels;
            RefreshLabels();
        }

        private void OnDisable()
        {
            IsOpen = false;
            Localization.Changed -= RefreshLabels;

            MenuManager.Restore();

            if (dimLayer != null) dimLayer.SetActive(false);

            Time.timeScale = savedTimeScale;
        }

        private void OnDestroy()
        {
            Localization.Changed -= RefreshLabels;
        }

        private void Wire()
        {
            if (wired) return;
            wired = true;

            if (psxToggle != null) psxToggle.onValueChanged.AddListener(OnPsxChanged);
            if (flareToggle != null) flareToggle.onValueChanged.AddListener(OnFlareChanged);
            if (shakeToggle != null) shakeToggle.onValueChanged.AddListener(OnShakeChanged);
            if (vsyncToggle != null) vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);
            if (vignetteButton != null) vignetteButton.onClick.AddListener(CycleVignette);
            if (aaButton != null) aaButton.onClick.AddListener(CycleAntiAliasing);
            if (fpsButton != null) fpsButton.onClick.AddListener(CycleFps);
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        }

        private void SyncControls()
        {
            initialised = false;

            if (psxToggle != null) psxToggle.isOn = GameSettings.PsxEnabled;
            if (flareToggle != null) flareToggle.isOn = GameSettings.LightFlareEnabled;
            if (shakeToggle != null) shakeToggle.isOn = GameSettings.CameraMotionEnabled;
            if (vsyncToggle != null) vsyncToggle.isOn = GameSettings.VSyncEnabled;

            initialised = true;
            RefreshLabels();
        }

        private void OnPsxChanged(bool value)
        {
            if (!initialised) return;
            GameSettings.SetPsxEnabled(value);
        }

        private void OnFlareChanged(bool value)
        {
            if (!initialised) return;
            GameSettings.SetLightFlareEnabled(value);
        }

        private void OnShakeChanged(bool value)
        {
            if (!initialised) return;
            GameSettings.SetCameraMotionEnabled(value);
        }

        private void OnVSyncChanged(bool value)
        {
            if (!initialised) return;
            GameSettings.SetVSyncEnabled(value);
        }

        private void CycleVignette()
        {
            int next = ((int)GameSettings.Vignette + 1) % 4;
            GameSettings.SetVignette((VignetteLevel)next);
            RefreshLabels();
        }

        private void CycleAntiAliasing()
        {
            int next = ((int)GameSettings.AntiAliasing + 1) % 4;
            GameSettings.SetAntiAliasing((AntiAliasingLevel)next);
            RefreshLabels();
        }

        private void CycleFps()
        {
            int[] options = GameSettings.FpsOptions;
            int index = 0;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == GameSettings.FpsLimit)
                {
                    index = i;
                    break;
                }
            }

            index = (index + 1) % options.Length;
            GameSettings.SetFpsLimit(options[index]);
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (vignetteValue != null) vignetteValue.text = VignetteLabel(GameSettings.Vignette);
            if (aaValue != null) aaValue.text = AntiAliasingLabel(GameSettings.AntiAliasing);
            if (fpsValue != null) fpsValue.text = FpsLabel(GameSettings.FpsLimit);
        }

        private static string VignetteLabel(VignetteLevel level)
        {
            switch (level)
            {
                case VignetteLevel.Low: return Localization.Keys("НИЗКОЕ", "LOW");
                case VignetteLevel.Medium: return Localization.Keys("СРЕДНЕЕ", "MEDIUM");
                case VignetteLevel.Ultra: return Localization.Keys("МАКСИМУМ", "ULTRA");
                default: return Localization.Keys("ВЫСОКОЕ", "HIGH");
            }
        }

        private static string AntiAliasingLabel(AntiAliasingLevel level)
        {
            switch (level)
            {
                case AntiAliasingLevel.Off: return Localization.Keys("ВЫКЛ", "OFF");
                case AntiAliasingLevel.SMAA: return "SMAA";
                case AntiAliasingLevel.TAA: return "TAA";
                default: return "FXAA";
            }
        }

        private static string FpsLabel(int limit)
        {
            return limit > 0 ? limit.ToString() : Localization.Keys("БЕЗ ОГРАНИЧЕНИЯ", "UNLIMITED");
        }

        private void OnBackClicked()
        {
            gameObject.SetActive(false);
        }
    }
}
