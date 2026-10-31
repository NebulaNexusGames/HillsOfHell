using CarGame.Combat;
using CarGame.Buildings;
using CarGame.Player;
using CarGame.Vehicle;
using CarGame.Items;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    [DisallowMultipleComponent]
    public class DamageHud : MonoBehaviour
    {
        /// <summary>Сколько HP приходится на один слайдер оверхила.</summary>
        private const float SegmentHp = 100f;

        [Header("Игрок снаружи")]
        [Tooltip("Слайдер здоровья игрока. Показывается, пока игрок не в машине")]
        [SerializeField] private Slider playerHealthSlider;
        [Tooltip("Image заливки слайдера игрока — он перекрашивается")]
        [SerializeField] private Image playerFillImage;
        [Tooltip("Текст здоровья игрока")]
        [SerializeField] private TMP_Text playerHealthText;

        [Header("Игрок в машине")]
        [Tooltip("Слайдер здоровья машины. Показывается, только пока игрок сидит в машине")]
        [SerializeField] private Slider carHealthSlider;
        [Tooltip("Image заливки слайдера машины — он перекрашивается")]
        [SerializeField] private Image carFillImage;
        [Tooltip("Текст здоровья машины рядом со слайдером. Показывается вместе со слайдером, пока игрок в машине")]
        [SerializeField] private TMP_Text carHealthText;


        [Header("Топливо в машине")]
        [Tooltip("Слайдер топлива машины. Отдельный объект, показывается, только пока игрок сидит в машине")]
        [SerializeField] private Slider carFuelSlider;
        [Tooltip("Image заливки слайдера топлива — он перекрашивается")]
        [SerializeField] private Image carFuelFillImage;
        [Tooltip("Текст процентов топлива машины. Отдельный объект, показывается вместе со слайдером, пока игрок в машине")]
        [SerializeField] private TMP_Text carFuelText;

        [Header("Машина под прицелом")]
        [Tooltip("Текст здоровья машины под прицелом. Появляется, только если в руках гаечный ключ и прицел смотрит на машину. Работает без слайдера")]
        [SerializeField] private TMP_Text aimedCarHealthText;

        [Tooltip("Текст процентов топлива при наведении на точку заправки")]
        [SerializeField] private TMP_Text aimedFuelText;
        [Header("Вид")]
        [Tooltip("Цвет заливки слайдера игрока при полном здоровье (базовый слайдер, 0-100)")]
        [SerializeField] private Color playerFillColor = new Color(0.86f, 0.22f, 0.20f);
        [Tooltip("Цвет заливки слайдера машины при полном здоровье")]
        [SerializeField] private Color carFillColor = new Color(0.4f, 0.68f, 0.9f);
        [Tooltip("Цвет заливки слайдера топлива машины")]
        [SerializeField] private Color fuelFillColor = new Color(0.95f, 0.78f, 0.22f);
        [Tooltip("Цвет заливки при низком здоровье")]
        [SerializeField] private Color lowFillColor = new Color(0.45f, 0.06f, 0.06f);
        [Tooltip("Ниже какого процента здоровье считается низким и заливка краснеет, 0..1")]
        [SerializeField] private float lowHealthThreshold = 0.35f;
        [Tooltip("Прятать слайдер, когда здоровье полное")]
        [SerializeField] private bool hideFullBars;

        [Header("Оверхил (копии слайдера как в Ultrakill)")]
        [Tooltip("Цвета слайдеров оверхила: 101-200, 201-300, 301-400 … Цвета повторяются по кругу")]
        [SerializeField] private Color[] segmentColors = new Color[]
        {
            new Color(0.32f, 0.85f, 0.36f), // 101-200 — зелёный
            new Color(0.30f, 0.55f, 1.00f), // 201-300 — синий
            new Color(0.95f, 0.90f, 0.25f), // 301-400 — жёлтый
            new Color(0.72f, 0.42f, 0.96f), // 401-500 — фиолетовый
            new Color(1.00f, 0.60f, 0.20f), // 501-600 — оранжевый
            new Color(0.25f, 0.90f, 0.95f), // 601-700 — голубой
            new Color(0.95f, 0.95f, 0.95f), // 701-800 — белый
            new Color(0.60f, 0.60f, 0.62f)  // 801-900 — серый
        };

        private PlayerController player;
        private PlayerInteractor interactor;
        private PlayerHand hand;
        private Health playerHealth;

        private readonly List<Slider> overhealSliders = new List<Slider>();

        private void Awake()
        {
            player = FindPlayer();
            if (player == null) return;

            playerHealth = player.Health;

            interactor = player.GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = player.GetComponentInChildren<PlayerInteractor>();

            hand = player.GetComponent<PlayerHand>();
            if (hand == null) hand = player.GetComponentInChildren<PlayerHand>();

            DetachPlayerText();
            WarnAboutEmptySlots();
            ApplyBarFrames();
        }

        /// <summary>
        /// Ставит рамку 9-слайса на фоны полос здоровья: слайдеры — авторские
        /// объекты сцены, поэтому материал назначается при запуске, как и у
        /// ячеек инвентаря.
        /// </summary>
        private void ApplyBarFrames()
        {
            ApplyBarFrame(playerHealthSlider);
            ApplyBarFrame(carHealthSlider);
            ApplyBarFrame(carFuelSlider);
        }

        private static Material fillMaskMaterial;

        /// <summary>
        /// Материал заливки: тот же шейдер рамки, но с прозрачной текстурой
        /// среза. От шейдера остаётся только скруглённая маска заливки — она
        /// повторяет срез углов рамки, но сами полосы рамки не рисует. Поэтому
        /// заливка занимает всю полосу и при этом не вылезает за рамку.
        /// </summary>
        private static Material FillMaskMaterial
        {
            get
            {
                if (fillMaskMaterial == null && NineSliceSkin.Material != null)
                {
                    fillMaskMaterial = new Material(NineSliceSkin.Material);
                    fillMaskMaterial.SetFloat("_Fill", 1f);
                    fillMaskMaterial.SetFloat("_FillInset", 2f);
                    fillMaskMaterial.SetFloat("_FillRadius", 3f);

                    Texture2D blank = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    blank.SetPixel(0, 0, Color.clear);
                    blank.Apply();
                    blank.hideFlags = HideFlags.HideAndDontSave;
                    fillMaskMaterial.SetTexture("_SliceTex", blank);
                }

                return fillMaskMaterial;
            }
        }

        private static void ApplyBarFrame(Slider bar)
        {
            if (bar == null) return;

            Graphic frame = null;
            Transform background = bar.transform.Find("Background");
            if (background != null) frame = background.GetComponent<Graphic>();
            if (frame == null) frame = bar.targetGraphic;
            if (frame != null) NineSliceSkin.Apply(frame);

            ApplyFillMask(bar);
        }

        /// <summary>
        /// Растягивает область заливки на всю полосу (без зазоров) и вешает на
        /// заливку материал-маску, чтобы её силуэт совпадал с рамкой.
        /// </summary>
        private static void ApplyFillMask(Slider bar)
        {
            if (bar == null || bar.fillRect == null) return;

            RectTransform area = bar.fillRect.parent as RectTransform;
            if (area != null && area != bar.transform)
            {
                area.offsetMin = Vector2.zero;
                area.offsetMax = Vector2.zero;
            }

            // Слайдер рулит только якорями заливки: собственный sizeDelta
            // заливки (10px) вылезал бы за правый край полосы мимо рамки.
            RectTransform fillRect = bar.fillRect;
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            Image fill = fillRect.GetComponent<Image>();
            Material material = FillMaskMaterial;
            if (fill != null && material != null) fill.material = material;
        }

        /// <summary>
        /// Текст HP не должен быть дочерним у слайдера: иначе он копируется
        /// вместе с ним и теряется среди копий оверхила. Выносим его к общему
        /// родителю слайдера (в контейнер) и поднимаем наверх — так он всегда
        /// один и всегда виден.
        /// </summary>
        private void DetachPlayerText()
        {
            if (playerHealthText == null || playerHealthSlider == null) return;

            Transform container = playerHealthSlider.transform.parent;
            if (container == null) return;

            if (playerHealthText.transform.parent != container)
                playerHealthText.transform.SetParent(container, false);

            playerHealthText.transform.SetAsLastSibling();
        }

        private void WarnAboutEmptySlots()
        {
            if (playerHealthSlider != null && playerHealthText != null && carHealthSlider != null
                && carHealthText != null && aimedCarHealthText != null) return;

            Debug.LogWarning(
                "[DamageHud] Не все объекты интерфейса назначены. Текст останется дефолтным, пока слот пуст. Проверь инспектор:\n"
                + $"  Player Health Slider: {(playerHealthSlider != null ? "ok" : "ПУСТО")}\n"
                + $"  Player Fill Image:    {(playerFillImage != null ? "ok" : "ПУСТО")}\n"
                + $"  Player Health Text:   {(playerHealthText != null ? "ok" : "ПУСТО")}\n"
                + $"  Car Health Slider:    {(carHealthSlider != null ? "ok" : "ПУСТО")}\n"
                + $"  Car Fill Image:       {(carFillImage != null ? "ok" : "ПУСТО")}\n"
                + $"  Car Health Text:      {(carHealthText != null ? "ok" : "ПУСТО")}\n"
                + $"  Aimed Car Health Text:{(aimedCarHealthText != null ? "ok" : "ПУСТО")}", this);
        }

        private void LateUpdate()
        {
            if (player == null) return;

            if (playerHealth == null) playerHealth = player.Health;

            bool inCar = player.IsInCar;

            UpdatePlayerBlock(!inCar);
            UpdateCarBlock(inCar);
            UpdateFuelBlock(inCar);

            UpdateAimedCarBlock(inCar);
        }

        private void UpdatePlayerBlock(bool active)
        {
            UpdateBar(playerHealthSlider, playerFillImage, playerFillColor, playerHealth, active);
            UpdateOverhealSliders(playerHealth, active);
            UpdatePlayerText(playerHealth, active);
        }

        private void UpdateCarBlock(bool active)
        {
            CarController vehicle = active ? player.CurrentVehicle : null;
            Health carHealth = vehicle != null ? vehicle.Health : null;

            UpdateBar(carHealthSlider, carFillImage, carFillColor, carHealth, carHealth != null);
            UpdateText(carHealthText, carHealth, carHealth != null);
        }

        /// <summary>
        /// Полоса топлива машины: 0-100% и текст с процентами. Слайдер и текст —
        /// отдельные объекты сцены, независимые от шкалы здоровья машины.
        /// </summary>
        private void UpdateFuelBlock(bool active)
        {
            CarController vehicle = active ? player.CurrentVehicle : null;

            if (vehicle == null || carFuelSlider == null)
            {
                if (carFuelSlider != null) Toggle(carFuelSlider, false);
                if (carFuelFillImage != null) Toggle(carFuelFillImage, false);
                SetTextHidden(carFuelText);
                return;
            }

            float normalized = vehicle.MaxFuel > 0.0001f
                ? Mathf.Clamp01(vehicle.Fuel / vehicle.MaxFuel)
                : 0f;

            Toggle(carFuelSlider, true);
            carFuelSlider.SetValueWithoutNotify(normalized);

            if (carFuelFillImage != null)
            {
                Toggle(carFuelFillImage, true);
                carFuelFillImage.fillAmount = normalized;
            }

            if (carFuelText != null)
            {
                Toggle(carFuelText, true);
                string val = Mathf.RoundToInt(normalized * 100f).ToString() + "%";
                if (carFuelText.text != val) carFuelText.text = val;
            }
        }

        private void UpdateAimedCarBlock(bool inCar)
        {
            // Текст процентов топлива: сначала точка заправки (бак машины),
            // затем физическая канистра (её собственный запас бензина).
            CarController refuelCar = ResolveAimedRefuelCar();
            if (refuelCar != null)
            {
                ShowAimedPercent(refuelCar.MaxFuel > 0.0001f ? refuelCar.Fuel / refuelCar.MaxFuel : 0f);
                SetTextHidden(aimedCarHealthText);
                return;
            }

            GasCanItem aimedCan = ResolveAimedGasCan();
            if (aimedCan != null)
            {
                ShowAimedPercent(aimedCan.FuelNormalized);
                SetTextHidden(aimedCarHealthText);
                return;
            }

            if (aimedFuelText != null) SetTextHidden(aimedFuelText);

            Health aimed = ResolveAimedCarHealth();
            if (inCar || aimed == null)
            {
                SetTextHidden(aimedCarHealthText);
                return;
            }

            SetText(aimedCarHealthText, aimed);
        }

        private void ShowAimedPercent(float normalized)
        {
            if (aimedFuelText == null) return;

            string val = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f).ToString() + "%";
            if (!aimedFuelText.gameObject.activeSelf) aimedFuelText.gameObject.SetActive(true);
            if (aimedFuelText.text != val) aimedFuelText.text = val;
        }

        private GasCanItem ResolveAimedGasCan()
        {
            if (interactor == null) return null;
            Component tc = interactor.Target as Component;
            if (tc == null) return null;

            GasCanItem can = tc.GetComponent<GasCanItem>();
            if (can == null) can = tc.GetComponentInParent<GasCanItem>();
            return can;
        }

        private CarController ResolveAimedRefuelCar()
        {
            if (interactor == null) return null;
            Component tc = interactor.Target as Component;
            if (tc == null) return null;

            CarGame.Vehicle.GasRefuelPoint point = tc.GetComponent<CarGame.Vehicle.GasRefuelPoint>();
            if (point == null) point = tc.GetComponentInParent<CarGame.Vehicle.GasRefuelPoint>();
            if (point == null) return null;

            return point.GetComponentInParent<CarController>();
        }

        private Health ResolveAimedCarHealth()
        {
            if (hand == null || !hand.IsHoldingWrench) return null;
            if (interactor == null) return null;

            Health aimed = interactor.AimedHealth;
            if (aimed == null || aimed.IsDead) return null;
            if (!aimed.IsCar && !aimed.IsStructure) return null;
            if (aimed.GetComponentInParent<IDamageReceiver>() != null) return null;

            return aimed;
        }

        /// <summary>
        /// Текст игрока показывает здоровье в том же виде, что и у машины:
        /// «current / max». Max — базовый максимум, он не растёт вместе с
        /// оверхилом (Health.Max), поэтому число не «плывёт» при лечении сверх
        /// предела. Число длинное, поэтому overflow включён — пусть вылезает,
        /// если не влезает в панель.
        /// </summary>
        private void UpdatePlayerText(Health health, bool active)
        {
            if (playerHealthText == null) return;

            if (health == null || !active)
            {
                SetTextHidden(playerHealthText);
                return;
            }

            playerHealthText.overflowMode = TextOverflowModes.Overflow;
            Toggle(playerHealthText, true);

            string value = $"{health.Current:0} / {health.Max:0}";
            if (playerHealthText.text != value) playerHealthText.text = value;
        }

        private void UpdateText(TMP_Text text, Health health, bool active)
        {
            if (text == null) return;

            if (health == null || !active)
            {
                SetTextHidden(text);
                return;
            }

            SetText(text, health);
        }

        private static void SetText(TMP_Text text, Health health)
        {
            Toggle(text, true);

            string value = $"{health.Current:0} / {health.Max:0}";
            if (text.text != value) text.text = value;
        }

        private static void SetTextHidden(TMP_Text text)
        {
            Toggle(text, false);

            if (text == null) return;
            if (text.text == string.Empty) return;

            text.text = string.Empty;
        }

        private void UpdateBar(Slider bar, Image fill, Color baseColor, Health health, bool active)
        {
            if (bar == null) return;

            if (health == null || !active)
            {
                Toggle(bar, false);
                return;
            }

            Toggle(bar, !hideFullBars || !health.IsFull || health.HasOverheal);

            // Базовый слайдер всегда показывает 0-100: при оверхиле он просто
            // заполнен до конца и перекрывается копиями.
            float normalized = health.Normalized;
            bar.SetValueWithoutNotify(normalized);

            if (fill == null) return;

            bool low = normalized <= lowHealthThreshold;
            fill.color = low
                ? Color.Lerp(lowFillColor, baseColor, normalized / Mathf.Max(lowHealthThreshold, 0.0001f))
                : baseColor;
        }

        /// <summary>
        /// Оверхил по образцу Ultrakill: на каждый 100 HP оверхила создаётся
        /// точная копия базового слайдера. Все слайдеры стоят в одной позиции и
        /// накладываются друг на друга; у копий прозрачный фон и свой цвет
        /// заливки, поэтому сквозь незаполненную часть видно нижний слайдер.
        /// Пустые копии скрываются.
        /// </summary>
        private void UpdateOverhealSliders(Health health, bool active)
        {
            if (playerHealthSlider == null || health == null || !active || health.Current <= SegmentHp + 0.0001f)
            {
                HideAllOverhealSliders();
                return;
            }

            float overhealHp = health.Current - SegmentHp;
            int count = Mathf.Max(1, Mathf.CeilToInt(overhealHp / SegmentHp));

            EnsureOverhealSliders(count);

            for (int i = 0; i < overhealSliders.Count; i++)
            {
                Slider slider = overhealSliders[i];

                if (i >= count)
                {
                    if (slider.gameObject.activeSelf) slider.gameObject.SetActive(false);
                    continue;
                }

                if (!slider.gameObject.activeSelf) slider.gameObject.SetActive(true);

                float segHp = Mathf.Clamp(overhealHp - i * SegmentHp, 0f, SegmentHp);
                slider.SetValueWithoutNotify(segHp / SegmentHp);

                if (slider.fillRect != null)
                {
                    Image fill = slider.fillRect.GetComponent<Image>();
                    if (fill != null) fill.color = ResolveSegmentColor(i);
                }
            }

            // Текст HP должен остаться поверх всех копий слайдера.
            if (playerHealthText != null && playerHealthText.transform.parent == playerHealthSlider.transform.parent)
                playerHealthText.transform.SetAsLastSibling();
        }

        private void HideAllOverhealSliders()
        {
            for (int i = 0; i < overhealSliders.Count; i++)
            {
                if (overhealSliders[i] != null && overhealSliders[i].gameObject.activeSelf)
                    overhealSliders[i].gameObject.SetActive(false);
            }
        }

        private void EnsureOverhealSliders(int count)
        {
            while (overhealSliders.Count < count)
                overhealSliders.Add(CreateOverhealSlider(overhealSliders.Count));
        }

        /// <summary>
        /// Создаёт копию базового слайдера в той же позиции: тот же размер/стиль,
        /// прозрачный фон, без ручки и текста. Копии накладываются друг на друга.
        /// </summary>
        private Slider CreateOverhealSlider(int index)
        {
            Transform parent = playerHealthSlider.transform.parent;

            GameObject copy = Instantiate(playerHealthSlider.gameObject, parent);
            copy.name = "OverhealSlider_" + (index + 1);
            copy.transform.SetAsLastSibling();

            // Текст HP — отдельный элемент и в копии попадать не должен.
            foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true))
                Destroy(text.gameObject);

            foreach (Button button in copy.GetComponentsInChildren<Button>(true))
                button.gameObject.SetActive(false);

            foreach (DamageHud nested in copy.GetComponentsInChildren<DamageHud>(true))
                if (nested != this) Destroy(nested);

            Slider slider = copy.GetComponent<Slider>();
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            if (slider.handleRect != null)
                slider.handleRect.gameObject.SetActive(false);

            Image fillImage = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            foreach (Image image in copy.GetComponentsInChildren<Image>(true))
            {
                if (image == fillImage) continue;

                Color color = image.color;
                color.a = 0f;
                image.color = color;
            }

            if (playerFillImage != null && fillImage != null)
            {
                fillImage.sprite = playerFillImage.sprite;
                fillImage.material = playerFillImage.material;
            }

            return slider;
        }

        private Color ResolveSegmentColor(int index)
        {
            if (segmentColors == null || segmentColors.Length == 0) return Color.white;
            return segmentColors[index % segmentColors.Length];
        }

        private static void Toggle(Component target, bool active)
        {
            if (target == null) return;

            GameObject owner = target.gameObject;
            bool isActive = owner.activeSelf;

            if (active && !isActive) owner.SetActive(true);
            else if (!active && isActive) owner.SetActive(false);
        }

        private static PlayerController FindPlayer()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }
    }
}
