using CarGame.Items;
using UnityEngine;

namespace CarGame.Player
{
    [DisallowMultipleComponent]
    public class PlayerHand : MonoBehaviour
    {
        [System.Serializable]
        public class HandVariant
        {
            [Tooltip("Объект варианта. Обычно это пустой объект или сама модель внутри Empty рук, чтобы вариант двигался вместе с рукой")]
            public GameObject variant;

            [Tooltip("ID предмета, которому соответствует этот вариант. Должен совпадать с ID у PickupItem")]
            public string itemId = string.Empty;
        }

        [Header("Точка удержания")]
        [Tooltip("Точка, к которой привязаны заготовленные варианты предметов. Если пусто — корень игрока")]
        [SerializeField] private Transform holdSocket;

        [Header("Варианты предметов в руках")]
        [Tooltip("Заготовленные варианты: объект и его ID. Все варианты выключены, пока игрок ничего не держит, и включается только тот, чей ID совпал с ID поднятого предмета")]
        [SerializeField] private HandVariant[] handVariants = new HandVariant[0];

        [Header("Состояния прицела")]
        [Tooltip("Объект обычного состояния прицела. Виден всегда, пока не показывается состояние взаимодействия")]
        [SerializeField] private GameObject crosshairNormal = null;
        [Tooltip("Объект состояния взаимодействия. Появляется только когда игрок может взаимодействовать с целью или взять предмет")]
        [SerializeField] private GameObject crosshairInteract = null;
        [Tooltip("Объект состояния огнестрела. Показывается вместо остальных, пока игрок держит ствол")]
        [SerializeField] private GameObject crosshairFirearm = null;
        [Tooltip("Не прятать обычный спрайт, когда показывается состояние взаимодействия. Выключено — спрайты заменяют друг друга")]
        [SerializeField] private bool keepNormalWhileInteracting;
        [Tooltip("Скрывать оба состояния прицела, пока игрок в машине")]
        [SerializeField] private bool hideInCar = true;
        [Tooltip("Во сколько раз увеличивается состояние взаимодействия при появлении. 1 — без увеличения")]
        [SerializeField] private float interactPunchScale = 1f;
        [Tooltip("Скорость появления и исчезновения состояния взаимодействия")]
        [SerializeField] private float crosshairFadeSpeed = 14f;

        [Header("Звук предмета в руке")]
        [Tooltip("Общий источник звука для предметов, которые бьют и звучат из руки. Если пусто — создаётся автоматически")]
        [SerializeField] private AudioSource sharedAudioSource;
        [Tooltip("Громкость общих звуков")]
        [SerializeField] private float sharedVolume = 1f;

        [Header("Бросок (зажать ПКМ)")]
        [Tooltip("На каком расстоянии от камеры появляется предмет при броске, м. Маленькое значение — ближе к точке прицела")]
        [SerializeField] private float throwOriginOffset = 0.6f;
        [Tooltip("Скорость броска без зажатия, м/с")]
        [SerializeField] private float minThrowSpeed = 2f;
        [Tooltip("Скорость броска при полном зажатии, м/с")]
        [SerializeField] private float maxThrowSpeed = 22f;
        [Tooltip("Сколько секунд держать ПКМ, чтобы выбрать максимальную дальность, с")]
        [SerializeField] private float throwChargeTime = 1.2f;

        [Header("Вариант без модели в сцене")]
        [Tooltip("Под какой размер в метрах подгоняется модель, взятая из префаба альт-версии, если в варианте руки в сцене её нет. 0 — не подгонять")]
        [SerializeField] private float handVisualSize = 0.22f;

        private readonly System.Collections.Generic.Dictionary<string, GameObject> variantsById = new System.Collections.Generic.Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.HashSet<string> reportedMissingIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.HashSet<GameObject> builtVisuals = new System.Collections.Generic.HashSet<GameObject>();

        private ItemDef heldFromInventory;
        private int heldFromInventorySlot = -1;
        private GameObject activeVariant;
        private PickupItem heldCanister;
        private float pendingCanisterFuel = -1f;
        private PlayerInteractor interactor;
        private PlayerController playerController;
        private PlayerFirearm firearm;
        private IHandUsable usable;
        private Vector3 interactBaseScale = Vector3.one;
        private float chargeStartTime;
        private float crosshairBlend;

        /// <summary>
        /// ПКМ вышла из режима стройки, но палец ещё зажат. Пока флаг стоит,
        /// выброс подавляется: иначе на следующем кадре заряд посчитался бы от
        /// прошлой метки времени (chargeStartTime в режиме стройки не пишется)
        /// и отпускание ПКМ швырнуло бы предмет со всей силы. Флаг снимается,
        /// как только ПКМ отпущена.
        /// </summary>
        private bool suppressThrowUntilRelease;

        public bool IsHolding => HeldItem != null || heldFromInventory != null;
        public PickupItem HeldItem { get; private set; }

        /// <summary>
        /// Предмет, который игрок вытащил из инвентаря, а не поднял с земли.
        /// У него нет объекта в мире: в руку попадает только заготовленный вариант.
        /// </summary>
        public ItemDef HeldFromInventory => heldFromInventory;

        /// <summary>
        /// Ячейка инвентаря, из которой показан предмет в руке. Нужна при броске:
        /// предмет лежит в инвентаре, и выброс должен убрать его оттуда, иначе на
        /// земле появилась бы вторая копия. -1 — предмет в руке не из инвентаря.
        /// </summary>
        public int HeldFromInventorySlot => heldFromInventorySlot;

        /// <summary>ID предмета в руке: поднятого с земли или взятого из инвентаря.</summary>
        public string HeldId
        {
            get
            {
                if (HeldItem != null) return ItemDefs.Canonical(HeldItem.ItemId);
                return heldFromInventory != null ? heldFromInventory.Id : string.Empty;
            }
        }

        public bool DropBlocked { get; private set; }

        /// <summary>
        /// Идёт применение расходника (инжектор, энергетик, пиво) с длительной
        /// анимацией. На это время игроку нельзя открывать инвентарь, менять слот
        /// хотбара и сдавать предмет в терминал: смена слота сбила бы анимацию, а
        /// сдача выдернула бы предмет прямо из применения. Флаг статический, чтобы
        /// InventoryUI и депозиты терминалов читали его без ссылки на руку.
        /// </summary>
        public static bool UseLocked { get; private set; }

        public Transform HoldSocket => holdSocket != null ? holdSocket : transform;
        public GameObject ActiveHandVariant => activeVariant;

        /// <summary>
        /// ID активного варианта руки ровно как в разметке handVariants
        /// (например "24" или "golden_bolt"), без разрешения через ItemDefs.
        /// Так чертёж сверяется с тем списком, который реально лежит в сцене,
        /// а не с полными именами предметов. Пусто, если активного варианта нет.
        /// </summary>
        public string ActiveVariantItemId
        {
            get
            {
                if (activeVariant == null) return string.Empty;

                for (int i = 0; i < handVariants.Length; i++)
                {
                    HandVariant entry = handVariants[i];
                    if (entry != null && entry.variant == activeVariant) return entry.itemId;
                }

                return string.Empty;
            }
        }
        public PlayerInteractor Interactor => interactor;
        public PlayerController Player => playerController;
        public AudioSource SharedAudioSource => sharedAudioSource;
        public float Charge { get; private set; }
        public bool CrosshairInteractive { get; private set; }
        public float CrosshairBlend => crosshairBlend;
        public GameObject CrosshairNormal => crosshairNormal;
        public GameObject CrosshairInteract => crosshairInteract;
        public GameObject CrosshairFirearm => crosshairFirearm;
        public IHandUsable ActiveUsable => usable;
        public bool IsHoldingWrench => usable is WrenchItem;

        /// <summary>
        /// Показывает ли рука предмет, который надо применить: ключ, инджектор.
        /// Такой предмет важнее ствола — оружие на это время снимается, чтобы в
        /// кадре не оказались одновременно ключ и пистолет. Патроны в руке
        /// применять нечего, поэтому ствол из-за них остаётся наготове.
        /// </summary>
        public bool ShowsHandUsable => IsHolding && usable != null;
        public bool IsInCar => playerController != null && playerController.IsInCar;

        /// <summary>
        /// Игрок держит огнестрел: либо поднятый предмет сам по классу «Огнестрел»,
        /// либо ствол, который сейчас экипирован цифрой. От этого зависит режим
        /// прицела и показ счётчика патронов.
        /// </summary>
        public bool HoldingFirearm
        {
            get
            {
                if (firearm != null && firearm.HasFirearm && firearm.EquippedDef != null && firearm.EquippedDef.IsFirearm) return true;

                if (heldFromInventory != null) return heldFromInventory.IsFirearm;
                if (HeldItem == null) return false;

                ItemDef def;
                return ItemDefs.TryGet(HeldItem.ItemId, out def) && def.IsFirearm;
            }
        }

        private void Reset()
        {
            holdSocket = transform;
        }

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = GetComponentInParent<PlayerInteractor>();
            playerController = GetComponent<PlayerController>();
            if (playerController == null) playerController = GetComponentInParent<PlayerController>();
            firearm = GetComponent<PlayerFirearm>();
            if (firearm == null) firearm = GetComponentInParent<PlayerFirearm>();
            UseLocked = false;
            if (crosshairInteract != null) interactBaseScale = crosshairInteract.transform.localScale;
            EnsureSharedAudioSource();
            EnsureHandBob();
            CacheHandVariants();
            crosshairBlend = CanInteractWithTarget() ? 1f : 0f;
            ApplyCrosshairState();
        }

        /// <summary>
        /// Вешает на точку удержания покачивание руки. Компонент живёт там же,
        /// где лежат все варианты предметов, и сам следит за базовой позой —
        /// ни сцену, ни варианты править не нужно.
        /// </summary>
        private void EnsureHandBob()
        {
            if (holdSocket == null || holdSocket == transform) return;
            if (holdSocket.GetComponent<HandBob>() == null) holdSocket.gameObject.AddComponent<HandBob>();
        }

        private void Update()
        {
            UpdateUsable();

            // Пока открыт любой интерфейс (инвентарь, пауза, терминал), ЛКМ и
            // ПКМ принадлежат ему, а не предмету: иначе клик по кнопке заодно
            // бил бы ключом по воздуху или выбрасывал вещь.
            if (CarGame.UI.UiState.AnyOpen)
            {
                Charge = 0f;
                return;
            }

            // Ствол в руку не встаёт — он экипирован и занимает её своим
            // вариантом, поэтому IsHolding у него false. Раньше здесь стояла
            // проверка только на IsHolding, и выброс ствола по ПКМ не доходил
            // до Drop() вовсе.
            if (!HasSomethingToThrow)
            {
                Charge = 0f;
                return;
            }

            if (DropBlocked)
            {
                Charge = 0f;
                return;
            }

            // Строй-предмет в режиме стройки по ПКМ не выбрасывается: ПКМ
            // возвращает его из режима стройки в обычное держание в руке.
            // В режиме держания ПКМ работает как обычно — предмет можно выбросить.
            if (usable is IBuildUsable buildUsable && buildUsable.BuildModeActive)
            {
                Charge = 0f;
                if (PlayerInteractor.DropPressed)
                {
                    buildUsable.OnSecondary(this);
                    // ПКМ вышла из стройки, но палец ещё зажат. До отпускания
                    // заряд считаться не должен: иначе предмет улетел бы со всей
                    // силой (см. suppressThrowUntilRelease).
                    suppressThrowUntilRelease = true;
                }
                return;
            }

            // Подавляем бросок, пока ПКМ, которой вышли из стройки, не отпустят.
            if (suppressThrowUntilRelease)
            {
                Charge = 0f;
                if (!PlayerInteractor.DropHeld) suppressThrowUntilRelease = false;
                return;
            }

            if (PlayerInteractor.DropPressed)
            {
                chargeStartTime = Time.time;
                Charge = 0f;
            }

            if (PlayerInteractor.DropHeld) Charge = Mathf.Clamp01((Time.time - chargeStartTime) / throwChargeTime);
            if (PlayerInteractor.DropReleased) Drop();

            // ЛКМ для спавн-кубов: бросок с эффектом спавна моба при контакте (как ПКМ, но иное поведение)
            if (PlayerInteractor.AttackPressed)
            {
                string heldId = HeldId;
                if (!string.IsNullOrEmpty(heldId) && (heldId == ItemDefs.SwordBotCubeId || heldId == ItemDefs.DinaBotCubeId || heldId == ItemDefs.DronCubeId))
                {
                    chargeStartTime = Time.time;
                    Charge = 0f;
                }
            }
            if (PlayerInteractor.AttackHeld)
            {
                string heldId = HeldId;
                if (!string.IsNullOrEmpty(heldId) && (heldId == ItemDefs.SwordBotCubeId || heldId == ItemDefs.DinaBotCubeId || heldId == ItemDefs.DronCubeId))
                {
                    Charge = Mathf.Clamp01((Time.time - chargeStartTime) / throwChargeTime);
                }
            }
            if (PlayerInteractor.AttackReleased)
            {
                string heldId = HeldId;
                if (!string.IsNullOrEmpty(heldId) && (heldId == ItemDefs.SwordBotCubeId || heldId == ItemDefs.DinaBotCubeId || heldId == ItemDefs.DronCubeId))
                {
                    DropCubeByLmb();
                    return;
                }
            }
        }

        /// <summary>
        /// Есть ли что выбросить: предмет в руке или экипированный ствол.
        /// </summary>
        private bool HasSomethingToThrow => IsHolding || (firearm != null && firearm.HasFirearm);

        private void UpdateUsable()
        {
            IHandUsable current = ResolveUsable();

            if (!ReferenceEquals(current, usable))
            {
                usable?.OnUseCanceled();
                usable = current;
            }

            if (usable == null) return;

            // Пока открыт любой интерфейс, ЛКМ занята им. Без этой проверки
            // ключ в руке бил бы по воздуху на каждый клик по слоту, а у
            // терминала предмет реагировал бы на нажатия панели.
            if (CarGame.UI.UiState.AnyOpen)
            {
                usable.OnUseCanceled();
                return;
            }

            if (PlayerInteractor.AttackPressed) usable.OnUsePressed(this);
            else if (PlayerInteractor.AttackHeld) usable.OnUseHeld(this);
            if (PlayerInteractor.AttackReleased) usable.OnUseReleased(this);
        }

        private IHandUsable ResolveUsable()
        {
            if (!IsHolding) return null;
            return ResolveVariantUsable(ActiveHandVariant);
        }

        private static IHandUsable ResolveVariantUsable(GameObject source)
        {
            if (source == null) return null;

            IHandUsable found = source.GetComponent<IHandUsable>();
            if (found == null) found = source.GetComponentInChildren<IHandUsable>();
            return found;
        }

        private void EnsureSharedAudioSource()
        {
            if (sharedAudioSource == null) sharedAudioSource = GetComponent<AudioSource>();
            if (sharedAudioSource == null) sharedAudioSource = gameObject.AddComponent<AudioSource>();

            sharedAudioSource.playOnAwake = false;
            sharedAudioSource.loop = false;
            sharedAudioSource.spatialBlend = 0f;
            sharedAudioSource.volume = sharedVolume;
        }

        public bool TryGetAimRay(out Ray ray)
        {
            if (interactor != null) return interactor.TryGetAimRay(out ray);

            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        private void LateUpdate()
        {
            UpdateHandVisibility();

            bool interactive = CanInteractWithTarget();
            CrosshairInteractive = interactive;
            crosshairBlend = Mathf.MoveTowards(crosshairBlend, interactive ? 1f : 0f, crosshairFadeSpeed * Time.deltaTime);
            ApplyCrosshairState();
        }

        /// <summary>
        /// Прячет всю руку целиком только в машине. За терминалом рука с
        /// предметом остаётся видимой. Варианты предметов не трогаются — гаснет
        /// только корень рук, поэтому логика удержания остаётся целой.
        /// </summary>
        private void UpdateHandVisibility()
        {
            if (holdSocket == null || holdSocket == transform) return;

            bool inCar = playerController != null && playerController.IsInCar;
            bool visible = !inCar;

            if (holdSocket.gameObject.activeSelf != visible) holdSocket.gameObject.SetActive(visible);
        }

        public bool CanInteractWithTarget()
        {
            if (interactor == null) return false;

            IInteractable target = interactor.Target;
            if (target == null) return false;
            if (hideInCar && playerController != null && playerController.IsInCar) return false;

            ICanInteract check = target as ICanInteract;
            return check == null || check.CanInteract(interactor);
        }

        /// <summary>
        /// Единая точка переключения прицела. Режим выбирается здесь же, а не
        /// снаружи: огнестрел определяется по классу предмета, который игрок
        /// сейчас держит или экипировал, взаимодействие — по наличию цели под
        /// прицелом, в машине прицел не показывается вовсе.
        /// </summary>
        private void ApplyCrosshairState()
        {
            bool inCar = hideInCar && playerController != null && playerController.IsInCar;

            // Цель под прицелом важнее оружия: игрок должен видеть, что с
            // предметом можно взаимодействовать, даже держа ствол. Поэтому
            // состояние взаимодействия показывается поверх боевого, а не
            // вместо него только когда ствола нет.
            bool showInteract = !inCar && crosshairBlend > 0.001f;
            bool showFirearm = !inCar && HoldingFirearm && !showInteract;
            bool showNormal = !inCar && !showFirearm && !showInteract;

            Toggle(crosshairFirearm, showFirearm);
            Toggle(crosshairNormal, showNormal);
            Toggle(crosshairInteract, showInteract);

            if (interactPunchScale <= 1.001f) return;
            if (crosshairInteract != null)
                crosshairInteract.transform.localScale = interactBaseScale * Mathf.Lerp(1f, interactPunchScale, crosshairBlend);
        }

        private static void Toggle(GameObject target, bool active)
        {
            if (target == null) return;
            if (target.activeSelf != active) target.SetActive(active);
        }

        /// <summary>
        /// Показывает в руке предмет из выбранной ячейки инвентаря. Сам предмет в
        /// ячейке остаётся: рука — это вид слота, а не место хранения. Раньше
        /// предмет при выборе ячейки забирался из инвентаря, и следующий предмет
        /// приходилось класть обратно — из-за этого в руке нельзя было просто
        /// переключаться между ячейками.
        ///
        /// slotIndex запоминается, чтобы бросок убрал предмет из этой же ячейки.
        /// </summary>
        public bool ShowInventoryItem(ItemDef def, int slotIndex)
        {
            if (def == null)
            {
                PutAway();
                return true;
            }

            GameObject variant = ResolveHandVariant(def.Id);
            if (variant == null)
            {
                // Варианта в руках нет, но предмет в инвентаре остаётся: слот
                // выбран, просто показать его нечем.
                PutAway();
                return false;
            }

            // Поднятый с земли предмет сменить можно: он возвращается на своё
            // место, а в руку встаёт выбранный предмет. Раньше здесь было
            // «return false» — пока в руке лежал поднятый предмет, слоты
            // инвентаря вообще не переключались.
            if (HeldItem != null) PutAway();

            usable?.OnUseCanceled();
            usable = null;

            heldFromInventory = def;
            heldFromInventorySlot = slotIndex;
            Charge = 0f;

            ShowHandVariant(variant);
            NotifyEquipped();
            return true;
        }

        /// <summary>
        /// Убирает предмет из руки, ничего не выбрасывая. Поднятый с земли
        /// просто возвращается на то же место, откуда его взяли: он и не
        /// двигался, а был выключен. Предмет из инвентаря никуда не девается —
        /// он остался в своей ячейке.
        ///
        /// Нужен для переключения между слотами и для освобождения руки под
        /// ствол: раньше на этих местах стоял Drop(), и выбор ячейки ронял
        /// вещь из руки на пол, хотя выброс должен быть только по ПКМ.
        /// </summary>
        public void PutAway()
        {
            if (!IsHolding) return;

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;

            FlushCanisterFuelToWorld();

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;
            Charge = 0f;
            DropBlocked = false;
            UseLocked = false;

            ShowHandVariant(null);

            if (item != null)
            {
                Vector3 position = item.transform.position;
                Quaternion rotation = item.transform.rotation;
                item.DropFromHand(position, rotation);
            }
        }

        public bool PickUp(PickupItem item)
        {
            if (item == null || item.IsHeld || item.IsConsumed || IsHolding) return false;

            GameObject variant = ResolveHandVariant(item.ItemId);
            HeldItem = item;
            chargeStartTime = Time.time;
            Charge = 0f;
            item.AttachToHand();
            ShowHandVariant(variant);
            BindCanister(item);
            NotifyEquipped();
            return true;
        }

        /// <summary>
        /// Сообщает предмету, что он только что подобран. Сброс анимации именно
        /// здесь обязателен: вариант руки один и тот же для всех предметов с
        /// одинаковым ID, поэтому после применения он остаётся в кадре лечения,
        /// и новый предмет в руке появлялся бы на этом кадре.
        /// </summary>
        private void NotifyEquipped()
        {
            CopyCanisterFuelToHand();
            IHandUsable current = ResolveUsable();
            if (current != null) current.OnEquipped(this);
        }

        /// <summary>
        /// Запрет на бросок. Нужен предметам с длительным применением
        /// (инджектор): пока идёт применение, выброс нельзя, иначе предмет
        /// можно выронить и снова подобрать, то есть накопить несколько копий.
        /// </summary>
        public void SetDropBlocked(bool value)
        {
            DropBlocked = value;
            UseLocked = value;
            if (!value) return;

            Charge = 0f;
            chargeStartTime = Time.time;
        }

        /// <summary>
        /// Освобождает руку под другой предмет, не бросая текущий.
        /// Вариант руки выключается, HeldItem очищается, а сам предмет
        /// переходит в состояние «израсходован» и больше не поднимается.
        /// </summary>
        public void ReleaseUsedItem()
        {
            if (!IsHolding) return;

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;

            FlushCanisterFuelToWorld();

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;
            Charge = 0f;
            DropBlocked = false;
            UseLocked = false;

            ShowHandVariant(null);

            if (item != null) item.MarkConsumed();
        }

        /// <summary>
        /// Тратит предмет, который игрок держал из инвентаря: инджектор после
        /// лечения уходит из ячейки по одной штуке, и рука освобождается.
        ///
        /// Объект варианта руки не трогается — он просто выключается и включится
        /// снова, если в ячейке ещё остался инджектор. Убирать из руки нечего:
        /// предмета из мира здесь нет, он лежит в ячейке.
        /// </summary>
        public bool ConsumeHeldInventoryItem()
        {
            if (!IsHolding) return false;

            ItemDef def = heldFromInventory;
            int slot = heldFromInventorySlot;

            // Рука освобождается до правки инвентаря: правка поднимет событие
            // Changed, по нему инвентарь пересоберёт вид и обновит руку заново —
            // и к этому моменту предмет уже не должен считаться применяемым.
            ReleaseUsedItem();
            if (def == null) return false;

            Inventory inventory = ResolveInventory();
            if (inventory == null)
            {
                Debug.LogWarning("[PlayerHand] Нет инвентаря: '" + def.DisplayName + "' израсходован, но ячейку уменьшить нечем.", this);
                return false;
            }

            return inventory.TryTakeOne(def, slot, out _);
        }

        /// <summary>
        /// Описание предмета, который сейчас в руке: и физического из мира, и
        /// выбранного из ячейки инвентаря. null — рука пуста или предмет не
        /// известен инвентарю. По нему депозитная деталь решает, это Golden Bolt.
        /// </summary>
        public ItemDef ResolveHeldDef()
        {
            if (heldFromInventory != null) return heldFromInventory;

            PickupItem item = HeldItem;
            if (item == null) return null;

            ItemDef def;
            return ItemDefs.TryGet(item.ItemId, out def) ? def : null;
        }

        /// <summary>
        /// Забирает предмет из руки, как будто он потрачен по назначению, и
        /// возвращает, что именно было в руке. Физический предмет помечается
        /// израсходованным и выключается, предмет из ячейки убывает по одной
        /// штуке — ни то, ни другое не уничтожается. null — рука пуста.
        /// </summary>
        public ItemDef ConsumeHeldItem()
        {
            ItemDef def = ResolveHeldDef();
            if (def == null || !IsHolding) return null;

            if (HeldItem != null) ReleaseUsedItem();
            else ConsumeHeldInventoryItem();

            return def;
        }

        /// <summary>
        /// Описание предмета для депозита, включая экипированный ствол. Оружие
        /// в руку не встаёт — его занимает вариант ствола, — поэтому ResolveHeldDef
        /// его не видит, и без этой ветки оружие нельзя было положить в ставку
        /// автомата. Всё, что уже в руке, по-прежнему важнее ствола.
        /// </summary>
        public ItemDef ResolveDepositableDef()
        {
            ItemDef held = ResolveHeldDef();
            if (held != null) return held;

            PlayerFirearm current = ResolveFirearm();
            return current != null && current.HasFirearm ? current.EquippedDef : null;
        }

        /// <summary>
        /// Забирает предмет для депозита так же, как ConsumeHeldItem, но умеет и
        /// экипированный ствол: сначала снимает его с руки, затем убирает одну
        /// единицу из его ячейки. Ни вариант руки, ни сам ствол не уничтожаются —
        /// вариант просто выключается, а предмет уходит из инвентаря.
        /// </summary>
        public ItemDef ConsumeDepositableItem()
        {
            if (IsHolding) return ConsumeHeldItem();

            PlayerFirearm current = ResolveFirearm();
            if (current == null || !current.HasFirearm) return null;

            ItemDef def = current.EquippedDef;
            int slot = current.EquippedIndex;
            current.Unequip();

            Inventory inventory = ResolveInventory();
            if (inventory != null && slot >= 0) inventory.RemoveAt(slot, 1);

            return def;
        }

        public void Drop()
        {
            if (DropBlocked) return;

            float charge = Charge;
            Charge = 0f;

            if (!IsHolding)
            {
                // Ствол в руку не встаёт: он экипирован, и руку занимает его
                // вариант. Но выбросить его тоже надо, поэтому предметом для
                // выброса берём экипированный ствол.
                DropEquippedFirearm(charge);
                return;
            }

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;
            ItemDef fromInventory = heldFromInventory;
            int fromSlot = heldFromInventorySlot;

            CaptureCanisterFuel();

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;
            ShowHandVariant(null);

            ResolveThrow(charge, out Vector3 direction, out Vector3 origin, out Quaternion rotation, out float speed);

            // Предмет из инвентаря лежит в ячейке, поэтому выброс сначала убирает его
            // оттуда: рука только показывает предмет, иначе на земле появилась бы
            // вторая копия. Вариант руки при этом не трогается — он включится
            // снова, когда игрок выберет ту же ячейку.
            if (fromInventory != null)
            {
                ThrowInventoryItem(fromInventory, fromSlot, origin, rotation, direction, speed);
                return;
            }

            if (item != null)
            {
                item.ThrowFromHand(origin, rotation, direction, speed, Player);
            }
        }

        private void DropCubeByLmb()
        {
            if (DropBlocked) return;

            float charge = Charge;
            Charge = 0f;

            if (!IsHolding)
            {
                DropEquippedFirearm(charge);
                return;
            }

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;
            ItemDef fromInventory = heldFromInventory;
            int fromSlot = heldFromInventorySlot;

            CaptureCanisterFuel();

            GameObject mobPrefab = ResolveSpawnMobPrefab(item);

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;

            ShowHandVariant(null);

            ResolveThrow(charge, out Vector3 direction, out Vector3 origin, out Quaternion rotation, out float speed);

            if (fromInventory != null)
            {
                ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
                Inventory inventory = ResolveInventory();
                if (catalog == null || inventory == null) return;
                if (!inventory.TryTakeOne(fromInventory, fromSlot, out int payload)) return;
                GameObject spawned = catalog.Spawn(fromInventory, origin, rotation, 1, payload, direction.sqrMagnitude > 0.0001f ? direction.normalized * Mathf.Max(0f, speed) : Vector3.zero, Player);
                if (spawned == null) { inventory.TryAdd(fromInventory, 1, payload, out _); return; }

                SpawnCubeItem spawnedCube = spawned.GetComponent<SpawnCubeItem>();
                if (spawnedCube == null) spawnedCube = spawned.AddComponent<SpawnCubeItem>();
                if (spawnedCube != null) spawnedCube.SetMobPrefab(mobPrefab != null ? mobPrefab : spawnedCube.MobPrefab);

                SpawnCubeProjectile proj = spawned.GetComponent<SpawnCubeProjectile>();
                if (proj == null) proj = spawned.AddComponent<SpawnCubeProjectile>();
                if (proj != null) proj.SetMobPrefab(spawnedCube != null ? spawnedCube.MobPrefab : mobPrefab);

                Rigidbody rb = spawned.GetComponent<Rigidbody>();
                if (rb == null) rb = spawned.AddComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = false;
                return;
            }

            if (item != null)
            {
                item.ThrowFromHand(origin, rotation, direction, speed, Player);
                SpawnCubeProjectile proj = item.GetComponent<SpawnCubeProjectile>();
                if (proj == null) proj = item.GetComponentInChildren<SpawnCubeProjectile>(true);
                if (proj == null) proj = item.gameObject.AddComponent<SpawnCubeProjectile>();
                if (proj != null)
                {
                    proj.enabled = true;
                    proj.SetMobPrefab(mobPrefab != null ? mobPrefab : proj.MobPrefab);
                }
            }
        }

        /// <summary>
        /// На какой моб должен превратиться выброшенный куб. Сначала ищем
        /// SpawnCubeItem на варианте руки или на физическом предмете: у префабов
        /// кубов ссылка на моб задана в инспекторе. Для предметов из инвентаря
        /// (HeldItem пуст) запасной вариант — у выброшенного экземпляра.
        /// </summary>
        private GameObject ResolveSpawnMobPrefab(PickupItem item)
        {
            GameObject mobPrefab = null;
            SpawnCubeItem cubeItem = null;
            if (activeVariant != null)
            {
                cubeItem = activeVariant.GetComponent<SpawnCubeItem>();
                if (cubeItem == null) cubeItem = activeVariant.GetComponentInChildren<SpawnCubeItem>(true);
                if (cubeItem != null) mobPrefab = cubeItem.MobPrefab;
            }
            if (mobPrefab == null && item != null)
            {
                SpawnCubeItem itemCube = item.GetComponent<SpawnCubeItem>();
                if (itemCube == null) itemCube = item.GetComponentInChildren<SpawnCubeItem>(true);
                if (itemCube != null) mobPrefab = itemCube.MobPrefab;
            }
            if (mobPrefab == null && item != null)
            {
                SpawnCubeProjectile itemProj = item.GetComponent<SpawnCubeProjectile>();
                if (itemProj == null) itemProj = item.GetComponentInChildren<SpawnCubeProjectile>(true);
                if (itemProj != null) mobPrefab = itemProj.MobPrefab;
            }
            return mobPrefab;
        }

        /// <summary>
        /// Бросок предмета, взятого из инвентаря. Сначала предмет убирается из
        /// ячейки вместе с состоянием (у ствола — с заряженными патронами), затем
        /// в мир ставится его альт-версия из Assets/Prefabs/Items.
        ///
        /// Если поставить предмет не удалось, он возвращается обратно в инвентарь:
        /// выброс не должен незаметно съедать вещь.
        /// </summary>
        private void ThrowInventoryItem(ItemDef def, int slotIndex, Vector3 origin, Quaternion rotation, Vector3 direction, float speed)
        {
            Vector3 velocity = direction.sqrMagnitude > 0.0001f
                ? direction.normalized * Mathf.Max(0f, speed)
                : Vector3.zero;

            SpawnInventoryItem(def, slotIndex, origin, rotation, velocity);
        }

        /// <summary>
        /// Снимает предмет с ячейки инвентаря и ставит в мир его альт-версию с
        /// указанной скоростью. Если поставить не удалось, предмет возвращается
        /// обратно в инвентарь: выброс не должен незаметно съедать вещь.
        /// Возвращает появившийся предмет (или null).
        /// </summary>
        private PickupItem SpawnInventoryItem(ItemDef def, int slotIndex, Vector3 origin, Quaternion rotation, Vector3 velocity)
        {
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null)
            {
                Debug.LogWarning("[PlayerHand] Нет каталога альт-версий предметов: '" + def.DisplayName + "' нечем бросить. Предмет остался в инвентаре.", this);
                return null;
            }

            Inventory inventory = ResolveInventory();
            if (inventory == null)
            {
                Debug.LogWarning("[PlayerHand] Нет инвентаря: '" + def.DisplayName + "' остался в руке и не выброшен.", this);
                return null;
            }

            if (!inventory.TryTakeOne(def, slotIndex, out int payload))
            {
                // Предмета в инвентаре уже нет: рука просто освобождается, ронять
                // в мир нечего.
                Debug.LogWarning("[PlayerHand] '" + def.DisplayName + "' уже нет в инвентаре — выбрасывать нечего.", this);
                return null;
            }

            GameObject spawned = catalog.Spawn(def, origin, rotation, 1, payload, velocity, Player);
            if (spawned == null)
            {
                inventory.TryAdd(def, 1, payload, out _);
                return null;
            }

            // Выброшенная из инвентаря канистра получает своё топливо, а не
            // стандартные 40: иначе бензин терялся бы при каждом выбросе.
            if (pendingCanisterFuel >= 0f)
            {
                GasCanItem spawnedCan = spawned.GetComponent<GasCanItem>();
                if (spawnedCan == null) spawnedCan = spawned.GetComponentInChildren<GasCanItem>();
                if (spawnedCan != null) spawnedCan.SetFuel(pendingCanisterFuel);
                pendingCanisterFuel = -1f;
            }

            return spawned.GetComponent<PickupItem>();
        }

        private Inventory ResolveInventory()
        {
            if (interactor != null && interactor.Inventory != null) return interactor.Inventory;
            return FindFirstObjectByType<Inventory>();
        }

        /// <summary>
        /// Направление, точка вылета, поворот и скорость броска. Считается по
        /// лучу прицела, а если его нет — по руке: так броски одинаковы и для
        /// предмета из руки, и для экипированного ствола.
        /// </summary>
        private void ResolveThrow(float charge, out Vector3 direction, out Vector3 origin, out Quaternion rotation, out float speed)
        {
            if (interactor != null && interactor.TryGetAimRay(out Ray aimRay))
            {
                direction = aimRay.direction;
                origin = aimRay.GetPoint(throwOriginOffset);
                rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            else
            {
                direction = HoldSocket.forward;
                origin = HoldSocket.position + direction * throwOriginOffset;
                rotation = HoldSocket.rotation;
            }

            speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, charge);
        }

        /// <summary>
        /// Выброс экипированного ствола. Ствол не берётся из инвентаря в руку, но
        /// он экипирован и занимает её своим вариантом, поэтому выбросить его
        /// по правой кнопке игрок должен иметь возможность.
        ///
        /// Ствол снимается с руки раньше, чем уходит из ячейки: иначе он остался
        /// бы экипированным уже без предмета, и счётчик патронов показывал бы
        /// несуществующий магазин.
        /// </summary>
        private void DropEquippedFirearm(float charge)
        {
            PlayerFirearm current = ResolveFirearm();
            if (current == null || !current.HasFirearm) return;

            ItemDef def = current.EquippedDef;
            int slot = current.EquippedIndex;
            if (def == null || slot < 0) return;

            ResolveThrow(charge, out Vector3 direction, out Vector3 origin, out Quaternion rotation, out float speed);

            current.Unequip();
            ThrowInventoryItem(def, slot, origin, rotation, direction, speed);
        }

        private PlayerFirearm ResolveFirearm()
        {
            if (firearm != null) return firearm;
            return FindFirstObjectByType<PlayerFirearm>();
        }

        public void DropAt(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!IsHolding || DropBlocked) return;

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;
            ItemDef fromInventory = heldFromInventory;
            int fromSlot = heldFromInventorySlot;

            CaptureCanisterFuel();

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;
            Charge = 0f;
            ShowHandVariant(null);

            // Предмет из инвентаря в руке не лежит — его показывает только вариант
            // руки. Убрать его в мир можно лишь сняв с ячейки и поставив альт-версию,
            // иначе DropFromHand упадёт на пустом HeldItem.
            if (fromInventory != null)
            {
                SpawnInventoryItem(fromInventory, fromSlot, worldPosition, worldRotation, Vector3.zero);
                return;
            }

            if (item != null) item.DropFromHand(worldPosition, worldRotation);
        }

        /// <summary>
        /// Режим FastWork багажника: предмет не выбрасывается над триггером, а
        /// сразу переносится в место хранения (слот/ячейку сундука). Из инвентаря
        /// предмет снимается с ячейки и ставится в мир альт-версией, из руки —
        /// освобождается и замораживается в слоте. Вариант руки при этом не
        /// трогается (он лишь показывает предмет) и не уничтожается.
        /// </summary>
        public PickupItem StoreHeldItemAt(Transform storage)
        {
            if (!IsHolding || DropBlocked || storage == null) return null;

            usable?.OnUseCanceled();
            usable = null;

            PickupItem item = HeldItem;
            ItemDef fromInventory = heldFromInventory;
            int fromSlot = heldFromInventorySlot;

            HeldItem = null;
            heldFromInventory = null;
            heldFromInventorySlot = -1;
            Charge = 0f;
            ShowHandVariant(null);

            if (fromInventory != null)
            {
                PickupItem world = SpawnInventoryItem(fromInventory, fromSlot, storage.position, storage.rotation, Vector3.zero);
                if (world != null) world.Store(storage, storage.position, storage.rotation);
                return world;
            }

            if (item == null) return null;

            item.DropFromHand(storage.position, storage.rotation);
            item.Store(storage, storage.position, storage.rotation);
            return item;
        }

        public void CacheHandVariants()
        {
            ShowHandVariant(null);
            variantsById.Clear();
            reportedMissingIds.Clear();

            for (int i = 0; i < handVariants.Length; i++)
            {
                HandVariant entry = handVariants[i];
                if (entry == null || entry.variant == null) continue;
                if (entry.variant.activeSelf) entry.variant.SetActive(false);

                // Варианты в сцене размечены номерами ("1" — ключ, "2" — ящик), а
                // предметы в инвентаре хранятся строками. Здесь ID приводится к
                // строковому виду, иначе ключ из инвентаря не нашёл бы свою руку.
                string key = ItemDefs.Canonical(entry.itemId);
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogWarning($"[PlayerHand] У варианта '{entry.variant.name}' пустой ID — в руке он не покажется", this);
                    continue;
                }

                if (variantsById.ContainsKey(key))
                {
                    Debug.LogWarning($"[PlayerHand] Дубликат ID '{key}' среди вариантов рук: '{entry.variant.name}'. Будет показан первый", this);
                    continue;
                }

                variantsById.Add(key, entry.variant);
            }
        }

        private GameObject ResolveHandVariant(string itemId)
        {
            GameObject variant = FindHandVariant(itemId);
            if (variant == null) WarnAboutMissingVariant(itemId);
            else EnsureVisual(itemId, variant);
            return variant;
        }

        /// <summary>
        /// У части предметов в сцене заготовлен пустой вариант: GameObject есть,
        /// скрипт и аниматор на месте, а рисовать нечем — сама модель лежит
        /// только внутри префаба альт-версии. Ссылаться на меш префаба сцена не
        /// умеет, поэтому модель достраивается здесь, один раз на вариант.
        /// Дальше вариант уже не пустой и пересобираться не будет: список
        /// собранных намеренно не чистится вместе с остальными вариантами.
        /// </summary>
        private void EnsureVisual(string itemId, GameObject variant)
        {
            if (variant == null || builtVisuals.Contains(variant)) return;
            builtVisuals.Add(variant);

            if (variant.GetComponentInChildren<Renderer>(true) != null) return;

            ItemDef def;
            if (!ItemDefs.TryGet(itemId, out def))
            {
                Debug.LogWarning($"[PlayerHand] У пустого варианта '{variant.name}' неизвестный ID '{itemId}' — показать его нечем", this);
                return;
            }

            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null)
            {
                Debug.LogWarning($"[PlayerHand] Нет каталога предметов — '{def.DisplayName}' в руке останется пустым", this);
                return;
            }

            catalog.BuildHandVisual(def, variant, handVisualSize);
        }

        /// <summary>
        /// Ищет заготовленный вариант руки по ID предмета. Сравнение идёт и по
        /// исходной строке, и по приведённой к канону, поэтому старые номера из
        /// сцены ("1") и строковые ID ("wrench") находят один и тот же вариант.
        /// </summary>
        public GameObject FindHandVariant(string itemId)
        {
            if (variantsById.Count == 0 || string.IsNullOrEmpty(itemId)) return null;

            GameObject variant;
            if (variantsById.TryGetValue(itemId, out variant)) return variant;

            string canonical = ItemDefs.Canonical(itemId);
            if (!string.IsNullOrEmpty(canonical) && variantsById.TryGetValue(canonical, out variant)) return variant;

            return null;
        }

        /// <summary>Все ID, для которых в руках есть готовый вариант.</summary>
        public System.Collections.Generic.IEnumerable<string> HandVariantIds()
        {
            return variantsById.Keys;
        }

        private void WarnAboutMissingVariant(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            if (!reportedMissingIds.Add(itemId)) return;

            Debug.LogWarning($"[PlayerHand] В руках нет варианта с ID '{itemId}' — предмет в руке не покажется", this);
        }

        /// <summary>
        /// Включает заготовленный вариант руки по ID. Этим же пользуется огнестрел:
        /// ствол лежит в руках как обычный вариант, а копия в runtime не создаётся.
        /// null — выключить текущий вариант.
        /// </summary>
        public GameObject ShowHandVariantById(string itemId)
        {
            GameObject variant = ResolveHandVariant(itemId);
            ShowHandVariant(variant);
            return variant;
        }

        private void ShowHandVariant(GameObject variant)
        {
            if (activeVariant != null && activeVariant != variant && activeVariant.activeSelf)
                activeVariant.SetActive(false);

            activeVariant = variant;

            if (variant != null && !variant.activeSelf) variant.SetActive(true);
        }

        /// <summary>
        /// Запоминает физическую канистру, из которой в руку попал бензин, и
        /// переносит её топливо в вариант руки. Так у каждой канистры в мире своё
        /// количество бензина, а не общее на всех.
        /// </summary>
        public void BindCanister(PickupItem canister)
        {
            if (canister == null) return;

            GasCanItem worldCan = canister.GetComponent<GasCanItem>();
            if (worldCan == null) worldCan = canister.GetComponentInChildren<GasCanItem>();
            if (worldCan == null) return;

            heldCanister = canister;
            CopyCanisterFuelToHand();
        }

        /// <summary>
        /// Переносит топливо физической канистры в вариант руки, чтобы в руке
        /// показывалось именно топливо этой канистры.
        /// </summary>
        private void CopyCanisterFuelToHand()
        {
            if (heldCanister == null) return;

            GasCanItem worldCan = heldCanister.GetComponent<GasCanItem>();
            if (worldCan == null) worldCan = heldCanister.GetComponentInChildren<GasCanItem>();
            GasCanItem handCan = ResolveActiveCanItem();
            if (worldCan == null || handCan == null) return;

            handCan.SetFuel(worldCan.CurrentFuel);
        }

        /// <summary>
        /// Переносит топливо из варианта руки обратно в физическую канистру —
        /// при уборке, выбросе или броске предмета.
        /// </summary>
        public void FlushCanisterFuelToWorld()
        {
            if (heldCanister == null) return;

            GasCanItem handCan = ResolveActiveCanItem();
            GasCanItem worldCan = heldCanister.GetComponent<GasCanItem>();
            if (worldCan == null) worldCan = heldCanister.GetComponentInChildren<GasCanItem>();

            if (handCan != null && worldCan != null) worldCan.SetFuel(handCan.CurrentFuel);
        }

        /// <summary>
        /// Запоминает топливо из варианта руки перед уборкой, чтобы применить его
        /// к появляющейся в мире канистре (выброс из инвентаря создаёт новый объект).
        /// </summary>
        private void CaptureCanisterFuel()
        {
            pendingCanisterFuel = -1f;
            GasCanItem handCan = ResolveActiveCanItem();
            if (handCan != null) pendingCanisterFuel = handCan.CurrentFuel;
        }

        private GasCanItem ResolveActiveCanItem()
        {
            return ResolveVariantComponent<GasCanItem>(activeVariant);
        }

        private static T ResolveVariantComponent<T>(GameObject variant) where T : Component
        {
            if (variant == null) return null;
            T component = variant.GetComponent<T>();
            if (component == null) component = variant.GetComponentInChildren<T>(true);
            return component;
        }
    }
}
