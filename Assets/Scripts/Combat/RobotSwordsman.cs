using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RobotSwordsman : EnemyBase
    {
        [Header("Характеристики")]
        [Tooltip("Максимальное здоровье робота")]
        [SerializeField] private float maxHealth = 60f;
        [Tooltip("Скорость преследования, м/с")]
        [SerializeField] private float moveSpeed = 3.6f;
        [Tooltip("Скорость поворота к цели, градусов/с")]
        [SerializeField] private float turnSpeed = 420f;
        [Tooltip("С какой дистанции робот бьёт, м")]
        [SerializeField] private float attackRange = 2.1f;
        [Tooltip("Урон за удар по игроку")]
        [SerializeField] private float attackDamage = 11f;
        [Tooltip("Множитель урона, если цель — машина")]
        [SerializeField] private float carDamageMultiplier = 1.4f;
        [Tooltip("Пауза между ударами, с")]
        [SerializeField] private float attackCooldown = 1.1f;

        [Header("Смерть")]
        [Tooltip("Звук гибели. Если пусто, будет использован встроенный процедурный звук")]
        [SerializeField] private AudioClip deathSound;
        [Tooltip("Проигрывать звук при смерти")]
        [SerializeField] private bool playDeathSound = true;
        [Tooltip("Громкость звука гибели")]
        [SerializeField] private float deathSoundVolume = 1f;

        [Header("Аниматор")]
        [Tooltip("Animator робота-мечника. Если пусто — ищется на этом объекте и на детях")]
        [SerializeField] private Animator animator;
        [Tooltip("Играть переходы между Idle/Walk/Run и замах атаки")]
        [SerializeField] private bool animateStrike = true;
        [Tooltip("Длительность перехода между состояниями, с. 0 — мгновенное переключение")]
        [SerializeField] private float crossFade = 0.12f;
        [Tooltip("Порог скорости, выше которого робот на патруле идёт (Walk), а не стоит (Idle), м/с")]
        [SerializeField] private float walkThreshold = 0.25f;
        [Tooltip("Скорость, с которой анимация преследования играет ХОДЬБУ (Walk), а не бег. Мечник в бою шагает к цели, а не бежит. 0 — играть Run как раньше")]
        [SerializeField] private float chaseAnimSpeed = 1f;

        [Header("Названия состояний в Animator")]
        [Tooltip("Стоит на месте, цель не видна")]
        [SerializeField] private string idleState = "Idle";
        [Tooltip("Идёт по патрулю, цель не видна")]
        [SerializeField] private string walkState = "Walk";
        [Tooltip("Бежит на цель")]
        [SerializeField] private string runState = "Run";
        [Tooltip("Атака")]
        [SerializeField] private string attackState = "Attack";

        [Header("Лут")]
        [SerializeField] private LootEntry[] loot = new LootEntry[0];
        [Tooltip("Насколько разлетаются выпавшие предметы, м")]
        [SerializeField] private float scatterRadius = 0.5f;

        private Rigidbody body;
        private float nextAttackTime;
        private bool hadTarget;
        private string currentState;
        private float attackLockTime;
        private float attackStopTimer;
        // Анти-залипание преследования: если робот не приближается к цели
        // (упёрся в невидимый коллайдер/стену), после chaseGiveUpTime секунд
        // без прогресса цель сбрасывается и возвращается патруль.
        [SerializeField] private float chaseGiveUpTime = 3f;
        private float prevChaseDistance = -1f;
        private float chaseNoProgressTimer;
        private float chaseProgress;
        private RobotVoice voice;

        public Animator Animator => animator;

        /// <summary>Голос робота (шаги/сигнал цели). Создаётся автоматически.</summary>
        protected RobotVoice Voice
        {
            get
            {
                if (voice == null) voice = GetComponent<RobotVoice>();
                if (voice == null) voice = gameObject.AddComponent<RobotVoice>();
                return voice;
            }
        }

        private void ApplyBloodMoonBuffs()
        {
            maxHealth *= BloodMoonHealthMultiplier;
            moveSpeed *= BloodMoonSpeedMultiplier;
            attackDamage *= BloodMoonDamageMultiplier;
        }

        private void Awake()
        {
            ApplyBloodMoonBuffs();

            health = GetComponent<Health>();
            if (health != null) health.Configure(maxHealth, Health.HpOwner.Enemy);

            body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>();

            BaseAwake();

            if (health != null) health.Died += OnDied;
        }

        private void Update()
        {
            if (health == null || health.IsDead) return;

            BaseUpdate();

            if (attackStopTimer > 0f) attackStopTimer -= Time.deltaTime;

            Vector3 destination = ResolveDestination();
            Vector3 flatDelta = Vector3.ProjectOnPlane(destination - FrontPosition, Vector3.up);
            float distance = flatDelta.magnitude;

            bool engaged = HasTarget;

            // Дистанция преследования: если робот упёрся в невидимый коллайдер
            // (стекло, триггер-блокер) и физически не может подойти ближе —
            // цель всё равно «не видна для атаки». Чтобы не залипать в беге на
            // месте, при отсутствии прогресса цель сбрасывается и робот
            // возвращается в патруль.
            if (engaged && distance > attackRange * 1.2f)
            {
                float progress = (prevChaseDistance < 0f ? 0f : prevChaseDistance - distance);
                chaseProgress += progress;
                chaseNoProgressTimer += Time.deltaTime;
                if (chaseNoProgressTimer > chaseGiveUpTime && chaseProgress < 0.35f)
                {
                    Target = null;
                    BeginPause(0.8f);
                    engaged = false;
                }
            }
            else
            {
                chaseNoProgressTimer = 0f;
                chaseProgress = 0f;
            }
            prevChaseDistance = engaged ? distance : -1f;

            bool striking = attackStopTimer > 0f;

            Vector3 direction = striking ? Vector3.zero : ComputeAvoidance(flatDelta);

            UpdateStuckCheck(
                body != null ? body.linearVelocity : Vector3.zero,
                !striking && direction.sqrMagnitude > 0.0001f);

            if (direction.sqrMagnitude > 0.0001f) FaceDirection(direction, turnSpeed, Time.deltaTime);

            // Пауза на точке патруля: робот обязан встать, а не «идти» на месте.
            bool mayMove = PatrolAllowsMove();
            float speed = !mayMove || striking ? 0f : engaged ? moveSpeed : PatrolSpeed;
            Move(mayMove ? direction : Vector3.zero, speed);

            UpdateAnimation(engaged);

            RobotVoice voice = Voice;
            if (voice != null && body != null)
                voice.Step(new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), engaged);

            if (!engaged || Target == null) return;
            if (distance > attackRange) return;
            if (Time.time < nextAttackTime) return;

            nextAttackTime = Time.time + attackCooldown;
            attackStopTimer = Mathf.Max(attackCooldown, AttackStateLength());
            PlayAttack();
            Strike(Target, distance);
        }

        private void UpdateAnimation(bool engaged)
        {
            hadTarget = engaged;
            if (animator == null) return;

            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;

            SetFloat("MoveSpeed", speed);
            SetBool("InCombat", engaged);

            // Ключ к «бегу на месте»: Play/CrossFade в обход переходовAnimator
            // каждый кадр сбрасывали состояние в начало — цикл шагов не
            // доигрывался и ноги дёргались на месте. Если контроллер управляет
            // движением через параметры (MoveSpeed / InCombat), код больше НИЧЕГО
            // не форсирует: аниматор сам переключает Idle/Walk/Run по значениям.
            bool animatorDriven = HasParameter("MoveSpeed") || HasParameter("InCombat");
            if (animatorDriven)
            {
                animator.speed = 1f;
                currentState = null;
                return;
            }

            if (attackLockTime > 0f)
            {
                attackLockTime -= Time.deltaTime;
                return;
            }

            // Пока робот реально не двигается, держим Idle, даже если цель рядом.
            // Run должен возвращаться только вместе с движением, а не сразу после удара.
            bool moving = speed > walkThreshold;
            if (!moving)
            {
                PlayState(idleState);
                animator.speed = 1f;
                return;
            }

            // Преследование играется ХОДЬБОЙ (Walk), а не бегом: у этой модели
            // нет нормального Run-цикла — на преследовании ноги «крутят на
            // месте». Поэтому в бою включаем то же состояние Walk, что и на
            // патруле, но проигрываем его в chaseAnimSpeed раз быстрее, чтобы
            // робот выглядел идущим к цели, а не скользящим.
            if (engaged && chaseAnimSpeed > 0.01f)
            {
                PlayState(walkState);
                animator.speed = Mathf.Max(0.05f, chaseAnimSpeed);
            }
            else
            {
                PlayState(engaged ? runState : walkState);
                animator.speed = 1f;
            }
        }

        private void PlayState(string state)
        {
            if (animator == null || string.IsNullOrEmpty(state)) return;
            if (currentState == state) return;

            currentState = state;
            int hash = Animator.StringToHash(state);

            if (HasState(hash))
            {
                if (crossFade <= 0.001f) animator.Play(hash, 0, 0f);
                else animator.CrossFadeInFixedTime(hash, crossFade, 0, 0f);
            }
        }

        private void PlayAttack()
        {
            if (!animateStrike || animator == null) return;
            if (string.IsNullOrEmpty(attackState)) return;

            int hash = Animator.StringToHash(attackState);
            if (!HasState(hash)) return;

            // Если контроллер сам переключает состояния по параметрам, удар
            // тоже запускается триггером, а не форсированным Play — иначе
            // Play каждый раз обрывает боевой цикл и «ломает» анимацию.
            if (HasParameter("Attack"))
            {
                FireTrigger("Attack");
                attackLockTime = AttackStateLength();
                return;
            }

            currentState = attackState;
            attackLockTime = AttackStateLength();

            if (crossFade <= 0.001f) animator.Play(hash, 0, 0f);
            else animator.CrossFadeInFixedTime(hash, crossFade * 0.5f, 0, 0f);
        }

        private float AttackStateLength()
        {
            AnimationClip[] clips = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips
                : null;

            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i].name != attackState) continue;
                    float speed = Mathf.Abs(animator.speed);
                    return speed > 0.01f ? clips[i].length / speed : clips[i].length;
                }
            }

            return Mathf.Max(0.1f, attackCooldown * 0.6f);
        }

        private bool HasState(int hash)
        {
            if (animator == null || !animator.isActiveAndEnabled) return false;
            if (animator.runtimeAnimatorController == null) return false;

            int layers = animator.layerCount;
            for (int i = 0; i < layers; i++)
            {
                if (animator.HasState(i, hash)) return true;
            }

            return false;
        }

        private void SetBool(string parameter, bool value)
        {
            if (HasParameter(parameter)) animator.SetBool(parameter, value);
        }

        private void SetFloat(string parameter, float value)
        {
            if (HasParameter(parameter)) animator.SetFloat(parameter, value);
        }

        private void FireTrigger(string parameter)
        {
            if (HasParameter(parameter)) animator.SetTrigger(parameter);
        }

        private bool HasParameter(string parameter)
        {
            if (animator == null || string.IsNullOrEmpty(parameter)) return false;
            if (animator.parameters == null || animator.parameters.Length == 0) return false;

            for (int i = 0; i < animator.parameters.Length; i++)
            {
                if (animator.parameters[i].name == parameter) return true;
            }

            return false;
        }

        private void Move(Vector3 direction, float speed)
        {
            if (body == null) return;

            Vector3 velocity = body.linearVelocity;
            Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);

            if (direction.sqrMagnitude < 0.0001f || speed <= 0.01f)
                flatVelocity = Vector3.MoveTowards(flatVelocity, Vector3.zero, moveSpeed * 4f * Time.deltaTime);
            else if (IsPathClear(direction) || climbing)
                flatVelocity = Vector3.MoveTowards(flatVelocity, direction.normalized * speed, moveSpeed * 8f * Time.deltaTime);
            else
                flatVelocity = Vector3.MoveTowards(flatVelocity, direction.normalized * speed * 0.15f, moveSpeed * 2f * Time.deltaTime);

            velocity.x = flatVelocity.x;
            velocity.z = flatVelocity.z;
            velocity.y = climbing ? Mathf.Max(velocity.y, ClimbSpeed(direction, speed)) : velocity.y;
            body.linearVelocity = velocity;
        }

        private void Strike(Health target, float distance)
        {
            Vector3 origin = FrontPosition + Vector3.up;
            Vector3 point = target.transform.position + Vector3.up;

            if (Physics.Linecast(origin, point, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(target.transform)
                && !hit.collider.transform.IsChildOf(transform))
                return;

            float damage = attackDamage;
            CarController car = target.GetComponentInParent<CarController>();
            if (car != null) damage *= carDamageMultiplier;

            target.TakeDamage(new DamageInfo(damage, gameObject, point, (point - origin).normalized));
        }

        private void OnDied(Health dead)
        {
            FireTrigger("Dead");
            SetBool("Attacking", false);
            SetBool("InCombat", false);

            DropLoot();

            PlayDeathSound();

            if (body != null) body.linearVelocity = Vector3.zero;

            Destroy(gameObject, 0.1f);
        }

        /// <summary>
        /// Мечник не взрывается: на его месте не должно быть опасной зоны урона.
        /// Гибель обозначается только звуком, после чего робот разлетается на лут.
        /// </summary>
        private void PlayDeathSound()
        {
            if (!playDeathSound) return;

            if (deathSound != null)
            {
                AudioSource.PlayClipAtPoint(deathSound, transform.position, deathSoundVolume);
                return;
            }

            Explosion.PlayFallbackSound(transform.position, deathSoundVolume);
        }

        private void DropLoot()
        {
            if (loot == null || loot.Length == 0) return;

            for (int i = 0; i < loot.Length; i++)
            {
                LootEntry entry = loot[i];
                if (entry == null || entry.prefab == null) continue;
                if (Random.value > entry.chance) continue;

                int amount = Random.Range(
                    Mathf.Min(entry.countMin, entry.countMax),
                    Mathf.Max(entry.countMin, entry.countMax) + 1);

                for (int n = 0; n < amount; n++) SpawnLootPiece(entry.prefab);
            }
        }

        private void SpawnLootPiece(GameObject prefab)
        {
            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            GameObject piece = Instantiate(prefab, transform.position + new Vector3(offset.x, 0.35f, offset.y), Random.rotation);
            if (piece == null) return;

            Rigidbody pieceBody = piece.GetComponentInChildren<Rigidbody>();
            if (pieceBody == null) return;

            pieceBody.AddForce(new Vector3(offset.x, 1.4f, offset.y) * 1.6f, ForceMode.Impulse);
            pieceBody.AddTorque(Random.insideUnitSphere * 4f, ForceMode.Impulse);
        }

        [System.Serializable]
        public class LootEntry
        {
            [Tooltip("Префаб выпадающего предмета")]
            public GameObject prefab;
            [Tooltip("Шанс выпадения, 0..1. 1 — всегда")]
            [Range(0f, 1f)] public float chance = 1f;
            [Tooltip("Сколько выпадает, минимум")]
            public int countMin = 1;
            [Tooltip("Сколько выпадает, максимум")]
            public int countMax = 1;
        }
    }
}