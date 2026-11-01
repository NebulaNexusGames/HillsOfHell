using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    /// <summary>
    /// Дверь автомобиля с физикой. Петли задают центр вращения, ручка —
    /// точка взаимодействия (как точка заправки/входа). При нажатии на ручку
    /// дверь оттолкнётся от игрока, начнёт свободно двигаться по физике. Если
    /// вернётся к стартовому углу — заморозится.
    /// </summary>
    public class CarDoor : MonoBehaviour
    {
        [Header("Крепление")]
        [Tooltip("Точка/объект с петлями. Центр вращения двери. Если пусто — transform двери")]
        [SerializeField] private Transform hinge;

        [Header("Физика")]
        [Tooltip("Радиус воздействия от игрока при открывании")]
        [SerializeField] private float pushRadius = 2.2f;
        [Tooltip("Сила отталкивания")]
        [SerializeField] private float pushForce = 600f;
        [Tooltip("Нижний порог отклонения, при котором дверь считается закрытой")]
        [SerializeField] private float closedAngleThreshold = 4f;
        [Tooltip("Скорость возврата к закрытому положению для проверки")]
        [SerializeField] private float closeCheckSpeed = 1.5f;
        [Tooltip("Время, за которое дверь должна почти вернуться в закрытое состояние, чтобы заморозиться")]
        [SerializeField] private float settleTime = 0.25f;

        private Rigidbody body;
        private Collider[] colliders;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private bool initialized;
        private bool locked;
        private float lastAngleFromStart;
        private float movingTowardsClosedTime;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();

            colliders = GetComponentsInChildren<Collider>(true);
            CacheStart();
        }

        private void CacheStart()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            initialized = true;
            lastAngleFromStart = GetAngleFromStart();
            movingTowardsClosedTime = 0f;
            LockDoor();
        }

        /// <summary>Заморозить дверь в текущем положении (закрыта/поставлена).</summary>
        public void LockDoor()
        {
            // Физический замок двери задаётся флагом, а не через Rigidbody:
            // kinematic-тело нельзя было использовать как замок, потому что
            // CarDoor вешается на тот же Rigidbody, что и у всей машины — замок
            // через kinematic держал всю машину в состоянии без гравитации
            // («парение в воздухе») и вызывал ошибки установки velocity на
            // kinematic-теле. Машина должна оставаться физичной.
            locked = true;
        }

        /// <summary>Разморозить дверь, дать ей подчиняться физике.</summary>
        public void UnlockDoor()
        {
            locked = false;
        }

        public bool IsClosed()
        {
            if (!initialized) CacheStart();
            return GetAngleFromStart() <= closedAngleThreshold;
        }

        /// <summary>Открыть/отпустить дверь от точки действия игрока.</summary>
        public void PushFrom(Vector3 playerPosition)
        {
            if (body == null) return;
            if (!initialized) CacheStart();

            UnlockDoor();

            Vector3 hingePosition = hinge != null ? hinge.position : transform.position;
            Vector3 direction = transform.position - playerPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = (transform.position - hingePosition).normalized;
            else direction.Normalize();

            Vector3 pushPoint = Vector3.Lerp(hingePosition, transform.position, 0.5f);
            body.AddForceAtPosition(direction * pushForce, pushPoint, ForceMode.Impulse);

            // Дополнительно раскручиваем вокруг петель
            Vector3 hingeAxis = hinge != null ? hinge.up : Vector3.up;
            Vector3 radial = pushPoint - hingePosition;
            Vector3 torqueAxis = Vector3.Cross(radial.normalized, direction).normalized;
            body.AddTorque(torqueAxis * pushForce * 0.4f, ForceMode.Impulse);
        }

        private void FixedUpdate()
        {
            if (!initialized) return;
            if (body == null || locked) return;

            float angle = GetAngleFromStart();
            if (angle <= closedAngleThreshold)
            {
                // Дверь почти вернулась к исходному положению — пытаемся
                // плавно остановить и заморозить.
                if (body.linearVelocity.magnitude < closeCheckSpeed && body.angularVelocity.magnitude < closeCheckSpeed)
                {
                    movingTowardsClosedTime += Time.fixedDeltaTime;
                    if (movingTowardsClosedTime >= settleTime)
                    {
                        transform.position = startPosition;
                        transform.rotation = startRotation;
                        LockDoor();
                        movingTowardsClosedTime = 0f;
                    }
                }
                else
                {
                    movingTowardsClosedTime = 0f;
                }
            }
            else
            {
                movingTowardsClosedTime = 0f;
            }

            lastAngleFromStart = angle;
        }

        private float GetAngleFromStart()
        {
            if (!initialized) return 0f;
            float angle = Quaternion.Angle(startRotation, transform.rotation);
            return angle;
        }
    }
}
