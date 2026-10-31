using CarGame.Combat;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Урон от предмета, который кто-то бросил. Любой поднимаемый и бросаемый
    /// предмет наносит урон тем, во что он врезался, — и тем сильнее, чем быстрее
    /// летел и чем тяжелее сам.
    ///
    /// Ключевое условие — предмет должен лететь. Лежащий на полу болт, который
    /// подкатился и стукнулся о ногу, урона не наносит: пока скорость ниже
    /// restingSpeed, предмет считается остановившимся. Это заодно снимает
    /// вопрос про вещи в багажнике и про предмет, положенный игроком на пол:
    /// там скорость нулевая, а ударов не происходит.
    ///
    /// Отдельного состояния «бросили» здесь нет намеренно. Скорость у предмету
    /// появляется не только от броска: подбрасывает взрыв, отталкивает ключ,
    /// выбивает из машины. Всё это — тоже удар, и всё это предмет ловит сам, по
    /// своей скорости.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ThrownItemDamage : MonoBehaviour
    {
        [Header("Когда предмет бьёт")]
        [Tooltip("С какой скорости удар начинает наносить урон, м/с. Ниже — это падение, а не удар")]
        [SerializeField] private float minImpactSpeed = 6.5f;
        [Tooltip("Ниже этой скорости предмет считается остановившимся и больше не бьёт, м/с")]
        [SerializeField] private float restingSpeed = 5f;
        [Tooltip("Сколько секунд одна и та же цель не бьётся одним предметом повторно. У цели бывает три коллайдера, и без этого один удар засчитывался бы трижды")]
        [SerializeField] private float repeatDelay = 0.2f;
        [Tooltip("Сколько секунд после броска предмет не бьёт того, кто его бросил, с. Иначе брошенный под ноги предмет бьёт хозяина в лицо")]
        [SerializeField] private float throwerGrace = 0.25f;

        [Header("Урон")]
        [Tooltip("Урон на максимальной скорости, без учёта веса")]
        [SerializeField] private float speedDamageMax = 18f;
        [Tooltip("С какой скорости урон выходит на максимум, м/с. Ровно скорость полностью заряженного броска: взрыв может разогнать предмет быстрее, и там урон просто упирается в потолок")]
        [SerializeField] private float maxDamageSpeed = 22f;
        [Tooltip("Вес, который урона не добавляет, кг. Всё легче этого веса бьёт только за скорость")]
        [SerializeField] private float neutralMass = 1.5f;
        [Tooltip("Сколько урона добавляет каждый килограмм сверх этого веса")]
        [SerializeField] private float damagePerKg = 9f;

        private Rigidbody body;
        private PlayerController thrower;
        private float flightSpeed;
        private float graceUntil;
        private int lastTargetId;
        private float nextRepeatTime;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null) body = GetComponentInParent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            // Скорость запоминается до шага физики. В OnCollisionEnter Rigidbody
            // уже показывает скорость после отскока, и по ней нельзя решить,
            // летел ли предмет: только что ударившийся предмет по этому же
            // признаку «уже не летит».
            flightSpeed = body != null && !body.isKinematic ? body.linearVelocity.magnitude : 0f;
        }

        /// <summary>
        /// Кто бросил предмет. Только чтобы не ударить его сразу же после броска:
        /// вещи в полёте не разбирают, чья это была рука.
        /// </summary>
        public void SetThrower(PlayerController player)
        {
            thrower = player;
            graceUntil = Time.time + throwerGrace;
        }

        /// <summary>Летит ли предмет прямо сейчас.</summary>
        private bool IsFlying => flightSpeed >= restingSpeed;

        /// <summary>
        /// Урон от удара на такой скорости. Скорость и вес складываются: чем
        /// быстрее летит, тем больше часть за скорость; чем тяжелее предмет, тем
        /// больше прибавка. Годится и для проверки баланса вручную.
        /// </summary>
        public float DamageAt(float speed)
        {
            float bySpeed = Mathf.InverseLerp(minImpactSpeed, maxDamageSpeed, speed) * speedDamageMax;
            float mass = body != null ? body.mass : 0f;
            float byWeight = Mathf.Max(0f, mass - neutralMass) * damagePerKg;
            return bySpeed + byWeight;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            if (!IsFlying) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            Health health = collision.collider.GetComponentInParent<Health>();
            if (health == null || health.IsDead) return;

            if (thrower != null && Time.time < graceUntil &&
                collision.collider.GetComponentInParent<PlayerController>() == thrower) return;

            // Повтор по той же цели гасим, по разным — нет: один предмет должен
            // иметь возможность пробить двух врагов подряд.
            int targetId = health.GetInstanceID();
            if (targetId == lastTargetId && Time.time < nextRepeatTime) return;
            lastTargetId = targetId;
            nextRepeatTime = Time.time + repeatDelay;

            float damage = DamageAt(speed);
            if (damage <= 0f) return;

            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.bounds.center;

            health.TakeDamage(new DamageInfo(damage, gameObject, point, collision.relativeVelocity.normalized));
        }
    }
}