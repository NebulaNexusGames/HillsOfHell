using CarGame.Combat;
using CarGame.UI;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarGame.Player
{
    /// <summary>
    /// Красная виньетка при получении урона и зелёная — при лечении. Рисуется
    /// как экранный оверлей: полноэкранный <see cref="RawImage"/> с процедурно
    /// сгенерированной радиальной текстурой, подкрашенной нужным цветом.
    /// Такой способ не зависит от настройки рендереров, поэтому виньетка видна
    /// всегда. Компонент реагирует и на здоровье игрока, и на здоровье машины —
    /// в игре основной урон обычно получает именно машина.
    /// </summary>
    [DisallowMultipleComponent]
    public class DamageVignette : MonoBehaviour
    {
        [Header("Урон")]
        [Tooltip("Цвет виньетки при получении урона")]
        [SerializeField] private Color color = new Color(0.72f, 0.04f, 0.04f, 1f);
        [Range(0f, 1f)]
        [Tooltip("Самый сильный оттенок виньетки")]
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

        [Header("Форма")]
        [Range(0f, 1f)]
        [Tooltip("Радиус прозрачного центра")]
        [SerializeField] private float innerRadius = 0.32f;
        [Range(0.05f, 1f)]
        [Tooltip("Ширина перехода от прозрачного к плотному")]
        [SerializeField] private float softness = 0.7f;

        [Header("Оверлей")]
        [Tooltip("Полноэкранный RawImage виньетки. Пусто — создастся автоматически")]
        [SerializeField] private RawImage overlay;

        [Header("Отладка")]
        [Tooltip("Клавиша вспышки урона для проверки виньетки прямо в игре")]
        [SerializeField] private bool debugFlashKey = true;
        [SerializeField] private Key debugFlash = Key.F9;

        private static Texture2D sharedTexture;
        private static float cachedInner = -1f;
        private static float cachedSoft = -1f;

        private PlayerController player;
        private Health health;
        private Health carHealth;
        private float intensity;
        private float holdTimer;
        private Color currentColor;
        private bool subscribed;

        private void Awake()
        {
            player = ResolvePlayer();
            currentColor = color;
            EnsureOverlay();
            Subscribe();
            Hide();
        }

        private void OnEnable()
        {
            EnsureOverlay();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            UnsubCar();
        }

        private void Update()
        {
            if (player == null) player = ResolvePlayer();
            if (player != null) TrackCarHealth();

            HandleDebugKey();

            float dt = Time.unscaledDeltaTime;

            if (holdTimer > 0f)
                holdTimer -= dt;
            else
                intensity = Mathf.MoveTowards(intensity, 0f, maxAlpha * dt / Mathf.Max(0.01f, fadeDuration));

            if (overlay != null)
                overlay.color = new Color(currentColor.r, currentColor.g, currentColor.b, Mathf.Clamp01(intensity));
        }

        private void TrackCarHealth()
        {
            CarController vehicle = player.CurrentVehicle;
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
            health = player != null ? player.Health : null;
            if (health == null) return;

            health.Damaged += OnDamaged;
            health.Healed += OnHealed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || health == null) return;

            health.Damaged -= OnDamaged;
            health.Healed -= OnHealed;
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
            Trigger(color, severity * damageSensitivity);
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

        private void Hide()
        {
            intensity = 0f;
            holdTimer = 0f;
            currentColor = color;
            if (overlay != null) overlay.color = Color.clear;
        }

        private void EnsureOverlay()
        {
            if (overlay == null)
            {
                // Отдельный канвас с высоким порядком сортировки: переиспользовать
                // чужой нельзя — виньетка оказалась бы под ним и была бы не видна.
                Canvas canvas = ScreenOverlay.CreateScreenCanvas("VignetteCanvas", 5000);
                GameObject go = new GameObject("VignetteOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));

                RectTransform rect = go.GetComponent<RectTransform>();
                rect.SetParent(canvas != null ? canvas.transform : transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                overlay = go.GetComponent<RawImage>();
            }

            RectTransform overlayRect = overlay.rectTransform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.SetAsLastSibling();

            overlay.texture = GetTexture();
            overlay.raycastTarget = false;
        }

        private void HandleDebugKey()
        {
            if (!debugFlashKey) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyboard[debugFlash] == null) return;
            if (!keyboard[debugFlash].wasPressedThisFrame) return;

            currentColor = color;
            intensity = maxAlpha;
            holdTimer = holdDuration;
        }

        private Texture2D GetTexture()
        {
            if (sharedTexture != null
                && Mathf.Approximately(cachedInner, innerRadius)
                && Mathf.Approximately(cachedSoft, softness))
            {
                return sharedTexture;
            }

            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "VignetteGradient"
            };

            float inner = Mathf.Clamp01(innerRadius);
            float outer = Mathf.Clamp01(inner + Mathf.Max(0.01f, softness));
            float maxDistance = 0.5f * Mathf.Sqrt(2f);
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float distance = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / maxDistance;
                    float t = Mathf.InverseLerp(inner, outer, distance);
                    t = t * t * (3f - 2f * t);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(t) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            sharedTexture = texture;
            cachedInner = innerRadius;
            cachedSoft = softness;
            return sharedTexture;
        }

        private static PlayerController ResolvePlayer()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }
    }
}
