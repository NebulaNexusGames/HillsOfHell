using CarGame.Controls;
using CarGame.Player;
using CarGame.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarGame.Items
{
    public enum FirearmAction
    {
        [InspectorName("Бездействие")] Idle = 0,
        [InspectorName("Выстрел")] Shoot = 1,
        [InspectorName("Перезарядка")] Reload = 2,
        [InspectorName("Осмотр")] Inspect = 3
    }

    /// <summary>
    /// Выбор ствола и ввод. Само оружие здесь не хранится: магазин, перезарядка,
    /// выстрел и анимация живут на своём варианте руки (WeaponItem) — так же,
    /// как ключ и инжектор живут на своих. Этот компонент решает, какой ствол
    /// экипирован, и передаёт ему нажатия.
    ///
    /// Проверка «есть ли ствол» идёт по самому предмету, а не через WeaponItem:
    /// если HasFirearm спрашивал бы у варианта, а вариант — обратно, эти два
    /// свойства завалили бы стек при первом же обращении.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerFirearm : MonoBehaviour
    {
        [Header("Ввод")]
        [Tooltip("Выбирать ствол цифрами 1..5 по слотам быстрого доступа")]
        [SerializeField] private bool selectByNumberKeys = true;
        [Tooltip("Разрешить стрелять, сидя в машине")]
        [SerializeField] private bool allowFireInCar;

        private Inventory inventory;
        private PlayerInteractor interactor;
        private PlayerController playerController;
        private FirearmViewmodel viewmodel;
        private PlayerHand hand;

        private int equippedIndex = -1;
        private WeaponItem weapon;
        private string weaponId;

        public event System.Action Changed;

        /// <summary>Есть ли экипированный ствол.</summary>
        public bool HasFirearm => equippedIndex >= 0 && EquippedDef != null && EquippedDef.IsFirearm;

        public FirearmSpec Spec => EquippedDef != null && EquippedDef.IsFirearm
            ? FirearmSpecs.Resolve(EquippedDef.Firearm)
            : null;
        public ItemDef EquippedDef { get; private set; }
        public int EquippedIndex => equippedIndex;

        /// <summary>
        /// Живой ствол в руке. Ищется по варианту руки того же ID, что и
        /// экипированный предмет: вариант один на вид ствола, и на нём же живёт
        /// вся работа оружия.
        /// </summary>
        public WeaponItem Weapon
        {
            get
            {
                if (EquippedDef == null) return null;
                if (weapon != null && weaponId == EquippedDef.Id) return weapon;

                weapon = null;
                weaponId = null;

                if (hand != null)
                {
                    GameObject variant = hand.FindHandVariant(EquippedDef.Id);
                    if (variant != null) weapon = variant.GetComponent<WeaponItem>();
                }

                if (weapon != null) weapon.Configure(EquippedDef.Id);
                weaponId = EquippedDef.Id;
                return weapon;
            }
        }

        public int Magazine => Weapon != null ? Weapon.Magazine : 0;
        public int MagazineCapacity => Weapon != null ? Weapon.MagazineCapacity : 0;
        public int ReserveAmmo => Weapon != null ? Weapon.ReserveAmmo : 0;
        public bool HasReserveForReload => Weapon != null && Weapon.HasReserveForReload;

        public FirearmAction Action => Weapon != null ? Weapon.Action : FirearmAction.Idle;
        public bool IsReloading => Weapon != null && Weapon.IsReloading;
        public bool IsInspecting => Weapon != null && Weapon.IsInspecting;
        public bool IsShooting => Weapon != null && Weapon.IsShooting;
        public bool IsBusy => Weapon != null && Weapon.IsBusy;

        public static bool ReloadPressed => KeyBindings.WasPressedThisFrame(GameAction.Reload);
        public static bool InspectPressed => KeyBindings.WasPressedThisFrame(GameAction.Inspect);
        public static bool InventoryPressed => KeyBindings.WasPressedThisFrame(GameAction.Inventory);

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            if (inventory == null) inventory = GetComponentInParent<Inventory>();

            interactor = GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = GetComponentInParent<PlayerInteractor>();

            playerController = GetComponent<PlayerController>();
            if (playerController == null) playerController = GetComponentInParent<PlayerController>();

            viewmodel = GetComponent<FirearmViewmodel>();
            if (viewmodel == null) viewmodel = GetComponentInParent<FirearmViewmodel>();

            hand = GetComponent<PlayerHand>();
            if (hand == null) hand = GetComponentInParent<PlayerHand>();
        }

        private void OnEnable()
        {
            if (inventory != null) inventory.Changed += RaiseChanged;
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.Changed -= RaiseChanged;
        }

        private void Update()
        {
            // Панель терминала ловит ЛКМ, только когда прицел стоит на пункте,
            // поэтому рядом с терминалом стрелять можно, а вот по самой панели — нет.
            // Депозитная деталь магазина та же история: прицел на ней — ЛКМ кладёт
            // Golden Bolt в слот, а не стреляет. У автомата так же работают приём
            // ставки и рычаг: прицел на них отдаёт клик автомату, не стволу.
            if (TerminalUI.AimingSlot || BoltDepositor.AimingDeposit || SlotMachineDeposit.AimingDeposit || SlotMachineLever.AimingLever || ExchangerTerminal.AimingExchange || ShopTerminalUI.AimingSlot || ExchangerTerminalUI.AimingButton || CraftingStation.AimingPress) return;

            // Пока расходник применяется, цифры не переключают ствол: иначе
            // смена оружия сбила бы анимацию применения.
            if (PlayerHand.UseLocked) return;

            // Пока открыт любой интерфейс, ЛКМ/ПКМ принадлежат ему: в инвентаре
            // цифры выбирают ячейки, а на терминале и панели клик не должен
            // превращаться в выстрел или перезарядку.
            if (CarGame.UI.UiState.AnyOpen) return;

            UpdateQuickSelect();
            UpdateTrigger();
        }

        private void UpdateQuickSelect()
        {
            if (!selectByNumberKeys || Keyboard.current == null) return;

            for (int i = 0; i < Inventory.QuickSlotCount; i++)
            {
                if (!QuickKeyDown(i)) continue;
                TryEquipFromQuickSlot(i);
                return;
            }
        }

        /// <summary>
        /// Проверки «можно ли сейчас стрелять» — всё, что касается игрока, здесь.
        /// Всё, что касается ствола, — в WeaponItem: там же читается кнопка с
        /// учётом того, зажатая она или нажата.
        /// </summary>
        private void UpdateTrigger()
        {
            bool inCar = playerController != null && playerController.IsInCar;

            if (HasFirearm && interactor != null && interactor.Hand.ShowsHandUsable)
            {
                // Применяемый предмет в руке важнее ствола: убираем оружие, чтобы в
                // кадре не оказались одновременно и инжектор, и пистолет. Патроны
                // применять нечего, поэтому из-за них ствол остаётся в руках —
                // иначе перезарядиться было бы нечем.
                Unequip();
            }

            if (!HasFirearm || inCar)
            {
                if (inCar) Weapon?.CancelActions();
                viewmodel?.Hide();
                return;
            }

            // Рука занята другим предметом: патроны, металл, любой выбранный слот.
            // Раньше ствол показывался здесь каждый кадр и забирал руку себе, так
            // что выбранный предмет было не видно, пока ствол экипирован. Теперь
            // предмет важнее ствола, но сам ствол остаётся экипированным.
            bool handTaken = HandHoldsOtherItem();

            // Сначала показываем вариант: пока ствол не показан, в нём не
            // достроены ни модель, ни Animator.
            if (!handTaken) viewmodel?.Show(EquippedDef.Firearm);

            WeaponItem current = Weapon;
            if (current == null) return;

            if (current.IsBusy) return;
            if (InventoryUI.AnyOpen) return;
            if (!allowFireInCar && inCar) return;

            // Перезарядка и осмотр остаются доступны всегда: с патронами в руке
            // перезарядиться всё равно надо, иначе их некуда было бы класть.
            if (ReloadPressed) TryStartReload();
            if (InspectPressed) TryStartInspect();

            // А вот стрелять с чужим предметом в руке незачем: в кадре ствола
            // нет, значит и выстрела быть не должно.
            if (handTaken) return;
            if (!current.WantsFire) return;

            if (current.Fire()) RaiseChanged();
        }

        /// <summary>
        /// В руке лежит не тот ствол, который экипирован, а другой предмет:
        /// металл, патроны или что-то ещё, выбранное игроком.
        /// </summary>
        private bool HandHoldsOtherItem()
        {
            PlayerHand current = hand != null ? hand : interactor != null ? interactor.Hand : null;
            if (current == null || !current.IsHolding || EquippedDef == null) return false;
            return !string.Equals(current.HeldId, EquippedDef.Id, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Экипирует ствол из слота быстрого доступа.
        /// </summary>
        public bool TryEquipFromQuickSlot(int index)
        {
            if (inventory == null) return false;
            if (!Inventory.IsQuickIndex(index)) return false;

            return TryEquipFromSlot(index);
        }

        /// <summary>
        /// Экипирует ствол из любого слота, а не только из быстрого: игрок мог
        /// переложить дробовик в обычную часть инвентаря и должен иметь право
        /// достать его оттуда кликом.
        ///
        /// У каждого ствола свой магазин, и магазин лежит в его же ячейке, так
        /// что переключение туда-обратно ничего не забывает. Общего заряда на
        /// пистолет и дробовик больше нет, и StoreMagazineIntoSlot не нужен:
        /// заряд и так живёт рядом с предметом.
        /// </summary>
        public bool TryEquipFromSlot(int index)
        {
            if (inventory == null) return false;
            if (index < 0 || index >= Inventory.TotalSlotCount) return false;

            ItemStack stack = inventory.Get(index);
            if (stack == null || stack.IsEmpty || !stack.Def.IsFirearm) return false;

            // Инструмент освобождает руку молча и остаётся в своей ячейке: он
            // показывается в руке, но выброс по-прежнему только по правой кнопке.
            // Раньше здесь стоял Drop(), и выбор ствола в инвентаре выбрасывал
            // ключ из руки на пол.
            if (interactor != null && interactor.Hand != null && interactor.Hand.IsHolding)
                interactor.Hand.PutAway();

            if (EquippedDef != null && EquippedDef.Id == stack.Def.Id)
            {
                equippedIndex = index;
                RaiseChanged();
                return true;
            }

            Unequip();

            equippedIndex = index;
            EquippedDef = stack.Def;

            viewmodel?.Show(EquippedDef.Firearm);

            // Новый ствол показывается первым, и только потом у него читается
            // магазин: номер слота и ID уже известны, а модель и Animator в
            // варианте к этому моменту достроены.
            WeaponItem current = Weapon;
            if (current != null)
            {
                current.CancelActions();

                ItemStack live = inventory.Get(index);
                if (live != null && !live.IsEmpty)
                {
                    int capacity = current.MagazineCapacity;
                    live.Payload = Mathf.Clamp(live.Payload, 0, capacity);
                }
            }

            if (Magazine <= 0 && HasReserveForReload) TryStartReload();

            RaiseChanged();
            return true;
        }

        public bool TryStartReload()
        {
            return Weapon != null && Weapon.TryStartReload();
        }

        public bool TryStartInspect()
        {
            return Weapon != null && Weapon.TryStartInspect();
        }

        /// <summary>
        /// Приводит номер экипированного слота в порядок после переноса предметов
        /// между ячейками. Слот, который занимал ствол, мог опустеть или достаться
        /// другому стволу, и тогда подсветка «экипировано» встала бы не туда.
        /// Если ствол вообще исчез из инвентаря, он снимается с руки.
        /// </summary>
        public void ReconcileEquippedIndex()
        {
            if (equippedIndex < 0 || EquippedDef == null) return;

            if (inventory != null)
            {
                int found = inventory.FindFirst(EquippedDef);
                if (found == equippedIndex) return;

                if (found >= 0)
                {
                    equippedIndex = found;
                    RaiseChanged();
                    return;
                }
            }

            Unequip();
        }

        public void Unequip()
        {
            Weapon?.CancelActions();

            if (equippedIndex < 0 && EquippedDef == null) return;

            equippedIndex = -1;
            EquippedDef = null;
            weapon = null;
            weaponId = null;

            viewmodel?.Hide();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        private static bool QuickKeyDown(int index)
        {
            switch (index)
            {
                case 0: return KeyBindings.WasPressedThisFrame(GameAction.Slot1);
                case 1: return KeyBindings.WasPressedThisFrame(GameAction.Slot2);
                case 2: return KeyBindings.WasPressedThisFrame(GameAction.Slot3);
                case 3: return KeyBindings.WasPressedThisFrame(GameAction.Slot4);
                case 4: return KeyBindings.WasPressedThisFrame(GameAction.Slot5);
                default: return false;
            }
        }
    }
}
