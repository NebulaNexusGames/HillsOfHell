using CarGame.Effects;
using CarGame.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CarGame.UI
{
    /// <summary>Качество виньетки: насколько сильно затемнены углы экрана.</summary>
    public enum VignetteLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3
    }

    /// <summary>Режим сглаживания, применяемый к игровым камерам.</summary>
    public enum AntiAliasingLevel
    {
        Off = 0,
        FXAA = 1,
        SMAA = 2,
        TAA = 3
    }

    /// <summary>
    /// Игровые настройки: звук, обзор и графика. Хранятся в PlayerPrefs и
    /// применяются сразу, поэтому переживают смену сцен и перезапуск игры.
    ///
    /// Настройки применяются на старте любой сцены
    /// (<see cref="RuntimeInitializeOnLoadMethod"/>), так что и главное меню, и
    /// пауза, и игровая сцена видят одни и те же значения. UI лишь читает и
    /// пишет их через <see cref="SettingsPanel"/> и <see cref="GraphicsPanel"/>.
    /// </summary>
    public static class GameSettings
    {
        public const float MinSensitivity = 0.25f;
        public const float MaxSensitivity = 3f;

        /// <summary>Варианты лимита кадров. 0 — без ограничения.</summary>
        public static readonly int[] FpsOptions = { 30, 60, 120, 0 };

        private const string VolumeKey = "cargame.volume";
        private const string SensitivityKey = "cargame.sensitivity";
        private const string InvertYKey = "cargame.invertY";
        private const string PsxKey = "cargame.psx";
        private const string LightFlareKey = "cargame.lightFlare";
        private const string VignetteKey = "cargame.vignette";
        private const string CameraMotionKey = "cargame.cameraMotion";
        private const string AntiAliasingKey = "cargame.aa";
        private const string VSyncKey = "cargame.vsync";
        private const string FpsKey = "cargame.fps";

        /// <summary>
        /// Версия сохранённых настроек. При изменении дефолтов поднимаем её — и
        /// старые PlayerPrefs (например, громкость 1 с прошлых версий) сбрасываются
        /// один раз на новые значения по умолчанию. Иначе игроки не видели бы ни
        /// одну правку дефолтов: сохранённые ключи всегда перекрывают их.
        ///
        /// Новые ключи графики версию не требуют: без сохранённого значения
        /// PlayerPrefs и так отдаёт дефолт.
        /// </summary>
        private const string PrefsVersionKey = "cargame.prefsVersion";
        private const int CurrentPrefsVersion = 1;

        /// <summary>Событие изменения любой настройки.</summary>
        public static event System.Action Changed;

        public static float MasterVolume { get; private set; } = DefaultVolume;
        public static float Sensitivity { get; private set; } = 1f;
        public static bool InvertY { get; private set; }

        public static bool PsxEnabled { get; private set; } = true;
        public static bool LightFlareEnabled { get; private set; } = true;
        public static VignetteLevel Vignette { get; private set; } = VignetteLevel.High;
        public static bool CameraMotionEnabled { get; private set; } = true;
        public static AntiAliasingLevel AntiAliasing { get; private set; } = AntiAliasingLevel.FXAA;
        public static bool VSyncEnabled { get; private set; } = true;
        public static int FpsLimit { get; private set; }

        /// <summary>Значение громкости по умолчанию (без сохранённых настроек).</summary>
        public const float DefaultVolume = 0.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Load();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapLate()
        {
            // Камеры живут в сцене, поэтому сглаживание можно навесить только
            // после первой загрузки. При смене сцен применяем его заново.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Apply();
        }

        public static void Load()
        {
            if (PlayerPrefs.GetInt(PrefsVersionKey, 0) != CurrentPrefsVersion)
            {
                ResetToDefaultsNoLoad();
                PlayerPrefs.SetInt(PrefsVersionKey, CurrentPrefsVersion);
            }

            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
            Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
            InvertY = PlayerPrefs.GetInt(InvertYKey, 0) == 1;

            PsxEnabled = PlayerPrefs.GetInt(PsxKey, 1) == 1;
            LightFlareEnabled = PlayerPrefs.GetInt(LightFlareKey, 1) == 1;
            Vignette = (VignetteLevel)Mathf.Clamp(PlayerPrefs.GetInt(VignetteKey, (int)VignetteLevel.High), 0, 3);
            CameraMotionEnabled = PlayerPrefs.GetInt(CameraMotionKey, 1) == 1;
            AntiAliasing = (AntiAliasingLevel)Mathf.Clamp(PlayerPrefs.GetInt(AntiAliasingKey, (int)AntiAliasingLevel.FXAA), 0, 3);
            VSyncEnabled = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
            FpsLimit = PlayerPrefs.GetInt(FpsKey, 0);

            Apply();
        }

        public static void SetVolume(float value)
        {
            MasterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, MasterVolume);
            Apply();
            Changed?.Invoke();
        }

        public static void SetSensitivity(float value)
        {
            Sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            PlayerPrefs.SetFloat(SensitivityKey, Sensitivity);
            Apply();
            Changed?.Invoke();
        }

        public static void SetInvertY(bool value)
        {
            InvertY = value;
            PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
            Apply();
            Changed?.Invoke();
        }

        public static void SetPsxEnabled(bool value)
        {
            PsxEnabled = value;
            PlayerPrefs.SetInt(PsxKey, value ? 1 : 0);
            Apply();
            Changed?.Invoke();
        }

        public static void SetLightFlareEnabled(bool value)
        {
            LightFlareEnabled = value;
            PlayerPrefs.SetInt(LightFlareKey, value ? 1 : 0);
            Apply();
            Changed?.Invoke();
        }

        public static void SetVignette(VignetteLevel value)
        {
            Vignette = value;
            PlayerPrefs.SetInt(VignetteKey, (int)value);
            Apply();
            Changed?.Invoke();
        }

        public static void SetCameraMotionEnabled(bool value)
        {
            CameraMotionEnabled = value;
            PlayerPrefs.SetInt(CameraMotionKey, value ? 1 : 0);
            Apply();
            Changed?.Invoke();
        }

        public static void SetAntiAliasing(AntiAliasingLevel value)
        {
            AntiAliasing = value;
            PlayerPrefs.SetInt(AntiAliasingKey, (int)value);
            Apply();
            Changed?.Invoke();
        }

        public static void SetVSyncEnabled(bool value)
        {
            VSyncEnabled = value;
            PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
            Apply();
            Changed?.Invoke();
        }

        public static void SetFpsLimit(int value)
        {
            FpsLimit = value;
            PlayerPrefs.SetInt(FpsKey, value);
            Apply();
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            ResetToDefaultsNoLoad();
            Load();
        }

        private static void ResetToDefaultsNoLoad()
        {
            PlayerPrefs.DeleteKey(VolumeKey);
            PlayerPrefs.DeleteKey(SensitivityKey);
            PlayerPrefs.DeleteKey(InvertYKey);
            PlayerPrefs.DeleteKey(PsxKey);
            PlayerPrefs.DeleteKey(LightFlareKey);
            PlayerPrefs.DeleteKey(VignetteKey);
            PlayerPrefs.DeleteKey(CameraMotionKey);
            PlayerPrefs.DeleteKey(AntiAliasingKey);
            PlayerPrefs.DeleteKey(VSyncKey);
            PlayerPrefs.DeleteKey(FpsKey);
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>Множитель силы амбиентной виньетки по уровню качества.</summary>
        public static float VignetteScale(VignetteLevel level)
        {
            switch (level)
            {
                case VignetteLevel.Low: return 0f;
                case VignetteLevel.Medium: return 0.5f;
                case VignetteLevel.Ultra: return 1.6f;
                default: return 1f;
            }
        }

        /// <summary>Раздаёт настройки тем, кто их слушает.</summary>
        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
            PlayerController.SensitivityMultiplier = Sensitivity;
            PlayerController.InvertY = InvertY;
            PlayerController.CameraMotionEnabled = CameraMotionEnabled;

            PSXPostFXFeature.RuntimeEnabled = PsxEnabled;
            LensFlareFeature.RuntimeEnabled = LightFlareEnabled;
            PostFXManager.AmbientVignetteScale = VignetteScale(Vignette);

            Application.targetFrameRate = FpsLimit > 0 ? FpsLimit : -1;
            QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;

            ApplyAntiAliasing();
        }

        /// <summary>Навешивает выбранное сглаживание на все игровые камеры.</summary>
        public static void ApplyAntiAliasing()
        {
            AntialiasingMode mode;
            switch (AntiAliasing)
            {
                case AntiAliasingLevel.Off: mode = AntialiasingMode.None; break;
                case AntiAliasingLevel.SMAA: mode = AntialiasingMode.SubpixelMorphologicalAntiAliasing; break;
                case AntiAliasingLevel.TAA: mode = AntialiasingMode.TemporalAntiAliasing; break;
                default: mode = AntialiasingMode.FastApproximateAntialiasing; break;
            }

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera.cameraType != CameraType.Game) continue;

                camera.GetUniversalAdditionalCameraData().antialiasing = mode;
                camera.GetUniversalAdditionalCameraData().antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }
}
