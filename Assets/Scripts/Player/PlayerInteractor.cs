using CarGame.Combat;
using CarGame.Controls;
using CarGame.Items;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarGame.Player
{
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Прицел")]
        [Tooltip("Камера, из центра экрана которой бьётся луч. Если пусто — Camera.main")]
        [SerializeField] private Camera aimCamera;
        [Tooltip("Слои, по которым можно взаимодействовать. Игрока лучше исключить")]
        [SerializeField] private LayerMask aimMask = ~0;
        [Tooltip("Дистанция взаимодействия, м")]
        [SerializeField] private float aimDistance = 4.5f;
        [Tooltip("С чем цеплять предметы: и обычные коллайдеры, и триггеры")]
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[64];

        private PlayerController playerController;
        private Inventory inventory;

        public PlayerHand Hand { get; private set; }
        public Inventory Inventory => inventory;
        public IInteractable Target { get; private set; }
        public Collider TargetCollider { get; private set; }
        public Health AimedHealth { get; private set; }

        /// <summary>Коллайдер и точка того, во что попал прицел, — по ним ключ
        /// бьёт именно по выбранной цели (постройке), а не по кузову под ней.</summary>
        public Collider AimedCollider { get; private set; }
        public Vector3 AimedPoint { get; private set; }
        public Camera AimCamera => aimCamera;

        public static bool InteractPressed => KeyBindings.WasPressedThisFrame(GameAction.Interact);
        public static bool AttackPressed => KeyBindings.WasPressedThisFrame(GameAction.Attack);
        public static bool AttackHeld => KeyBindings.IsPressed(GameAction.Attack);
        public static bool AttackReleased => KeyBindings.WasReleasedThisFrame(GameAction.Attack);
        public static bool DropPressed => KeyBindings.WasPressedThisFrame(GameAction.Drop);
        public static bool DropHeld => KeyBindings.IsPressed(GameAction.Drop);
        public static bool DropReleased => KeyBindings.WasReleasedThisFrame(GameAction.Drop);
        public static bool InteractHeld => KeyBindings.IsPressed(GameAction.Interact);
        public static bool InteractReleased => KeyBindings.WasReleasedThisFrame(GameAction.Interact);

        private void Awake()
        {
            if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
            if (aimCamera == null) aimCamera = Camera.main;
            Hand = GetComponent<PlayerHand>();
            if (Hand == null) Hand = GetComponentInParent<PlayerHand>();
            playerController = GetComponent<PlayerController>();
            if (playerController == null) playerController = GetComponentInParent<PlayerController>();
            inventory = GetComponent<Inventory>();
            if (inventory == null) inventory = GetComponentInParent<Inventory>();
        }

        private void Update()
        {
            UpdateTarget();
            if (!InteractPressed) return;

            // За рулём руки заняты: подбирать предметы и дёргать цели нельзя.
            if (playerController != null && playerController.IsInCar) return;

            if (Target != null) Target.Interact(this);
        }

        public void UpdateTarget()
        {
            Target = null;
            TargetCollider = null;
            AimedHealth = null;
            AimedCollider = null;
            AimedPoint = Vector3.zero;
            if (aimCamera == null) return;

            if (!TryGetAimRay(out Ray aimRay)) return;

            int count = Physics.RaycastNonAlloc(aimRay, hitBuffer, aimDistance, aimMask, triggerInteraction);

            float bestDistance = float.MaxValue;
            float bestHealthDistance = float.MaxValue;
            float bestStructureDistance = float.MaxValue;
            Health structureHealth = null;
            Collider structureCollider = null;
            Vector3 structurePoint = Vector3.zero;
            Health otherHealth = null;
            Collider otherCollider = null;
            Vector3 otherPoint = Vector3.zero;
            CargoZone cargoZone = null;
            Vector3 cargoPoint = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;

            // Прочность постройки лежит на дочерней «настоящей» детали, а
            // целится игрок в коллайдер корня — достаём её через чертёж
            // раньше прочности родителя (у турели на машине это кузов).
            Health aimed = Blueprint.ResolveHealth(hit.collider);

            // У двери нет ХП: её Health — только проводник урона, показывать
            // прочность в прицеле нельзя.
            if (aimed != null && aimed.GetComponentInParent<IDamageReceiver>() != null) aimed = null;

            if (aimed != null && !aimed.IsPlayer)
            {
                // Постройка важнее всего, что под ней: если прицел лёг и на
                // турель, и на кузов, ключ должен работать с турелью. Иначе
                // урон уходил бы в машину, а постройка оставалась целой.
                if (aimed.IsStructure)
                {
                    if (hit.distance < bestStructureDistance)
                    {
                        bestStructureDistance = hit.distance;
                        structureHealth = aimed;
                        structureCollider = hit.collider;
                        structurePoint = hit.point;
                    }
                }
                else if (hit.distance < bestHealthDistance)
                {
                    bestHealthDistance = hit.distance;
                    otherHealth = aimed;
                    otherCollider = hit.collider;
                    otherPoint = hit.point;
                }
            }

                if (cargoZone == null)
                {
                    cargoZone = hit.collider.GetComponentInParent<CargoZone>();
                    cargoPoint = hit.point;
                }

                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable == null) continue;
                if (hit.distance >= bestDistance) continue;

                // Стена важнее цели: если между игроком и целью ближе стоит
                // сплошной объект (без IInteractable и не своя машина), цель за
                // ним недостижима — иначе в машину можно сесть через стену.
                if (IsBlockedBySolid(hit, interactable, count)) continue;

                bestDistance = hit.distance;
                Target = interactable;
                TargetCollider = hit.collider;
            }

            AimedHealth = structureHealth != null ? structureHealth : otherHealth;
            AimedCollider = structureHealth != null ? structureCollider : otherCollider;
            AimedPoint = structureHealth != null ? structurePoint : otherPoint;

            PreferStoredCargoItem(cargoZone, cargoPoint);
        }

        private bool IsBlockedBySolid(RaycastHit target, IInteractable interactable, int count)
        {
            Collider targetCollider = target.collider;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null) continue;
                if (hit.distance >= target.distance) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;

                // Свой объект цели (сам триггер входа) дорогу не перекрывает.
                if (hit.collider == targetCollider) continue;
                if (hit.collider.GetComponentInParent<IInteractable>() == interactable) continue;

                // Триггеры (двери, зоны) не мешают: они не сплошные.
                if (hit.collider.isTrigger) continue;

                // Всё остальное сплошное — стена, чужой объект и даже кузов
                // самой машины — перекрывает вход: двери нужно ВИДЕТЬ напрямую.
                return true;
            }

            return false;
        }

        public bool TryGetAimRay(out Ray ray)
        {
            if (aimCamera == null)
            {
                ray = default;
                return false;
            }

            ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        private void PreferStoredCargoItem(CargoZone cargoZone, Vector3 point)
        {
            if (cargoZone == null) return;
            if (playerController != null && playerController.IsInCar) return;

            if (Hand != null && Hand.IsHolding)
            {
                if (!(Target is PickupItem)) return;

                Target = cargoZone;
                TargetCollider = cargoZone.GetComponentInChildren<Collider>();
                return;
            }

            if (Target is PickupItem) return;

            PickupItem stored = cargoZone.FindStoredItemNear(point);
            if (stored == null) return;

            Target = stored;
            TargetCollider = stored.GetComponentInChildren<Collider>();
        }

        public bool TryGetTarget<T>(out T target) where T : class, IInteractable
        {
            target = Target as T;
            return target != null;
        }
    }
}
