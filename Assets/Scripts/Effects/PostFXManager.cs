using CarGame.Combat;
using CarGame.Player;
using CarGame.UI;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarGame.Effects
{
    /// <summary>
    /// Единый пост-процессинг сцены: все экранные эффекты (виньетка урона и
    /// лечения, пульс при низком здоровье, белая вспышка с затемнением при
    /// смерти, произвольные вспышки) живут в одном месте — на одном объекте
    /// PostFXManager. Один такой компонент на игровую сцену заменяет разрозненные
    /// виньетки на игроке и машине.
    ///
    /// Эффекты выполняются НАСТОЯЩЕЙ постобработкой, а не оверлеем на худе:
    /// менеджер выставляет глобальные свойства виньетки (_DamageVignetteIntensity
    /// и др.), которые читает URP-проход DamageVignetteFeature на общем рендерере.
    /// Так сцена темнеет/заливается цветом честно — после отрисовки мира и UI.
    ///
    /// При гибели игрока менеджер проигрывает быструю белую вспышку, переходящую
    /// во тьму, и плавно (не мгновенно) опускает до нуля громкость всех
    /// AudioSource сцены. После возрождения звук так же плавно возвращается, а
    /// виньетка гаснет. При загрузке сцены без менеджера (меню, экран смерти)
    /// глобальные свойства сбрасываются, чтобы нигде не оставалось чужой темноты.
    /// </summary>
    [DisallowMultipleComponent]
    public class PostFXManager : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_DamageVignetteIntensity");
        private static readonly int ColorId = Shader.PropertyToID("_DamageVignetteColor");
        private static readonly int InnerId = Shader.PropertyToID("_DamageVignetteInner");
        private static readonly int SoftnessId = Shader.PropertyToID("_DamageVignetteSoftness");
        private static readonly int SolidId = Shader.PropertyToID("_DamageVignetteSolid");

        /// <summary>
        /// Множитель силы постоянной виньетки из настроек графики. 0 — углы не
        /// затемняются, 1 — как задумано в инспекторе, больше — плотнее.
        /// Статический, чтобы настройки применялись раньше, чем найдётся менеджер.
        /// </summary>
        public static float AmbientVignetteScale = 1f;

        [Header("Урон")]
        [Tooltip("Цвет виньетки при получении урона")]
        [SerializeField] private Color damageColor = new Color(0.72f, 0.04f, 0.04f, 1f);
        [Range(0f, 1f)]
        [Tooltip("Самый сильный оттенок вспышки")]
        [SerializeField] private float maxAlpha = 0.8f;
        [Range(0f, 1f)]
        [Tooltip("Минимальная видимая интенсивность вспышки")]
        [SerializeField] private float minAlpha = 0.22f;
        [Tooltip("Насколько тяжесть удара усиливает вспышку")]
        [SerializeField] private float damageSensitivity = 1.6f;
        [Tooltip("Сколько вспышка держится на максимуме, сек")]
        [SerializeField] private float holdDuration = 0.1f;
        [Tooltip("За сколько вспышка гаснет, сек")]
        [SerializeField] private float fadeDuration = 0.5f;

        [Header("Лечение")]
        [Tooltip("Цвет виньетки при лечении")]
        [SerializeField] private Color healColor = new Color(0.16f, 0.72f, 0.24f, 1f);
        [Tooltip("Насколько лечение усиливает зелёную вспышку")]
        [SerializeField] private float healSensitivity = 1.2f;

        [Header("Низкое здоровье")]
        [Tooltip("Включить постоянную «пульсирующую» виньетку при низком здоровье")]
        [SerializeField] private bool lowHealthEnabled = true;
        [Range(0f, 1f)]
        [Tooltip("Доля здоровья, ниже которой начинает пульсировать виньетка")]
        [SerializeField] private float lowHealthThreshold = 0.35f;
        [Tooltip("Цвет пульса при низком здоровье")]
        [SerializeField] private Color pulseColor = new Color(0.5f, 0.015f, 0.015f, 1f);
        [Range(0f, 1f)]
        [Tooltip("Минимальная интенсивность пульса при низком здоровье")]
        [SerializeField] private float pulseMinAlpha = 0.15f;
        [Range(0f, 1f)]
        [Tooltip("Максимальная интенсивность пульса при низком здоровье")]
        [SerializeField] private float pulseMaxAlpha = 0.5f;
        [Tooltip("Скорость пульсации, гц")]
        [SerializeField] private float pulseRate = 2.2f;

        [Header("Форма")]
        [Range(0f, 1f)]
        [Tooltip("Радиус прозрачного центра виньетки")]
        [SerializeField] private float innerRadius = 0.32f;
        [Range(0.05f, 1f)]
        [Tooltip("Ширина перехода от прозрачного к плотному")]
        [SerializeField] private float softness = 0.7f;

        [Header("Постоянная виньетка")]
        [Tooltip("Всегда лёгкая чёрная виньетка в углах, как в Hello Neighbor")]
        [SerializeField] private bool ambientVignette = true;
        [Range(0f, 1f)]
        [Tooltip("Толщина постоянной чёрной виньетки")]
        [SerializeField] private float ambientVignetteAlpha = 0.2f;

        [Header("Блики")]
        [Tooltip("Включить объёмный bloom (блики в глаза от яркого света) и лёгкую тёплую гамму")]
        [SerializeField] private bool glareEnabled = true;
        [Range(0f, 1.5f)]
        [Tooltip("Сила bloom-бликов")]
        [SerializeField] private float glareIntensity = 0.45f;
        [Tooltip("Яркость, выше которой свет начинает «сиять». Держим выше 1, чтобы обычный белый текст и иконки UI не светились — сияют только по-настоящему яркие объекты")]
        [SerializeField] private float bloomThreshold = 1.5f;
        [Tooltip("Тёплый оттенок бликов")]
        [SerializeField] private Color glareTint = new Color(1f, 0.9f, 0.7f, 1f);

        [Header("Освещение (Hello Neighbor)")]
        [Tooltip("Применить тёплое солнце, мягкие тени и лёгкую дымку при старте сцены")]
        [SerializeField] private bool applyLighting = true;
        [Tooltip("Тёплый цвет солнца")]
        [SerializeField] private Color sunColor = new Color(1f, 0.92f, 0.78f, 1f);
        [Tooltip("Интенсивность солнца")]
        [SerializeField] private float sunIntensity = 1.15f;
        [Tooltip("Плотность дымки (тумана)")]
        [SerializeField] private float fogDensity = 0.006f;

        [Header("Смерть")]
        [Tooltip("Сколько держится белая вспышка в момент смерти, сек")]
        [SerializeField] private float deathFlashSeconds = 0.12f;
        [Tooltip("За сколько вспышка переходит во тьму, сек")]
        [SerializeField] private float deathDarkenSeconds = 0.45f;
        [Tooltip("За сколько громкость всех звуков плавно падает до нуля, сек")]
        [SerializeField] private float deathAudioFadeSeconds = 0.9f;
        [Tooltip("За сколько громкость возвращается после возрождения, сек")]
        [SerializeField] private float reviveAudioFadeSeconds = 0.45f;

        [Header("Затемнение всего UI при смерти")]
        [Tooltip("Порядок сортировки канваса вспышки смерти. Обязан быть выше всего остального интерфейса, иначе белизна/тьма смерти перекроет только мир, но не прицел и полоски здоровья")]
        [SerializeField] private int deathOverlaySortingOrder = 5000;

        [Header("Отладка")]
        [Tooltip("Клавиша вспышки урона для проверки прямо в игре")]
        [SerializeField] private bool debugFlashKey = true;
        [SerializeField] private Key debugFlash = Key.F9;

        private PlayerController player;
        private Health health;
        private Health carHealth;
        private float intensity;
        private float holdTimer;
        private float pulsePhase;
        private Color currentColor;
        private bool subscribed;

        private bool deathActive;
        private float deathTime;
        private AudioSource[] audioSources;
        private float[] audioBaseVolumes;
        private float audioFactorCurrent = 1f;
        private float audioFactorTarget = 1f;
        private float audioFadeRate = 1f;

        private Volume volume;
        private bool ownsProfile;

        private Image deathOverlayImage;

        private void Awake()
        {
            player = ResolvePlayer();
            currentColor = damageColor;

            // Если в сцене есть цикл дня и ночи, свет и туман ведёт он —
            // разовая настройка здесь только перебила бы его.
            if (applyLighting && FindFirstObjectByType<DayNightCycle>() == null) ApplyLighting();
            EnableCameraPostProcessing();
            EnsureVolume();

            EnsureDeathOverlay();

            ResetGlobalPostFX();
        }

        private void OnEnable()
        {
            ResetGlobalPostFX();
        }

        private void OnDisable()
        {
            ResetGlobalPostFX();
            HideDeathOverlay();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            UnsubCar();
            ReleaseAudio();
            if (ownsProfile && volume != null && volume.profile != null)
                Destroy(volume.profile);
            ResetGlobalPostFX();
            HideDeathOverlay();
        }

        private void Update()
        {
            if (player == null) player = ResolvePlayer();
            if (player != null && !subscribed) Subscribe();
            if (player != null) TrackCarHealth();

            HandleDebugKey();

            float dt = Time.unscaledDeltaTime;

            if (deathActive)
            {
                deathTime += dt;
                ApplyDeath(deathTime);
            }
            else
            {
                if (holdTimer > 0f)
                    holdTimer -= dt;
                else
                    intensity = Mathf.MoveTowards(intensity, 0f, dt / Mathf.Max(0.01f, fadeDuration));

                ApplyNormalVignette();
            }

            UpdateAudio(dt);
        }

        /// <summary>
        /// Произвольная вспышка. Публичный «выход» для остальных эффектов сцены
        /// (уроны, события): передаётся цвет и сила.
        /// </summary>
        public void Flash(Color tint, float strength)
        {
            Trigger(tint, Mathf.Clamp01(strength));
        }

        private void ApplyNormalVignette()
        {
            float pulse = LowHealthAlpha();
            float flash = Mathf.Max(intensity, pulse);
            float baseAlpha = ambientVignette ? Mathf.Clamp01(ambientVignetteAlpha * AmbientVignetteScale) : 0f;
            float alpha = Mathf.Max(baseAlpha, flash);

            Color flashColor = pulse > 0f ? pulseColor : currentColor;
            float blend = flash <= 0f ? 0f : Mathf.InverseLerp(Mathf.Max(0.01f, baseAlpha), 1f, flash);
            Color color = Color.Lerp(Color.black, flashColor, Mathf.Clamp01(blend));

            ApplyGlobals(color, alpha, innerRadius, softness, 0f);
            HideDeathOverlay();
        }

        /// <summary>«Пульс» при низком здоровье игрока или машины, 0 — когда всё в порядке.</summary>
        private float LowHealthAlpha()
        {
            if (!lowHealthEnabled) return 0f;

            float ratio = CurrentHealthRatio();
            if (ratio >= lowHealthThreshold) return 0f;

            float factor = 1f - ratio / Mathf.Max(0.01f, lowHealthThreshold);
            pulsePhase += Time.unscaledDeltaTime * pulseRate * Mathf.PI * 2f;
            float wave = 0.5f + 0.5f * Mathf.Sin(pulsePhase);
            return Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, wave) * factor;
        }

        private float CurrentHealthRatio()
        {
            float worst = 1f;

            if (health != null && health.Max > 0f)
                worst = Mathf.Min(worst, health.Current / health.Max);

            if (carHealth != null && carHealth.Max > 0f)
                worst = Mathf.Min(worst, carHealth.Current / carHealth.Max);

            return worst;
        }

        private void OnPlayerDied(Health dead)
        {
            if (!deathActive)
            {
                deathActive = true;
                deathTime = 0f;
                CaptureAudio();
                audioFactorTarget = 0f;
                audioFadeRate = DeathSmoothRate(deathAudioFadeSeconds);
            }
        }

        private void OnPlayerRevived(Health dead)
        {
            deathActive = false;
            intensity = 0f;
            holdTimer = 0f;
            currentColor = damageColor;
            audioFactorTarget = 1f;
            audioFadeRate = DeathSmoothRate(reviveAudioFadeSeconds);
            HideDeathOverlay();
        }

        /// <summary>
        /// Постоянная времени экспоненциального затухания: за указанное время
        /// значение успевает пройти ~98% пути, остаток дотягивается плавно.
        /// </summary>
        private static float DeathSmoothRate(float seconds)
        {
            return 4f / Mathf.Max(0.01f, seconds);
        }

        private void ApplyDeath(float t)
        {
            Color color;

            if (t <= deathFlashSeconds)
            {
                color = Color.white;
            }
            else
            {
                float darken = Mathf.InverseLerp(deathFlashSeconds, deathFlashSeconds + Mathf.Max(0.01f, deathDarkenSeconds), t);
                float smooth = darken * darken * (3f - 2f * darken);
                color = Color.Lerp(Color.white, Color.black, smooth);
            }

            // Дублируем вспышку полноэкранным оверлеем на канвасе: сама
            // пост-обработка рисуется до UI и не закрывает прицел и полоски.
            ApplyGlobals(color, 1f, 0f, 1f, 1f);
            SetDeathOverlay(color);
        }

        /// <summary>
        /// Создаёт полноэкранный оверлей на отдельном канвасе с очень высоким
        /// порядком сортировки — только он способен перекрыть весь интерфейс
        /// (прицел, полоски здоровья), поверх которого пост-обработка не ложится.
        /// </summary>
        private void EnsureDeathOverlay()
        {
            if (deathOverlayImage != null) return;

            Canvas canvas = ScreenOverlay.CreateScreenCanvas("DeathFlashCanvas", deathOverlaySortingOrder);
            canvas.transform.SetParent(transform, false);
            deathOverlayImage = ScreenOverlay.CreateFullScreenImage(canvas.transform, "DeathFlash", -1);
            deathOverlayImage.enabled = false;
        }

        private void SetDeathOverlay(Color color)
        {
            if (deathOverlayImage == null) return;
            deathOverlayImage.enabled = true;
            deathOverlayImage.color = color;
        }

        private void HideDeathOverlay()
        {
            if (deathOverlayImage != null && deathOverlayImage.enabled)
                deathOverlayImage.enabled = false;
        }

        private void ApplyGlobals(Color color, float alpha, float inner, float soft, float solid)
        {
            Shader.SetGlobalColor(ColorId, color);
            Shader.SetGlobalFloat(IntensityId, Mathf.Clamp01(alpha));
            Shader.SetGlobalFloat(InnerId, Mathf.Clamp01(inner));
            Shader.SetGlobalFloat(SoftnessId, Mathf.Clamp01(soft));
            Shader.SetGlobalFloat(SolidId, Mathf.Clamp01(solid));
        }

        private void CaptureAudio()
        {
            ReleaseAudio();
            AudioSource[] found = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            if (found == null || found.Length == 0) return;

            audioSources = found;
            audioBaseVolumes = new float[found.Length];
            for (int i = 0; i < found.Length; i++)
                audioBaseVolumes[i] = found[i].volume;

            audioFactorCurrent = 1f;
        }

        private void ReleaseAudio()
        {
            audioSources = null;
            audioBaseVolumes = null;
        }

        private void UpdateAudio(float dt)
        {
            if (audioSources == null || audioBaseVolumes == null)
            {
                audioFactorCurrent = audioFactorTarget = 1f;
                return;
            }

            audioFactorCurrent = Mathf.Lerp(audioFactorTarget, audioFactorCurrent, Mathf.Exp(-audioFadeRate * dt));

            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] == null) continue;
                audioSources[i].volume = audioBaseVolumes[i] * audioFactorCurrent;
            }
        }

        /// <summary>
        /// Следит, чтобы пайплайн сцены и камера писались в HDR-буфер. Без HDR
        /// bloom не сработает вовсе: неоновые материалы и небо дают яркость не
        /// выше 1, и стандартный порог 1.05 их никогда не пробивает. Ассет URP
        /// можно включить прямо из проигрывания — это меняет только настройку.
        /// </summary>
        private static void EnsurePipelineHdr()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline && !pipeline.supportsHDR)
                pipeline.supportsHDR = true;
        }

        /// <summary>Тёплые блики bloom прямо в глаза, как в Hello Neighbor alpha.</summary>
        private void EnsureVolume()
        {
            EnsurePipelineHdr();

            volume = GetComponent<Volume>();
            if (volume == null) volume = gameObject.AddComponent<Volume>();

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ownsProfile = true;

            if (glareEnabled)
            {
                Bloom bloom = profile.Add<Bloom>(overrides: true);
                bloom.intensity.value = glareIntensity;
                bloom.threshold.value = Mathf.Max(0f, bloomThreshold);
                bloom.scatter.value = 0.7f;
                bloom.tint.value = glareTint;
                bloom.highQualityFiltering.overrideState = true;

                ColorAdjustments color = profile.Add<ColorAdjustments>(overrides: true);
                color.saturation.value = 8f;
                color.contrast.value = 4f;
            }

            volume.isGlobal = true;
            volume.priority = 100f;
            volume.profile = profile;
            volume.weight = 1f;
        }

        /// <summary>
        /// Включает постобработку на игровой камере: иначе не заработают ни volume
        /// bloom, ни эффекты рендерера URP.
        /// </summary>
        private static void EnableCameraPostProcessing()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            camera.allowHDR = true;

            UniversalAdditionalCameraData extra = camera.GetComponent<UniversalAdditionalCameraData>();
            if (extra == null) extra = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
        }

        /// <summary>
        /// Лёгкая стилизация освещения под Hello Neighbor alpha 1: тёплое солнце,
        /// мягкие тени, амбиент из скайбокса и еле заметная дымка. Применяется
        /// только при старте игровой сцены, меню не трогается.
        /// </summary>
        private void ApplyLighting()
        {
            Light sun = FindDirectionalLight();
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.9f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Max(0f, fogDensity);
            RenderSettings.fogColor = new Color(0.42f, 0.48f, 0.55f, 1f);
        }

        private static Light FindDirectionalLight()
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                    return lights[i];
            }
            return null;
        }

        private void TrackCarHealth()
        {
            CarController vehicle = player != null ? player.CurrentVehicle : null;
            Health latest = vehicle != null ? vehicle.Health : null;
            if (latest == carHealth) return;

            UnsubCar();
            carHealth = latest;
            if (carHealth != null)
            {
                carHealth.Damaged += OnDamaged;
                carHealth.Healed += OnHealed;
            }
        }

        private void Subscribe()
        {
            player = ResolvePlayer();
            health = player != null ? player.Health : null;
            if (health == null) return;

            health.Damaged += OnDamaged;
            health.Healed += OnHealed;
            health.Died += OnPlayerDied;
            health.Revived += OnPlayerRevived;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || health == null) return;

            health.Damaged -= OnDamaged;
            health.Healed -= OnHealed;
            health.Died -= OnPlayerDied;
            health.Revived -= OnPlayerRevived;
            subscribed = false;
        }

        private void UnsubCar()
        {
            if (carHealth == null) return;

            carHealth.Damaged -= OnDamaged;
            carHealth.Healed -= OnHealed;
            carHealth = null;
        }

        private void OnDamaged(Health source, DamageInfo info)
        {
            Health h = source != null ? source : health;
            float severity = info.amount / Mathf.Max(1f, h != null ? h.Max : 1f);
            Trigger(damageColor, severity * damageSensitivity);
        }

        private void OnHealed(Health source, float amount)
        {
            Health h = source != null ? source : health;
            float severity = amount / Mathf.Max(1f, h != null ? h.Max : 1f);
            Trigger(healColor, severity * healSensitivity);
        }

        private void Trigger(Color tint, float strength)
        {
            currentColor = tint;
            float target = Mathf.Clamp01(strength);
            intensity = Mathf.Max(intensity, Mathf.Lerp(minAlpha, maxAlpha, target));
            holdTimer = holdDuration;
        }

        private void HandleDebugKey()
        {
            if (!debugFlashKey) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyboard[debugFlash] == null) return;
            if (!keyboard[debugFlash].wasPressedThisFrame) return;

            Trigger(damageColor, 1f);
        }

        private static PlayerController ResolvePlayer()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }

        /// <summary>
        /// Сброс глобальных свойств виньетки к нейтральным. Вызывается и при
        /// создании менеджера, и при загрузке сцены, где менеджера нет
        /// (меню, экран смерти), чтобы нигде не оставалась чужая темнота.
        /// </summary>
        public static void ResetGlobalPostFX()
        {
            Shader.SetGlobalFloat(IntensityId, 0f);
            Shader.SetGlobalFloat(SolidId, 0f);
            Shader.SetGlobalColor(ColorId, Color.white);
            Shader.SetGlobalFloat(InnerId, 0.32f);
            Shader.SetGlobalFloat(SoftnessId, 0.7f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSceneReset()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<PostFXManager>() == null)
                ResetGlobalPostFX();
        }
    }
}