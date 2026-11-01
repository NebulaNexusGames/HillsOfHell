using CarGame.Items;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Результат одного выстрела. Выстрел в игре всегда рейкаст: пуля не летит,
    /// поэтому точка попадания известна в момент выстрела, а не потом.
    /// </summary>
    public struct HitscanResult
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 Point;
        public float Distance;
        public Health Target;

        /// <summary>
        /// Нормаль поверхности, в которую попал луч (для декалей). Если цели нет
        /// и нормаль не получена — вектор вниз как запасной вариант ориентации.
        /// </summary>
        public Vector3 Normal;

        /// <summary>Есть ли реальная поверхность для декали: попали во что-то твёрдое.</summary>
        public bool HasSurface { get { return Distance > 0.01f && Normal.sqrMagnitude > 0.5f; } }

        public bool HitSomething { get { return Target != null; } }
    }

    /// <summary>
    /// Базовый выстрел. Общий для всех, кто стреляет, чтобы правила попадания
    /// и урона не расходились между стрелками.
    /// </summary>
    public static class Hitscan
    {
        private const int MaxHits = 16;
        private const float MinDirectionSqr = 0.0001f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxHits];

        /// <summary>
        /// Разводит направление внутри конуса. Угол задаётся в градусах и
        /// распределяется равномерно по диску, поэтому крайний выстрел и центр
        /// конуса равновероятны.
        /// </summary>
        public static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0.001f) return direction;

            Vector2 disk = Random.insideUnitCircle * (degrees * Mathf.Deg2Rad);

            return Quaternion.Euler(disk.y, disk.x, 0f) * direction.normalized;
        }

        /// <summary>
        /// Пускает луч. Коллайдеры самого стрелка пропускаются, иначе выстрел
        /// цеплял бы собственную машину или корпус. Если точное попадание прошло
        /// мимо мелкой цели, assistRadius подбирает ближайшую цель рядом с
        /// лучом — это замена прежней проверки через OverlapSphere.
        /// </summary>
        public static HitscanResult Cast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            float assistRadius,
            LayerMask mask,
            Transform ignore)
        {
            if (direction.sqrMagnitude <= MinDirectionSqr) direction = Vector3.up;

            HitscanResult result = new HitscanResult
            {
                Origin = origin,
                Direction = direction.normalized,
                Distance = maxDistance
            };

            result.Point = origin + result.Direction * maxDistance;

            int count = Physics.RaycastNonAlloc(
                origin,
                result.Direction,
                HitBuffer,
                maxDistance,
                mask,
                QueryTriggerInteraction.Ignore);

            Collider nearestCollider = NearestCollider(count, ignore, out float nearest);

            // Нормаль твёрдой поверхности для декали: берём из ближайшего
            // реального попадания луча.
            Vector3 surfaceNormal = Vector3.zero;
            if (nearestCollider != null)
            {
                for (int i = 0; i < count; i++)
                {
                    if (HitBuffer[i].collider == nearestCollider)
                    {
                        surfaceNormal = HitBuffer[i].normal;
                        break;
                    }
                }
            }

            // Дрон — это триггер: так он не толкает игрока и машину, а лучи с
            // QueryTriggerInteraction.Ignore его не видят вообще, поэтому по
            // нему нельзя было выстрелить. Если обычный луч не задел вообще
            // ничего, ищем ближайшую цель среди триггеров. Стены и кузов при
            // этом по-прежнему останавливают выстрел: нашлись бы первым лучом,
            // и до триггерной цели дело не дошло бы.
            if (nearestCollider == null)
            {
                count = Physics.RaycastNonAlloc(
                    origin,
                    result.Direction,
                    HitBuffer,
                    maxDistance,
                    mask,
                    QueryTriggerInteraction.Collide);

                nearestCollider = NearestDamageable(count, ignore, out nearest);
            }

            if (nearestCollider != null)
            {
                result.Distance = nearest;
                result.Point = origin + result.Direction * nearest;
                result.Target = FindTarget(nearestCollider);
                result.Normal = surfaceNormal;
            }

            if (result.Target == null || assistRadius <= 0.001f) return result;

            Collider[] overlaps = Physics.OverlapSphere(
                result.Point,
                assistRadius,
                mask,
                QueryTriggerInteraction.Ignore);

            Collider assisted = FindAssisted(overlaps, ignore);
            if (assisted == null)
            {
                // Помощь прицеливания должна видеть и цель-триггер: иначе луч
                // проходит рядом с дроном и промахивается.
                overlaps = Physics.OverlapSphere(
                    result.Point,
                    assistRadius,
                    mask,
                    QueryTriggerInteraction.Collide);

                assisted = FindAssisted(overlaps, ignore);
            }

            if (assisted != null)
            {
                result.Target = FindTarget(assisted);
                result.Point = assisted.ClosestPoint(origin);
                result.Distance = Vector3.Distance(origin, result.Point);
            }

            return result;
        }

        /// <summary>
        /// Ближайший коллайдер луча, кроме коллайдеров самого стрелка.
        /// </summary>
        private static Collider NearestCollider(int count, Transform ignore, out float nearest)
        {
            nearest = float.MaxValue;
            Collider nearestCollider = null;

            for (int i = 0; i < count; i++)
            {
                if (IsIgnored(HitBuffer[i].collider, ignore)) continue;
                if (IsInvulnerable(HitBuffer[i].collider)) continue;
                if (HitBuffer[i].distance >= nearest) continue;

                nearest = HitBuffer[i].distance;
                nearestCollider = HitBuffer[i].collider;
            }

            return nearestCollider;
        }

        /// <summary>
        /// Ближайший коллайдер, за которым есть здоровье. Триггеры в игре —
        /// это зоны и цель-триггеры вроде дрона, а не поверхности: попадание
        /// считается только там, где есть кому принимать урон.
        /// </summary>
        private static Collider NearestDamageable(int count, Transform ignore, out float nearest)
        {
            nearest = float.MaxValue;
            Collider nearestCollider = null;

            for (int i = 0; i < count; i++)
            {
                if (IsIgnored(HitBuffer[i].collider, ignore)) continue;
                if (IsInvulnerable(HitBuffer[i].collider)) continue;
                if (FindTarget(HitBuffer[i].collider) == null) continue;
                if (HitBuffer[i].distance >= nearest) continue;

                nearest = HitBuffer[i].distance;
                nearestCollider = HitBuffer[i].collider;
            }

            return nearestCollider;
        }

        /// <summary>
        /// Первая цель, которую помощь прицеливания может подобрать рядом с
        /// точкой попадания.
        /// </summary>
        private static Collider FindAssisted(Collider[] overlaps, Transform ignore)
        {
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (IsIgnored(overlaps[i], ignore)) continue;
                if (IsInvulnerable(overlaps[i])) continue;
                if (FindTarget(overlaps[i]) == null) continue;

                return overlaps[i];
            }

            return null;
        }

        /// <summary>
        /// Наносит урон цели выстрела. Множитель на машину и мгновенное убийство
        /// вынесены сюда, потому что нужны обоим стрелкам.
        /// </summary>
        public static void ApplyDamage(
            in HitscanResult shot,
            Transform source,
            float damage,
            bool instantKill,
            float carDamageMultiplier)
        {
            Health target = shot.Target;
            if (target == null || target.IsDead) return;

            float amount = damage;

            CarController car = target.IsCar ? target.GetComponentInParent<CarController>() : null;
            if (car != null) amount *= Mathf.Max(0f, carDamageMultiplier);

            if (instantKill) amount = Mathf.Max(amount, target.Max + 1000f);

            target.TakeDamage(new DamageInfo(
                amount,
                source != null ? source.gameObject : null,
                shot.Point,
                shot.Direction));
        }

        private static bool IsIgnored(Collider collider, Transform ignore)
        {
            if (collider == null) return true;
            if (ignore == null) return false;

            Transform candidate = collider.transform;
            return candidate == ignore || candidate.IsChildOf(ignore);
        }

        /// <summary>
        /// Коллайдер неуязвимой цели: выстрел проходит сквозь него, как сквозь
        /// пустое место. Так пули не задевают игрока, пока он сидит в машине.
        /// </summary>
        private static bool IsInvulnerable(Collider collider)
        {
            Health health = Blueprint.ResolveHealth(collider);
            if (health == null) return false;
            if (!health.IsInvulnerable) return false;

            // Неуязвимый объект с приёмником урона (дверь) всё равно должен
            // получать попадания — просто без потери ХП.
            return health.GetComponentInParent<IDamageReceiver>() == null;
        }

        private static Health FindTarget(Collider collider)
        {
            Health target = Blueprint.ResolveHealth(collider);
            return target != null && !target.IsDead ? target : null;
        }
    }
}
