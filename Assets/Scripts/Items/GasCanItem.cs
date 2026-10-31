using CarGame.Combat;
using CarGame.Controls;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Канистра с бензином. В мире — физический объект, взрывается при
    /// попадании, если в ней ещё есть бензин. В руке — заправляет машину по
    /// зажатию LMB/интеракта у точки заправки.
    /// Анимация управляется булевыми переменными аниматора: Use = true во время заправки, Use = false в простое.
    /// </summary>
    [DisallowMultipleComponent]
    public class GasCanItem : MonoBehaviour, IHandUsable
    {
        [Header("Бензин")]
        [SerializeField] private float maxFuel = 40f;
        [SerializeField] private float currentFuel = 40f;

        [Header("Взрыв")]
        [Tooltip("Прочность физической канистры. Health вешается кодом, в префабе его нет")]
        [SerializeField] private float explosionHealth = 30f;
        [Tooltip("Префаб эффекта взрыва. На его месте ничего не остаётся — объект канистры удаляется")]
        [SerializeField] private GameObject explosionEffectPrefab;
        [SerializeField] private float explosionRadius = 4f;
        [SerializeField] private float explosionDamage = 60f;
        [SerializeField] private float explosionKnockback = 12f;
        [SerializeField] private bool explodeOnlyWhenFull = true;
        [SerializeField] private bool explodeWhenDamaged = true;

        [Header("Заправка")]
        [SerializeField] private float refuelRate = 10f;

        [Header("Анимация")]
        [Tooltip("Имя булевой переменной, включающейся при заправке (Use/Using/Refuel)")]
        [SerializeField] private string useBool = "Use";
        [Tooltip("Альтернативное имя булевой переменной для бездействия")]
        [SerializeField] private string idleBool = "";

        private Health health;
        private bool exploded;
        private CarController refuelingCar;
        private bool refueling;
        private Animator animator;
        private PlayerHand hand;

        public float CurrentFuel => currentFuel;
        public float MaxFuel => maxFuel;
        public bool IsEmpty => currentFuel <= 0.0001f;
        public bool IsFull => currentFuel >= maxFuel - 0.0001f;

        /// <summary>
        /// Это физическая (мировая) канистра, а не вариант в руке игрока. Именно
        /// её можно подобрать, по ней стреляют, и только она взрывается и удаляется.
        /// </summary>
        private bool IsPhysical => GetComponent<PickupItem>() != null && transform.parent == null;

        /// <summary>
        /// Задаёт количество бензина в этой канистре. Используется для переноса
        /// топлива между физической канистрой и её вариантом в руке.
        /// </summary>
        public void SetFuel(float value)
        {
            currentFuel = Mathf.Clamp(value, 0f, maxFuel);
        }

        /// <summary>
        /// Доля бензина в канистре, 0..1. Для отображения процентов при наведении.
        /// </summary>
        public float FuelNormalized => maxFuel > 0.0001f ? Mathf.Clamp01(currentFuel / maxFuel) : 0f;

        private void Awake()
        {
            currentFuel = Mathf.Clamp(currentFuel, 0f, maxFuel);

            // Только физическая канистра получает прочность: по ней стреляют.
            // Health не хранится в префабе — создаётся кодом здесь. Владелец — не
            // враг: турели стреляют только по IsEnemy, поэтому канистра не должна
            // быть в их списке целей (иначе они «стреляли по канистрам»).
            if (!IsPhysical) return;

            health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();
            health.Configure(Mathf.Max(1f, explosionHealth), Health.HpOwner.Consumable);
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
            StopRefuel();
        }

        private void OnDamaged(Health h, DamageInfo info)
        {
            if (exploded) return;
            if (!explodeWhenDamaged) return;
            if (explodeOnlyWhenFull && !IsFull) return;
            if (IsEmpty) return;

            Explode();
        }

        private void OnDied(Health h)
        {
            if (exploded) return;
            if (explodeOnlyWhenFull && !IsFull) return;
            if (IsEmpty) return;

            Explode();
        }

        private void Explode()
        {
            if (exploded) return;
            exploded = true;

            Vector3 pos = transform.position;

            // Как и все взрывы в игре: появление префаба взрыва на месте и
            // удаление самого объекта. Вариант руки не трогаем.
            if (explosionEffectPrefab != null)
            {
                GameObject source = Instantiate(explosionEffectPrefab, pos, Quaternion.identity);
                ExplosionEffect effect = source != null ? source.GetComponent<ExplosionEffect>() : null;
                if (effect != null) effect.Ignore(gameObject);
            }
            else
            {
                Explosion.Settings settings = Explosion.Default(explosionRadius, explosionDamage, explosionKnockback);
                settings.killPlayerInstantly = false;
                Explosion.Detonate(pos, settings);
            }

            if (IsPhysical) Destroy(gameObject);
        }

        public void OnEquipped(PlayerHand source)
        {
            hand = source;
            ResolveAnimator();
            SetUseBool(false);
            StopRefuel();
        }

        public void OnUsePressed(PlayerHand source)
        {
            hand = source;
            ResolveAnimator();
        }

        public void OnUseHeld(PlayerHand source)
        {
            hand = source;
            ResolveAnimator();
        }

        public void OnUseReleased(PlayerHand source)
        {
            hand = source;
            StopRefuel();
        }

        public void OnUseCanceled()
        {
            StopRefuel();
        }

        public void StartRefuel(CarController car)
        {
            if (car == null || IsEmpty) return;
            if (refuelingCar == car && refueling) return;

            refuelingCar = car;
            refueling = true;
            SetUseBool(true);
        }

        public void StopRefuel()
        {
            if (!refueling && refuelingCar == null)
            {
                SetUseBool(false);
                return;
            }
            refueling = false;
            refuelingCar = null;
            SetUseBool(false);
        }

        private void Update()
        {
            bool held = PlayerInteractor.AttackHeld || KeyBindings.IsPressed(GameAction.Interact);
            bool released = PlayerInteractor.AttackReleased || PlayerInteractor.InteractReleased;

            if (refueling && (released || !held))
            {
                StopRefuel();
                if (hand != null) hand.SetDropBlocked(false);
                return;
            }

            if (!refueling || refuelingCar == null) return;
            if (IsEmpty)
            {
                StopRefuel();
                if (hand != null) hand.SetDropBlocked(false);
                return;
            }
            if (hand == null || !hand.IsHolding)
            {
                StopRefuel();
                if (hand != null) hand.SetDropBlocked(false);
                return;
            }

            // Если отвели прицел от точки заправки — прерываем заправку.
            PlayerInteractor interactor = hand.GetComponentInParent<PlayerInteractor>();
            if (interactor != null)
            {
                GasRefuelPoint point = interactor.Target as GasRefuelPoint;
                if (point == null && interactor.TargetCollider != null)
                {
                    point = interactor.TargetCollider.GetComponentInParent<GasRefuelPoint>();
                }
                if (point == null || point.Car != refuelingCar)
                {
                    StopRefuel();
                    if (hand != null) hand.SetDropBlocked(false);
                    return;
                }
            }

            float amount = refuelRate * Time.deltaTime;
            if (currentFuel < amount) amount = currentFuel;
            if (refuelingCar.MaxFuel - refuelingCar.Fuel < amount) amount = refuelingCar.MaxFuel - refuelingCar.Fuel;

            if (amount <= 0f)
            {
                StopRefuel();
                if (hand != null) hand.SetDropBlocked(false);
                return;
            }

            refuelingCar.Refuel(amount);
            currentFuel -= amount;

            if (currentFuel <= 0f)
            {
                currentFuel = 0f;
                StopRefuel();
                if (hand != null) hand.SetDropBlocked(false);
            }
        }

        private void ResolveAnimator()
        {
            if (hand != null && hand.ActiveHandVariant != null)
            {
                var a = hand.ActiveHandVariant.GetComponentInChildren<Animator>();
                if (a != null) animator = a;
            }
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void SetUseBool(bool value)
        {
            ResolveAnimator();
            if (animator == null) return;
            try
            {
                if (!string.IsNullOrEmpty(useBool))
                    animator.SetBool(useBool, value);
                if (!string.IsNullOrEmpty(idleBool))
                    animator.SetBool(idleBool, !value);
            }
            catch { }
        }
    }
}


