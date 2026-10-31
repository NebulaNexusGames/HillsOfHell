using System;
using CarGame.Controls;
using CarGame.Items;
using CarGame.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarGame.UI
{
/// <summary>
    /// Инвентарь разделён надвое:
    ///
    /// — Хотбар (слоты 1-5) живёт в основном интерфейсе и виден всегда, чтобы
    ///   цифрами выбирать ствол, не открывая окно.
    /// — Окно инвентаря на B показывает только 25 основных слотов и подсказку.
    ///
    /// Панель, слоты и подписи авторские — объекты сцены, которые разложены
    /// вручную и связаны здесь через поля. Скрипт только перерисовывает их
    /// содержимое. Ячейки при открытии не создаются и не копируются: их ровно
    /// 30, по одной на слот, и они уже лежат в сцене.
    ///
    /// Перерисовка идёт только по событию «инвентарь изменился», а не каждый
    /// кадр. Иначе тридцать Image и текстовых полей каждый кадр пересобирали бы
    /// канвас, и на открытии окно подвисало.
    ///
    /// Клики ловятся напрямую по координатам мыши, а не через EventSystem: в
    /// сцене стоит legacy StandaloneInputModule, который не видит новый Input
    /// System, на котором сделано всё остальное управление.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryUI : MonoBehaviour
    {
        [Serializable]
        public sealed class SlotView
        {
            [Tooltip("Фон слота")]
            public Image Background;
            [Tooltip("Подпись предмета внизу")]
            public TMP_Text Name;
            [Tooltip("Количество в правом верхнем углу")]
            public TMP_Text Amount;
            [Tooltip("Картинка предмета. Заполняется один раз из ItemPreviewService")]
            public RawImage Preview;
        }

        [Header("Вид хотбара")]
        [Tooltip("Сколько секунд хотбар остаётся на экране после нажатия цифры")]
        [SerializeField] private float hotbarVisibleTime = 2.5f;

        [Header("Перетаскивание")]
        [Tooltip("На сколько пикселей надо сдвинуть курсор от нажатой ячейки, чтобы начался перенос. Небольшой запас: иначе перенос начинался бы при обычном клике")]
        [SerializeField] private float dragThreshold = 6f;
        [Tooltip("Размер картинки предмета, летящей за курсором, в пикселях")]
        [SerializeField] private float dragGhostSize = 96f;

        [Header("Звук")]
        [Tooltip("Звук переноса предмета между ячейками")]
        [SerializeField] private AudioClip moveSound;
        [Tooltip("Звук, когда предмет забирается в руку или выбрасывается")]
        [SerializeField] private AudioClip takeSound;
        [SerializeField, Range(0f, 1f)] private float uiVolume = 0.6f;

        [Header("Иконки превью")]
        [Tooltip("Запас кадра превью в ячейке, как в терминалах: модель почти во всю иконку")]
        [SerializeField, Range(1f, 2f)] private float iconPadding = 1.06f;

        [Header("Данные")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private PlayerFirearm firearm;

        [Header("Вид (авторские объекты)")]
        [Tooltip("Корень окна инвентаря. Скрывается и показывается на B")]
        [SerializeField] private RectTransform panel;
        [Tooltip("Хотбар основного интерфейса. Виден всегда, окном не управляется")]
        [SerializeField] private RectTransform hotbar;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text hintText;
        [Tooltip("Слоты по порядку: сначала быстрые, потом основные")]
        [SerializeField] private SlotView[] slots = Array.Empty<SlotView>();

        [Header("Цвета")]
        [Tooltip("Фон обычной ячейки: плоский и непрозрачный, чтобы рамка была видна")]
        [SerializeField] private Color emptySlotColor = new Color(0.11f, 0.11f, 0.12f, 0.95f);
        [SerializeField] private Color quickSlotColor = new Color(0.24f, 0.25f, 0.28f, 0.95f);
        [SerializeField] private Color selectedSlotColor = new Color(0.95f, 0.82f, 0.35f, 0.95f);
        [SerializeField] private Color equippedSlotColor = new Color(0.4f, 0.85f, 0.45f, 0.95f);
        [Tooltip("Подсветка ячейки, под которую в данный момент можно отпустить перенос")]
        [SerializeField] private Color dragTargetSlotColor = new Color(0.75f, 0.76f, 0.8f, 0.95f);
        [SerializeField] private Color labelColor = Color.white;

        [Header("Тексты")]
        [SerializeField] private string title = "INVENTORY";
        [SerializeField] private string hint =
            "B — close   •   LMB on hotbar — hold item   •   Drag / Shift+LMB — move item   •   1-5 — quick slot   •   RMB — drop";

        private Canvas canvas;
        private PlayerController player;
        private PlayerHand hand;
        private PlayerInteractor interactor;
        private AudioSource uiAudio;
        private float hotbarHideTime;
        private int selected = -1;
        private bool dirty = true;

        // Что именно лежало в выбранном слоте в прошлый раз. Выбор закреплён за
        // номером ячейки, а не за предметом, поэтому рука сверяется с этим
        // снимком: как только содержимое слота другое — предмет в руке меняется.
        private ItemDef selectedSnapshot;
        private int selectedAmountSnapshot;
        private int selectedPayloadSnapshot;
        private bool equipping;

        // Ставится на время переноса предметов. Инвентарь об этом сообщает
        // событием, и обновление руки вклинилось бы в середину переноса: снятие
        // ствола пишет заряд в его ячейку, а к этому моменту ячейка уже пуста.
        private bool suspendHandRefresh;

        // Перенос предмета курсором. dragFrom — откуда взяли, dragTarget — под что
        // сейчас можно отпустить, dragMoved — курсор уже сдвинут достаточно далеко.
        private int dragFrom = -1;
        private int dragTarget = -1;
        private bool dragMoved;
        private Vector2 dragStartPosition;
        private RectTransform dragGhost;
        private RawImage dragGhostIcon;
        private TMP_Text dragGhostLabel;

        // Подсветка состояний ячеек: наведение — белая заливка, нажатие —
        // короткая красная вспышка. Клики ловятся по координатам, поэтому
        // состояние наведения считается вручную, а не через EventSystem.
        private static readonly Color pressSlotColor = new Color(1f, 0.13f, 0.13f, 1f);
        private const float pressFlashTime = 0.12f;
        private int hoverSlot = -1;
        private int pressSlot = -1;
        private float pressUntil;

        public bool IsOpen { get; private set; }
        public static bool AnyOpen { get; private set; }

        /// <summary>
        /// Единственный инвентарь в сцене. Нужен предметам: поднятая с земли
        /// вещь, попавшая в быстрый слот, сразу просит показать её в руке, и
        /// делать это через FindFirstObjectByType на каждую подобранную вещь
        /// незачем.
        /// </summary>
        public static InventoryUI Instance { get; private set; }

        private int SlotCount
        {
            get { return slots != null ? slots.Length : 0; }
        }

        private void Awake()
        {
            // Открытие инвентаря разблокирует курсор, и окно может потерять
            // фокус. При выключенном Run In Background Unity ставит на паузу весь
            // мир вместе с физикой, хотя игрок должен терять только управление
            // персонажем. Держим фон включённым принудительно, независимо от
            // настройки Player Settings.
            Application.runInBackground = true;

            Instance = this;
            AnyOpen = false;
            IsOpen = false;

            canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

            player = FindFirstObjectByType<PlayerController>();
            hand = FindFirstObjectByType<PlayerHand>();
            interactor = FindFirstObjectByType<PlayerInteractor>();

            if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
            if (firearm == null) firearm = FindFirstObjectByType<PlayerFirearm>();
            if (interactor != null && interactor.Hand != null) hand = interactor.Hand;

            if (titleText != null) titleText.text = title;
            if (hintText != null) hintText.text = hint;

            if (inventory != null) inventory.Changed += MarkDirty;
            if (firearm != null) firearm.Changed += MarkDirty;

            if (panel == null)
            {
                Debug.LogError("[InventoryUI] Не назначена панель инвентаря", this);
                enabled = false;
                return;
            }

            if (SlotCount < Inventory.TotalSlotCount)
            {
                Debug.LogWarning("[InventoryUI] Назначено слотов: " + SlotCount + " из " + Inventory.TotalSlotCount, this);
            }

            if (hotbar == null)
            {
                Debug.LogWarning("[InventoryUI] Не назначен хотбар: слоты 1-5 останутся внутри окна инвентаря", this);
            }

            // Палитра слотов фиксирована кодом. В сцене могли остаться старые
            // значения (пустая ячейка с alpha 0.06 делала рамку невидимой),
            // поэтому цвета назначаются здесь, а не берутся из сериализации.
            ApplyCanonicalSlotColors();

            ApplyNineSliceSkin();

            uiAudio = gameObject.GetComponent<AudioSource>();
            if (uiAudio == null) uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.spatialBlend = 0f;

SetOpen(false);
        }

        /// <summary>
        /// Накладывает 9-слайс материал рамки на ячейки инвентаря и хотбара.
        /// Ячейки — авторские объекты сцены, поэтому материал ставится здесь
        /// один раз при запуске, а не в инструменте сборки. Фон окна рамку не
        /// получает: она на нём бесполезна.
        /// </summary>
        private void ApplyNineSliceSkin()
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) NineSliceSkin.ApplyOutline(slots[i].Background);
            }
        }

        /// <summary>
        /// Ставит канонические цвета ячеек. Цвет вершины несёт и цвет рамки, и
        /// заливку: (r,g,b) — цвет рамки, альфа — количество заливки. Поэтому
        /// в покое заливки нет (видна только белая рамка), под курсором и на
        /// выбранном слоте заливка белая, а при нажатии и рамка, и заливка
        /// красные (см. <see cref="pressSlotColor"/>).
        /// </summary>
        private void ApplyCanonicalSlotColors()
        {
            emptySlotColor = new Color(1f, 1f, 1f, 0.5f);
            quickSlotColor = new Color(1f, 1f, 1f, 0.5f);
            selectedSlotColor = new Color(1f, 1f, 1f, 1f);
            equippedSlotColor = new Color(1f, 1f, 1f, 1f);
            dragTargetSlotColor = new Color(1f, 1f, 1f, 1f);
        }

        /// <summary>
        /// Картинка предмета, летящая за курсором при переносе. Создаётся кодом,
        /// а не лежит в сцене объектом: она нужна только на время переноса, и
        /// хранить её в сцене незачем.
        /// </summary>
        private void EnsureDragGhost()
        {
            if (dragGhost != null || canvas == null) return;

            GameObject root = new GameObject("InventoryDragGhost", typeof(RectTransform));
            dragGhost = root.GetComponent<RectTransform>();
            dragGhost.SetParent(canvas.transform, false);
            dragGhost.sizeDelta = new Vector2(dragGhostSize, dragGhostSize);
            dragGhost.pivot = new Vector2(0.5f, 0.5f);
            root.SetActive(false);

            // Свой канвас с высоким порядком отрисовки: призрак обязан лежать
            // поверх панели и хотбара, а не под ними.
            Canvas ghostCanvas = root.AddComponent<Canvas>();
            ghostCanvas.overrideSorting = true;
            ghostCanvas.sortingOrder = 100;

            GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform iconRect = icon.GetComponent<RectTransform>();
            iconRect.SetParent(dragGhost, false);
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(1f, 1f);
            iconRect.offsetMin = new Vector2(6f, 6f);
            iconRect.offsetMax = new Vector2(-6f, -6f);

            dragGhostIcon = icon.GetComponent<RawImage>();
            dragGhostIcon.raycastTarget = false;
            dragGhostIcon.color = new Color(1f, 1f, 1f, 0.9f);

            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(dragGhost, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(0f, 24f);

            // TMP_Text — абстрактный класс, добавлять его в объект нельзя: только
            // конкретный TextMeshProUGUI.
            dragGhostLabel = label.GetComponent<TextMeshProUGUI>();
            dragGhostLabel.raycastTarget = false;
            dragGhostLabel.alignment = TextAlignmentOptions.Center;
            dragGhostLabel.fontSize = 18f;
            dragGhostLabel.enableWordWrapping = false;
            dragGhostLabel.color = labelColor;
        }

        private void Start()
        {
            // Первая перерисовка в Awake может случиться раньше, чем PlayerHand
            // и ItemPreviewService соберут свои данные, и тогда иконки не найдутся
            // никогда. В Start все Awake проекта уже отработали — рисуем заново.
            if (ItemPreviewService.Instance != null) ItemPreviewService.Instance.ResetFailedAttempts();

            // Первый слот хотбара выбран с самого старта: игрок сразу видит в
            // руке то, что лежит в ячейке 1, и не должен нажимать «1» вслепую.
            // Если ячейка пуста — рука просто останется пустой, это не ошибка.
            if (selected < 0 && Inventory.QuickSlotCount > 0)
            {
                selected = 0;
                EquipSelected();
            }

            Refresh(true);

            // Стартовый ключ тоже нужно показать: иначе игра начинается с
            // предметом в инвентаре, которого игрок не видит.
            ShowHotbar();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;

            if (inventory != null) inventory.Changed -= MarkDirty;
            if (firearm != null) firearm.Changed -= MarkDirty;

            if (dragGhost != null) Destroy(dragGhost.gameObject);
            dragGhost = null;

            if (AnyOpen) SetOpen(false);
        }

        /// <summary>
        /// Инвентарь изменился: помечаем перерисовку и показываем хотбар.
        ///
        /// Без вспышки хотбар поднятый предмет не видно вообще — окно инвентаря
        /// закрыто, а цифры игрок не нажимал. Раньше хотбар появлялся только от
        /// нажатия цифры, и новый предмет словно пропадал бесследно.
        /// </summary>
        private void MarkDirty()
        {
            dirty = true;
            ShowHotbar();
            RefreshHandIfSelectedSlotChanged();
        }

        /// <summary>
        /// Обновляет руку, если содержимое выбранного слота изменилось.
        ///
        /// Выбор принадлежит номеру ячейки, а не предмету: игрок выбрал первый
        /// слот хотбара, значит рука показывает то, что в этом слоте. Перенос
        /// предмета в другую ячейку или замена его другим обязаны отразиться на
        /// руке, а снимок содержимого — самый дешёвый способ это заметить.
        /// </summary>
        private void RefreshHandIfSelectedSlotChanged()
        {
            // Внутри EquipSelected снимок обновляется в конце, поэтому пока он
            // не сходится, а сходиться он будет только там же. Иначе обновление
            // руки вызывало бы само себя.
            if (equipping || suspendHandRefresh) return;
            if (selected < 0 || inventory == null) return;

            ItemStack stack = inventory.Get(selected);
            bool empty = stack == null || stack.IsEmpty;
            ItemDef def = empty ? null : stack.Def;
            int amount = empty ? 0 : stack.Amount;
            int payload = empty ? 0 : stack.Payload;

            if (def == selectedSnapshot && amount == selectedAmountSnapshot && payload == selectedPayloadSnapshot) return;

            EquipSelected();
        }

        /// <summary>
        /// Запоминает, что лежит в выбранном слоте. Снимок читается только в
        /// RefreshHandIfSelectedSlotChanged, поэтому важен момент съёма: он
        /// должен совпадать с состоянием, которое игрок сейчас видит на руке.
        /// </summary>
        private void RememberSelectedSlot()
        {
            ItemStack stack = selected >= 0 && inventory != null ? inventory.Get(selected) : null;
            bool empty = stack == null || stack.IsEmpty;

            selectedSnapshot = empty ? null : stack.Def;
            selectedAmountSnapshot = empty ? 0 : stack.Amount;
            selectedPayloadSnapshot = empty ? 0 : stack.Payload;
        }

        private void ShowHotbar()
        {
            if (hotbar == null) return;

            // В машине хотбар не показывается: рука и слоты там недоступны.
            if (player != null && player.IsInCar) return;

            hotbarHideTime = Time.time + hotbarVisibleTime;
            if (!hotbar.gameObject.activeSelf) hotbar.gameObject.SetActive(true);
        }

        /// <summary>
        /// Показывает в руке предмет указанного быстрого слота. Зовётся, когда
        /// подобранная вещь легла в один из слотов 1-5: игрок должен сразу
        /// увидеть её в руке, даже не открывая инвентарь.
        /// </summary>
        public void FocusQuickSlot(int index)
        {
            if (!Inventory.IsQuickIndex(index)) return;
            if (player != null && player.IsInCar) return;

            selected = index;
            EquipSelected();
            ShowHotbar();
            Refresh(IsOpen);
        }

        private void UpdateHotbarVisibility()
        {
            if (hotbar == null) return;

            bool want = IsOpen || Time.time < hotbarHideTime;
            if (hotbar.gameObject.activeSelf != want) hotbar.gameObject.SetActive(want);
        }

        private void Update()
        {
            // Пока расходник (инжектор, энергетик, пиво) применяется, ввод
            // интерфейса выключен: B не открывает окно, цифры не переключают
            // слот, а мышь не таскает предметы — иначе смена слота или сдача
            // сбили бы анимацию применения.
            if (PlayerHand.UseLocked) return;

            // В машине рука занята рулём: инвентарь не открывается, хотбар не
            // показывается. Если окно было открыто в момент посадки — закрываем.
            if (player != null && player.IsInCar)
            {
                if (IsOpen) SetOpen(false);
                if (hotbar != null && hotbar.gameObject.activeSelf) hotbar.gameObject.SetActive(false);
                return;
            }

            if (PlayerFirearm.InventoryPressed && !PauseMenuUI.AnyPaused) SetOpen(!IsOpen);

            // Хотбар всплывает на цифры и снова прячется: цифры нужны для выбора
            // слота, а постоянно висящая панель закрывала бы обзор снизу.
            bool quickPressed = false;
            for (int i = 0; i < Inventory.QuickSlotCount; i++)
            {
                if (!QuickKeyDown(i)) continue;
                quickPressed = true;
                UseQuickSlot(i);
            }

            if (quickPressed) ShowHotbar();

            if (hotbar != null && hotbar.gameObject.activeSelf && !IsOpen && Time.time >= hotbarHideTime)
                hotbar.gameObject.SetActive(false);

            UpdateDrag();
            HandleMouse();
            UpdateSlotPointer();

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) EndDrag();
        }

        private static bool QuickKeyDown(int index)
        {
            switch (index)
            {
                case 0: return KeyBindings.WasPressedThisFrame(GameAction.Slot1);
                case 1: return KeyBindings.WasPressedThisFrame(GameAction.Slot2);
                case 2: return KeyBindings.WasPressedThisFrame(GameAction.Slot3);
                case 3: return KeyBindings.WasPressedThisFrame(GameAction.Slot4);
                default: return KeyBindings.WasPressedThisFrame(GameAction.Slot5);
            }
        }

        /// <summary>
        /// Цифра 1-5. Выбирает слот: рука показывает его предмет, а пустой слот
        /// гасит варианты рук. Старый ствол при этом снимается.
        /// </summary>
        private void UseQuickSlot(int index)
        {
            if (inventory == null) return;
            if (!Inventory.IsQuickIndex(index)) return;

            selected = index;
            EquipSelected();
            Refresh(IsOpen);
        }

        public void SetOpen(bool open)
        {
            // Инвентарь не открывается поверх открытой паузы: кто первый, тот и
            // главный. Уже открытый инвентарь закрывать/переключать можно всегда.
            if (open && !IsOpen && PauseMenuUI.AnyPaused) return;

            if (IsOpen == open)
            {
                if (panel != null) panel.gameObject.SetActive(open);
                UpdateHotbarVisibility();
                Refresh(IsOpen);
                return;
            }

            IsOpen = open;
            AnyOpen = open;

            if (panel != null) panel.gameObject.SetActive(open);

            // Внутри окна слоты 1-5 всё равно показаны, поэтому отдельный хотбар
            // на экране при открытом окне был бы второй копией тех же ячеек.
            if (hotbar != null)
            {
                bool wantHotbar = open || Time.time < hotbarHideTime;
                if (hotbar.gameObject.activeSelf != wantHotbar) hotbar.gameObject.SetActive(wantHotbar);
            }

            // Выбранный слот при закрытии окна не сбрасывается: выбор закреплён за
            // номером ячейки, и предмет в руке должен остаться тем же, что игрок
            // выбрал. Раньше здесь стояло "selected = -1", и рука теряла предмет
            // каждый раз при нажатии TAB.

            CancelDrag();
            dirty = true;
            ApplyCursor(open);

            Refresh(IsOpen);
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        private void ApplyCursor(bool open)
        {
            // Обзор, движение и курсор — общий «эталон» меню: его запоминает и
            // возвращает UiState, поэтому инвентарь и пауза (TAB и Esc), открытые
            // одновременно, не перетирают состояние друг друга. Закрытие одного
            // из них не разблокирует управление, пока открыто второе.
            if (open) UiState.EnterMenu();
            else UiState.LeaveMenu();
        }        private void LateUpdate()
        {
            if (!dirty) return;
            Refresh(IsOpen);
        }

        /// <summary>
        /// Перерисовка. Закрытое окно обновляет только хотбар — он всё равно на
        /// экране. Каждый слот трогается только когда что-то реально поменялось:
        /// иначе смена цвета и текста каждый кадр заставляла бы канвас
        /// пересобирать геометрию, и игра подвисала бы на открытом инвентаре.
        /// </summary>
        private void Refresh(bool full)
        {
            if (inventory == null || panel == null) return;

            ItemPreviewService previews = ItemPreviewService.Instance;
            int count = full ? SlotCount : Mathf.Min(Inventory.QuickSlotCount, SlotCount);

            for (int i = 0; i < count; i++)
            {
                SlotView view = slots[i];
                if (view == null) continue;

                bool usable = i < Inventory.TotalSlotCount;
                if (view.Background != null && view.Background.gameObject.activeSelf != usable)
                    view.Background.gameObject.SetActive(usable);

                if (!usable) continue;

                ItemDef def = inventory.DefAt(i);
                bool equipped = firearm != null && firearm.HasFirearm && firearm.EquippedIndex == i;

                // Подсвечивается только та ячейка, из которой предмет реально
                // взят в руку, а не все ячейки с копиями того же предмета.
                if (!equipped && hand != null && hand.IsHolding && hand.HeldFromInventory != null)
                    equipped = hand.HeldFromInventorySlot == i;

                if (view.Background != null)
                {
                    // Проверка выделения идёт раньше проверки пустой ячейки: иначе пустая
                    // ячейка всегда оставалась бы серой и выбранную было не видно.
                    // Подсветка цели переноса важнее выделения: она показывает,
                    // куда именно предмет упадёт, если отпустить кнопку сейчас.
                    // Порядок: нажатие идёт раньше наведения, иначе вспышку не
                    // видно; цель переноса важнее наведения, наведение — важнее
                    // выделения.
                    bool pressed = pressSlot == i && Time.time < pressUntil;
                    Color wanted = pressed
                        ? pressSlotColor
                        : dragTarget == i && dragMoved
                            ? dragTargetSlotColor
                            : hoverSlot == i
                                ? dragTargetSlotColor
                                : selected == i
                                    ? selectedSlotColor
                                    : equipped
                                        ? equippedSlotColor
                                        : Inventory.IsQuickIndex(i)
                                            ? quickSlotColor
                                            : emptySlotColor;

                    if (view.Background.color != wanted) view.Background.color = wanted;
                }

                if (view.Preview != null)
                {
                    ResetPreviewUv(view.Preview);
                    ApplyPreview(view.Preview, def, previews, iconPadding);
                }

                if (view.Name != null)
                {
                    string value = def != null ? def.DisplayName : string.Empty;
                    if (view.Name.text != value) view.Name.text = value;

                    Color wantedName = def != null ? labelColor : new Color(1f, 1f, 1f, 0.25f);
                    if (view.Name.color != wantedName) view.Name.color = wantedName;
                }

                if (view.Amount == null) continue;

                ItemStack stack = inventory.Get(i);
                int amount = stack != null && !stack.IsEmpty && stack.Def.IsStackable ? stack.Amount : 0;
                string amountText = amount > 0 ? amount.ToString() : string.Empty;
                if (view.Amount.text != amountText) view.Amount.text = amountText;
            }

            dirty = false;
        }

        /// <summary>
        /// Слоты лежат в сцене с полосатым uvRect: текстура обрезана по вертикали
        /// и растянута на квадрат, иконка выходит сплющенной. Возвращаем полный
        /// кадр — превью квадратное, как в терминалах магазина и спавна.
        /// </summary>
        private static readonly Rect FullPreviewUv = new Rect(0f, 0f, 1f, 1f);

        /// <summary>
        /// Возвращает превью в честный кадр. Разовое исправление разметки сцены:
        /// дальше поля не сравниваются, поэтому uvRect не трогается каждый кадр.
        /// </summary>
        private static void ResetPreviewUv(RawImage image)
        {
            if (image.uvRect.x == FullPreviewUv.x && image.uvRect.y == FullPreviewUv.y
                && image.uvRect.width == FullPreviewUv.width && image.uvRect.height == FullPreviewUv.height)
                return;

            image.uvRect = FullPreviewUv;
        }

        /// <summary>
        /// Показывает 3D-картинку предмета. Текстура готовится сервисом один раз
        /// на тип предмета, поэтому здесь только присваивание готовой ссылки.
        /// </summary>
        private static void ApplyPreview(RawImage image, ItemDef def, ItemPreviewService previews, float padding)
        {
            Texture2D texture = def != null && previews != null ? previews.Get(def, padding) : null;

            if (image.texture != texture) image.texture = texture;
            if (image.enabled != (texture != null)) image.enabled = texture != null;
        }

        /// <summary>
        /// Клики мышью по инвентарю. ЛКМ выбирает ячейку и показывает её предмет
        /// в руке, ПКМ выбрасывает предмет из ячейки. Обе кнопки работают и по
        /// хотбару, и по основной сетке, но по сетке — только при открытом окне.
        /// </summary>
        private void HandleMouse()
        {
            HandleHotbarMouse();
            if (IsOpen) HandlePanelMouse();
        }

        /// <summary>
        /// Считает ячейку под курсором и гасит прошедшую вспышку нажатия.
        /// Клики идут по координатам, а не через EventSystem, поэтому наведение
        /// приходится вести вручную — только тогда у ячейки есть белая заливка.
        /// </summary>
        private void UpdateSlotPointer()
        {
            if (pressSlot >= 0 && Time.time >= pressUntil)
            {
                pressSlot = -1;
                dirty = true;
            }

            bool interactive = IsOpen || (hotbar != null && hotbar.gameObject.activeSelf);
            int hover = -1;
            if (interactive && Mouse.current != null)
                hover = HitTest(Mouse.current.position.ReadValue());

            if (hover != hoverSlot)
            {
                hoverSlot = hover;
                dirty = true;
            }
        }

        private void FlashPress(int index)
        {
            if (index < 0) return;

            pressSlot = index;
            pressUntil = Time.time + pressFlashTime;
            dirty = true;
        }

        /// <summary>
        /// Клики по хотбару. Работают всегда, независимо от окна инвентаря.
        /// ЛКМ выбирает слот и показывает его предмет в руке, а с зажатым
        /// Shift — отправляет предмет в основную часть инвентаря.
        /// </summary>
        private void HandleHotbarMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                int hit = HitTest(mouse.position.ReadValue());
                if (hit >= 0 && Inventory.IsQuickIndex(hit))
                {
                    FlashPress(hit);
                    if (IsShiftHeld()) ShiftMove(hit);
                    else Select(hit);
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && IsOpen)
            {
                int hit = HitTest(mouse.position.ReadValue());
                if (hit >= 0 && Inventory.IsQuickIndex(hit))
                {
                    FlashPress(hit);
                    DropFromSlot(hit);
                }
            }
        }

        /// <summary>
        /// Клики по окну инвентаря. Работают только при открытом окне.
        ///
        /// ЛКМ по основной ячейке не берёт предмет в руку — держать в руках
        /// можно только то, что лежит в хотбаре. Здесь клик начинает перенос
        /// (предмет переезжает в другую ячейку), а с зажатым Shift —
        /// перебрасывает предмет в хотбар.
        /// </summary>
        private void HandlePanelMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                int hit = HitTest(mouse.position.ReadValue());
                if (hit >= 0 && !Inventory.IsQuickIndex(hit))
                {
                    FlashPress(hit);
                    if (IsShiftHeld()) ShiftMove(hit);
                    else BeginDrag(hit);
                }
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                int hit = HitTest(mouse.position.ReadValue());
                if (hit >= 0 && !Inventory.IsQuickIndex(hit))
                {
                    FlashPress(hit);
                    DropFromSlot(hit);
                }
            }
        }

        private static bool IsShiftHeld()
        {
            return KeyBindings.IsPressed(GameAction.Sprint);
        }

        /// <summary>
        /// Shift+ЛКМ: переброска предмета между хотбаром и основной сеткой.
        /// Из инвентаря предмет уходит в первый подходящий слот хотбара, из
        /// хотбара — обратно в основную сетку. Сначала предмет складывается в
        /// существующий стак того же типа, и только потом занимает пустую ячейку.
        /// </summary>
        private void ShiftMove(int from)
        {
            if (inventory == null || inventory.IsEmpty(from)) return;

            bool toQuick = !Inventory.IsQuickIndex(from);

            // Ручка не обновляется посреди переноса: снятие ствола записало бы
            // заряд в уже опустевшую ячейку. После переноса обновим руку вручную.
            suspendHandRefresh = true;
            bool moved = MoveBetweenRegions(from, toQuick);
            suspendHandRefresh = false;

            if (!moved) return;

            if (firearm != null) firearm.ReconcileEquippedIndex();
            EquipSelected();
            PlayUi(moveSound);
            Refresh(IsOpen);
        }

        private bool MoveBetweenRegions(int from, bool toQuick)
        {
            int start = toQuick ? 0 : Inventory.QuickSlotCount;
            int end = toQuick ? Inventory.QuickSlotCount : Inventory.TotalSlotCount;

            ItemStack source = inventory.Get(from);
            if (source == null || source.IsEmpty) return false;

            // Сначала сливаем с совместимым стаком, чтобы не плодить половинки.
            for (int i = start; i < end; i++)
            {
                ItemStack target = inventory.Get(i);
                if (target == null || target.IsEmpty) continue;
                if (!ReferenceEquals(target.Def, source.Def) || !target.Def.IsStackable) continue;
                if (target.Amount >= target.Def.MaxStack) continue;
                if (inventory.MoveOrSwap(from, i)) return true;
            }

            // Иначе — в первую пустую ячейку целевой зоны.
            for (int i = start; i < end; i++)
            {
                if (!inventory.IsEmpty(i)) continue;
                if (inventory.MoveOrSwap(from, i)) return true;
            }

            return false;
        }

        /// <summary>
        /// ЛКМ по ячейке. Одно нажатие — и ячейка выбрана: рука показывает её
        /// предмет, пустая ячейка выключает все варианты рук.
        ///
        /// Предмет остаётся в ячейке: рука — это вид выбранного слота, а не место
        /// хранения. Раньше для этого предмет забирался из инвентаря, и
        /// переключение между слотами ломало его учёт.
        ///
        /// Это же нажатие начинает перенос: если курсор увели дальше порога и
        /// отпущен над другой ячейкой, предмет переезжает туда. Нажатие и
        /// отпускание на одном слоте остаётся простым выбором ячейки.
        ///
        /// Ни одна из этих веток ничего не выбрасывает. Выброс — только ПКМ.
        /// </summary>
        private void Select(int hit)
        {
            if (selected != hit)
            {
                selected = hit;
                EquipSelected();
                PlayUi(moveSound);
                Refresh(IsOpen);
            }

            BeginDrag(hit);
        }

        /// <summary>
        /// Запоминает, с какой ячейки начат перенос. Само перетаскивание
        /// отслеживается в Update: показывать призрак и подсветку надо не в момент
        /// нажатия, а когда курсор действительно пошёл в сторону.
        /// </summary>
        private void BeginDrag(int index)
        {
            // Перенос курсором возможен только при открытом окне: там курсор
            // свободен. С закрытым окном он заперт, и «тащить» предмет нечем.
            if (!IsOpen) return;
            if (index < 0 || inventory == null) return;
            if (inventory.Get(index) == null || inventory.Get(index).IsEmpty) return;

            EnsureDragGhost();

            dragFrom = index;
            dragTarget = -1;
            dragMoved = false;
            dragStartPosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        }

        private void UpdateDrag()
        {
            if (dragFrom < 0) return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                CancelDrag();
                return;
            }

            Vector2 position = mouse.position.ReadValue();

            if (!dragMoved && (position - dragStartPosition).sqrMagnitude >= dragThreshold * dragThreshold)
                dragMoved = true;

            int hover = dragMoved ? HitTest(position) : -1;
            if (dragTarget != hover)
            {
                dragTarget = hover;
                dirty = true;
            }

            if (!dragMoved)
            {
                HideDragGhost();
                return;
            }

            UpdateDragGhost(position);
        }

        private void EndDrag()
        {
            if (dragFrom < 0) return;

            int from = dragFrom;
            int to = dragTarget;

            CancelDrag();

            if (to < 0 || to == from) return;

            // Перенос меняет инвентарь, а инвентарь об этом сообщает событием.
            // Обновление руки на этом шаге было бы лишним и вредным: если переносят
            // ствол, снятие оружия записало бы заряд в уже освободившуюся ячейку
            // и вернуло бы ствол обратно. Поэтому рука ждёт конца переноса.
            suspendHandRefresh = true;
            bool moved = inventory.MoveOrSwap(from, to);
            suspendHandRefresh = false;

            if (!moved) return;

            // Номера слотов могли поменяться местами: ствол, который стоял в
            // одной ячейке, теперь в другой, и его надо найти заново.
            if (firearm != null) firearm.ReconcileEquippedIndex();

            // Выбор за собой не переносится: он закреплён за номером слота.
            // Поэтому если перенесли предмет из выбранной ячейки — рука
            // опустеет, а если на её место встал другой — рука покажет его.
            EquipSelected();
            PlayUi(moveSound);
            Refresh(IsOpen);
        }

        private void CancelDrag()
        {
            dragFrom = -1;
            dragTarget = -1;
            dragMoved = false;
            HideDragGhost();

            if (dirty) Refresh(IsOpen);
        }

        private void HideDragGhost()
        {
            if (dragGhost != null && dragGhost.gameObject.activeSelf) dragGhost.gameObject.SetActive(false);
        }

        private void UpdateDragGhost(Vector2 screenPosition)
        {
            if (dragGhost == null) return;

            ItemStack stack = inventory != null ? inventory.Get(dragFrom) : null;
            ItemDef def = stack != null && !stack.IsEmpty ? stack.Def : null;

            if (dragGhostIcon != null)
            {
                Texture2D texture = def != null && ItemPreviewService.Instance != null
                    ? ItemPreviewService.Instance.Get(def)
                    : null;

                dragGhostIcon.texture = texture;
                dragGhostIcon.enabled = texture != null;
            }

            if (dragGhostLabel != null)
            {
                string text = def != null ? def.DisplayName : string.Empty;
                int amount = stack != null && !stack.IsEmpty && def.IsStackable ? stack.Amount : 0;
                if (amount > 0) text += " x" + amount;
                if (dragGhostLabel.text != text) dragGhostLabel.text = text;
            }

            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            Vector2 local;
            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, cam, out local))
                dragGhost.anchoredPosition = local;

            if (!dragGhost.gameObject.activeSelf) dragGhost.gameObject.SetActive(true);
        }

        /// <summary>
        /// Показывает в руке предмет выбранного слота. Пустой слот — это не
        /// ошибка, а «руки пусты»: так игрок и прячет инструмент, не выбрасывая
        /// его, поэтому включение слотов при переключении обязано гасить вариант.
        /// </summary>
        private void EquipSelected()
        {
            if (equipping) return;

            // Даже когда руки нет, снимок обновить надо. Иначе RefreshHandIfSelectedSlotChanged
            // видел бы прежнее содержимое слота и звал EquipSelected на каждое событие
            // инвентаря, не получая ничего взамен.
            if (hand == null || inventory == null)
            {
                RememberSelectedSlot();
                return;
            }

            // Обновление руки поднимает событие инвентаря, а оно зовёт MarkDirty,
            // который снова сверяет выбранный слот. Пока снимок не обновлён,
            // сверка всегда расходилась бы и вызывала EquipSelected по кругу.
            equipping = true;

            try
            {
                ItemStack stack = selected >= 0 ? inventory.Get(selected) : null;
                if (stack == null || stack.IsEmpty)
                {
                    hand.PutAway();
                    if (firearm != null && firearm.HasFirearm) firearm.Unequip();
                    return;
                }

                ItemDef def = stack.Def;

                if (def.IsFirearm)
                {
                    if (firearm != null) firearm.TryEquipFromSlot(selected);
                    hand.PutAway();
                    return;
                }

                // Любой предмет инвентаря показывается в руке, кроме ствола:
                // ствол только что снят с руки и показывает себя сам. Предмет
                // при этом остаётся в ячейке — из инвентаря его не выбирали,
                // поэтому выброс по ПКМ должен работать без потери вещи.
                // Варианта в руках нет у предмета без модели: ShowInventoryItem
                // тогда просто убирает руку, предмет остаётся в ячейке.
                hand.ShowInventoryItem(def, selected);
            }
            finally
            {
                RememberSelectedSlot();
                equipping = false;
            }
        }

        /// <summary>
        /// Выброс предмета из ячейки в мир по ПКМ. Единственное место, где
        /// предмет покидает инвентарь по инициативе игрока: выбор ячейки
        /// переносит предметы и переключает вариант руки, но ничего не выбрасывает.
        /// </summary>
        private void DropFromSlot(int index)
        {
            if (inventory == null || hand == null || interactor == null) return;

            ItemStack stack = inventory.Get(index);
            if (stack == null || stack.IsEmpty) return;

            // Снятие оружия пишет заряженные патроны в его же ячейку. Это должно
            // произойти до RemoveAt/TakeAt: дальше стак чистится, и заряд вместе
            // с выброшенным стволом потерялся бы.
            if (firearm != null && firearm.EquippedIndex == index) firearm.Unequip();

            // Из руки убирается только тот предмет, который выбрасывается.
            // Остальное остаётся: ПКМ по чужой ячейке не должен ронять ключ.
            if (hand.HeldId == ItemDefs.Canonical(stack.Def.Id)) hand.PutAway();

            // Снять предмет сразу с состоянием: альт-версия на полу должна
            // помнить, сколько в стволе было заряжено.
            if (!inventory.TakeAt(index, out ItemDef def, out int amount, out int payload)) return;

            // В мире должна появиться альт-версия из Assets/Prefabs/Items: та же
            // вещь с коллайдером и физикой, что лежит на земле в сцене. Не
            // получилось поставить — возвращаем предмет на его же ячейку, иначе
            // игрок просто теряет вещь, не понимая почему.
            if (!SpawnAltVersion(def, amount, payload))
            {
                inventory.SetAt(index, def, amount, payload);
                Refresh(true);
                return;
            }

            PlayUi(takeSound);
            Refresh(true);
        }

        /// <summary>
        /// Ставит в мир альт-версию выбрасываемого предмета.
        /// </summary>
        private bool SpawnAltVersion(ItemDef def, int amount, int payload)
        {
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null)
            {
                Debug.LogWarning("[InventoryUI] Нет каталога альт-версий: '" + def.DisplayName + "' выбросить нечем. Предмет остался в инвентаре.", this);
                return false;
            }

            Vector3 direction;
            Vector3 origin;
            Quaternion rotation;

            if (hand.TryGetAimRay(out Ray ray))
            {
                direction = ray.direction;
                origin = ray.GetPoint(0.8f);
                rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            else
            {
                direction = hand.HoldSocket.forward;
                origin = hand.HoldSocket.position + direction * 0.8f;
                rotation = hand.HoldSocket.rotation;
            }

            Vector3 velocity = direction.sqrMagnitude > 0.0001f ? direction.normalized * 6f : Vector3.zero;
            return catalog.Spawn(def, origin, rotation, amount, payload, velocity, player) != null;
        }

        private void PlayUi(AudioClip clip)
        {
            if (clip == null || uiAudio == null) return;
            uiAudio.PlayOneShot(clip, uiVolume);
        }

        private int HitTest(Vector2 screenPosition)
        {
            if (canvas == null) return -1;

            Camera cam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            int count = Mathf.Min(SlotCount, Inventory.TotalSlotCount);

            for (int i = 0; i < count; i++)
            {
                SlotView view = slots[i];
                if (view == null || view.Background == null) continue;

                RectTransform rect = view.Background.rectTransform;
                if (!rect.gameObject.activeInHierarchy) continue;
                if (Inventory.IsQuickIndex(i))
                {
                    Vector2 local;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, cam, out local)) continue;
                    if (Mathf.Abs(local.x) > rect.rect.width * 0.5f || Mathf.Abs(local.y) > rect.rect.height * 0.5f) continue;
                    return i;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, cam)) return i;
            }

            return -1;
        }
    }
}
