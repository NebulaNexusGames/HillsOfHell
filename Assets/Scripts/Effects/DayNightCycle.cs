using CarGame.Combat;
using CarGame.Player;
using System.Collections.Generic;
using UnityEngine;

namespace CarGame.Effects
{
    /// <summary>
    /// Цикл дня и ночи. Инспектор хранит 4 пары цветов неба (верх/низ для дня,
    /// ночи, утра и вечера), длительности фаз, текстуры солнца и луны, параметры
    /// фаз луны и настройки кровавой луны.
    ///
    /// Низ неба всегда темнее верха (это проверяется и принудительно), цвет
    /// земли ещё темнее низа. Солнце и луна рисуются как экранные спрайты,
    /// поэтому свои текстуры видны всегда и не зависят от дальности прорисовки.
    /// </summary>
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        public enum MoonPhase
        {
            [InspectorName("Месяц")] Crescent = 0,
            [InspectorName("Полнолуние")] Full = 1,
            [InspectorName("Новолуние")] New = 2
        }

        [Header("Sky Colors")]
        [Tooltip("Верх неба днём")]
        [SerializeField] private Color daySkyTopColor = new Color(0.53f, 0.81f, 0.92f);
        [Tooltip("Низ неба (горизонт) днём. Должен быть темнее верха")]
        [SerializeField] private Color daySkyBottomColor = new Color(0.31f, 0.55f, 0.71f);
        [Tooltip("Верх неба ночью")]
        [SerializeField] private Color nightSkyTopColor = new Color(0.04f, 0.04f, 0.12f);
        [Tooltip("Низ неба ночью. Должен быть темнее верха")]
        [SerializeField] private Color nightSkyBottomColor = new Color(0.02f, 0.02f, 0.06f);
        [Tooltip("Верх неба утром")]
        [SerializeField] private Color morningSkyTopColor = new Color(0.59f, 0.39f, 0.59f);
        [Tooltip("Низ неба утром. Должен быть темнее верха")]
        [SerializeField] private Color morningSkyBottomColor = new Color(0.71f, 0.39f, 0.31f);
        [Tooltip("Верх неба вечером")]
        [SerializeField] private Color eveningSkyTopColor = new Color(0.78f, 0.39f, 0.20f);
        [Tooltip("Низ неба вечером. Должен быть темнее верха")]
        [SerializeField] private Color eveningSkyBottomColor = new Color(0.47f, 0.20f, 0.12f);
        [Tooltip("Во сколько раз цвет земли темнее нижней части неба")]
        [Range(0.05f, 1f)]
        [SerializeField] private float groundDarken = 0.5f;

        [Header("Clouds and Stars")]
        [Tooltip("Плотность облаков: 0 — чистое небо, 1 — сплошной покров")]
        [Range(0f, 1f)]
        [SerializeField] private float cloudCoverage = 0.45f;
        [Tooltip("Скорость дрейфа облаков")]
        [Range(0f, 1f)]
        [SerializeField] private float cloudSpeed = 0.03f;
        [Tooltip("Цвет облаков днём")]
        [SerializeField] private Color dayCloudColor = new Color(0.97f, 0.97f, 1f);
        [Tooltip("Цвет облаков ночью")]
        [SerializeField] private Color nightCloudColor = new Color(0.12f, 0.13f, 0.20f);
        [Tooltip("Яркость звёзд в самую тёмную часть ночи")]
        [Range(0f, 2f)]
        [SerializeField] private float starIntensity = 1f;

        [Header("Time Settings")]
        [Tooltip("Стартовое время суток: 0 — полночь, 0.5 — полдень")]
        [Range(0f, 1f)]
        [SerializeField] private float startTime = 0.35f;
        [Tooltip("Длительность дня, сек")]
        [SerializeField] private float dayDuration = 300f;
        [Tooltip("Длительность ночи, сек")]
        [SerializeField] private float nightDuration = 120f;
        [Tooltip("Длительность утра и вечера, сек")]
        [SerializeField] private float transitionDuration = 90f;
        [Tooltip("Текущее время суток. Меняется само; можно править вручную (поставь паузу)")]
        [Range(0f, 1f)]
        [SerializeField] private float currentTime = 0.35f;
        [Tooltip("Остановить течение времени")]
        [SerializeField] private bool paused;

        [Header("Celestial Bodies")]
        [Tooltip("Направленный свет-солнце. Если пусто — ищется на сцене")]
        [SerializeField] private Light sun;
        [Tooltip("Яркость солнца днём")]
        [SerializeField] private float maxSunIntensity = 1.35f;
        [Tooltip("Цвет солнца днём")]
        [SerializeField] private Color daySunColor = new Color(1f, 0.96f, 0.86f);
        [Tooltip("Цвет солнца ночью (луна)")]
        [SerializeField] private Color nightSunColor = new Color(0.35f, 0.45f, 0.75f);
        [Tooltip("Цвет солнца на рассвете и закате")]
        [SerializeField] private Color horizonSunColor = new Color(1f, 0.55f, 0.3f);
        [Tooltip("Поворот солнца вокруг вертикали")]
        [SerializeField] private float sunYaw = -30f;
        [Tooltip("Текстура солнца. Пусто — сгенерируется белый круг со свечением")]
        [SerializeField] private Texture2D sunTexture;
        [Tooltip("Текстура полной луны. Пусто — сгенерируется")]
        [SerializeField] private Texture2D moonTexture;
        [Tooltip("Текстура серпа луны (фаза «Месяц»). Пусто — сгенерируется")]
        [SerializeField] private Texture2D crescentMoonTexture;
        [Tooltip("Текстура новолуния. Пусто — сгенерируется")]
        [SerializeField] private Texture2D newMoonTexture;
        [Tooltip("Текстура кровавой луны. Пусто — сгенерируется")]
        [SerializeField] private Texture2D bloodMoonTexture;
        [Tooltip("Размер солнца/луны на небе: доля высоты экрана")]
        [Range(0.01f, 0.4f)]
        [SerializeField] private float celestialBodySize = 0.08f;
        [Tooltip("Яркость (непрозрачность) свечения солнца/луны")]
        [Range(0f, 1f)]
        [SerializeField] private float celestialBodyIntensity = 1f;
        [Tooltip("Желаемое расстояние до солнца/луны в мире, м (обрезается дальностью камеры)")]
        [SerializeField] private float celestialDistance = 500f;

        [Header("Moon Phases")]
        [Tooltip("Сколько ночей длится одна фаза луны")]
        [Min(1)]
        [SerializeField] private int moonPhaseDuration = 3;
        [Tooltip("Текущая фаза луны")]
        [SerializeField] private MoonPhase currentMoonPhase = MoonPhase.Crescent;
        [Tooltip("Сколько ночей фаза уже длится (таймер смены фазы)")]
        [SerializeField] private int moonPhaseTimer;

        [Header("Blood Moon")]
        [Tooltip("Шанс, что в полнолуние начнётся кровавая луна, %")]
        [Range(0f, 100f)]
        [SerializeField] private float bloodMoonChance = 50f;
        [Tooltip("Активна ли кровавая луна прямо сейчас")]
        [SerializeField] private bool isBloodMoonActive;
        [Tooltip("Множитель скорости мобов, %")]
        [SerializeField] private float enemySpeedMultiplier = 1.5f;
        [Tooltip("Множитель дальности зрения мобов")]
        [SerializeField] private float enemyVisionMultiplier = 2f;
        [Tooltip("Множитель здоровья мобов")]
        [SerializeField] private float enemyHealthMultiplier = 1.5f;
        [Tooltip("Множитель урона мобов")]
        [SerializeField] private float enemyDamageMultiplier = 1.5f;
        [Tooltip("Урон игроку в секунду, пока он смотрит на кровавую луну")]
        [SerializeField] private float playerLookDamagePerSecond = 1f;
        [Tooltip("Порог угла, градусы: если игрок смотрит ближе этого угла к луне — получает урон")]
        [Range(1f, 90f)]
        [SerializeField] private float playerLookAngle = 30f;
        [Tooltip("Красноватый оттенок, который кровавая луна подмешивает в небо")]
        [SerializeField] private Color bloodMoonSkyTint = new Color(0.65f, 0.08f, 0.08f);

        [Header("Light / Fog / Distance")]
        [Tooltip("Яркость фонового света днём. Ниже — тени темнее (в тенях виден только фон)")]
        [SerializeField] private float dayAmbient = 0.72f;
        [Tooltip("Яркость фонового света ночью")]
        [SerializeField] private float nightAmbient = 0.18f;
        [Tooltip("Экспозиция неба днём (для резервного встроенного скайбокса)")]
        [SerializeField] private float dayExposure = 1.1f;
        [Tooltip("Экспозиция неба ночью (для резервного встроенного скайбокса)")]
        [SerializeField] private float nightExposure = 0.35f;
        [Tooltip("Плотность тумана глубокой ночью")]
        [SerializeField] private float nightFogDensity = 0.08f;
        [Tooltip("Плотность тумана днём — намного меньше, чем ночью")]
        [SerializeField] private float dayFogDensity = 0.008f;
        [Tooltip("Цвет дневного тумана")]
        [SerializeField] private Color dayFogColor = new Color(0.42f, 0.48f, 0.55f);
        [Tooltip("Максимальная дальность камеры днём, м")]
        [SerializeField] private float dayFarClip = 1000f;
        [Tooltip("Максимальная дальность камеры ночью, м")]
        [SerializeField] private float nightFarClip = 25f;

        private struct Crater
        {
            public Vector2 center;
            public float radius;
            public float darken;
        }

        private Material skyMaterial;
        private Camera viewCamera;
        private PlayerController player;
        private GameObject sunObject;
        private GameObject moonObject;
        private Material sunMaterial;
        private Material moonMaterial;

        private Texture2D genSun;
        private Texture2D genMoonFull;
        private Texture2D genMoonCrescent;
        private Texture2D genMoonNew;
        private Texture2D genBlood;

        private bool wasNight;
        private bool bloodMoonActivated;

        /// <summary>Текущее время суток, доля 0..1.</summary>
        public float Time01 => currentTime;

        /// <summary>Активна ли кровавая луна.</summary>
        public bool IsBloodMoonActive => isBloodMoonActive;

        /// <summary>Текущая фаза луны.</summary>
        public MoonPhase CurrentMoonPhase => currentMoonPhase;

        /// <summary>
        /// Страховка: если в игровой сцене (там, где есть игрок) не оказалось
        /// объекта DayNightCycle, создаём его на лету.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInGameplayScene()
        {
            if (FindFirstObjectByType<DayNightCycle>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;

            GameObject go = new GameObject("DayNightCycle");
            go.AddComponent<DayNightCycle>();
        }

        private void Awake()
        {
            currentTime = Mathf.Repeat(startTime, 1f);

            if (sun == null) sun = FindDirectionalLight();
            viewCamera = Camera.main;

            SetupSky();
            EnsureCelestialBodies();

            wasNight = IsNightTime();
            if (wasNight) BeginNight();

            EvaluateBloodMoon();
            Apply();
        }

        private void OnDisable()
        {
            // Статику между сценами не тащим: без активного цикла кровавой луны нет.
            BloodMoonState.Reset();
        }

        private void Update()
        {
            if (!paused)
                currentTime = Mathf.Repeat(currentTime + Time.deltaTime / TotalCycleSeconds(), 1f);

            UpdateNightState();
            EvaluateBloodMoon();
            Apply();
        }

        public void SetTime(float t)
        {
            currentTime = Mathf.Repeat(t, 1f);
            UpdateNightState();
            EvaluateBloodMoon();
            Apply();
        }

        private float TotalCycleSeconds()
        {
            float total = dayDuration + nightDuration + 2f * transitionDuration;
            return Mathf.Max(1f, total);
        }

        /// <summary>Границы фаз на шкале 0..1, посчитанные из длительностей.</summary>
        private void ComputePhases(out float nightEnd, out float morningEnd, out float eveningStart, out float nightStart)
        {
            float day = Mathf.Max(0.0001f, dayDuration);
            float night = Mathf.Max(0f, nightDuration);
            float trans = Mathf.Max(0.0001f, transitionDuration);
            float total = day + night + 2f * trans;

            float nightHalf = night * 0.5f / total;
            float morning = trans / total;
            float daytime = day / total;

            nightEnd = nightHalf;
            morningEnd = nightEnd + morning;
            eveningStart = morningEnd + daytime;
            nightStart = eveningStart + morning;
        }

        private bool IsNightTime()
        {
            ComputePhases(out float nightEnd, out _, out _, out float nightStart);
            return currentTime < nightEnd || currentTime >= nightStart;
        }

        private void UpdateNightState()
        {
            bool night = IsNightTime();
            if (night && !wasNight) BeginNight();
            else if (!night && wasNight) EndNight();
            wasNight = night;
        }

        private void BeginNight()
        {
            moonPhaseTimer++;
            if (moonPhaseTimer >= Mathf.Max(1, moonPhaseDuration))
            {
                moonPhaseTimer = 0;
                currentMoonPhase = NextPhase(currentMoonPhase);
            }

            // Кровавая луна бросается один раз за ночь и только в полнолуние.
            bloodMoonActivated = currentMoonPhase == MoonPhase.Full
                && UnityEngine.Random.Range(0f, 100f) < bloodMoonChance;
        }

        private void EndNight()
        {
            // Кровавая луна живёт ровно одну ночь: отдельного таймера у неё нет.
            bloodMoonActivated = false;
        }

        /// <summary>
        /// Кровавая луна активна только пока идёт ночь, фаза — полнолуние и
        /// выпал бросок. Проверяется каждый кадр, поэтому событие не может
        /// закончиться посреди ночи или начаться днём.
        /// </summary>
        private void EvaluateBloodMoon()
        {
            bool shouldBe = IsNightTime()
                && currentMoonPhase == MoonPhase.Full
                && bloodMoonActivated;

            if (shouldBe != isBloodMoonActive) SetBloodMoon(shouldBe);
        }

        private void SetBloodMoon(bool active)
        {
            isBloodMoonActive = active;

            if (active)
            {
                BloodMoonState.IsActive = true;
                BloodMoonState.EnemySpeedMultiplier = enemySpeedMultiplier;
                BloodMoonState.EnemyVisionMultiplier = enemyVisionMultiplier;
                BloodMoonState.EnemyHealthMultiplier = enemyHealthMultiplier;
                BloodMoonState.EnemyDamageMultiplier = enemyDamageMultiplier;
                BloodMoonState.PlayerLookDamagePerSecond = playerLookDamagePerSecond;
            }
            else
            {
                BloodMoonState.Reset();
            }
        }

        private static MoonPhase NextPhase(MoonPhase phase)
        {
            switch (phase)
            {
                case MoonPhase.Crescent: return MoonPhase.Full;
                case MoonPhase.Full: return MoonPhase.New;
                default: return MoonPhase.Crescent;
            }
        }

        private void Apply()
        {
            float light = LightAmount(currentTime);
            float night = 1f - light;

            Color top = SampleSky(currentTime, true);
            Color bottom = EnsureDarker(SampleSky(currentTime, false), top);

            if (isBloodMoonActive)
            {
                top = Color.Lerp(top, bloodMoonSkyTint, 0.55f);
                bottom = EnsureDarker(Color.Lerp(bottom, bloodMoonSkyTint * 0.45f, 0.55f), top);
            }

            Color ground = bottom * Mathf.Clamp(groundDarken, 0.05f, 1f);

            ApplySky(top, bottom, ground, light);
            ApplyAmbient(top, bottom, ground, light);
            ApplySun(light);
            ApplyFog(night);
            ApplyDrawDistance(night);
            ApplyCelestialBodies();
            ApplyLookDamage();
        }

        /// <summary>Цвет неба по фазе суток. top — верхняя часть, иначе нижняя.</summary>
        private Color SampleSky(float t, bool top)
        {
            ComputePhases(out float nightEnd, out float morningEnd, out float eveningStart, out float nightStart);

            Color nightC = top ? nightSkyTopColor : nightSkyBottomColor;
            Color dayC = top ? daySkyTopColor : daySkyBottomColor;
            Color morningC = top ? morningSkyTopColor : morningSkyBottomColor;
            Color eveningC = top ? eveningSkyTopColor : eveningSkyBottomColor;

            if (t < nightEnd) return nightC;

            if (t < morningEnd)
            {
                float mid = (nightEnd + morningEnd) * 0.5f;
                return t < mid
                    ? Color.Lerp(nightC, morningC, Ease(t, nightEnd, mid))
                    : Color.Lerp(morningC, dayC, Ease(t, mid, morningEnd));
            }

            if (t < eveningStart) return dayC;

            if (t < nightStart)
            {
                float mid = (eveningStart + nightStart) * 0.5f;
                return t < mid
                    ? Color.Lerp(dayC, eveningC, Ease(t, eveningStart, mid))
                    : Color.Lerp(eveningC, nightC, Ease(t, mid, nightStart));
            }

            return nightC;
        }

        /// <summary>0 — ночь, 1 — день, плавно на рассвете и закате.</summary>
        private float LightAmount(float t)
        {
            ComputePhases(out float nightEnd, out float morningEnd, out float eveningStart, out float nightStart);

            if (t < nightEnd) return 0f;
            if (t < morningEnd) return Ease(t, nightEnd, morningEnd);
            if (t < eveningStart) return 1f;
            if (t < nightStart) return 1f - Ease(t, eveningStart, nightStart);
            return 0f;
        }

        /// <summary>Принудительно делает низ темнее верха, если цвета поменяли местами.</summary>
        private static Color EnsureDarker(Color bottom, Color top)
        {
            float lb = Luminance(bottom);
            float lt = Luminance(top);
            if (lb >= lt && lb > 0.0001f)
            {
                float factor = Mathf.Clamp01(lt / lb * 0.92f);
                bottom = new Color(bottom.r * factor, bottom.g * factor, bottom.b * factor, bottom.a);
            }

            return bottom;
        }

        private static float Luminance(Color c)
        {
            return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        }

        private void ApplySky(Color top, Color bottom, Color ground, float light)
        {
            if (skyMaterial == null) return;

            if (skyMaterial.HasProperty("_TopColor"))
            {
                skyMaterial.SetColor("_TopColor", top);
                skyMaterial.SetColor("_BottomColor", bottom);
                skyMaterial.SetColor("_GroundColor", ground);
                skyMaterial.SetFloat("_HorizonBlend", 0.35f);
                skyMaterial.SetFloat("_HorizonSharpness", 1.4f);
                ApplySkyDecor(light);
                return;
            }

            // Резерв: встроенный процедурный скайбокс.
            if (skyMaterial.HasProperty("_SkyTint")) skyMaterial.SetColor("_SkyTint", top);
            if (skyMaterial.HasProperty("_GroundColor")) skyMaterial.SetColor("_GroundColor", ground);
            if (skyMaterial.HasProperty("_Exposure"))
                skyMaterial.SetFloat("_Exposure", Mathf.Lerp(nightExposure, dayExposure, light));
        }

        /// <summary>Облака и звёзды поверх градиента неба.</summary>
        private void ApplySkyDecor(float light)
        {
            if (skyMaterial.HasProperty("_CloudCoverage"))
                skyMaterial.SetFloat("_CloudCoverage", Mathf.Clamp01(cloudCoverage));
            if (skyMaterial.HasProperty("_CloudSpeed"))
                skyMaterial.SetFloat("_CloudSpeed", Mathf.Max(0f, cloudSpeed));
            if (skyMaterial.HasProperty("_CloudColor"))
            {
                Color cloud = Color.Lerp(nightCloudColor, dayCloudColor, light);
                if (isBloodMoonActive) cloud = Color.Lerp(cloud, bloodMoonSkyTint, 0.35f);
                skyMaterial.SetColor("_CloudColor", cloud);
            }
            if (skyMaterial.HasProperty("_StarsIntensity"))
            {
                float night = 1f - light;
                float stars = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, night)) * starIntensity;
                skyMaterial.SetFloat("_StarsIntensity", stars);
            }
        }

        private void ApplyAmbient(Color top, Color bottom, Color ground, float light)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = top;
            RenderSettings.ambientEquatorColor = bottom;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbient, dayAmbient, light);
        }

        private void ApplySun(float light)
        {
            if (sun == null) sun = FindDirectionalLight();
            if (sun == null) return;

            Vector3 dir = SunDirection();
            sun.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);

            float horizonWarm = Mathf.Sin(light * Mathf.PI);
            Color color = Color.Lerp(nightSunColor, daySunColor, light);
            color = Color.Lerp(color, horizonSunColor, horizonWarm * 0.85f);
            if (isBloodMoonActive) color = Color.Lerp(color, bloodMoonSkyTint, 0.5f);

            sun.color = color;
            sun.intensity = maxSunIntensity * light;
            sun.shadows = LightShadows.Soft;
            // Тени темнее: чем ближе к 1, тем меньше прямого света остаётся в
            // затенённых местах — там виден только фоновый свет.
            sun.shadowStrength = Mathf.Lerp(0.8f, 1f, light);

            RenderSettings.sun = sun;
        }

        private void ApplyFog(float night)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            float density = Mathf.Lerp(Mathf.Max(0f, dayFogDensity), Mathf.Max(0f, nightFogDensity), night);
            RenderSettings.fogDensity = density;
            RenderSettings.fogColor = Color.Lerp(dayFogColor, Color.black, night);
        }

        private void ApplyDrawDistance(float night)
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            viewCamera.farClipPlane = Mathf.Lerp(dayFarClip, nightFarClip, night);
        }

        /// <summary>Направление от зрителя к солнцу по текущему времени.</summary>
        private Vector3 SunDirection()
        {
            float a = (currentTime - 0.25f) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
            return (Quaternion.Euler(0f, sunYaw, 0f) * dir).normalized;
        }

        private void ApplyCelestialBodies()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            if (sunObject == null || moonObject == null) EnsureCelestialBodies();
            if (sunObject == null || moonObject == null) return;

            Vector3 sunDir = SunDirection();
            Vector3 moonDir = -sunDir;

            Texture2D sunTex = TextureFor(sunTexture, ref genSun, GenerateSunTexture);
            UpdateCelestial(sunObject, sunMaterial, sunDir, sunTex, daySunColor, celestialBodyIntensity);

            Color moonTint = isBloodMoonActive ? new Color(1f, 0.45f, 0.4f) : Color.white;
            float moonIntensity = celestialBodyIntensity;
            if (!isBloodMoonActive && currentMoonPhase == MoonPhase.New) moonIntensity *= 0.06f;

            Texture2D moonTex = ResolveMoonTexture();
            UpdateCelestial(moonObject, moonMaterial, moonDir, moonTex, moonTint, moonIntensity);
        }

        /// <summary>
        /// Ставит 3D-объект светила по направлению, держит постоянный угловой
        /// размер и прячет его за горизонтом. Это обычная 3D-геометрия, поэтому
        /// здания перекрывают её, а прицел (UI) всегда рисуется поверх.
        /// </summary>
        private void UpdateCelestial(GameObject body, Material material, Vector3 direction,
            Texture2D texture, Color tint, float intensity)
        {
            if (body == null) return;

            bool aboveHorizon = direction.y > -0.02f;
            if (body.activeSelf != aboveHorizon) body.SetActive(aboveHorizon);
            if (!aboveHorizon) return;

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null) return;
            }

            Vector3 dir = direction.normalized;

            // Держим светило внутри дальности камеры, иначе ночью его обрежет.
            float distance = Mathf.Clamp(celestialDistance, 1f, Mathf.Max(1f, viewCamera.farClipPlane * 0.95f));

            body.transform.position = viewCamera.transform.position + dir * distance;
            body.transform.rotation = Quaternion.LookRotation(dir);

            float halfHeight = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            float size = Mathf.Max(0.1f, halfHeight * 2f * celestialBodySize);
            body.transform.localScale = new Vector3(size, size, size);

            if (material == null) return;

            if (texture != null && material.mainTexture != texture) material.mainTexture = texture;

            Color color = tint;
            color.a = Mathf.Clamp01(intensity);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Emission")) material.SetFloat("_Emission", 1f + Mathf.Clamp01(intensity));
        }

        private void ApplyLookDamage()
        {
            if (!isBloodMoonActive || playerLookDamagePerSecond <= 0f) return;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            if (player == null) player = FindPlayer();
            if (player == null) return;

            Health health = player.Health;
            if (health == null || health.IsDead) return;

            Vector3 moonDir = -SunDirection();
            if (moonDir.sqrMagnitude < 0.0001f) return;
            moonDir.Normalize();

            if (Vector3.Dot(viewCamera.transform.forward, moonDir) < Mathf.Cos(playerLookAngle * Mathf.Deg2Rad)) return;

            Vector3 point = health.transform.position;
            health.TakeDamage(new DamageInfo(
                playerLookDamagePerSecond * Time.deltaTime,
                gameObject,
                point,
                -moonDir));
        }

        private void SetupSky()
        {
            Shader shader = Shader.Find("CarGame/SkyGradient");
            if (shader != null)
            {
                skyMaterial = new Material(shader) { name = "DayNightSky (Instance)" };
                RenderSettings.skybox = skyMaterial;
                return;
            }

            // Резерв: копия текущего скайбокса, чтобы не портить общий материал.
            Material current = RenderSettings.skybox;
            if (current != null)
            {
                skyMaterial = new Material(current) { name = "DayNightSky (Fallback)" };
                RenderSettings.skybox = skyMaterial;
            }
        }

        private void EnsureCelestialBodies()
        {
            Shader shader = Shader.Find("CarGame/Celestial");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");

            if (sunObject == null)
                sunObject = CreateCelestialObject("SunObject", shader, out sunMaterial);

            if (moonObject == null)
                moonObject = CreateCelestialObject("MoonObject", shader, out moonMaterial);
        }

        /// <summary>Плоский Quad с unlit-материалом — солнце или луна в 3D-мире.</summary>
        private GameObject CreateCelestialObject(string name, Shader shader, out Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            material = new Material(shader);
            material.name = name + " (Instance)";
            renderer.sharedMaterial = material;

            go.SetActive(false);
            return go;
        }

        private Texture2D ResolveMoonTexture()
        {
            if (isBloodMoonActive)
                return TextureFor(bloodMoonTexture, ref genBlood, GenerateBloodMoonTexture);

            switch (currentMoonPhase)
            {
                case MoonPhase.Full:
                    return TextureFor(moonTexture, ref genMoonFull, GenerateMoonFullTexture);
                case MoonPhase.New:
                    return TextureFor(newMoonTexture, ref genMoonNew, GenerateMoonNewTexture);
                default:
                    return TextureFor(crescentMoonTexture, ref genMoonCrescent, GenerateMoonCrescentTexture);
            }
        }

        private static Texture2D TextureFor(Texture2D assigned, ref Texture2D generated, System.Func<Texture2D> generator)
        {
            if (assigned != null) return assigned;
            if (generated != null) return generated;
            return generator != null ? (generated = generator()) : null;
        }

        private static float Ease(float t, float from, float to)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
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

        private static PlayerController FindPlayer()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }

        // --- Сгенерированные текстуры (если свои не заданы в инспекторе) ---

        private static Texture2D GenerateSunTexture()
        {
            const int size = 128;
            Texture2D texture = NewTexture(size, "SunGen");
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2(0.5f, 0.5f);
            float discRadius = 0.18f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float d = Vector2.Distance(p, center);

                    float disc = Mathf.Clamp01((discRadius - d) * size / 1.5f);
                    float glow = Mathf.Clamp01(1f - d / 0.5f);
                    glow *= glow;

                    float alpha = Mathf.Clamp01(Mathf.Max(disc, glow * 0.55f));
                    Color color = Color.Lerp(new Color(1f, 0.82f, 0.45f), Color.white, disc);
                    color.a = alpha;
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D GenerateMoonFullTexture()
        {
            return GenerateMoonDisk(new Color(0.87f, 0.86f, 0.80f), true, 0f);
        }

        private static Texture2D GenerateMoonCrescentTexture()
        {
            return GenerateMoonDisk(new Color(0.87f, 0.86f, 0.80f), true, 0.17f);
        }

        private static Texture2D GenerateMoonNewTexture()
        {
            return GenerateMoonDisk(new Color(0.03f, 0.03f, 0.05f), false, 0f);
        }

        private static Texture2D GenerateBloodMoonTexture()
        {
            return GenerateMoonDisk(new Color(0.62f, 0.10f, 0.08f), true, 0f);
        }

        private static Texture2D GenerateMoonDisk(Color diskColor, bool craters, float cutOffset)
        {
            const int size = 128;
            Texture2D texture = NewTexture(size, "MoonGen");
            Color32[] pixels = new Color32[size * size];

            Vector2 center = new Vector2(0.5f, 0.5f);
            float radius = 0.4f;

            List<Crater> craterList = null;
            if (craters)
            {
                craterList = new List<Crater>();
                System.Random rng = new System.Random(90210);
                for (int i = 0; i < 10; i++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float distance = (float)rng.NextDouble() * radius * 0.72f;
                    craterList.Add(new Crater
                    {
                        center = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                        radius = Mathf.Lerp(0.03f, 0.09f, (float)rng.NextDouble()),
                        darken = Mathf.Lerp(0.15f, 0.4f, (float)rng.NextDouble())
                    });
                }
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float d = Vector2.Distance(p, center);
                    float alpha = Mathf.Clamp01((radius - d) * size / 1.5f);

                    if (alpha <= 0f)
                    {
                        pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }

                    if (cutOffset > 0f)
                    {
                        float cutDistance = Vector2.Distance(p, center + new Vector2(cutOffset, -0.03f));
                        alpha *= Mathf.Clamp01((cutDistance - radius * 0.98f) * size / 1.5f);
                    }

                    Color color = diskColor;
                    if (craterList != null)
                    {
                        for (int i = 0; i < craterList.Count; i++)
                        {
                            Crater crater = craterList[i];
                            float cd = Vector2.Distance(p, crater.center);
                            if (cd >= crater.radius) continue;

                            float k = Mathf.Clamp01((crater.radius - cd) / crater.radius);
                            color = Color.Lerp(color, color * (1f - crater.darken), k);
                        }
                    }

                    color.a = Mathf.Clamp01(alpha);
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
        }
    }
}
