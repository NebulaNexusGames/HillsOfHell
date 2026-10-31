using CarGame.Combat;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    /// <summary>
    /// Таран: триггерный объём спереди машины. Наносит урон врагам при контакте,
    /// а сила удара растёт со скоростью — таран сам определяет скорость своего
    /// передвижения по собственному Rigidbody и сам считает множитель:
    /// множитель = скорость / minSpeed, без ограничения сверху. Параметра
    /// максимальной скорости у тарана нет.
    ///
    /// Урон получают только враги (<see cref="Health.IsEnemy"/>): сама машина и
    /// игрок не считаются целями. На объекте должен быть триггерный коллайдер —
    /// он входит в состав корпуса машины и срабатывает через её Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarRam : MonoBehaviour
    {
        [Header("Урон")]
        [Tooltip("Базовый урон: урон при скорости удара, равной minSpeed")]
        [SerializeField] private float maxDamage = 55f;
        [Tooltip("Минимальная скорость, с которой таран вообще бьёт, м/с. При ней множитель скорости равен 1")]
        [SerializeField] private float minSpeed = 2.5f;
        [Tooltip("Пауза между ударами по одной цели, с")]
        [SerializeField] private float hitCooldown = 0.5f;

        private Rigidbody body;
        private float lastHitTime = -999f;

        private void Awake()
        {
            // Вложенных Rigidbody не бывает, поэтому ближайший Rigidbody по
            // иерархии — корпус машины, и его скорость и есть скорость тарана.
            body = GetComponentInParent<Rigidbody>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            Health target = other.GetComponentInParent<Health>();
            if (target == null || !target.IsEnemy || target.IsDead) return;
            if (target.GetComponentInParent<PlayerController>() != null) return;
            if (Time.time - lastHitTime < hitCooldown) return;

            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            if (speed < minSpeed) return;

            float multiplier = speed / Mathf.Max(0.01f, minSpeed);
            float damage = maxDamage * multiplier;
            if (damage <= 0.5f) return;

            lastHitTime = Time.time;

            Vector3 direction = other.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) direction = transform.forward;

            target.TakeDamage(new DamageInfo(damage, gameObject, other.ClosestPoint(transform.position), direction.normalized));
        }
    }
}
