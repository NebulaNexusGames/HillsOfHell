using CarGame.Audio;
using CarGame.Combat;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Buildings
{
    /// <summary>
    /// Дверь здания, вращающаяся строго вокруг точки петель.
    /// Rigidbody остаётся кинематическим, поэтому дверь не может улететь или
    /// сместиться: положение вычисляется как поворот вокруг петель.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BuildingDoor : MonoBehaviour, IDamageReceiver
    {
        [Header("Петли")]
        [Tooltip("Объект петель. Настоящий центр вращения двери.")]
        [SerializeField] private Transform hinge;
        [Tooltip("Ось вращения петель в локальных координатах объекта петель. Обычно вверх (Y).")]
        [SerializeField] private Vector3 hingeAxis = Vector3.up;

        [Header("Открывание")]
        [Tooltip("Скорость открывания в градусах в секунду")]
        [SerializeField] private float openSpeed = 140f;
        [Tooltip("Скорость закрывания в градусах в секунду")]
        [SerializeField] private float closeSpeed = 140f;
        [Tooltip("Ускорение разгона/торможения двери")]
        [SerializeField] private float acceleration = 700f;
        [Tooltip("Максимальный угол открывания в градусах")]
        [SerializeField] private float openAngle = 110f;
        [Tooltip("Открывать в обратную сторону (если игрок не найден)")]
        [SerializeField] private bool reverseOpenDirection;
        [Tooltip("Насколько сильно игрок может толкать дверь телом")]
        [SerializeField] private float playerPushForce = 90f;

        [Header("Урон")]
        [Tooltip("Урон, полностью открывающий дверь")]
        [SerializeField] private float damageForFullOpen = 50f;
        [Tooltip("Скорость открытия от урона при максимальном уроне")]
        [SerializeField] private float damageOpenSpeed = 360f;

        [Header("Звук")]
        [Tooltip("Источник звука двери. Если не задан — создастся автоматически")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Звук открытия. Если пусто — сгенерируется процедурный")]
        [SerializeField] private AudioClip openSound;
        [Tooltip("Звук закрытия, играет когда дверь возвращается в исходное положение")]
        [SerializeField] private AudioClip closeSound;
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 0.8f;

        [Header("Игнорируемые коллайдеры")]
        [Tooltip("Коллайдеры, с которыми дверь не должна сталкиваться")]
        [SerializeField] private Collider[] ignoredColliders;

        [Header("Блокировка вращения")]
        [Tooltip("Запретить вращение вокруг локальной оси X")]
        [SerializeField] private bool freezeRotationX;
        [Tooltip("Запретить вращение вокруг локальной оси Y")]
        [SerializeField] private bool freezeRotationY;
        [Tooltip("Запретить вращение вокруг локальной оси Z")]
        [SerializeField] private bool freezeRotationZ;

        [Header("Блокировка в покое")]
        [Tooltip("Угол отклонения, при котором дверь считается закрытой")]
        [SerializeField] private float closedAngleThreshold = 3f;
        [Tooltip("Время, которое дверь должна простоять почти закрытой")]
        [SerializeField] private float settleTime = 0.2f;
        [Tooltip("Зазор, при котором игрок мешает закрыванию")]
        [SerializeField] private float closingBlockMargin = 0.15f;

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        private Rigidbody body;
        private Health health;
        private RigidbodyConstraints baseConstraints;
        private Quaternion startRotation;
        private Vector3 startPosition;
        private Vector3 hingePosition;
        private Vector3 worldAxis;

        private Bounds localBounds;
        private bool hasLocalBounds;

        private float currentAngle;
        private float targetAngle;
        private float currentSpeed;
        private float moveSpeed;
        private float openSign = 1f;
        private float minAngle;
        private float maxAngle;
        private float settleTimer;
        private bool locked = true;
        private bool wasOpen;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();

            // Дверь не участвует в динамике — её двигает только скрипт
            HingeJoint leftover = GetComponent<HingeJoint>();
            if (leftover != null) Destroy(leftover);

            baseConstraints = body.constraints;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = BuildRotationConstraints();

            startRotation = transform.rotation;
            startPosition = transform.position;
            hingePosition = GetHingePosition();
            worldAxis = GetHingeAxis();

            openSign = OpeningSign;
            minAngle = Mathf.Min(0f, openAngle * openSign);
            maxAngle = Mathf.Max(0f, openAngle * openSign);
            currentAngle = 0f;
            targetAngle = 0f;
            moveSpeed = openSpeed;
            locked = true;

            CacheLocalBounds();
            ConfigureAudio();
            ConfigureIgnoredCollisions();
            ConfigureHealth();
            ApplyRotation();
        }

        private RigidbodyConstraints BuildRotationConstraints()
        {
            RigidbodyConstraints constraints = baseConstraints;
            if (freezeRotationX) constraints |= RigidbodyConstraints.FreezeRotationX;
            if (freezeRotationY) constraints |= RigidbodyConstraints.FreezeRotationY;
            if (freezeRotationZ) constraints |= RigidbodyConstraints.FreezeRotationZ;
            return constraints;
        }

        private void CacheLocalBounds()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            hasLocalBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;

                Bounds worldBounds = collider.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 local = transform.InverseTransformPoint(corner);

                    if (!hasLocalBounds)
                    {
                        localBounds = new Bounds(local, Vector3.zero);
                        hasLocalBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(local);
                    }
                }
            }
        }

        private void ConfigureHealth()
        {
            health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();

            // Health здесь — только проводник урона: дверь бессмертна и не
            // показывает полосу ХП, а урон открывает её через IDamageReceiver.
            health.Configure(1f, Health.HpOwner.Structure);
            health.SetInvulnerable(true);
            health.Set(1f);
        }

        public void ReceiveDamage(DamageInfo info)
        {
            HandleDamage(info);
        }

        private void HandleDamage(DamageInfo info)
        {
            if (body == null) return;

            float amount = Mathf.Max(0f, info.amount);
            if (amount <= 0.0001f) return;

            float normalized = Mathf.Clamp01(amount / Mathf.Max(0.01f, damageForFullOpen));

            float sign;
            if (!IsClosed())
            {
                sign = openSign;
            }
            else
            {
                sign = ResolveDamageSign(info);
            }

            openSign = sign;
            minAngle = Mathf.Min(0f, openAngle * sign);
            maxAngle = Mathf.Max(0f, openAngle * sign);

            float openDistance = openAngle * Mathf.Max(0.15f, normalized);
            float targetAbs = Mathf.Min(openAngle, Mathf.Abs(currentAngle) + openDistance);
            targetAngle = openSign * Mathf.Clamp(targetAbs, 0f, openAngle);

            currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);
            moveSpeed = Mathf.Lerp(openSpeed, Mathf.Max(openSpeed, damageOpenSpeed), normalized);
            locked = false;
            wasOpen = true;
            settleTimer = 0f;

            PlaySound(openSound, ProceduralAudio.DoorOpen());
        }

        private float ResolveDamageSign(DamageInfo info)
        {
            Vector3 direction = Vector3.ProjectOnPlane(info.direction, worldAxis);

            if (direction.sqrMagnitude < 0.0001f && info.point != Vector3.zero)
            {
                direction = Vector3.ProjectOnPlane(info.point - hingePosition, worldAxis);
            }

            if (direction.sqrMagnitude < 0.0001f) return OpeningSign;
            direction.Normalize();

            Vector3 radial = Vector3.ProjectOnPlane(info.point - hingePosition, worldAxis);
            if (radial.sqrMagnitude < 0.000001f)
                radial = Vector3.ProjectOnPlane(startPosition - hingePosition, worldAxis);
            if (radial.sqrMagnitude < 0.000001f) return OpeningSign;
            radial.Normalize();

            Vector3 tangent = Vector3.Cross(worldAxis, radial).normalized;
            float dot = Vector3.Dot(tangent, direction);
            if (Mathf.Abs(dot) < 0.0001f) return OpeningSign;
            return dot > 0f ? 1f : -1f;
        }

        private void ConfigureAudio()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 3f;
            audioSource.maxDistance = 20f;
        }

        private void ConfigureIgnoredCollisions()
        {
            if (ignoredColliders == null || ignoredColliders.Length == 0) return;

            Collider[] ownColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider own = ownColliders[i];
                if (own == null) continue;

                for (int j = 0; j < ignoredColliders.Length; j++)
                {
                    Collider ignored = ignoredColliders[j];
                    if (ignored == null || ignored == own) continue;
                    Physics.IgnoreCollision(own, ignored, true);
                }
            }
        }

        private void PlaySound(AudioClip clip, AudioClip fallback)
        {
            if (audioSource == null) return;
            AudioClip resolved = clip != null ? clip : fallback;
            if (resolved == null) return;
            audioSource.PlayOneShot(resolved, soundVolume);
        }

        public bool IsClosed()
        {
            return Mathf.Abs(currentAngle) <= closedAngleThreshold;
        }

        public void Lock()
        {
            locked = true;
            currentSpeed = 0f;
        }

        public void Unlock()
        {
            if (IsHingeAxisFrozen()) return;
            locked = false;
        }

        private float OpeningSign
        {
            get { return reverseOpenDirection ? -1f : 1f; }
        }

        /// <summary>
        /// Открыть, если закрыта, иначе закрыть.
        /// </summary>
        public void Toggle()
        {
            if (body == null) return;

            if (locked || IsClosed()) Open();
            else Close();
        }

        public void Open()
        {
            if (body == null) return;
            if (IsHingeAxisFrozen()) return;

            float sign = ResolveOpenSign();
            openSign = sign;
            minAngle = Mathf.Min(0f, openAngle * sign);
            maxAngle = Mathf.Max(0f, openAngle * sign);

            currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);
            targetAngle = openSign * openAngle;
            moveSpeed = openSpeed;
            locked = false;
            wasOpen = true;
            settleTimer = 0f;

            PlaySound(openSound, ProceduralAudio.DoorOpen());
        }

        public void Close()
        {
            if (body == null || locked) return;

            targetAngle = 0f;
            moveSpeed = closeSpeed;
            settleTimer = 0f;
        }

        /// <summary>
        /// Толчок телом игрока. Работает только когда дверь открыта.
        /// </summary>
        public void PushFromPlayer(Vector3 velocity, Vector3 contactPoint)
        {
            if (body == null || locked) return;
            if (IsHingeAxisFrozen()) return;

            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0001f) return;

            Vector3 radial = Vector3.ProjectOnPlane(contactPoint - hingePosition, worldAxis);
            if (radial.sqrMagnitude < 0.000001f) return;

            Vector3 tangent = Vector3.Cross(worldAxis, radial.normalized).normalized;
            float push = Vector3.Dot(tangent, horizontal);
            if (Mathf.Abs(push) < 0.01f) return;

            float before = Mathf.Abs(targetAngle);

            targetAngle = Mathf.Clamp(
                targetAngle + push * playerPushForce * Time.fixedDeltaTime,
                minAngle, maxAngle);

            if (Mathf.Abs(targetAngle) <= closedAngleThreshold) targetAngle = 0f;

            moveSpeed = Mathf.Abs(targetAngle) < before ? closeSpeed : openSpeed;
            wasOpen = true;
        }

        private float ResolveOpenSign()
        {
            PlayerController player = FindPlayer();
            if (player == null) return OpeningSign;

            Vector3 hingeToDoor = Vector3.ProjectOnPlane(startPosition - hingePosition, worldAxis);
            if (hingeToDoor.sqrMagnitude < 0.000001f)
                hingeToDoor = Vector3.ProjectOnPlane(startRotation * Vector3.forward, worldAxis);
            if (hingeToDoor.sqrMagnitude < 0.000001f)
                hingeToDoor = Vector3.ProjectOnPlane(startRotation * Vector3.right, worldAxis);
            if (hingeToDoor.sqrMagnitude < 0.000001f) return OpeningSign;
            hingeToDoor.Normalize();

            Vector3 playerDir = Vector3.ProjectOnPlane(player.transform.position - hingePosition, worldAxis);
            if (playerDir.sqrMagnitude < 0.000001f) return OpeningSign;
            playerDir.Normalize();

            Vector3 tangent = Vector3.Cross(worldAxis, hingeToDoor).normalized;
            return Vector3.Dot(tangent, playerDir) > 0f ? -1f : 1f;
        }

        private PlayerController FindPlayer()
        {
            PlayerController player = FindObjectOfType<PlayerController>();
            if (player != null) return player;

            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) return null;
            return go.GetComponent<PlayerController>() ?? go.GetComponentInParent<PlayerController>();
        }

        private bool IsHingeAxisFrozen()
        {
            Vector3 local = Quaternion.Inverse(startRotation) * worldAxis;
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);

            if (ax >= ay && ax >= az) return freezeRotationX;
            if (ay >= az) return freezeRotationY;
            return freezeRotationZ;
        }

        private Vector3 GetHingePosition()
        {
            return hinge != null ? hinge.position : transform.position;
        }

        private Vector3 GetHingeAxis()
        {
            Vector3 localAxis = hingeAxis.sqrMagnitude < 0.0001f ? Vector3.up : hingeAxis.normalized;
            Transform reference = hinge != null ? hinge : transform;
            return reference.TransformDirection(localAxis).normalized;
        }

        private void FixedUpdate()
        {
            if (body == null || locked) return;

            float remaining = targetAngle - currentAngle;
            if (Mathf.Abs(remaining) > 0.001f)
            {
                float direction = Mathf.Sign(remaining);
                float nextSpeed = Mathf.MoveTowards(currentSpeed, direction * moveSpeed, acceleration * Time.fixedDeltaTime);
                float nextAngle = currentAngle + nextSpeed * Time.fixedDeltaTime;

                if (Mathf.Sign(targetAngle - nextAngle) != direction)
                {
                    nextAngle = targetAngle;
                    nextSpeed = 0f;
                }

                bool closing = Mathf.Abs(targetAngle) < Mathf.Abs(currentAngle);
                if (closing && WouldHitPlayer(nextAngle))
                {
                    // Игрок мешает закрытию — дверь останавливается и ждёт
                    targetAngle = currentAngle;
                    currentSpeed = 0f;
                    settleTimer = 0f;
                    return;
                }

                currentAngle = nextAngle;
                currentSpeed = nextSpeed;
                ApplyRotation();
            }
            else
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, acceleration * Time.fixedDeltaTime);
            }

            bool reachingClosed = Mathf.Approximately(targetAngle, 0f) && IsClosed();
            if (reachingClosed)
            {
                settleTimer += Time.fixedDeltaTime;
                if (settleTimer >= settleTime)
                {
                    currentAngle = 0f;
                    targetAngle = 0f;
                    currentSpeed = 0f;
                    ApplyRotation();
                    locked = true;
                    settleTimer = 0f;

                    if (wasOpen)
                    {
                        wasOpen = false;
                        PlaySound(closeSound, ProceduralAudio.DoorClose());
                    }
                }
            }
            else
            {
                settleTimer = 0f;
            }
        }

        private bool WouldHitPlayer(float angle)
        {
            if (!hasLocalBounds) return false;

            PlayerController player = FindPlayer();
            if (player == null) return false;

            Quaternion delta = Quaternion.AngleAxis(angle, worldAxis);
            Vector3 position = hingePosition + delta * (startPosition - hingePosition);
            Quaternion rotation = delta * startRotation;

            Vector3 center = position + rotation * localBounds.center;
            Vector3 extents = localBounds.extents + Vector3.one * closingBlockMargin;

            int count = Physics.OverlapBoxNonAlloc(
                center, extents, OverlapBuffer, rotation, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = OverlapBuffer[i];
                if (hit == null) continue;
                if (hit.GetComponentInParent<PlayerController>() == player) return true;
            }

            return false;
        }

        private void ApplyRotation()
        {
            Quaternion delta = Quaternion.AngleAxis(currentAngle, worldAxis);
            transform.rotation = delta * startRotation;
            transform.position = hingePosition + delta * (startPosition - hingePosition);
        }
    }
}
