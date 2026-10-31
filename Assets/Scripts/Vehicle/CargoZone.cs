using CarGame.Items;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class CargoZone : MonoBehaviour, IInteractable, ICanInteract
    {
        [Header("Хранение")]
        [Tooltip("Куда привязываются предметы, чтобы ехать вместе с машиной. Если пусто — корень зоны")]
        [SerializeField] private Transform cargoAnchor = null;
        [Tooltip("Максимум предметов в багажнике")]
        [SerializeField] private int maxItems = 8;
        [Tooltip("Режим FastWork: по кнопке E предмет не выбрасывается над триггером, а сразу переносится в место хранения (слот багажника/ячейку сундука). Если выключено — предмет появляется над триггером и попадает в багажник, коснувшись Anchor")]
        [SerializeField] private bool fastWork;
        [Tooltip("Точки, на которых раскладываются предметы после попадания в триггер анкора. Обычно это пустые объекты — дети Anchor, по одному на место. Если список пуст, берутся все дети Anchor без коллайдера. Объекты используются как есть: предметы не копируются и не пересоздаются")]
        [SerializeField] private Transform[] storageSlots = new Transform[0];

        [Header("Выгрузка из рук")]
        [Tooltip("Точка, из которой выпадает предмет. Если задана — предмет появится ровно там, иначе — в случайной точке над триггером багажника")]
        [SerializeField] private Transform ejectPoint = null;
        [Tooltip("Высота над верхом триггера багажника, на которой появляется предмет при выгрузке из рук, м")]
        [SerializeField] private float ejectHeight = 0.3f;
        [Tooltip("Запасная точка вылета относительно корня зоны, если коллайдер зоны не задан, м")]
        [SerializeField] private Vector3 ejectOffset = new Vector3(0f, 0.5f, 0.8f);

        [Tooltip("Коллайдер зоны прицеливания. Если пусто — первый на объекте")]
        [SerializeField] private Collider zoneCollider;
        [Tooltip("Показывать точки крепления, пока игрок внутри триггера багажника")]
        [SerializeField] private bool showPointsWhenPlayerInside = true;
        [Tooltip("Объекты-подсказки, которые видны только при открытом багажнике. Обычно это Point1..Point9 внутри Anchor. Если точка не указана, она берётся из списка мест хранения, поэтому заполнять вручную нужно только те, что не совпадают со слотами")]
        [SerializeField] private Transform[] cargoPoints = new Transform[0];
        [Tooltip("Машина-носитель, чей основной коллайдер игнорируют предметы багажника. Если пусто — ищется в родителях")]
        [SerializeField] private CarController vehicle;
        [Tooltip("Насколько далеко от точки попадания луча искать предмет, уже лежащий в багажнике, м")]
        [SerializeField] private float pickupSearchRadius = 2.5f;

        private readonly System.Collections.Generic.List<PickupItem> storedItems = new System.Collections.Generic.List<PickupItem>();
        private readonly System.Collections.Generic.List<Transform> slotFrames = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<PickupItem> slotItems = new System.Collections.Generic.List<PickupItem>();

        private bool warnedAboutMissingSlots;

        private readonly System.Collections.Generic.List<Renderer> pointRenderers = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<bool> pointStates = new System.Collections.Generic.List<bool>();
        private readonly System.Collections.Generic.List<Transform> pointTransforms = new System.Collections.Generic.List<Transform>();
        private bool playerInside;
        private bool pointsVisible;
        private bool warnedAboutPlayer;
        private float nextOverlapCheckAt;

        public Transform Anchor => cargoAnchor != null ? cargoAnchor : transform;
        /// <summary>
        /// Полка багажника. Отдельный объект под неё не создаётся: роль полки
        /// играет сам объект зоны, который уже несёт все её скрипты.
        /// </summary>
        public Transform StorageRoot => transform;
        public int SlotCount => slotFrames.Count;
        public int StoredCount { get { PruneStored(); return storedItems.Count; } }
        public bool CanAcceptItem => StoredCount < maxItems;

        private void Reset()
        {
            zoneCollider = GetComponent<Collider>();
            if (zoneCollider != null) zoneCollider.isTrigger = true;
            if (vehicle == null) vehicle = GetComponentInParent<CarController>();
        }

        private void Awake()
        {
            if (zoneCollider == null) zoneCollider = GetComponent<Collider>();
            if (zoneCollider != null) zoneCollider.isTrigger = true;
            if (vehicle == null) vehicle = GetComponentInParent<CarController>();
            ConfigureAnchorTrigger();
            BuildStorageSlots(Anchor);
            CachePointRenderers();
            ApplyPointVisibility(false);
        }

        public bool IsPlayerInside => playerInside;

        private void Update()
        {
            if (CheckPlayerInside()) playerInside = true;
            ApplyPointVisibility(showPointsWhenPlayerInside && playerInside);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayer(other)) playerInside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other)) playerInside = false;
        }

        /// <summary>
        /// Страховка на случай, если игрок оказался внутри триггера до того,
        /// как Unity успел прислать OnTriggerEnter: сцена загрузилась, или игрок
        /// возродился прямо в багажнике. Без неё точки не показались бы.
        /// </summary>
        private bool CheckPlayerInside()
        {
            if (playerInside) return true;
            if (zoneCollider == null) return false;

            // Обычно внутри триггера мы узнаём из OnTriggerEnter/Exit, а этот запрос —
            // только страховка. Без троттлинга он гонял бы OverlapBox каждый кадр.
            if (Time.unscaledTime < nextOverlapCheckAt) return false;
            nextOverlapCheckAt = Time.unscaledTime + 0.25f;

            Collider[] hits = Physics.OverlapBox(
                zoneCollider.bounds.center,
                zoneCollider.bounds.extents,
                zoneCollider.transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsPlayer(hits[i])) continue;

                if (!warnedAboutPlayer)
                {
                    warnedAboutPlayer = true;
                    Debug.LogWarning(
                        "[CargoZone] Игрок внутри триггера багажника без OnTriggerEnter. " +
                        "Проверь, что у персонажа есть CharacterController и триггер не выключен.",
                        this);
                }

                return true;
            }

            return false;
        }

        private static bool IsPlayer(Collider other)
        {
            return other != null && other.GetComponentInParent<PlayerController>() != null;
        }

        private void CachePointRenderers()
        {
            pointRenderers.Clear();
            pointStates.Clear();
            pointTransforms.Clear();

            // Подсказки и места хранения — одни и те же объекты Point1..Point12.
            // Раньше список подсказок брался только из cargoPoints, и в сцене он был
            // заполнен одним элементом: остальные точки не попадали в кэш, цикл
            // показа проходил один раз, и скрыть можно было только первую точку.
            // Теперь недостающие точки добираются из мест хранения.
            System.Collections.Generic.List<Transform> points = CollectPoints();

            for (int i = 0; i < points.Count; i++)
            {
                Renderer renderer = points[i].GetComponentInChildren<Renderer>();
                if (renderer == null) continue;

                pointRenderers.Add(renderer);
                pointStates.Add(renderer.enabled);
                pointTransforms.Add(points[i]);
            }
        }

        /// <summary>
        /// Порядок важен: он связывает индекс подсказки с местом хранения, а
        /// совпадение проверяется по transform, чтобы скрывалось нужное место.
        /// </summary>
        private System.Collections.Generic.List<Transform> CollectPoints()
        {
            System.Collections.Generic.List<Transform> points = new System.Collections.Generic.List<Transform>();

            if (cargoPoints != null)
            {
                for (int i = 0; i < cargoPoints.Length; i++)
                {
                    if (cargoPoints[i] != null && !points.Contains(cargoPoints[i])) points.Add(cargoPoints[i]);
                }
            }

            if (storageSlots != null)
            {
                for (int i = 0; i < storageSlots.Length; i++)
                {
                    if (storageSlots[i] != null && !points.Contains(storageSlots[i])) points.Add(storageSlots[i]);
                }
            }

            return points;
        }

        /// <summary>
        /// Точка-подсказка видна только когда багажник открыт и место свободно.
        /// Занятое место скрывается, иначе висящие свободные точки противоречат
        /// тому, что слот уже занят предметом.
        /// </summary>
        private void ApplyPointVisibility(bool trunkOpen)
        {
            pointsVisible = trunkOpen;

            for (int i = 0; i < pointRenderers.Count; i++)
            {
                Renderer renderer = pointRenderers[i];
                if (renderer == null) continue;

                bool target = trunkOpen && !IsPointOccupied(i) ? pointStates[i] : false;
                if (renderer.enabled == target) continue;

                renderer.enabled = target;
            }
        }

        private bool IsPointOccupied(int pointIndex)
        {
            int slot = FindSlotForPoint(pointIndex);
            if (slot < 0) return false;

            PickupItem occupant = slotItems[slot];
            return occupant != null && occupant.IsStored;
        }

        /// <summary>
        /// Подсказки и места хранения — обычно одни и те же объекты Point1..Point9,
        /// поэтому слот ищется по совпадению transform: порядок в инспекторе может
        /// не совпадать, и по индексу место скрывалось бы не то.
        /// </summary>
        private int FindSlotForPoint(int pointIndex)
        {
            if (pointIndex >= 0 && pointIndex < pointTransforms.Count)
            {
                Transform point = pointTransforms[pointIndex];

                for (int i = 0; i < slotFrames.Count; i++)
                {
                    if (slotFrames[i] == point) return i;
                }
            }

            return pointIndex >= 0 && pointIndex < slotFrames.Count ? pointIndex : -1;
        }

        /// <summary>
        /// Места хранения — это те же объекты, что уже стоят в сцене внутри Anchor.
        /// Раньше под каждую точку создавалась безмасштабная копия в отдельном
        /// объекте CargoStorage, но PickupItem.Store и сам восстанавливает мировой
        /// масштаб после SetParent, так что копии были лишними.
        /// </summary>
        private void BuildStorageSlots(Transform anchor)
        {
            if (anchor == null) return;

            System.Collections.Generic.List<Transform> sources = new System.Collections.Generic.List<Transform>();

            if (storageSlots != null)
            {
                for (int i = 0; i < storageSlots.Length; i++)
                {
                    if (storageSlots[i] != null) sources.Add(storageSlots[i]);
                }
            }

            if (sources.Count == 0)
            {
                for (int i = 0; i < anchor.childCount; i++)
                {
                    Transform child = anchor.GetChild(i);
                    if (child.GetComponent<Collider>() == null) sources.Add(child);
                }
            }

            for (int i = 0; i < sources.Count; i++)
            {
                slotFrames.Add(sources[i]);
                slotItems.Add(null);
            }

            if (slotFrames.Count > 0 || warnedAboutMissingSlots) return;

            warnedAboutMissingSlots = true;
            Debug.LogWarning("[CargoZone] Не заданы точки хранения — предметы останутся там, где упали. Создай пустые объекты внутри Anchor, по одному на место", this);
        }

        private void ConfigureAnchorTrigger()
        {
            if (cargoAnchor == null) return;

            Collider anchorCollider = cargoAnchor.GetComponent<Collider>();
            if (anchorCollider == null)
            {
                BoxCollider box = cargoAnchor.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                anchorCollider = box;
            }
            else
            {
                anchorCollider.isTrigger = true;
            }

            CargoSettleTrigger settle = cargoAnchor.GetComponent<CargoSettleTrigger>();
            if (settle == null) settle = cargoAnchor.gameObject.AddComponent<CargoSettleTrigger>();
            settle.Configure(this);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;

            if (fastWork)
            {
                FastStoreFromHand(interactor);
                return;
            }

            PickupItem item = interactor.Hand.HeldItem;
            interactor.Hand.DropAt(GetEjectPosition(), GetEjectRotation());
            IgnoreItemCollisions(item);
        }

        /// <summary>
        /// FastWork: предмет сразу уходит в свободный слот хранения, минуя выброс
        /// над триггером и касание Anchor. Предмет из инвентаря ставится в мир
        /// альт-версией и тоже замораживается в слоте.
        /// </summary>
        private bool FastStoreFromHand(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return false;
            PlayerHand hand = interactor.Hand;
            if (hand == null) return false;

            Transform frame = null;
            int slot = FindFreeSlot(hand.HeldItem != null ? hand.HeldItem.transform.position : Anchor.position);
            if (slot >= 0) frame = slotFrames[slot];
            if (frame == null) frame = StorageRoot;

            PickupItem item = hand.StoreHeldItemAt(frame);
            if (item == null) return false;

            if (slot >= 0) slotItems[slot] = item;

            IgnoreItemCollisions(item);
            if (!storedItems.Contains(item)) storedItems.Add(item);

            // Место только что заняли — пересчитываем подсказки сразу.
            ApplyPointVisibility(pointsVisible);
            return true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || interactor.Hand == null) return false;
            return interactor.Hand.IsHolding && CanAcceptItem;
        }

        public void Settle(PickupItem item)
        {
            if (item == null || item.IsStored) return;
            if (!CanAcceptItem) return;

            IgnoreItemCollisions(item);

            int slot = FindFreeSlot(item.transform.position);
            if (slot >= 0)
            {
                Transform frame = slotFrames[slot];
                slotItems[slot] = item;
                item.Store(frame, frame.position, frame.rotation);
            }
            else
            {
                item.Store(StorageRoot, item.transform.position, item.transform.rotation);
            }

            if (!storedItems.Contains(item)) storedItems.Add(item);

            // Место только что заняли — пересчитываем подсказки сразу, не дожидаясь
            // следующего кадра, иначе точка мигает перед своим исчезновением.
            ApplyPointVisibility(pointsVisible);
        }

        private int FindFreeSlot(Vector3 from)
        {
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < slotItems.Count; i++)
            {
                PickupItem occupant = slotItems[i];
                if (occupant != null && occupant.IsStored) continue;

                Transform frame = slotFrames[i];
                if (frame == null) continue;

                float sqr = (frame.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = i;
            }

            return best;
        }

        public PickupItem FindStoredItemNear(Vector3 point)
        {
            PruneStored();

            PickupItem best = null;
            float bestSqr = pickupSearchRadius * pickupSearchRadius;

            for (int i = 0; i < storedItems.Count; i++)
            {
                PickupItem item = storedItems[i];
                if (item == null) continue;

                float sqr = (item.transform.position - point).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = item;
            }

            return best;
        }

        /// <summary>
        /// Забирает из багажника ровно один случайный метал и тратит его на ремонт.
        /// Метал выбирается случайно, поэтому preference больше не влияет на выбор:
        /// если в багажнике лежат платина и скrap, за удар можно попасться любой.
        /// </summary>
        public bool TryConsumeMetal(float amount, WrenchItem.MetalPreference preference, out MetalType usedType, out float repaired)
        {
            usedType = MetalType.Scrap;
            repaired = 0f;
            if (amount <= 0.01f) return false;

            PruneStored();
            if (storedItems.Count == 0) return false;

            int count = storedItems.Count;
            int start = Random.Range(0, count);

            for (int offset = 0; offset < count; offset++)
            {
                PickupItem item = storedItems[(start + offset) % count];
                if (item == null) continue;

                MetalItem metal = item.GetComponent<MetalItem>();
                if (metal == null) continue;

                float perUnit = metal.RepairAmount;
                if (perUnit <= 0.01f) continue;

                usedType = metal.Type;
                repaired = perUnit;
                Destroy(metal.gameObject);
                return true;
            }

            return false;
        }

                public int CountMetal(MetalType type)
        {
            PruneStored();
            int count = 0;

            for (int i = 0; i < storedItems.Count; i++)
            {
                PickupItem item = storedItems[i];
                if (item == null) continue;

                MetalItem metal = item.GetComponent<MetalItem>();
                if (metal != null && metal.Type == type) count++;
            }

            return count;
        }

        private void PruneStored()
        {
            for (int i = storedItems.Count - 1; i >= 0; i--)
            {
                PickupItem item = storedItems[i];
                if (item != null && item.IsStored) continue;
                storedItems.RemoveAt(i);
            }
        }

        private void IgnoreItemCollisions(PickupItem item)
        {
            if (item == null || vehicle == null) return;
            vehicle.SetStoredItemCollision(item, true);
        }

        private Vector3 GetEjectPosition()
        {
            if (ejectPoint != null) return ejectPoint.position;

            if (zoneCollider != null && zoneCollider.bounds.size.sqrMagnitude > 0.0001f)
            {
                Bounds bounds = zoneCollider.bounds;
                float x = Random.Range(bounds.min.x, bounds.max.x);
                float z = Random.Range(bounds.min.z, bounds.max.z);
                return new Vector3(x, bounds.max.y + ejectHeight, z);
            }

            return transform.position + transform.TransformDirection(ejectOffset);
        }

        private Quaternion GetEjectRotation()
        {
            if (ejectPoint != null) return ejectPoint.rotation;
            return transform.rotation;
        }
    }
}