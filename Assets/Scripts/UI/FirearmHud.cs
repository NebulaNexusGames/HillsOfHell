using CarGame.Items;
using CarGame.Player;
using TMPro;
using UnityEngine;

namespace CarGame.UI
{
    /// <summary>
    /// Боевая подсказка у огнестрела: счётчик патронов. Показывается, только
    /// когда игрок держит предмет класса «Огнестрел». Переключением прицела
    /// занимается PlayerHand — здесь каждый вариант прицела не трогается.
    /// </summary>
    [DisallowMultipleComponent]
    public class FirearmHud : MonoBehaviour
    {
        [Header("Данные")]
        [SerializeField] private PlayerFirearm firearm;

        [Header("Патроны")]
        [Tooltip("Текст счётчика патронов")]
        [SerializeField] private TMP_Text ammoText;
        [Tooltip("Показывать количество патронов в запасе отдельным числом")]
        [SerializeField] private bool appendReserve = true;
        [Tooltip("Формат строки. {0} — в магазине, {1} — ёмкость магазина, {2} — запас")]
        [SerializeField] private string format = "{0} / {1}";
        [SerializeField] private string reserveFormat = "   ·   {0} reserve";
        [SerializeField] private string emptyLabel = "EMPTY";
        [Tooltip("Кегль счётчика. Задаётся кодом, чтобы текст не разъезжался после правок сцены")]
        [SerializeField] private float fontSize = 28f;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private Color lowColor = new Color(1f, 0.36f, 0.3f);
        [Tooltip("Ниже какой доли магазина счётчик краснеет, 0..1")]
        [SerializeField] private float lowFraction = 0.25f;

        [Header("Видимость")]
        [Tooltip("Прятать счётчик в машине")]
        [SerializeField] private bool hideInCar = true;
        [Tooltip("Прятать счётчик, пока открыт инвентарь")]
        [SerializeField] private bool hideWithInventory = true;

        private PlayerController player;

        public bool Visible { get; private set; }

        private void Awake()
        {
            if (firearm == null) firearm = FindFirstObjectByType<PlayerFirearm>();
            if (firearm == null) firearm = GetComponentInParent<PlayerFirearm>();

            player = FindFirstObjectByType<PlayerController>();

            if (ammoText != null && fontSize > 0f) ammoText.fontSize = fontSize;
        }

        private void LateUpdate()
        {
            if (firearm == null) return;

            bool show = firearm.EquippedDef != null && firearm.EquippedDef.IsFirearm;
            if (show && hideInCar && player != null && player.IsInCar) show = false;
            if (show && hideWithInventory && InventoryUI.AnyOpen) show = false;

            // HUD мешает только когда прицел стоит на пункте терминала.
            if (show && TerminalUI.AimingSlot) show = false;

            Visible = show;
            UpdateAmmo(show);
        }

        private void UpdateAmmo(bool show)
        {
            if (ammoText == null) return;

            if (!show)
            {
                Toggle(ammoText.gameObject, false);
                return;
            }

            Toggle(ammoText.gameObject, true);

            int magazine = firearm.Magazine;
            int capacity = Mathf.Max(1, firearm.MagazineCapacity);
            int reserve = firearm.ReserveAmmo;

            // В сцене поле формата может остаться пустым (так и было: счётчик
            // показывал один запас без «Количество\Максимум»). Пустая строка
            // формата — это не «ничего не показывать», а забытое значение,
            // поэтому подставляем формат по умолчанию.
            string mainFormat = string.IsNullOrWhiteSpace(format) ? "{0}\\{1}" : format;
            string value = magazine <= 0 ? emptyLabel : string.Format(mainFormat, magazine, capacity);
            if (appendReserve && magazine > 0) value += string.Format(reserveFormat, reserve);

            if (ammoText.text != value) ammoText.text = value;

            float fraction = capacity <= 0 ? 0f : magazine / (float)capacity;
            ammoText.color = fraction <= lowFraction ? lowColor : color;
        }

        private static void Toggle(GameObject target, bool active)
        {
            if (target == null) return;
            if (target.activeSelf != active) target.SetActive(active);
        }
    }
}
