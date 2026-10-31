using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Items
{
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class PickupItem : MonoBehaviour, IInteractable, ICanInteract
    {
        [Header("Идентификатор")]
        [Tooltip("ID предмета. По нему в руках игрока находится заготовленный вариант с таким же ID")]
        [SerializeField] private string itemId = string.Empty;

        [Header("Физика")]
        [Tooltip("Масса, кг. Перекрывается весом из ItemDefs, если предмет инвентарю известен")]
        [SerializeField] private float mass = 2f;
        [Tooltip("После заморозки в багажнике коллайдеры становятся триггерами, чтобы не толкать машину")]
        [SerializeField] private bool storedCollidersAreTriggers = true;

        [Header("Коллизия с игроком")]
        [Tooltip("Предмет не толкает игрока и не мешает ему пройти: коллизия этого предмета с игроком игнорируется")]
        [SerializeField] private bool ignorePlayerCollision;

        [Header("В инвентарь")]
        [Tooltip("Сколько единиц забирает этот предмет в инвентарь. Имеет смысл для металла и патронов")]
        [SerializeField] private int stackAmount = 1;

        [Header("Состояние")]
        [Tooltip("Состояние предмета, переживающее выброс: у ствола — сколько патронов заряжено")]
        [SerializeField] private int payload;

        private readonly System.Collections.Generic.List<Collider> colliderCache = new System.Collections.Generic.List<Collider>();
        private readonly System.Collections.Generic.List<bool> triggerCache = new System.Collections.Generic.List<bool>();

        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; private set; }
        public bool IsStored { get; private set; }
        public bool IsConsumed { get; private set; }
        public string ItemId => itemId;

        /// <summary>Состояние предмета: у ствола — заряженные патроны.</summary>
        public int Payload => payload;

        /// <summary>Сколько единиц забирает в инвентарь этот экземпляр (металл, патроны).</summary>
        public int StackAmount => Mathf.Max(1, stackAmount);

        public void SetItemId(string value)
        {
            itemId = value ?? string.Empty;
        }

        public void SetStackAmount(int value)
        {
            stackAmount = Mathf.Max(1, value);
        }

        public void SetPayload(int value)
        {
            payload = value;
        }

        /// <summary>Игнорирует ли предмет коллизию с игроком.</summary>
        public bool IgnoresPlayerCollision => ignorePlayerCollision;

        /// <summary>Включает/выключает игнорирование коллизии с игроком.</summary>
        public void SetIgnorePlayerCollision(bool value)
        {
            ignorePlayerCollision = value;
            if (value) ApplyPlayerCollisionIgnore();
        }

        /// <summary>
        /// Отключает столкновения всех коллайдеров предмета с коллайдерами
        /// игрока: предмет не толкает игрока и не мешает пройти сквозь себя.
        /// Пары игнорирования живут столько же, сколько коллайдеры, поэтому
        /// достаточно применить один раз — при появлении предмета.
        /// </summary>
        public void ApplyPlayerCollisionIgnore()
        {
            if (!ignorePlayerCollision) return;

            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            Collider[] playerColliders = player.GetComponentsInChildren<Collider>(true);
            Collider[] itemColliders = GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < itemColliders.Length; i++)
            {
                if (itemColliders[i] == null) continue;

                for (int j = 0; j < playerColliders.Length; j++)
                {
                    if (playerColliders[j] == null) continue;
                    Physics.IgnoreCollision(itemColliders[i], playerColliders[j], true);
                }
            }
        }

        private bool warnedAboutScaledParent;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = ResolveMass();

            // Предмет, который можно поднять и бросить, при ударе наносит урон.
            // Компонент ставится здесь, а не вручную в префабах, чтобы правило
            // работало одинаково и для префабов из Assets/Prefabs/Items, и для
            // предметов, расставленных прямо в сцене.
            if (GetComponent<ThrownItemDamage>() == null) gameObject.AddComponent<ThrownItemDamage>();

            ApplyPlayerCollisionIgnore();
        }

        /// <summary>
        /// Масса берётся из описания предмета, а не из поля над инспектором.
        /// Один и тот же вес нужен и физике, и урону от удара: расходиться они
        /// не должны, иначе тяжёлый по урону предмет окажется лёгким в полёте.
        /// Поле остаётся запасным вариантом для предметов, которых нет в ItemDefs.
        /// </summary>
        private float ResolveMass()
        {
            ItemDef def = ResolveInventoryDef();
            return def != null ? def.Weight : mass;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor == null || interactor.Hand == null) return;
            if (IsConsumed) return;

            // Канистра переносит своё топливо в руку: у каждой канистры в мире
            // свой запас бензина, а не общий на всех.
            interactor.Hand.BindCanister(this);

            // Сначала инвентарь, рука — только если места нет. Раньше инструменты
            // всегда шли в руку, и второй гаечный ключ невозможно было поднять,
            // пока первый лежал в руке: CanInteract возвращал false.
            if (TryStoreInInventory(interactor)) return;

            interactor.Hand.PickUp(this);
        }

        /// <summary>
        /// Любой предмет, для которого инвентарь знает ID, уходит в ячейку: и
        /// металл, и патроны, и инструмент, и строй-предметы (щит, турель, таран).
        /// Инвентарь хранит сколько угодно предметов, а рука лишь показывает вариант
        /// выбранного слота, поэтому держать предмет в руке не нужно — достаточно
        /// выбрать его ячейку. Так подобранный строй-предмет не остаётся висеть в
        /// руке, а появляется в хотбаре и не возвращается туда, где лежал.
        ///
        /// Состояние предмета (у ствола — заряженные патроны) переносится в стак
        /// вместе с предметом. Объект при этом не удаляется, а просто
        /// выключается через MarkConsumed.
        /// </summary>
        private bool TryStoreInInventory(PlayerInteractor interactor)
        {
            Inventory inventory = interactor.Inventory;
            if (inventory == null) return false;

            ItemDef def = ResolveInventoryDef();
            if (def == null) return false;

            int amount = Mathf.Max(1, stackAmount);
            if (!inventory.TryAdd(def, amount, payload, out int slotIndex)) return false;

            MarkConsumed();

            // Поднятая вещь легла в быстрый слот — сразу показываем её в руке,
            // как если бы игрок нажал её цифру. Из основной сетки ничего не
            // берём: рука показывает только хотбар.
            if (Inventory.IsQuickIndex(slotIndex))
            {
                CarGame.UI.InventoryUI ui = CarGame.UI.InventoryUI.Instance;
                if (ui != null) ui.FocusQuickSlot(slotIndex);
            }

            return true;
        }

        private ItemDef ResolveInventoryDef()
        {
            ItemDef def;
            if (ItemDefs.TryGet(ItemId, out def)) return def;

            def = null;

            MetalItem metal = GetComponent<MetalItem>();
            if (metal == null) metal = GetComponentInChildren<MetalItem>();

            return metal != null ? ItemDefs.Resolve(metal.Type) : null;
        }

        /// <summary>
        /// Уходит ли предмет в ячейку инвентаря (см. <see cref="TryStoreInInventory"/>).
        /// </summary>
        private bool IsInventoryItem()
        {
            return ResolveInventoryDef() != null;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || interactor.Hand == null) return false;
            if (IsConsumed || IsHeld) return false;

            // Предмет с известным инвентарю ID поднимается всегда: инвентарь
            // хранит сколько угодно вещей, поэтому занятая рука — не помеха.
            // Иначе второй гаечный ключ было бы невозможно поднять, пока первый
            // показан в руке.
            if (IsInventoryItem() && interactor.Inventory != null) return true;

            return !interactor.Hand.IsHolding;
        }

        /// <summary>
        /// Одноразовый предмет: помечается израсходованным и больше не поднимается.
        /// Объект не удаляется — просто остаётся выключенным, как и вариант в руке.
        /// </summary>
        public void MarkConsumed()
        {
            IsConsumed = true;
            IsHeld = false;
            IsStored = false;

            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }

            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>
        /// Предмет берётся в руку. Никаких копий не создаётся: в руках
        /// показывается только заранее заготовленный вариант из PlayerHand с
        /// таким же ID, а сам объект на время удержания просто выключается.
        /// </summary>
        public void AttachToHand()
        {
            if (IsHeld) return;

            if (IsStored)
            {
                CarController car = GetComponentInParent<CarController>();
                if (car != null) car.SetStoredItemCollision(this, false);
            }

            IsHeld = true;
            IsStored = false;

            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void DropFromHand(Vector3 worldPosition, Quaternion worldRotation)
        {
            PrepareForRelease(worldPosition, worldRotation);

            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }

        public void ThrowFromHand(Vector3 worldPosition, Quaternion worldRotation, Vector3 direction, float speed, PlayerController thrower = null)
        {
            PrepareForRelease(worldPosition, worldRotation);
            NotifyThrown(thrower);

            if (Body == null) return;
            Body.linearVelocity = direction.sqrMagnitude > 0.0001f ? direction.normalized * Mathf.Max(0f, speed) : Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.WakeUp();
        }

        protected virtual void OnThrown(PlayerController thrower)
        {
        }

        /// <summary>
        /// Передаёт предмету, кто его бросил. Летящим предмет считается сам
        /// себе по скорости, но первые доли секунды после броска он не бьёт
        /// хозяина: иначе брошенный под собственные ноги ключ бьёт игрока в лицо.
        /// </summary>
        private void NotifyThrown(PlayerController thrower)
        {
            ThrownItemDamage impact = GetComponent<ThrownItemDamage>();
            if (impact != null) impact.SetThrower(thrower);
        }

        private void PrepareForRelease(Vector3 worldPosition, Quaternion worldRotation)
        {
            Vector3 worldScale = transform.lossyScale;

            IsHeld = false;
            IsStored = false;

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetWorldScale(transform, worldScale);

            RestoreColliderStates();
            if (Body != null)
            {
                Body.isKinematic = false;
                Body.useGravity = true;
            }
        }

        public void Freeze()
        {
            if (Body == null) return;

            CacheColliderStates();
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            Body.useGravity = false;
            if (storedCollidersAreTriggers) SetCollidersTrigger(true);
        }

        public void Store(Transform parent, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (IsStored) return;

            Vector3 worldScale = transform.lossyScale;
            Freeze();
            IsStored = true;
            if (parent == null) return;

            WarnAboutScaledParent(parent);

            transform.SetParent(parent, true);
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetWorldScale(transform, worldScale);
        }

        private void WarnAboutScaledParent(Transform anchor)
        {
            if (warnedAboutScaledParent) return;
            warnedAboutScaledParent = true;

            Vector3 parentScale = anchor.lossyScale;
            float min = Mathf.Min(parentScale.x, Mathf.Min(parentScale.y, parentScale.z));
            float max = Mathf.Max(parentScale.x, Mathf.Max(parentScale.y, parentScale.z));
            if (Mathf.Approximately(min, max)) return;

            Debug.LogWarning($"[PickupItem] '{anchor.name}' масштабирован неравномерно ({parentScale}). Точно сохранить размер предмета под таким родителем нельзя — нужен безмасштабный каркас", this);
        }

        public void IgnoreCollisionWith(Collider other)
        {
            if (other == null) return;

            foreach (Collider itemCollider in GetComponentsInChildren<Collider>(true))
            {
                if (itemCollider == null) continue;
                Physics.IgnoreCollision(itemCollider, other, true);
            }
        }

        private void CacheColliderStates()
        {
            colliderCache.Clear();
            triggerCache.Clear();
            colliderCache.AddRange(GetComponentsInChildren<Collider>(true));
            foreach (Collider itemCollider in colliderCache) triggerCache.Add(itemCollider.isTrigger);
        }

        private void SetCollidersTrigger(bool isTrigger)
        {
            for (int i = 0; i < colliderCache.Count; i++)
            {
                if (colliderCache[i] != null) colliderCache[i].isTrigger = isTrigger;
            }
        }

        private void RestoreColliderStates()
        {
            for (int i = 0; i < colliderCache.Count; i++)
            {
                if (colliderCache[i] == null) continue;
                colliderCache[i].enabled = true;
                colliderCache[i].isTrigger = i < triggerCache.Count && triggerCache[i];
            }
            colliderCache.Clear();
            triggerCache.Clear();
        }

        private static void SetWorldScale(Transform target, Vector3 worldScale)
        {
            Transform parent = target.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            target.localScale = new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 1e-5f ? value : value / divisor;
        }
    }
}